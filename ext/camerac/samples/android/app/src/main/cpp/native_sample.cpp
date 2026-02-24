/*
 * native_sample.cpp  –  JNI bridge for the camerac Android sample.
 *
 * Called from MainActivity once CAMERA permission is granted.
 * All output goes to Logcat under the tag "CameracSample".
 */

#include <jni.h>
#include <android/log.h>
#include <unistd.h>   // usleep
#include <atomic>

#include "camerac.h"

#define TAG  "CameracSample"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

// ---------------------------------------------------------------------------

static std::atomic<int> s_frame_count{0};

static void on_frame(camDevice /*device*/, const camFrame *frame, void * /*ud*/)
{
    int n = ++s_frame_count;
    if (n % 30 == 1) {   // first of each 30-frame batch
        LOGI("[frame %4d]  %dx%d  fmt=%-8s  pitch=%d  ts=%llu ns  rot=%.0f°%s",
             n,
             frame->width, frame->height,
             cam_pixel_format_name(frame->format),
             frame->pitch,
             (unsigned long long)frame->timestamp_ns,
             (double)frame->rotation,
             frame->data2 ? "  [biplanar]" : "");
    }
}

static void on_permission(camDevice /*device*/, camPermission perm, void * /*ud*/)
{
    switch (perm) {
        case CAM_PERMISSION_APPROVED:
            LOGI("[permission] Approved – frames will start arriving.");
            break;
        case CAM_PERMISSION_DENIED:
            LOGE("[permission] Denied – cannot capture.");
            break;
        default:
            break;
    }
}

// ---------------------------------------------------------------------------
// JNI entry point: called from MainActivity on a background thread.
// ---------------------------------------------------------------------------
extern "C"
JNIEXPORT void JNICALL
Java_com_camerac_sample_MainActivity_runCameracTest(JNIEnv * /*env*/, jobject /*thiz*/)
{
    LOGI("=================================================");
    LOGI("camerac Android sample  (backend: %s)",
         cam_get_backend() ? cam_get_backend() : "not yet initialized");
    LOGI("=================================================");

    // 1. Initialise ----------------------------------------------------------
    if (!cam_init()) {
        LOGE("cam_init() failed: %s", cam_get_error());
        return;
    }
    LOGI("Backend: %s", cam_get_backend());

    // 2. Enumerate devices ---------------------------------------------------
    int count = cam_get_device_count();
    LOGI("Found %d camera(s):", count);

    for (int i = 0; i < count; i++) {
        camDeviceInfo info;
        if (!cam_get_device_info(i, &info)) continue;

        LOGI("  [%d] %-30s  id: %s  pos: %s",
             i, info.name, info.device_id, cam_position_name(info.position));

        for (int s = 0; s < info.num_specs && s < 5; s++) {
            const camSpec *sp = &info.specs[s];
            LOGI("      spec %d: %4dx%-4d  %2d/%d fps  %s",
                 s,
                 sp->width, sp->height,
                 sp->fps_numerator, sp->fps_denominator,
                 cam_pixel_format_name(sp->format));
        }
        cam_free_device_info(&info);
    }

    if (count == 0) {
        LOGW("No cameras found – shutting down.");
        cam_shutdown();
        return;
    }

    // 3. Open the first back camera ------------------------------------------
    s_frame_count = 0;
    camSpec req = { 1280, 720, 30, 1, CAM_PIXEL_FORMAT_NV12 };
    camDevice dev = cam_open(0, &req, on_frame, nullptr);
    if (!dev) {
        LOGE("cam_open() failed: %s", cam_get_error());
        cam_shutdown();
        return;
    }
    LOGI("Camera opened – capturing ~5 s …");
    cam_set_permission_callback(dev, on_permission, nullptr);

    // 4. Run the capture loop for ~5 s / 300 frames --------------------------
    while (s_frame_count < 300) {
        cam_update();          // dispatch permission callbacks
        usleep(16000);         // ~16 ms → ~60 Hz poll
    }

    // 5. Report actual spec --------------------------------------------------
    camSpec actual{};
    if (cam_get_actual_spec(dev, &actual)) {
        LOGI("Actual spec: %dx%d  %d/%d fps  %s",
             actual.width, actual.height,
             actual.fps_numerator, actual.fps_denominator,
             cam_pixel_format_name(actual.format));
    }
    LOGI("Captured %d frames – test PASSED.", (int)s_frame_count);

    // 6. Tear down -----------------------------------------------------------
    cam_close(dev);
    cam_shutdown();
    LOGI("=================================================");
    LOGI("camerac sample done.");
    LOGI("=================================================");
}
