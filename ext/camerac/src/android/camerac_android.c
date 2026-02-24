/*
 * camerac_android.c  –  Android backend using Camera2 NDK API
 *
 * Requires NDK API >= 24 (Android 7.0)
 *
 * AndroidManifest.xml:
 *   <uses-permission android:name="android.permission.CAMERA"/>
 *   <uses-feature android:name="android.hardware.camera"/>
 *
 * Link: -lcamera2ndk -lmediandk
 */

#include "../camerac_internal.h"

#if defined(__ANDROID__)

#include <dlfcn.h>
#include <jni.h>
#include <android/native_window.h>
#include <android/log.h>

/* Force API 24 for camera headers */
#if __ANDROID_API__ < 24
#undef  __ANDROID_API__
#define __ANDROID_API__ 24
#endif

#include <camera/NdkCameraDevice.h>
#include <camera/NdkCameraManager.h>
#include <media/NdkImage.h>
#include <media/NdkImageReader.h>

#define ANDROID_LOG_TAG "camerac"
#define ALOGI(...) __android_log_print(ANDROID_LOG_INFO,  ANDROID_LOG_TAG, __VA_ARGS__)
#define ALOGE(...) __android_log_print(ANDROID_LOG_ERROR, ANDROID_LOG_TAG, __VA_ARGS__)

/* ------------------------------------------------------------------ */
/* Dynamic function pointers (dlopen to support older devices)         */
/* ------------------------------------------------------------------ */

static void *s_libcamera = NULL;
static void *s_libmedia  = NULL;

typedef ACameraManager* (*pfn_ACameraManager_create)(void);
typedef void            (*pfn_ACameraManager_delete)(ACameraManager*);
typedef camera_status_t (*pfn_ACameraManager_getCameraIdList)(ACameraManager*, ACameraIdList**);
typedef void            (*pfn_ACameraManager_deleteCameraIdList)(ACameraIdList*);
typedef camera_status_t (*pfn_ACameraManager_getCameraCharacteristics)(ACameraManager*, const char*, ACameraMetadata**);
typedef void            (*pfn_ACameraMetadata_free)(ACameraMetadata*);
typedef camera_status_t (*pfn_ACameraMetadata_getConstEntry)(const ACameraMetadata*, uint32_t, ACameraMetadata_const_entry*);
typedef camera_status_t (*pfn_ACameraManager_openCamera)(ACameraManager*, const char*, ACameraDevice_StateCallbacks*, ACameraDevice**);
typedef camera_status_t (*pfn_ACameraDevice_close)(ACameraDevice*);
typedef camera_status_t (*pfn_ACameraDevice_createCaptureRequest)(const ACameraDevice*, ACameraDevice_request_template, ACaptureRequest**);
typedef void            (*pfn_ACaptureRequest_free)(ACaptureRequest*);
typedef camera_status_t (*pfn_ACaptureRequest_addTarget)(ACaptureRequest*, const ACameraOutputTarget*);
typedef camera_status_t (*pfn_ACameraOutputTarget_create)(ACameraWindowType*, ACameraOutputTarget**);
typedef void            (*pfn_ACameraOutputTarget_free)(ACameraOutputTarget*);
typedef camera_status_t (*pfn_ACaptureSessionOutputContainer_create)(ACaptureSessionOutputContainer**);
typedef void            (*pfn_ACaptureSessionOutputContainer_free)(ACaptureSessionOutputContainer*);
typedef camera_status_t (*pfn_ACaptureSessionOutputContainer_add)(ACaptureSessionOutputContainer*, const ACaptureSessionOutput*);
typedef camera_status_t (*pfn_ACaptureSessionOutput_create)(ACameraWindowType*, ACaptureSessionOutput**);
typedef void            (*pfn_ACaptureSessionOutput_free)(ACaptureSessionOutput*);
typedef camera_status_t (*pfn_ACameraDevice_createCaptureSession)(ACameraDevice*, const ACaptureSessionOutputContainer*, const ACameraCaptureSession_stateCallbacks*, ACameraCaptureSession**);
typedef void            (*pfn_ACameraCaptureSession_close)(ACameraCaptureSession*);
typedef camera_status_t (*pfn_ACameraCaptureSession_setRepeatingRequest)(ACameraCaptureSession*, ACameraCaptureSession_captureCallbacks*, int, ACaptureRequest**, int*);

