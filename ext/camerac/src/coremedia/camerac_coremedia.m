/*
 * camerac_coremedia.m  –  macOS / iOS backend using AVFoundation
 *
 * Requires linking: AVFoundation, CoreMedia, CoreVideo
 *
 * Info.plist keys needed:
 *   <key>NSCameraUsageDescription</key> <string>Camera access</string>
 *
 * macOS entitlement:
 *   <key>com.apple.security.device.camera</key> <true/>
 */

#include "../camerac_internal.h"

#if defined(__APPLE__)

#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>

#if TARGET_OS_IOS && !TARGET_OS_TV
  #define USE_UIKIT_ROTATION
  #import <UIKit/UIKit.h>
#endif

/* ------------------------------------------------------------------ */
/* Format helpers                                                      */
/* ------------------------------------------------------------------ */

static camPixelFormat fourcc_to_pixel_format(FourCharCode fmt)
{
    switch (fmt) {
        case kCMPixelFormat_16LE555:                            return CAM_PIXEL_FORMAT_XRGB1555;
        case kCMPixelFormat_16LE565:                            return CAM_PIXEL_FORMAT_RGB565;
        case kCMPixelFormat_24RGB:                              return CAM_PIXEL_FORMAT_RGB24;
        case kCMPixelFormat_32ARGB:                             return CAM_PIXEL_FORMAT_ARGB32;
        case kCMPixelFormat_32BGRA:                             return CAM_PIXEL_FORMAT_BGRA32;
        case kCMPixelFormat_422YpCbCr8:                         return CAM_PIXEL_FORMAT_UYVY;
        case kCMPixelFormat_422YpCbCr8_yuvs:                    return CAM_PIXEL_FORMAT_YUY2;
        case kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange:   return CAM_PIXEL_FORMAT_NV12;
        case kCVPixelFormatType_420YpCbCr8BiPlanarFullRange:    return CAM_PIXEL_FORMAT_NV12;
        case kCVPixelFormatType_420YpCbCr10BiPlanarVideoRange:  return CAM_PIXEL_FORMAT_P010;
        case kCVPixelFormatType_420YpCbCr10BiPlanarFullRange:   return CAM_PIXEL_FORMAT_P010;
        default: return CAM_PIXEL_FORMAT_UNKNOWN;
    }
}

/* Forward declaration so CamPrivateCameraData can hold a delegate pointer
   before the full @interface CamCaptureDelegate is defined.             */
@class CamCaptureDelegate;

/* ------------------------------------------------------------------ */
/* Private device data                                                 */
/* ------------------------------------------------------------------ */

@interface CamPrivateCameraData : NSObject
@property (nonatomic, retain) AVCaptureSession            *session;
@property (nonatomic, retain) CamCaptureDelegate           *delegate;
@property (nonatomic, retain) AVCaptureVideoDataOutput     *output;
@property (nonatomic, strong) dispatch_queue_t              queue;
@property (nonatomic, assign) BOOL                          permFired;
@end

@implementation CamPrivateCameraData
@end

/* ------------------------------------------------------------------ */
/* Delegate to receive frames on a GCD queue                           */
/* ------------------------------------------------------------------ */

@interface CamCaptureDelegate : NSObject <AVCaptureVideoDataOutputSampleBufferDelegate>
@property camDevice_t *device;
- (instancetype)initWithDevice:(camDevice_t *)dev;
@end

@implementation CamCaptureDelegate

- (instancetype)initWithDevice:(camDevice_t *)dev
{
    self = [super init];
    if (self) { _device = dev; }
    return self;
}

