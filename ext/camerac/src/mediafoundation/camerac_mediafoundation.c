/*
 * camerac_mediafoundation.c  –  Windows backend using Media Foundation
 *
 * Requires linking: mf mfplat mfreadwrite mfuuid ole32 oleaut32
 */

#include "../camerac_internal.h"

#if defined(_WIN32)

#define COBJMACROS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <mfobjects.h>

#include <strsafe.h>

#pragma comment(lib, "mf.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "ole32.lib")

/* Known GUIDs */
static const GUID CAM_MFMediaType_Video =
    { 0x73646976, 0x0000, 0x0010,
      { 0x80,0x00,0x00,0xAA,0x00,0x38,0x9B,0x71 } };

static const GUID CAM_MF_MT_MAJOR_TYPE =
    { 0x48eba18e,0xf8c9,0x4687,
      { 0xbf,0x11,0x0a,0x74,0xc9,0xf9,0x6a,0x8f } };

static const GUID CAM_MF_MT_SUBTYPE =
    { 0xf7e34c9a,0x42e8,0x4714,
      { 0xb7,0x4b,0xcb,0x29,0xd7,0x2c,0x35,0xe5 } };

static const GUID CAM_MF_MT_FRAME_SIZE =
    { 0x1652c33d,0xd6b2,0x4012,
      { 0xb8,0x34,0x72,0x03,0x08,0x49,0xa3,0x7d } };

static const GUID CAM_MF_MT_FRAME_RATE =
    { 0xc459a2e8,0x3d2c,0x4e44,
      { 0xb1,0x32,0xfe,0xe5,0x15,0x6c,0x7b,0xb0 } };

static const GUID CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE =
    { 0xc60ac5fe,0x252a,0x478f,
      { 0xa0,0xef,0xbc,0x8f,0xa5,0xf7,0xca,0xd3 } };

static const GUID CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID =
    { 0x8ac3587a,0x4ae7,0x42d8,
      { 0x99,0xe0,0x0a,0x60,0x13,0xee,0xf9,0x0f } };

static const GUID CAM_MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME =
    { 0x60d0e559,0x52f8,0x4fa2,
      { 0xbb,0xce,0xac,0xdb,0x34,0xa8,0xec,0x01 } };

static const GUID CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK =
    { 0x58f0aad8,0x22bf,0x4f8a,
      { 0xbb,0x3d,0xd2,0xc4,0x97,0x8c,0x6e,0x2f } };

/* Pixel format map */
#define DECLARE_GUID_LOCAL(name, l, w1, w2, ...) \
    static const GUID name = { l, w1, w2, { __VA_ARGS__ } }

DECLARE_GUID_LOCAL(CAM_MFVideoFormat_RGB24,  20, 0, 0x0010, 0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71);
DECLARE_GUID_LOCAL(CAM_MFVideoFormat_RGB32,  22, 0, 0x0010, 0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71);
DECLARE_GUID_LOCAL(CAM_MFVideoFormat_ARGB32, 21, 0, 0x0010, 0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71);
DECLARE_GUID_LOCAL(CAM_MFVideoFormat_RGB565, 23, 0, 0x0010, 0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71);
DECLARE_GUID_LOCAL(CAM_MFVideoFormat_RGB555, 24, 0, 0x0010, 0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71);

/* YUV FourCC GUIDs */
#define MF_FOURCC_GUID(cc) \
    { (DWORD)(cc), 0x0000, 0x0010, {0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71} }

static camPixelFormat mf_guid_to_pixel_format(const GUID *subtype)
{
    if (IsEqualGUID(subtype, &CAM_MFVideoFormat_RGB24))  return CAM_PIXEL_FORMAT_RGB24;
    if (IsEqualGUID(subtype, &CAM_MFVideoFormat_RGB32))  return CAM_PIXEL_FORMAT_XRGB8888;
    if (IsEqualGUID(subtype, &CAM_MFVideoFormat_ARGB32)) return CAM_PIXEL_FORMAT_ARGB32;
    if (IsEqualGUID(subtype, &CAM_MFVideoFormat_RGB565)) return CAM_PIXEL_FORMAT_RGB565;
    if (IsEqualGUID(subtype, &CAM_MFVideoFormat_RGB555)) return CAM_PIXEL_FORMAT_XRGB1555;

    /* YUV formats identified by FourCC stored in subtype.Data1 */
#define CHECK_FOURCC(cc, fmt) \
    do { GUID g = MF_FOURCC_GUID(cc); \
         if (IsEqualGUID(subtype, &g)) return (fmt); } while(0)

    CHECK_FOURCC(MAKEFOURCC('Y','U','Y','2'), CAM_PIXEL_FORMAT_YUY2);
    CHECK_FOURCC(MAKEFOURCC('U','Y','V','Y'), CAM_PIXEL_FORMAT_UYVY);
    CHECK_FOURCC(MAKEFOURCC('Y','V','1','2'), CAM_PIXEL_FORMAT_YV12);
    CHECK_FOURCC(MAKEFOURCC('I','Y','U','V'), CAM_PIXEL_FORMAT_IYUV);
    CHECK_FOURCC(MAKEFOURCC('N','V','1','2'), CAM_PIXEL_FORMAT_NV12);
    CHECK_FOURCC(MAKEFOURCC('M','J','P','G'), CAM_PIXEL_FORMAT_MJPEG);
#undef CHECK_FOURCC

    return CAM_PIXEL_FORMAT_UNKNOWN;
}