typedef media_status_t (*pfn_AImageReader_new)(int32_t, int32_t, int32_t, int32_t, AImageReader**);
typedef void           (*pfn_AImageReader_delete)(AImageReader*);
typedef media_status_t (*pfn_AImageReader_getWindow)(AImageReader*, ANativeWindow**);
typedef media_status_t (*pfn_AImageReader_setImageListener)(AImageReader*, AImageReader_ImageListener*);
typedef media_status_t (*pfn_AImageReader_acquireNextImage)(AImageReader*, AImage**);
typedef void           (*pfn_AImage_delete)(AImage*);
typedef media_status_t (*pfn_AImage_getTimestamp)(const AImage*, int64_t*);
typedef media_status_t (*pfn_AImage_getNumberOfPlanes)(const AImage*, int32_t*);
typedef media_status_t (*pfn_AImage_getPlaneRowStride)(const AImage*, int, int32_t*);
typedef media_status_t (*pfn_AImage_getPlaneData)(const AImage*, int, uint8_t**, int*);
typedef media_status_t (*pfn_AImage_getWidth)(const AImage*, int32_t*);
typedef media_status_t (*pfn_AImage_getHeight)(const AImage*, int32_t*);

#define DECL_FN(name) static pfn_##name p##name = NULL
DECL_FN(ACameraManager_create);
DECL_FN(ACameraManager_delete);
DECL_FN(ACameraManager_getCameraIdList);
DECL_FN(ACameraManager_deleteCameraIdList);
DECL_FN(ACameraManager_getCameraCharacteristics);
DECL_FN(ACameraMetadata_free);
DECL_FN(ACameraMetadata_getConstEntry);
DECL_FN(ACameraManager_openCamera);
DECL_FN(ACameraDevice_close);
DECL_FN(ACameraDevice_createCaptureRequest);
DECL_FN(ACaptureRequest_free);
DECL_FN(ACaptureRequest_addTarget);
DECL_FN(ACameraOutputTarget_create);
DECL_FN(ACameraOutputTarget_free);
DECL_FN(ACaptureSessionOutputContainer_create);
DECL_FN(ACaptureSessionOutputContainer_free);
DECL_FN(ACaptureSessionOutputContainer_add);
DECL_FN(ACaptureSessionOutput_create);
DECL_FN(ACaptureSessionOutput_free);
DECL_FN(ACameraDevice_createCaptureSession);
DECL_FN(ACameraCaptureSession_close);
DECL_FN(ACameraCaptureSession_setRepeatingRequest);
DECL_FN(AImageReader_new);
DECL_FN(AImageReader_delete);
DECL_FN(AImageReader_getWindow);
DECL_FN(AImageReader_setImageListener);
DECL_FN(AImageReader_acquireNextImage);
DECL_FN(AImage_delete);
DECL_FN(AImage_getTimestamp);
DECL_FN(AImage_getNumberOfPlanes);
DECL_FN(AImage_getPlaneRowStride);
DECL_FN(AImage_getPlaneData);
DECL_FN(AImage_getWidth);
DECL_FN(AImage_getHeight);