- (void)captureOutput:(AVCaptureOutput *)output
didOutputSampleBuffer:(CMSampleBufferRef)sampleBuffer
       fromConnection:(AVCaptureConnection *)connection
{
    CAM_UNUSED(output);
    CAM_UNUSED(connection);

    camDevice_t *device = self.device;
    if (!device || !device->hidden) return;

    CamPrivateCameraData *hidden =
        (__bridge CamPrivateCameraData *)device->hidden;

    /* Permission check – fire at most once to avoid duplicate queuing.       */
    /* requestAccessForMediaType:completionHandler: in OpenDevice is the      */
    /* primary path; this is a fallback for cases where the first frame       */
    /* arrives before the completion handler fires (rare, but possible).      */
    if (!hidden.permFired) {
        AVAuthorizationStatus status =
            [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
        if (status != AVAuthorizationStatusNotDetermined) {
            hidden.permFired = YES;
            camPermission perm = (status == AVAuthorizationStatusAuthorized)
                ? CAM_PERMISSION_APPROVED : CAM_PERMISSION_DENIED;
            cam_permission_outcome(device, perm);
        }
    }

    if (device->permission != CAM_PERMISSION_APPROVED) return;

    /* Lock image buffer */
    CVImageBufferRef imageBuffer = CMSampleBufferGetImageBuffer(sampleBuffer);
    if (!imageBuffer) return;

    CVPixelBufferLockBaseAddress(imageBuffer, kCVPixelBufferLock_ReadOnly);

    size_t w = CVPixelBufferGetWidth(imageBuffer);
    size_t h = CVPixelBufferGetHeight(imageBuffer);

    CMTime pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer);
    uint64_t ts_ns = (uint64_t)(CMTimeGetSeconds(pts) * 1e9);

    camFrame frame;
    memset(&frame, 0, sizeof(frame));
    frame.width        = (int)w;
    frame.height       = (int)h;
    frame.timestamp_ns = ts_ns;
    frame.format       = device->actual_spec.format;

    size_t plane_count = CVPixelBufferGetPlaneCount(imageBuffer);
    if (plane_count >= 2) {
        /* Bi-planar format (e.g. NV12 / P010): deliver both planes. */
        frame.data  = CVPixelBufferGetBaseAddressOfPlane(imageBuffer, 0);
        frame.pitch = (int)CVPixelBufferGetBytesPerRowOfPlane(imageBuffer, 0);
        frame.data2  = CVPixelBufferGetBaseAddressOfPlane(imageBuffer, 1);
        frame.pitch2 = (int)CVPixelBufferGetBytesPerRowOfPlane(imageBuffer, 1);
    } else {
        /* Packed / single-plane format. */
        frame.data  = CVPixelBufferGetBaseAddress(imageBuffer);
        frame.pitch = (int)CVPixelBufferGetBytesPerRow(imageBuffer);
    }

#ifdef USE_UIKIT_ROTATION
    UIDeviceOrientation orient = [[UIDevice currentDevice] orientation];
    switch (orient) {
        case UIDeviceOrientationLandscapeLeft:  frame.rotation = 90.0f;  break;
        case UIDeviceOrientationLandscapeRight: frame.rotation = -90.0f; break;
        case UIDeviceOrientationPortraitUpsideDown: frame.rotation = 180.0f; break;
        default: frame.rotation = 0.0f; break;
    }
#endif

    cam_deliver_frame(device, &frame);

    CVPixelBufferUnlockBaseAddress(imageBuffer, kCVPixelBufferLock_ReadOnly);
}

- (void)captureOutput:(AVCaptureOutput *)output
  didDropSampleBuffer:(CMSampleBufferRef)sampleBuffer
       fromConnection:(AVCaptureConnection *)connection
{
    CAM_UNUSED(output);
    CAM_UNUSED(sampleBuffer);
    CAM_UNUSED(connection);
}

@end

/* ------------------------------------------------------------------ */
/* Private camera handle (stored in device->handle)                    */
/* ------------------------------------------------------------------ */

typedef struct CoreMediaDeviceHandle {
    char unique_id[512];
    char name[256];
    camPosition position;
} CoreMediaDeviceHandle;

/* ------------------------------------------------------------------ */
/* DetectDevices                                                       */
/* ------------------------------------------------------------------ */

