/*
 * camerac_sample.c  –  minimal example showing the camerac API.
 *
 * Compile (desktop):
 *   cc -o camerac_sample camerac_sample.c -I../include -L../libs/... -lcamerac
 */

#include "camerac.h"
#include <stdio.h>
#include <string.h>
#include <time.h>

static int s_frame_count = 0;

static void on_frame(camDevice device, const camFrame *frame, void *userdata)
{
    (void)userdata;
    s_frame_count++;
    if (s_frame_count % 30 == 0) {
        printf("[frame %4d]  %dx%d  fmt=%-8s  pitch=%d  ts=%llu ns  rot=%.0f°",
               s_frame_count,
               frame->width, frame->height,
               cam_pixel_format_name(frame->format),
               frame->pitch,
               (unsigned long long)frame->timestamp_ns,
               (double)frame->rotation);

        /* For bi-planar formats (NV12, P010, NV21) both planes are populated.
         * plane 0 = Y (luma):  frame->data  / frame->pitch
         * plane 1 = UV (chroma): frame->data2 / frame->pitch2  */
        if (frame->data2) {
            printf("  [biplanar: UV pitch=%d]", frame->pitch2);
        }
        printf("\n");
    }
}

static void on_permission(camDevice device, camPermission perm, void *userdata)
{
    (void)device; (void)userdata;
    switch (perm) {
        case CAM_PERMISSION_APPROVED:
            printf("[permission] Approved – frames will start arriving.\n");
            break;
        case CAM_PERMISSION_DENIED:
            printf("[permission] Denied.\n");
            break;
        default:
            break;
    }
}

int main(void)
{
    printf("camerac sample – backend: %s\n",
           cam_get_backend() ? cam_get_backend() : "(not initialized)");

    if (!cam_init()) {
        fprintf(stderr, "cam_init failed: %s\n", cam_get_error());
        return 1;
    }

    printf("Backend: %s\n", cam_get_backend());

    int count = cam_get_device_count();
    printf("Found %d camera(s):\n\n", count);

    for (int i = 0; i < count; i++) {
        camDeviceInfo info;
        if (!cam_get_device_info(i, &info)) continue;

        printf("  [%d] %s  (id: %s)  position: %s\n",
               i, info.name, info.device_id,
               cam_position_name(info.position));

        for (int s = 0; s < info.num_specs && s < 5; s++) {
            const camSpec *sp = &info.specs[s];
            printf("       spec %d: %dx%d @ %d/%d fps  %s\n",
                   s, sp->width, sp->height,
                   sp->fps_numerator, sp->fps_denominator,
                   cam_pixel_format_name(sp->format));
        }
        cam_free_device_info(&info);
        printf("\n");
    }

    if (count == 0) {
        printf("No cameras found – exiting.\n");
        cam_shutdown();
        return 0;
    }

    /* Open the first camera at a default spec */
    camSpec req = { 1280, 720, 30, 1, CAM_PIXEL_FORMAT_NV12 };
    camDevice dev = cam_open(0, &req, on_frame, NULL);
    if (!dev) {
        fprintf(stderr, "cam_open failed: %s\n", cam_get_error());
        cam_shutdown();
        return 1;
    }

    cam_set_permission_callback(dev, on_permission, NULL);

    printf("Capturing 300 frames (or until stopped)…\n");

    /* Simulate a simple main loop */
    while (s_frame_count < 300) {
        cam_update(); /* dispatch permission callbacks */

#if defined(_WIN32)
        Sleep(16);
#elif defined(__EMSCRIPTEN__)
        /* Emscripten doesn't use a blocking loop */
        break;
#else
        struct timespec ts = { 0, 16000000L }; /* 16 ms */
        nanosleep(&ts, NULL);
#endif
    }

    printf("Captured %d frames.\n", s_frame_count);

    camSpec actual;
    if (cam_get_actual_spec(dev, &actual)) {
        printf("Actual spec: %dx%d @ %d/%d fps  %s\n",
               actual.width, actual.height,
               actual.fps_numerator, actual.fps_denominator,
               cam_pixel_format_name(actual.format));
    }

    cam_close(dev);
    cam_shutdown();

    printf("Done.\n");
    return 0;
}
