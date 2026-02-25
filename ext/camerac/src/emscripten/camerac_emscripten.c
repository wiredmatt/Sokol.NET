/*
 * camerac_emscripten.c  –  Emscripten (browser) backend
 *
 * Uses the browser's getUserMedia API through EM_ASM.
 * JavaScript entry point expects Module['SDL3'] compatibility layer
 * or a dedicated Module['camerac'] namespace.
 *
 * The app must call cam_open() after cam_init(); the browser
 * will then prompt for camera permission.
 */

#include "../camerac_internal.h"

#if defined(__EMSCRIPTEN__)

#include <emscripten/emscripten.h>

/* clang-format off */

/* ------------------------------------------------------------------ */
/* Private per-device data                                             */
/* ------------------------------------------------------------------ */

typedef struct EmscriptenPrivateData {
    int device_js_id;   /* index into JS-side camera array          */
    int width;
    int height;
    void *rgba_buf;     /* reused RGBA scratch buffer               */
} EmscriptenPrivateData;

/* ------------------------------------------------------------------ */
/* JS glue: injected once at DetectDevices()                           */
/* ------------------------------------------------------------------ */

static bool s_js_init = false;

static void inject_js(void)
{
    if (s_js_init) return;
    s_js_init = true;

    EM_ASM({
        if (typeof Module['_camerac'] === 'undefined') {
            /* Avoid JS object literals with commas: C pre-processor treats
             * commas inside {} as macro argument separators.           */
            var c = {};
            c.devices = [];
            c.streams = {};
            Module['_camerac'] = c;
        }
    });
}

/* ------------------------------------------------------------------ */
/* DetectDevices                                                       */
/* ------------------------------------------------------------------ */

static void EMSCRIPTENCAMERA_DetectDevices(void)
{
    inject_js();

    /* On Emscripten we register a placeholder device representing the
     * browser's default camera.  The real device list is not available
     * synchronously (it requires an async enumerateDevices() call after
     * permission is granted).  We add a single "Default Camera" device. */
    camSpec spec = { 1280, 720, 30, 1, CAM_PIXEL_FORMAT_RGBA32 };

    int *handle = (int *)malloc(sizeof(int));
    *handle = 0;
    cam_add_device("Default Camera", "browser-default",
                   CAM_POSITION_UNKNOWN, 1, &spec, handle);
}

/* ------------------------------------------------------------------ */
/* C entry point called back from JS permission handler                */
/* ------------------------------------------------------------------ */

EMSCRIPTEN_KEEPALIVE
void camerac_emscripten_permission_outcome(camDevice_t *device,
                                            int          approved,
                                            int          w,
                                            int          h)
{
    EmscriptenPrivateData *priv =
        device ? (EmscriptenPrivateData *)device->hidden : NULL;
    if (!priv) return;

    if (approved) {
        priv->width  = w ? w : 1280;
        priv->height = h ? h : 720;
        device->actual_spec.width  = priv->width;
        device->actual_spec.height = priv->height;

        free(priv->rgba_buf);
        priv->rgba_buf = malloc((size_t)priv->width *
                                (size_t)priv->height * 4);
        cam_permission_outcome(device, CAM_PERMISSION_APPROVED);
    } else {
        cam_permission_outcome(device, CAM_PERMISSION_DENIED);
    }
}

/* ------------------------------------------------------------------ */
/* OpenDevice                                                          */
/* ------------------------------------------------------------------ */

static bool EMSCRIPTENCAMERA_OpenDevice(camDevice_t *device,
                                         const camSpec *spec)
{
    int w = spec ? spec->width  : 1280;
    int h = spec ? spec->height : 720;

    EmscriptenPrivateData *priv =
        (EmscriptenPrivateData *)calloc(1, sizeof(EmscriptenPrivateData));
    priv->device_js_id = 0;
    priv->width        = w;
    priv->height       = h;
    priv->rgba_buf     = malloc((size_t)w * (size_t)h * 4);
    device->hidden     = priv;

    device->actual_spec.width  = w;
    device->actual_spec.height = h;
    device->actual_spec.format = CAM_PIXEL_FORMAT_RGBA32;

    /* Kick off getUserMedia in JS.
     * Rules for EM_ASM: the C pre-processor treats commas inside {} as
     * macro argument separators (only () and [] protect commas).  So we
     * must NOT use multi-property JS object literals {a:1, b:2} at the
     * top level of an EM_ASM block – build objects with property assignment
     * instead, and keep all commas inside function-call parentheses.    */
    MAIN_THREAD_EM_ASM({
        var devPtr = $0;
        var w      = $1;
        var h      = $2;

        if (typeof Module['_camerac'] === 'undefined') {
            var c = {};
            c.devices = [];
            c.streams = {};
            Module['_camerac'] = c;
        }

        /* Build constraints without top-level commas in {} */
        var vc = {};
        vc.width  = {ideal: w};
        vc.height = {ideal: h};
        var constraints = {};
        constraints.video = vc;
        constraints.audio = false;

        navigator.mediaDevices.getUserMedia(constraints)
            .then(function(stream) {
                var video = document.createElement('video');
                video.srcObject = stream;
                video.play();

                var canvas = document.createElement('canvas');
                canvas.width  = w;
                canvas.height = h;
                var ctx = canvas.getContext('2d');

                Module['_camerac'].stream = stream;
                Module['_camerac'].video  = video;
                Module['_camerac'].canvas = canvas;
                Module['_camerac'].ctx2d  = ctx;

                video.addEventListener('loadedmetadata', function() {
                    Module['_camerac_emscripten_permission_outcome'](
                        devPtr, 1, video.videoWidth, video.videoHeight);
                });
            })
            .catch(function(err) {
                console.error('[camerac] getUserMedia error:', err);
                Module['_camerac_emscripten_permission_outcome'](devPtr, 0, 0, 0);
            });
    }, (int)(uintptr_t)device, w, h);

    return true;
}