static void COREMEDIA_DetectDevices(void)
{
    /* Use AVCaptureDeviceDiscoverySession (available macOS 10.15 / iOS 10)
       instead of the deprecated devicesWithMediaType:.                   */
#if TARGET_OS_IOS && !TARGET_OS_TV
    NSArray<AVCaptureDeviceType> *types = @[
        AVCaptureDeviceTypeBuiltInWideAngleCamera,
        AVCaptureDeviceTypeBuiltInTelephotoCamera,
        AVCaptureDeviceTypeBuiltInUltraWideCamera
    ];
    AVCaptureDeviceDiscoverySession *session =
        [AVCaptureDeviceDiscoverySession
            discoverySessionWithDeviceTypes:types
                                  mediaType:AVMediaTypeVideo
                                   position:AVCaptureDevicePositionUnspecified];
#else
    NSArray<AVCaptureDeviceType> *types = @[
        AVCaptureDeviceTypeBuiltInWideAngleCamera,
        AVCaptureDeviceTypeExternalUnknown
    ];
    AVCaptureDeviceDiscoverySession *session =
        [AVCaptureDeviceDiscoverySession
            discoverySessionWithDeviceTypes:types
                                  mediaType:AVMediaTypeVideo
                                   position:AVCaptureDevicePositionUnspecified];
#endif
    NSArray<AVCaptureDevice *> *devices = session.devices;

    for (AVCaptureDevice *avDev in devices) {
        /* Gather supported formats */
        NSArray<AVCaptureDeviceFormat *> *formats = avDev.formats;
        int num_specs = 0;
        camSpec *specs = NULL;

        for (AVCaptureDeviceFormat *fmt in formats) {
            CMFormatDescriptionRef desc = fmt.formatDescription;
            FourCharCode fourcc = CMFormatDescriptionGetMediaSubType(desc);
            camPixelFormat pf = fourcc_to_pixel_format(fourcc);
            if (pf == CAM_PIXEL_FORMAT_UNKNOWN) continue;

            CMVideoDimensions dim = CMVideoFormatDescriptionGetDimensions(desc);

            for (AVFrameRateRange *range in fmt.videoSupportedFrameRateRanges) {
                specs = (camSpec *)realloc(specs,
                    sizeof(camSpec) * (size_t)(num_specs + 1));
                camSpec *s = &specs[num_specs++];
                s->width           = dim.width;
                s->height          = dim.height;
                s->fps_numerator   = (int)range.maxFrameRate;
                s->fps_denominator = 1;
                s->format          = pf;
            }
        }

        camPosition pos = CAM_POSITION_UNKNOWN;
#if TARGET_OS_IOS && !TARGET_OS_TV
        if (avDev.position == AVCaptureDevicePositionFront)
            pos = CAM_POSITION_FRONT_FACING;
        else if (avDev.position == AVCaptureDevicePositionBack)
            pos = CAM_POSITION_BACK_FACING;
#endif

        CoreMediaDeviceHandle *handle =
            (CoreMediaDeviceHandle *)calloc(1, sizeof(CoreMediaDeviceHandle));
        strncpy(handle->unique_id,
                avDev.uniqueID.UTF8String ? avDev.uniqueID.UTF8String : "",
                sizeof(handle->unique_id) - 1);
        strncpy(handle->name,
                avDev.localizedName.UTF8String ? avDev.localizedName.UTF8String : "",
                sizeof(handle->name) - 1);
        handle->position = pos;

        cam_add_device(handle->name,
                       handle->unique_id,
                       pos,
                       num_specs, specs,
                       handle);
        free(specs);
    }
}

/* ------------------------------------------------------------------ */
/* OpenDevice                                                          */
/* ------------------------------------------------------------------ */

static bool COREMEDIA_OpenDevice(camDevice_t *device, const camSpec *spec)
{
    CoreMediaDeviceHandle *handle =
        (CoreMediaDeviceHandle *)device->handle;

    AVCaptureDevice *avDev = [AVCaptureDevice
        deviceWithUniqueID:[NSString stringWithUTF8String:handle->unique_id]];
    if (!avDev) {
        return cam_set_error("Could not find AVCaptureDevice '%s'",
                             handle->unique_id);
    }

    CamPrivateCameraData *hidden = [[CamPrivateCameraData alloc] init];
    hidden.session = [[AVCaptureSession alloc] init];

    device->hidden = (void *)CFBridgingRetain(hidden);

    /* Configure input */
    NSError *error = nil;
    AVCaptureDeviceInput *input =
        [AVCaptureDeviceInput deviceInputWithDevice:avDev error:&error];
    if (!input) {
        CFBridgingRelease(device->hidden);
        device->hidden = NULL;
        return cam_set_error("Failed to create AVCaptureDeviceInput");
    }
    [hidden.session addInput:input];

    /* Configure output */
    AVCaptureVideoDataOutput *output = [[AVCaptureVideoDataOutput alloc] init];
    output.alwaysDiscardsLateVideoFrames = YES;

    /* Pick pixel format */
    camSpec actual = (spec && spec->format != CAM_PIXEL_FORMAT_UNKNOWN) ?
        *spec : (device->num_specs > 0 ? device->all_specs[0] : *spec);

    /* Choose session preset */
    if (actual.width >= 3840) {
        hidden.session.sessionPreset = AVCaptureSessionPreset3840x2160;
    } else if (actual.width >= 1920) {
        hidden.session.sessionPreset = AVCaptureSessionPreset1920x1080;
    } else if (actual.width >= 1280) {
        hidden.session.sessionPreset = AVCaptureSessionPreset1280x720;
    } else if (actual.width >= 640) {
        hidden.session.sessionPreset = AVCaptureSessionPreset640x480;
    } else {
        hidden.session.sessionPreset = AVCaptureSessionPresetMedium;
    }

    /* Callback dispatch queue – retained in hidden so we can stop it later */
    hidden.queue    = dispatch_queue_create("camerac.coremedia", DISPATCH_QUEUE_SERIAL);
    hidden.delegate = [[CamCaptureDelegate alloc] initWithDevice:device];
    hidden.output   = output;

    [output setSampleBufferDelegate:hidden.delegate queue:hidden.queue];

    [hidden.session addOutput:output];

    /* Update actual spec */
    device->actual_spec = actual;

    /* Request permission then start session.
       The completionHandler is the primary permission path.  We mark
       permFired here so the per-frame fallback in the delegate doesn't
       double-enqueue the result if the handler fires before the first frame. */
    [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo
        completionHandler:^(BOOL granted) {
        hidden.permFired = YES;
        cam_permission_outcome(device,
            granted ? CAM_PERMISSION_APPROVED : CAM_PERMISSION_DENIED);
    }];

    [hidden.session startRunning];

    return true;
}