#define LOAD_FN(lib, name) \
    p##name = (pfn_##name)dlsym(lib, #name); \
    if (!p##name) { ALOGE("dlsym %s failed", #name); return false; }

static bool load_camera_libs(void)
{
    s_libcamera = dlopen("libcamera2ndk.so", RTLD_NOW | RTLD_LOCAL);
    if (!s_libcamera) { ALOGE("dlopen libcamera2ndk.so failed: %s", dlerror()); return false; }

    s_libmedia = dlopen("libmediandk.so", RTLD_NOW | RTLD_LOCAL);
    if (!s_libmedia) { ALOGE("dlopen libmediandk.so failed: %s", dlerror()); return false; }

    LOAD_FN(s_libcamera, ACameraManager_create);
    LOAD_FN(s_libcamera, ACameraManager_delete);
    LOAD_FN(s_libcamera, ACameraManager_getCameraIdList);
    LOAD_FN(s_libcamera, ACameraManager_deleteCameraIdList);
    LOAD_FN(s_libcamera, ACameraManager_getCameraCharacteristics);
    LOAD_FN(s_libcamera, ACameraMetadata_free);
    LOAD_FN(s_libcamera, ACameraMetadata_getConstEntry);
    LOAD_FN(s_libcamera, ACameraManager_openCamera);
    LOAD_FN(s_libcamera, ACameraDevice_close);
    LOAD_FN(s_libcamera, ACameraDevice_createCaptureRequest);
    LOAD_FN(s_libcamera, ACaptureRequest_free);
    LOAD_FN(s_libcamera, ACaptureRequest_addTarget);
    LOAD_FN(s_libcamera, ACameraOutputTarget_create);
    LOAD_FN(s_libcamera, ACameraOutputTarget_free);
    LOAD_FN(s_libcamera, ACaptureSessionOutputContainer_create);
    LOAD_FN(s_libcamera, ACaptureSessionOutputContainer_free);
    LOAD_FN(s_libcamera, ACaptureSessionOutputContainer_add);
    LOAD_FN(s_libcamera, ACaptureSessionOutput_create);
    LOAD_FN(s_libcamera, ACaptureSessionOutput_free);
    LOAD_FN(s_libcamera, ACameraDevice_createCaptureSession);
    LOAD_FN(s_libcamera, ACameraCaptureSession_close);
    LOAD_FN(s_libcamera, ACameraCaptureSession_setRepeatingRequest);

    LOAD_FN(s_libmedia, AImageReader_new);
    LOAD_FN(s_libmedia, AImageReader_delete);
    LOAD_FN(s_libmedia, AImageReader_getWindow);
    LOAD_FN(s_libmedia, AImageReader_setImageListener);
    LOAD_FN(s_libmedia, AImageReader_acquireNextImage);
    LOAD_FN(s_libmedia, AImage_delete);
    LOAD_FN(s_libmedia, AImage_getTimestamp);
    LOAD_FN(s_libmedia, AImage_getNumberOfPlanes);
    LOAD_FN(s_libmedia, AImage_getPlaneRowStride);
    LOAD_FN(s_libmedia, AImage_getPlaneData);
    LOAD_FN(s_libmedia, AImage_getWidth);
    LOAD_FN(s_libmedia, AImage_getHeight);

    return true;
}

/* ------------------------------------------------------------------ */
/* Private per-device data                                             */
/* ------------------------------------------------------------------ */

typedef struct AndroidPrivateData {
    ACameraDevice                  *cam_device;
    AImageReader                   *reader;
    ANativeWindow                  *window;
    ACaptureSessionOutput          *session_output;
    ACaptureSessionOutputContainer *session_output_container;
    ACameraOutputTarget            *output_target;
    ACaptureRequest                *request;
    ACameraCaptureSession          *capture_session;
    camDevice_t                    *parent;
} AndroidPrivateData;

/* ------------------------------------------------------------------ */
/* Image listener                                                      */
/* ------------------------------------------------------------------ */

static void android_image_callback(void *context, AImageReader *reader)
{
    AndroidPrivateData *priv = (AndroidPrivateData *)context;
    if (!priv) return;

    AImage *image = NULL;
    if (pAImageReader_acquireNextImage(reader, &image) != AMEDIA_OK || !image)
        return;

    int32_t w = 0, h = 0;
    pAImage_getWidth (image, &w);
    pAImage_getHeight(image, &h);

    int64_t ts = 0;
    pAImage_getTimestamp(image, &ts);

    int32_t planes = 0;
    pAImage_getNumberOfPlanes(image, &planes);

    /* YUV_420_888 always has 3 planes, but on most Android devices the
     * hardware delivers NV12 (semi-planar): plane 0 = Y, plane 1 = interleaved
     * UV (identical to NV12 chroma plane).  We expose both so the caller can
     * work with the raw NV12 data without any copy/conversion.
     *
     * plane 0  →  frame.data  / frame.pitch   (Y  luma)
     * plane 1  →  frame.data2 / frame.pitch2  (UV chroma, interleaved)
     */
    uint8_t *y_data = NULL; int y_len = 0; int32_t y_pitch = 0;
    uint8_t *uv_data = NULL; int uv_len = 0; int32_t uv_pitch = 0;

    if (planes > 0) {
        pAImage_getPlaneData(image, 0, &y_data, &y_len);
        pAImage_getPlaneRowStride(image, 0, &y_pitch);
    }
    if (planes > 1) {
        pAImage_getPlaneData(image, 1, &uv_data, &uv_len);
        pAImage_getPlaneRowStride(image, 1, &uv_pitch);
    }

    camFrame frame;
    memset(&frame, 0, sizeof(frame));
    frame.data         = y_data;
    frame.pitch        = (int)y_pitch;
    frame.data2        = uv_data;
    frame.pitch2       = (int)uv_pitch;
    frame.width        = w;
    frame.height       = h;
    frame.timestamp_ns = (uint64_t)ts;
    frame.format       = CAM_PIXEL_FORMAT_NV12; /* Camera2 YUV_420_888 → NV12 */

    cam_deliver_frame(priv->parent, &frame);

    pAImage_delete(image);
}

/* ------------------------------------------------------------------ */
/* Device state callbacks                                              */
/* ------------------------------------------------------------------ */

static void on_camera_disconnected(void *context, ACameraDevice *device)
{
    CAM_UNUSED(device);
    AndroidPrivateData *priv = (AndroidPrivateData *)context;
    if (priv && priv->parent) {
        priv->parent->shutdown = 1;
    }
}

static void on_camera_error(void *context, ACameraDevice *device, int error)
{
    CAM_UNUSED(device);
    ALOGE("Camera2 error %d", error);
    on_camera_disconnected(context, device);
}

/* ------------------------------------------------------------------ */
/* DetectDevices                                                       */
/* ------------------------------------------------------------------ */

static ACameraManager *s_cam_manager = NULL;

static void ANDROIDCAMERA_DetectDevices(void)
{
    s_cam_manager = pACameraManager_create();
    if (!s_cam_manager) return;

    ACameraIdList *id_list = NULL;
    if (pACameraManager_getCameraIdList(s_cam_manager, &id_list) != ACAMERA_OK)
        return;

    for (int i = 0; i < id_list->numCameras; i++) {
        const char *id = id_list->cameraIds[i];

        ACameraMetadata *meta = NULL;
        pACameraManager_getCameraCharacteristics(s_cam_manager, id, &meta);

        camPosition pos = CAM_POSITION_UNKNOWN;
        char name[64];
        snprintf(name, sizeof(name), "Camera %s", id);

        if (meta) {
            ACameraMetadata_const_entry entry;
            if (pACameraMetadata_getConstEntry(meta,
                    ACAMERA_LENS_FACING, &entry) == ACAMERA_OK) {
                uint8_t facing = entry.data.u8[0];
                if (facing == ACAMERA_LENS_FACING_FRONT)
                    pos = CAM_POSITION_FRONT_FACING;
                else if (facing == ACAMERA_LENS_FACING_BACK)
                    pos = CAM_POSITION_BACK_FACING;
            }
            pACameraMetadata_free(meta);
        }

        /* Default spec: 1280x720 NV12 @ 30fps */
        camSpec spec = { 1280, 720, 30, 1, CAM_PIXEL_FORMAT_NV12 };

        char *handle_id = strdup(id);
        cam_add_device(name, id, pos, 1, &spec, handle_id);
    }

    pACameraManager_deleteCameraIdList(id_list);
}

/* ------------------------------------------------------------------ */
/* OpenDevice                                                          */
/* ------------------------------------------------------------------ */

static bool ANDROIDCAMERA_OpenDevice(camDevice_t *device, const camSpec *spec)
{
    const char *id = (const char *)device->handle;

    int w  = spec ? spec->width  : 1280;
    int h  = spec ? spec->height : 720;

    AndroidPrivateData *priv =
        (AndroidPrivateData *)calloc(1, sizeof(AndroidPrivateData));
    priv->parent = device;

    /* Create image reader (AIMAGE_FORMAT_YUV_420_888 = 0x23) */
    if (pAImageReader_new(w, h, 0x23 /* YUV_420_888 */, 4,
                          &priv->reader) != AMEDIA_OK) {
        free(priv);
        return cam_set_error("AImageReader_new failed");
    }

    AImageReader_ImageListener listener = {
        .context      = priv,
        .onImageAvailable = android_image_callback
    };
    pAImageReader_setImageListener(priv->reader, &listener);
    pAImageReader_getWindow(priv->reader, &priv->window);

    /* Open camera */
    ACameraDevice_StateCallbacks state_cb = {
        .context          = priv,
        .onDisconnected   = on_camera_disconnected,
        .onError          = on_camera_error
    };

    if (pACameraManager_openCamera(s_cam_manager, id, &state_cb,
                                    &priv->cam_device) != ACAMERA_OK) {
        pAImageReader_delete(priv->reader);
        free(priv);
        return cam_set_error("ACameraManager_openCamera failed");
    }

    /* Create capture request */
    pACameraDevice_createCaptureRequest(priv->cam_device,
        TEMPLATE_PREVIEW, &priv->request);
    pACameraOutputTarget_create(priv->window, &priv->output_target);
    pACaptureRequest_addTarget(priv->request, priv->output_target);

    /* Create session */
    pACaptureSessionOutputContainer_create(&priv->session_output_container);
    pACaptureSessionOutput_create(priv->window, &priv->session_output);
    pACaptureSessionOutputContainer_add(priv->session_output_container,
                                        priv->session_output);

    ACameraCaptureSession_stateCallbacks sess_cb = {
        .context            = priv,
        .onClosed           = NULL,
        .onReady            = NULL,
        .onActive           = NULL
    };
    pACameraDevice_createCaptureSession(priv->cam_device,
        priv->session_output_container, &sess_cb, &priv->capture_session);

    pACameraCaptureSession_setRepeatingRequest(priv->capture_session,
        NULL, 1, &priv->request, NULL);

    device->actual_spec.width  = w;
    device->actual_spec.height = h;
    device->actual_spec.format = CAM_PIXEL_FORMAT_NV12;
    device->hidden = priv;

    /* Android permission is handled by the Java layer before this is called */
    cam_permission_outcome(device, CAM_PERMISSION_APPROVED);

    return true;
}

/* ------------------------------------------------------------------ */
/* WaitDevice / AcquireFrame / ReleaseFrame                            */
/* ------------------------------------------------------------------ */

static bool ANDROIDCAMERA_WaitDevice(camDevice_t *device) {
    CAM_UNUSED(device); return false; /* callback-driven */
}
static camFrameResult ANDROIDCAMERA_AcquireFrame(camDevice_t *d, camFrame *f) {
    CAM_UNUSED(d); CAM_UNUSED(f); return CAM_FRAME_SKIP;
}
static void ANDROIDCAMERA_ReleaseFrame(camDevice_t *d, camFrame *f) {
    CAM_UNUSED(d); CAM_UNUSED(f);
}

/* ------------------------------------------------------------------ */
/* CloseDevice / FreeDeviceHandle / Deinitialize                       */
/* ------------------------------------------------------------------ */

static void ANDROIDCAMERA_CloseDevice(camDevice_t *device)
{
    if (!device || !device->hidden) return;
    AndroidPrivateData *priv = (AndroidPrivateData *)device->hidden;

    if (priv->capture_session) pACameraCaptureSession_close(priv->capture_session);
    if (priv->request)         pACaptureRequest_free(priv->request);
    if (priv->output_target)   pACameraOutputTarget_free(priv->output_target);
    if (priv->session_output_container)
        pACaptureSessionOutputContainer_free(priv->session_output_container);
    if (priv->session_output)  pACaptureSessionOutput_free(priv->session_output);
    if (priv->cam_device)      pACameraDevice_close(priv->cam_device);
    if (priv->reader)          pAImageReader_delete(priv->reader);

    free(priv);
    device->hidden = NULL;
}

static void ANDROIDCAMERA_FreeDeviceHandle(camDevice_t *device)
{
    if (device && device->handle) { free(device->handle); device->handle = NULL; }
}

static void ANDROIDCAMERA_Deinitialize(void)
{
    if (s_cam_manager) { pACameraManager_delete(s_cam_manager); s_cam_manager = NULL; }
    if (s_libcamera)   { dlclose(s_libcamera); s_libcamera = NULL; }
    if (s_libmedia)    { dlclose(s_libmedia);  s_libmedia  = NULL; }
}

/* ------------------------------------------------------------------ */
/* Bootstrap                                                           */
/* ------------------------------------------------------------------ */

static bool ANDROIDCAMERA_Init(camDriverImpl *impl)
{
    if (!load_camera_libs()) return false;

    impl->DetectDevices             = ANDROIDCAMERA_DetectDevices;
    impl->OpenDevice                = ANDROIDCAMERA_OpenDevice;
    impl->CloseDevice               = ANDROIDCAMERA_CloseDevice;
    impl->WaitDevice                = ANDROIDCAMERA_WaitDevice;
    impl->AcquireFrame              = ANDROIDCAMERA_AcquireFrame;
    impl->ReleaseFrame              = ANDROIDCAMERA_ReleaseFrame;
    impl->FreeDeviceHandle          = ANDROIDCAMERA_FreeDeviceHandle;
    impl->Deinitialize              = ANDROIDCAMERA_Deinitialize;
    impl->ProvidesOwnCallbackThread = true;
    return true;
}

camBootstrap ANDROIDCAMERA_bootstrap = {
    "android",
    "Android Camera2 NDK",
    ANDROIDCAMERA_Init,
    false
};

#endif /* __ANDROID__ */