/* ------------------------------------------------------------------ */
/* WaitDevice / AcquireFrame / ReleaseFrame                            */
/* ------------------------------------------------------------------ */

static bool EMSCRIPTENCAMERA_WaitDevice(camDevice_t *device)
{
    CAM_UNUSED(device);
    return false; /* no background thread: frames are driven by the JS tick */
}

static camFrameResult EMSCRIPTENCAMERA_AcquireFrame(camDevice_t *device,
                                                     camFrame    *frame)
{
    EmscriptenPrivateData *priv = (EmscriptenPrivateData *)device->hidden;
    if (!priv || !priv->rgba_buf) return CAM_FRAME_SKIP;

    if (device->permission != CAM_PERMISSION_APPROVED) return CAM_FRAME_SKIP;

    int w = priv->width;
    int h = priv->height;

    int ok = MAIN_THREAD_EM_ASM_INT({
        const w    = $0;
        const h    = $1;
        const rgba = $2;
        const cam  = Module['_camerac'];
        if (!cam || !cam.ctx2d || !cam.video) return 0;
        cam.ctx2d.drawImage(cam.video, 0, 0, w, h);
        const img = cam.ctx2d.getImageData(0, 0, w, h).data;
        HEAPU8.set(img, rgba);
        return 1;
    }, w, h, priv->rgba_buf);

    if (!ok) return CAM_FRAME_SKIP;

    frame->data         = priv->rgba_buf;
    frame->pitch        = w * 4;
    frame->timestamp_ns = 0; /* best-effort */

    return CAM_FRAME_READY;
}

/* ReleaseFrame: data lives in priv->rgba_buf, no action needed */
static void EMSCRIPTENCAMERA_ReleaseFrame(camDevice_t *device, camFrame *frame)
{
    CAM_UNUSED(device);
    frame->data  = NULL;
    frame->pitch = 0;
}

/* ------------------------------------------------------------------ */
/* CloseDevice / FreeDeviceHandle / Deinitialize                       */
/* ------------------------------------------------------------------ */

static void EMSCRIPTENCAMERA_CloseDevice(camDevice_t *device)
{
    if (!device || !device->hidden) return;
    EmscriptenPrivateData *priv = (EmscriptenPrivateData *)device->hidden;

    MAIN_THREAD_EM_ASM({
        const cam = Module['_camerac'];
        if (!cam) return;
        if (cam.stream) {
            cam.stream.getTracks().forEach(function(t) { t.stop(); });
        }
        Module['_camerac'] = {};
    });

    free(priv->rgba_buf);
    free(priv);
    device->hidden = NULL;
}

static void EMSCRIPTENCAMERA_FreeDeviceHandle(camDevice_t *device)
{
    if (device && device->handle) { free(device->handle); device->handle = NULL; }
}

static void EMSCRIPTENCAMERA_Deinitialize(void) {}

/* ------------------------------------------------------------------ */
/* Bootstrap                                                           */
/* ------------------------------------------------------------------ */

static bool EMSCRIPTENCAMERA_Init(camDriverImpl *impl)
{
    impl->DetectDevices             = EMSCRIPTENCAMERA_DetectDevices;
    impl->OpenDevice                = EMSCRIPTENCAMERA_OpenDevice;
    impl->CloseDevice               = EMSCRIPTENCAMERA_CloseDevice;
    impl->WaitDevice                = EMSCRIPTENCAMERA_WaitDevice;
    impl->AcquireFrame              = EMSCRIPTENCAMERA_AcquireFrame;
    impl->ReleaseFrame              = EMSCRIPTENCAMERA_ReleaseFrame;
    impl->FreeDeviceHandle          = EMSCRIPTENCAMERA_FreeDeviceHandle;
    impl->Deinitialize              = EMSCRIPTENCAMERA_Deinitialize;
    impl->ProvidesOwnCallbackThread = true; /* browser drives frames */
    return true;
}

camBootstrap EMSCRIPTENCAMERA_bootstrap = {
    "emscripten",
    "Emscripten getUserMedia (browser)",
    EMSCRIPTENCAMERA_Init,
    false
};

/* clang-format on */

#endif /* __EMSCRIPTEN__ */