/* ------------------------------------------------------------------ */
/* Device handle                                                       */
/* ------------------------------------------------------------------ */

typedef struct MFDeviceHandle {
    WCHAR friendly_name[256];
    WCHAR symbolic_link[512];
} MFDeviceHandle;

/* ------------------------------------------------------------------ */
/* Private per-device data                                             */
/* ------------------------------------------------------------------ */

typedef struct MFPrivateData {
    IMFSourceReader *reader;
    void            *frame_buf;
    DWORD            frame_buf_size;
} MFPrivateData;

/* ------------------------------------------------------------------ */
/* DetectDevices                                                       */
/* ------------------------------------------------------------------ */

static void MEDIAFOUNDATION_DetectDevices(void)
{
    IMFAttributes *attrs = NULL;
    MFCreateAttributes(&attrs, 1);
    IMFAttributes_SetGUID(attrs, &CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                          &CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);

    IMFActivate **devices  = NULL;
    UINT32       dev_count = 0;
    MFEnumDeviceSources(attrs, &devices, &dev_count);
    IMFAttributes_Release(attrs);

    for (UINT32 i = 0; i < dev_count; i++) {
        IMFActivate *activate = devices[i];

        WCHAR fname[256] = {0};
        UINT32 len = 0;
        IMFAttributes_GetString(activate, &CAM_MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME,
                                fname, _countof(fname), &len);

        WCHAR symlink[512] = {0};
        IMFAttributes_GetString(activate,
            &CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
            symlink, _countof(symlink), &len);

        /* Convert friendly name to UTF-8 */
        char name_utf8[256] = {0};
        WideCharToMultiByte(CP_UTF8, 0, fname, -1, name_utf8, sizeof(name_utf8), NULL, NULL);

        char symlink_utf8[512] = {0};
        WideCharToMultiByte(CP_UTF8, 0, symlink, -1, symlink_utf8, sizeof(symlink_utf8), NULL, NULL);

        /* Enumerate formats by opening source reader briefly */
        IMFMediaSource *source = NULL;
        IMFActivate_ActivateObject(activate, &IID_IMFMediaSource, (void **)&source);

        camSpec *specs = NULL;
        int num_specs = 0;

        if (source) {
            IMFAttributes *reader_attrs = NULL;
            MFCreateAttributes(&reader_attrs, 1);

            IMFSourceReader *reader = NULL;
            MFCreateSourceReaderFromMediaSource(source, reader_attrs, &reader);
            IMFAttributes_Release(reader_attrs);

            if (reader) {
                for (DWORD si = 0; ; si++) {
                    IMFMediaType *type = NULL;
                    HRESULT hr = IMFSourceReader_GetNativeMediaType(
                        reader, MF_SOURCE_READER_FIRST_VIDEO_STREAM, si, &type);
                    if (FAILED(hr)) break;

                    GUID major = {0}, sub = {0};
                    IMFMediaType_GetGUID(type, &CAM_MF_MT_MAJOR_TYPE, &major);
                    IMFMediaType_GetGUID(type, &CAM_MF_MT_SUBTYPE,    &sub);

                    if (IsEqualGUID(&major, &CAM_MFMediaType_Video)) {
                        camPixelFormat pf = mf_guid_to_pixel_format(&sub);
                        if (pf != CAM_PIXEL_FORMAT_UNKNOWN) {
                            UINT64 frame_size = 0, frame_rate = 0;
                            IMFMediaType_GetUINT64(type, &CAM_MF_MT_FRAME_SIZE, &frame_size);
                            IMFMediaType_GetUINT64(type, &CAM_MF_MT_FRAME_RATE, &frame_rate);

                            UINT32 w = (UINT32)(frame_size >> 32);
                            UINT32 h = (UINT32)(frame_size & 0xFFFFFFFF);
                            UINT32 fps_n = (UINT32)(frame_rate >> 32);
                            UINT32 fps_d = (UINT32)(frame_rate & 0xFFFFFFFF);
                            if (fps_d == 0) fps_d = 1;

                            specs = (camSpec *)realloc(specs,
                                sizeof(camSpec) * (size_t)(num_specs + 1));
                            camSpec *s = &specs[num_specs++];
                            s->width           = (int)w;
                            s->height          = (int)h;
                            s->fps_numerator   = (int)fps_n;
                            s->fps_denominator = (int)fps_d;
                            s->format          = pf;
                        }
                    }
                    IMFMediaType_Release(type);
                }
                IMFSourceReader_Release(reader);
            }
            IMFMediaSource_Release(source);
        }

        MFDeviceHandle *handle = (MFDeviceHandle *)calloc(1, sizeof(MFDeviceHandle));
        memcpy(handle->friendly_name, fname, sizeof(fname));
        memcpy(handle->symbolic_link, symlink, sizeof(symlink));

        cam_add_device(name_utf8, symlink_utf8,
                       CAM_POSITION_UNKNOWN,
                       num_specs, specs, handle);
        free(specs);
        IMFActivate_Release(activate);
    }

    CoTaskMemFree(devices);
}