/* ------------------------------------------------------------------ */
/* CloseDevice                                                         */
/* ------------------------------------------------------------------ */

static void COREMEDIA_CloseDevice(camDevice_t *device)
{
    if (!device || !device->hidden) return;
    CamPrivateCameraData *hidden =
        (__bridge_transfer CamPrivateCameraData *)device->hidden;

    /* Detach delegate before stopping so no frames arrive after close */
    if (hidden.output) {
        [hidden.output setSampleBufferDelegate:nil queue:nil];
        hidden.output = nil;
    }
    hidden.delegate = nil;

    [hidden.session stopRunning];
    hidden.session = nil;
    hidden.queue   = nil;  /* ARC releases the queue */
    device->hidden = NULL;
}

/* ------------------------------------------------------------------ */
/* WaitDevice / AcquireFrame / ReleaseFrame                           */
/* ------------------------------------------------------------------ */

/* The delegate above drives the callback directly; these are not used
   because ProvidesOwnCallbackThread = true. */

static bool COREMEDIA_WaitDevice(camDevice_t *device) {
    CAM_UNUSED(device); return false;
}
static camFrameResult COREMEDIA_AcquireFrame(camDevice_t *device,
                                              camFrame *frame) {
    CAM_UNUSED(device); CAM_UNUSED(frame); return CAM_FRAME_SKIP;
}
static void COREMEDIA_ReleaseFrame(camDevice_t *device, camFrame *frame) {
    CAM_UNUSED(device); CAM_UNUSED(frame);
}

/* ------------------------------------------------------------------ */
/* FreeDeviceHandle / Deinitialize                                     */
/* ------------------------------------------------------------------ */

static void COREMEDIA_FreeDeviceHandle(camDevice_t *device)
{
    if (device && device->handle) {
        free(device->handle);
        device->handle = NULL;
    }
}

static void COREMEDIA_Deinitialize(void) {}

/* ------------------------------------------------------------------ */
/* Bootstrap                                                           */
/* ------------------------------------------------------------------ */

static bool COREMEDIA_Init(camDriverImpl *impl)
{
    impl->DetectDevices           = COREMEDIA_DetectDevices;
    impl->OpenDevice              = COREMEDIA_OpenDevice;
    impl->CloseDevice             = COREMEDIA_CloseDevice;
    impl->WaitDevice              = COREMEDIA_WaitDevice;
    impl->AcquireFrame            = COREMEDIA_AcquireFrame;
    impl->ReleaseFrame            = COREMEDIA_ReleaseFrame;
    impl->FreeDeviceHandle        = COREMEDIA_FreeDeviceHandle;
    impl->Deinitialize            = COREMEDIA_Deinitialize;
    impl->ProvidesOwnCallbackThread = true;
    return true;
}

camBootstrap COREMEDIA_bootstrap = {
    "coremedia",
    "AVFoundation / CoreMedia (Apple)",
    COREMEDIA_Init,
    false
};

#endif /* __APPLE__ */