/* ------------------------------------------------------------------ */
/* OpenDevice                                                          */
/* ------------------------------------------------------------------ */

static bool MEDIAFOUNDATION_OpenDevice(camDevice_t *device, const camSpec *spec)
{
    MFDeviceHandle *handle = (MFDeviceHandle *)device->handle;

    /* Create attributes referencing the symbolic link */
    IMFAttributes *attrs = NULL;
    MFCreateAttributes(&attrs, 2);
    IMFAttributes_SetGUID(attrs, &CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                          &CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
    IMFAttributes_SetString(attrs,
        &CAM_MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
        handle->symbolic_link);

    IMFMediaSource *source = NULL;
    HRESULT hr = MFCreateDeviceSource(attrs, &source);
    IMFAttributes_Release(attrs);

    if (FAILED(hr)) {
        return cam_set_error("MFCreateDeviceSource failed (0x%08X)", (unsigned)hr);
    }

    IMFAttributes *reader_attrs = NULL;
    MFCreateAttributes(&reader_attrs, 1);

    IMFSourceReader *reader = NULL;
    hr = MFCreateSourceReaderFromMediaSource(source, reader_attrs, &reader);
    IMFAttributes_Release(reader_attrs);
    IMFMediaSource_Release(source);

    if (FAILED(hr)) {
        return cam_set_error("MFCreateSourceReaderFromMediaSource failed (0x%08X)", (unsigned)hr);
    }

    /* Set the requested media type */
    if (spec && spec->format != CAM_PIXEL_FORMAT_UNKNOWN) {
        IMFMediaType *type = NULL;
        MFCreateMediaType(&type);
        IMFMediaType_SetGUID(type, &CAM_MF_MT_MAJOR_TYPE, &CAM_MFMediaType_Video);

        /* Find matching subtype */
        for (DWORD si = 0; ; si++) {
            IMFMediaType *native = NULL;
            if (FAILED(IMFSourceReader_GetNativeMediaType(
                    reader, MF_SOURCE_READER_FIRST_VIDEO_STREAM, si, &native)))
                break;

            GUID sub = {0};
            IMFMediaType_GetGUID(native, &CAM_MF_MT_SUBTYPE, &sub);
            if (mf_guid_to_pixel_format(&sub) == spec->format) {
                IMFSourceReader_SetCurrentMediaType(
                    reader, MF_SOURCE_READER_FIRST_VIDEO_STREAM, NULL, native);
                device->actual_spec = *spec;
                IMFMediaType_Release(native);
                break;
            }
            IMFMediaType_Release(native);
        }
        IMFMediaType_Release(type);
    }

    /* On Windows permission is always granted (user is prompted by system) */
    cam_permission_outcome(device, CAM_PERMISSION_APPROVED);

    MFPrivateData *priv = (MFPrivateData *)calloc(1, sizeof(MFPrivateData));
    priv->reader = reader;
    device->hidden = priv;

    return true;
}

/* ------------------------------------------------------------------ */
/* WaitDevice / AcquireFrame / ReleaseFrame                            */
/* ------------------------------------------------------------------ */

static bool MEDIAFOUNDATION_WaitDevice(camDevice_t *device)
{
    CAM_UNUSED(device);
    return true; /* ReadSample itself is blocking */
}

static camFrameResult MEDIAFOUNDATION_AcquireFrame(camDevice_t *device,
                                                    camFrame    *frame)
{
    MFPrivateData *priv = (MFPrivateData *)device->hidden;
    if (!priv || !priv->reader) return CAM_FRAME_ERROR;

    DWORD      stream_flags = 0;
    LONGLONG   timestamp    = 0;
    IMFSample *sample       = NULL;

    HRESULT hr = IMFSourceReader_ReadSample(
        priv->reader,
        MF_SOURCE_READER_FIRST_VIDEO_STREAM,
        0, NULL, &stream_flags, &timestamp, &sample);

    if (FAILED(hr) || !sample) return CAM_FRAME_SKIP;

    if (stream_flags & MF_SOURCE_READERF_ERROR) {
        if (sample) IMFSample_Release(sample);
        return CAM_FRAME_ERROR;
    }

    IMFMediaBuffer *buffer = NULL;
    hr = IMFSample_ConvertToContiguousBuffer(sample, &buffer);
    if (FAILED(hr)) {
        IMFSample_Release(sample);
        return CAM_FRAME_SKIP;
    }

    BYTE  *data       = NULL;
    DWORD  max_length = 0, cur_length = 0;
    hr = IMFMediaBuffer_Lock(buffer, &data, &max_length, &cur_length);

    if (SUCCEEDED(hr)) {
        if (cur_length > priv->frame_buf_size) {
            free(priv->frame_buf);
            priv->frame_buf      = malloc(cur_length);
            priv->frame_buf_size = cur_length;
        }
        memcpy(priv->frame_buf, data, cur_length);
        IMFMediaBuffer_Unlock(buffer);

        frame->data         = priv->frame_buf;
        frame->timestamp_ns = (uint64_t)(timestamp * 100); /* 100-ns units -> ns */
        frame->pitch        = device->actual_spec.width *
                              cam_bytes_per_pixel(device->actual_spec.format);
    }

    IMFMediaBuffer_Release(buffer);
    IMFSample_Release(sample);

    return (SUCCEEDED(hr) && frame->data) ? CAM_FRAME_READY : CAM_FRAME_SKIP;
}

static void MEDIAFOUNDATION_ReleaseFrame(camDevice_t *device, camFrame *frame)
{
    CAM_UNUSED(device);
    frame->data  = NULL;
    frame->pitch = 0;
}

/* ------------------------------------------------------------------ */
/* CloseDevice / FreeDeviceHandle / Deinitialize                       */
/* ------------------------------------------------------------------ */

static void MEDIAFOUNDATION_CloseDevice(camDevice_t *device)
{
    if (!device || !device->hidden) return;
    MFPrivateData *priv = (MFPrivateData *)device->hidden;
    if (priv->reader) {
        IMFSourceReader_Release(priv->reader);
        priv->reader = NULL;
    }
    free(priv->frame_buf);
    free(priv);
    device->hidden = NULL;
}

static void MEDIAFOUNDATION_FreeDeviceHandle(camDevice_t *device)
{
    if (device && device->handle) {
        free(device->handle);
        device->handle = NULL;
    }
}

static void MEDIAFOUNDATION_Deinitialize(void)
{
    MFShutdown();
}

/* ------------------------------------------------------------------ */
/* Bootstrap                                                           */
/* ------------------------------------------------------------------ */

static bool MEDIAFOUNDATION_Init(camDriverImpl *impl)
{
    HRESULT hr = MFStartup(MF_VERSION, MFSTARTUP_NOSOCKET);
    if (FAILED(hr)) return false;

    impl->DetectDevices             = MEDIAFOUNDATION_DetectDevices;
    impl->OpenDevice                = MEDIAFOUNDATION_OpenDevice;
    impl->CloseDevice               = MEDIAFOUNDATION_CloseDevice;
    impl->WaitDevice                = MEDIAFOUNDATION_WaitDevice;
    impl->AcquireFrame              = MEDIAFOUNDATION_AcquireFrame;
    impl->ReleaseFrame              = MEDIAFOUNDATION_ReleaseFrame;
    impl->FreeDeviceHandle          = MEDIAFOUNDATION_FreeDeviceHandle;
    impl->Deinitialize              = MEDIAFOUNDATION_Deinitialize;
    impl->ProvidesOwnCallbackThread = false;
    return true;
}

camBootstrap MEDIAFOUNDATION_bootstrap = {
    "mediafoundation",
    "Windows Media Foundation",
    MEDIAFOUNDATION_Init,
    false
};

#endif /* _WIN32 */
