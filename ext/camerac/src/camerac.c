/*
 * camerac.c  –  core implementation / device management
 */

#include "camerac_internal.h"

#include <stdarg.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

/* ------------------------------------------------------------------ */
/* Error string                                                        */
/* ------------------------------------------------------------------ */

/* Simple per-thread error string using thread_local / __thread */
#if defined(_WIN32)
  static __declspec(thread) char t_error[512];
#elif defined(__EMSCRIPTEN__)
  /* Emscripten is single-threaded */
  static char t_error[512];
#else
  static __thread char t_error[512];
#endif

bool cam_set_error(const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(t_error, sizeof(t_error), fmt, ap);
    va_end(ap);
    return false;
}

void cam_clear_error(void)
{
    t_error[0] = '\0';
}

CAMERAC_API const char *cam_get_error(void)
{
    return t_error;
}

/* ------------------------------------------------------------------ */
/* Bootstrap table – included backends                                 */
/* ------------------------------------------------------------------ */

/* Forward declarations of each platform's bootstrap struct */
#if defined(__APPLE__)
  extern camBootstrap COREMEDIA_bootstrap;
#endif
#if defined(_WIN32)
  extern camBootstrap MEDIAFOUNDATION_bootstrap;
#endif
#if defined(__linux__) && !defined(__ANDROID__)
  extern camBootstrap V4L2_bootstrap;
#endif
#if defined(__ANDROID__)
  extern camBootstrap ANDROIDCAMERA_bootstrap;
#endif
#if defined(__EMSCRIPTEN__)
  extern camBootstrap EMSCRIPTENCAMERA_bootstrap;
#endif

static const camBootstrap *const s_bootstrap[] = {
#if defined(__APPLE__)
    &COREMEDIA_bootstrap,
#endif
#if defined(_WIN32)
    &MEDIAFOUNDATION_bootstrap,
#endif
#if defined(__linux__) && !defined(__ANDROID__)
    &V4L2_bootstrap,
#endif
#if defined(__ANDROID__)
    &ANDROIDCAMERA_bootstrap,
#endif
#if defined(__EMSCRIPTEN__)
    &EMSCRIPTENCAMERA_bootstrap,
#endif
    NULL
};

/* ------------------------------------------------------------------ */
/* Global driver state                                                 */
/* ------------------------------------------------------------------ */

camDriver g_cam_driver;
static bool s_initialized = false;

/* ------------------------------------------------------------------ */
/* Capture thread entry                                                */
/* ------------------------------------------------------------------ */

#if defined(_WIN32)
static DWORD WINAPI capture_thread_proc(LPVOID arg)
{
    cam_capture_thread_run((camDevice_t *)arg);
    return 0;
}
#else
static void *capture_thread_proc(void *arg)
{
    cam_capture_thread_run((camDevice_t *)arg);
    return NULL;
}
#endif

void cam_capture_thread_run(camDevice_t *device)
{
    device->running = 1;

    while (!device->shutdown) {
        /* Wait for next frame */
        if (g_cam_driver.impl.WaitDevice) {
            if (!g_cam_driver.impl.WaitDevice(device)) {
                break;
            }
        }

        if (device->shutdown) break;

        camFrame frame;
        memset(&frame, 0, sizeof(frame));

        camFrameResult result = g_cam_driver.impl.AcquireFrame(device, &frame);

        if (result == CAM_FRAME_READY) {
            frame.width  = device->actual_spec.width;
            frame.height = device->actual_spec.height;
            frame.format = device->actual_spec.format;
            cam_deliver_frame(device, &frame);
            if (g_cam_driver.impl.ReleaseFrame) {
                g_cam_driver.impl.ReleaseFrame(device, &frame);
            }
        } else if (result == CAM_FRAME_ERROR) {
            CAM_LOG("Capture error on device '%s'", device->name);
            break;
        }
        /* CAM_FRAME_SKIP: just loop again */
    }

    device->running = 0;
}

/* ------------------------------------------------------------------ */
/* cam_add_device – called by backends during DetectDevices()          */
/* ------------------------------------------------------------------ */

camDevice_t *cam_add_device(const char    *name,
                             const char    *device_id,
                             camPosition    position,
                             int            num_specs,
                             const camSpec *specs,
                             void          *handle)
{
    camDevice_t *dev = (camDevice_t *)calloc(1, sizeof(camDevice_t));
    if (!dev) return NULL;

    cam_mutex_init(&dev->lock);

    strncpy(dev->name,      name      ? name      : "", sizeof(dev->name)      - 1);
    strncpy(dev->device_id, device_id ? device_id : "", sizeof(dev->device_id) - 1);
    dev->position   = position;
    dev->permission = CAM_PERMISSION_PENDING;
    dev->handle     = handle;

    if (num_specs > 0 && specs) {
        dev->all_specs = (camSpec *)malloc(sizeof(camSpec) * (size_t)num_specs);
        if (dev->all_specs) {
            memcpy(dev->all_specs, specs, sizeof(camSpec) * (size_t)num_specs);
            dev->num_specs = num_specs;
        }
    }

    cam_mutex_lock(&g_cam_driver.devices_lock);
    dev->next = g_cam_driver.devices;
    g_cam_driver.devices = dev;
    g_cam_driver.device_count++;
    cam_mutex_unlock(&g_cam_driver.devices_lock);

    return dev;
}

/* ------------------------------------------------------------------ */
/* cam_deliver_frame                                                   */
/* ------------------------------------------------------------------ */

void cam_deliver_frame(camDevice_t *device, const camFrame *frame)
{
    if (device->frame_cb) {
        device->frame_cb(device, frame, device->frame_userdata);
    }
}

/* ------------------------------------------------------------------ */
/* cam_permission_outcome – queued and dispatched on main thread       */
/* ------------------------------------------------------------------ */

void cam_permission_outcome(camDevice_t *device, camPermission result)
{
    camPendingPermission *p =
        (camPendingPermission *)malloc(sizeof(camPendingPermission));
    if (!p) return;
    p->device = device;
    p->result = result;
    p->next   = NULL;

    cam_mutex_lock(&g_cam_driver.pending_perms_lock);
    camPendingPermission **tail = &g_cam_driver.pending_perms;
    while (*tail) tail = &(*tail)->next;
    *tail = p;
    cam_mutex_unlock(&g_cam_driver.pending_perms_lock);
}

/* ------------------------------------------------------------------ */
/* cam_init                                                            */
/* ------------------------------------------------------------------ */

CAMERAC_API bool cam_init(void)
{
    if (s_initialized) return true;

    memset(&g_cam_driver, 0, sizeof(g_cam_driver));
    cam_mutex_init(&g_cam_driver.devices_lock);
    cam_mutex_init(&g_cam_driver.pending_perms_lock);

    /* Try each bootstrap in order */
    for (int i = 0; s_bootstrap[i] != NULL; i++) {
        if (s_bootstrap[i]->demand_only) continue;
        if (s_bootstrap[i]->init(&g_cam_driver.impl)) {
            g_cam_driver.name = s_bootstrap[i]->name;
            g_cam_driver.desc = s_bootstrap[i]->desc;
            CAM_LOG("Initialized backend: %s (%s)",
                    g_cam_driver.name, g_cam_driver.desc);
            break;
        }
    }

    if (!g_cam_driver.name) {
        cam_set_error("No camera backend available");
        cam_mutex_destroy(&g_cam_driver.devices_lock);
        cam_mutex_destroy(&g_cam_driver.pending_perms_lock);
        return false;
    }

    /* Enumerate devices */
    if (g_cam_driver.impl.DetectDevices) {
        g_cam_driver.impl.DetectDevices();
    }

    s_initialized = true;
    return true;
}

/* ------------------------------------------------------------------ */
/* cam_shutdown                                                        */
/* ------------------------------------------------------------------ */

CAMERAC_API void cam_shutdown(void)
{
    if (!s_initialized) return;

    /* Close all open devices */
    cam_mutex_lock(&g_cam_driver.devices_lock);
    camDevice_t *dev = g_cam_driver.devices;
    cam_mutex_unlock(&g_cam_driver.devices_lock);

    while (dev) {
        camDevice_t *next = dev->next;
        /* Signal shutdown and wait for thread */
        dev->shutdown = 1;
#if defined(_WIN32)
        if (dev->thread) {
            WaitForSingleObject(dev->thread, 5000);
            CloseHandle(dev->thread);
            dev->thread = NULL;
        }
#else
        if (dev->running) {
            pthread_join(dev->thread, NULL);
        }
#endif
        if (g_cam_driver.impl.CloseDevice)  g_cam_driver.impl.CloseDevice(dev);
        if (g_cam_driver.impl.FreeDeviceHandle) g_cam_driver.impl.FreeDeviceHandle(dev);

        free(dev->all_specs);
        cam_mutex_destroy(&dev->lock);
        free(dev);
        dev = next;
    }

    /* Drain pending permissions */
    cam_mutex_lock(&g_cam_driver.pending_perms_lock);
    camPendingPermission *pp = g_cam_driver.pending_perms;
    g_cam_driver.pending_perms = NULL;
    cam_mutex_unlock(&g_cam_driver.pending_perms_lock);
    while (pp) {
        camPendingPermission *next = pp->next;
        free(pp);
        pp = next;
    }

    if (g_cam_driver.impl.Deinitialize) {
        g_cam_driver.impl.Deinitialize();
    }

    cam_mutex_destroy(&g_cam_driver.devices_lock);
    cam_mutex_destroy(&g_cam_driver.pending_perms_lock);

    memset(&g_cam_driver, 0, sizeof(g_cam_driver));
    s_initialized = false;
}

/* ------------------------------------------------------------------ */
/* cam_get_backend                                                     */
/* ------------------------------------------------------------------ */

CAMERAC_API const char *cam_get_backend(void)
{
    return s_initialized ? g_cam_driver.name : NULL;
}

/* ------------------------------------------------------------------ */
/* Device enumeration                                                  */
/* ------------------------------------------------------------------ */

CAMERAC_API int cam_get_device_count(void)
{
    return s_initialized ? g_cam_driver.device_count : 0;
}

CAMERAC_API bool cam_get_device_info(int index, camDeviceInfo *out_info)
{
    if (!s_initialized || !out_info || index < 0) {
        return cam_set_error("Invalid argument");
    }

    cam_mutex_lock(&g_cam_driver.devices_lock);
    camDevice_t *dev = g_cam_driver.devices;
    /* devices are stored newest-first; reverse to match insertion order */
    /* build a temporary array for indexed access */
    camDevice_t **arr = (camDevice_t **)malloc(
        sizeof(camDevice_t *) * (size_t)g_cam_driver.device_count);
    int count = 0;
    while (dev) { arr[count++] = dev; dev = dev->next; }
    cam_mutex_unlock(&g_cam_driver.devices_lock);

    if (index >= count) {
        free(arr);
        return cam_set_error("Device index out of range");
    }

    /* reverse so index 0 = first added */
    camDevice_t *target = arr[count - 1 - index];
    free(arr);

    memset(out_info, 0, sizeof(*out_info));
    strncpy(out_info->name,      target->name,      sizeof(out_info->name)      - 1);
    strncpy(out_info->device_id, target->device_id, sizeof(out_info->device_id) - 1);
    out_info->position  = target->position;
    out_info->num_specs = target->num_specs;

    if (target->num_specs > 0 && target->all_specs) {
        camSpec *copy = (camSpec *)malloc(sizeof(camSpec) * (size_t)target->num_specs);
        if (copy) {
            memcpy(copy, target->all_specs,
                   sizeof(camSpec) * (size_t)target->num_specs);
        }
        out_info->specs = copy;
    }

    return true;
}

CAMERAC_API void cam_free_device_info(camDeviceInfo *info)
{
    if (info) {
        free((void *)info->specs);
        info->specs = NULL;
        info->num_specs = 0;
    }
}

/* ------------------------------------------------------------------ */
/* cam_open                                                            */
/* ------------------------------------------------------------------ */

CAMERAC_API camDevice cam_open(int                device_index,
                                const camSpec     *requested,
                                camFrameCallback   frame_cb,
                                void              *userdata)
{
    if (!s_initialized) {
        cam_set_error("cam_init() not called");
        return NULL;
    }

    /* Find the device by index */
    cam_mutex_lock(&g_cam_driver.devices_lock);
    camDevice_t **arr = (camDevice_t **)malloc(
        sizeof(camDevice_t *) * (size_t)g_cam_driver.device_count);
    int count = 0;
    camDevice_t *dev = g_cam_driver.devices;
    while (dev) { arr[count++] = dev; dev = dev->next; }
    cam_mutex_unlock(&g_cam_driver.devices_lock);

    if (device_index < 0 || device_index >= count) {
        free(arr);
        cam_set_error("Device index out of range");
        return NULL;
    }

    camDevice_t *target = arr[count - 1 - device_index];
    free(arr);

    /* Store callbacks */
    target->frame_cb       = frame_cb;
    target->frame_userdata = userdata;
    target->shutdown       = 0;

    /* Determine spec to use */
    camSpec spec_to_use;
    if (requested) {
        spec_to_use = *requested;
    } else if (target->num_specs > 0) {
        spec_to_use = target->all_specs[0];
    } else {
        memset(&spec_to_use, 0, sizeof(spec_to_use));
    }
    target->requested_spec = spec_to_use;
    target->actual_spec    = spec_to_use;  /* backend may override */

    if (!g_cam_driver.impl.OpenDevice(target, &spec_to_use)) {
        return NULL;
    }

    /* Start capture thread (unless backend manages its own thread) */
    if (!g_cam_driver.impl.ProvidesOwnCallbackThread) {
#if defined(_WIN32)
        target->thread = CreateThread(NULL, 0, capture_thread_proc,
                                       target, 0, NULL);
        if (!target->thread) {
            g_cam_driver.impl.CloseDevice(target);
            return cam_set_error("Failed to create capture thread"), NULL;
        }
#else
        if (pthread_create(&target->thread, NULL,
                           capture_thread_proc, target) != 0) {
            g_cam_driver.impl.CloseDevice(target);
            return cam_set_error("Failed to create capture thread"), NULL;
        }
#endif
    }

    return target;
}

/* ------------------------------------------------------------------ */
/* cam_close                                                           */
/* ------------------------------------------------------------------ */

CAMERAC_API void cam_close(camDevice device)
{
    if (!device) return;

    device->shutdown = 1;

    if (!g_cam_driver.impl.ProvidesOwnCallbackThread) {
#if defined(_WIN32)
        if (device->thread) {
            WaitForSingleObject(device->thread, 5000);
            CloseHandle(device->thread);
            device->thread = NULL;
        }
#else
        if (device->running) {
            pthread_join(device->thread, NULL);
        }
#endif
    }

    if (g_cam_driver.impl.CloseDevice) {
        g_cam_driver.impl.CloseDevice(device);
    }

    device->frame_cb       = NULL;
    device->frame_userdata = NULL;
    device->perm_cb        = NULL;
    device->perm_userdata  = NULL;
}

/* ------------------------------------------------------------------ */
/* Permission                                                          */
/* ------------------------------------------------------------------ */

CAMERAC_API camPermission cam_get_permission(camDevice device)
{
    return device ? device->permission : CAM_PERMISSION_UNKNOWN;
}

CAMERAC_API void cam_set_permission_callback(camDevice             device,
                                              camPermissionCallback cb,
                                              void                 *userdata)
{
    if (!device) return;
    device->perm_cb       = cb;
    device->perm_userdata = userdata;
}

/* ------------------------------------------------------------------ */
/* Device queries                                                      */
/* ------------------------------------------------------------------ */

CAMERAC_API bool cam_get_actual_spec(camDevice device, camSpec *out_spec)
{
    if (!device || !out_spec) return false;
    *out_spec = device->actual_spec;
    return true;
}

CAMERAC_API const char *cam_get_device_name(camDevice device)
{
    return device ? device->name : NULL;
}

CAMERAC_API camPosition cam_get_device_position(camDevice device)
{
    return device ? device->position : CAM_POSITION_UNKNOWN;
}

/* ------------------------------------------------------------------ */
/* cam_update – dispatch pending permission callbacks (main thread)    */
/*              and pump frames for single-threaded backends           */
/* ------------------------------------------------------------------ */

CAMERAC_API void cam_update(void)
{
    if (!s_initialized) return;

    /* Dispatch pending permission callbacks */
    cam_mutex_lock(&g_cam_driver.pending_perms_lock);
    camPendingPermission *list = g_cam_driver.pending_perms;
    g_cam_driver.pending_perms = NULL;
    cam_mutex_unlock(&g_cam_driver.pending_perms_lock);

    while (list) {
        camPendingPermission *next = list->next;
        camDevice_t *dev   = list->device;
        camPermission perm = list->result;
        free(list);

        dev->permission = perm;
        if (dev->perm_cb) {
            dev->perm_cb(dev, perm, dev->perm_userdata);
        }

        list = next;
    }

    /* For backends that do NOT use a background thread (e.g. Emscripten),
     * we must pump frames here on the main thread, mirroring what
     * cam_capture_thread_run() does in the threaded case.              */
    if (g_cam_driver.impl.ProvidesOwnCallbackThread) {
        camDevice_t *dev = g_cam_driver.devices;
        while (dev) {
            if (!dev->shutdown &&
                dev->permission == CAM_PERMISSION_APPROVED &&
                dev->frame_cb != NULL)
            {
                camFrame frame;
                memset(&frame, 0, sizeof(frame));

                camFrameResult result =
                    g_cam_driver.impl.AcquireFrame(dev, &frame);

                if (result == CAM_FRAME_READY) {
                    frame.width  = dev->actual_spec.width;
                    frame.height = dev->actual_spec.height;
                    frame.format = dev->actual_spec.format;
                    cam_deliver_frame(dev, &frame);
                    if (g_cam_driver.impl.ReleaseFrame) {
                        g_cam_driver.impl.ReleaseFrame(dev, &frame);
                    }
                }
            }
            dev = dev->next;
        }
    }
}

/* ------------------------------------------------------------------ */
/* Utility                                                             */
/* ------------------------------------------------------------------ */

CAMERAC_API int cam_bytes_per_pixel(camPixelFormat fmt)
{
    switch (fmt) {
        case CAM_PIXEL_FORMAT_RGB24:    return 3;
        case CAM_PIXEL_FORMAT_BGR24:    return 3;
        case CAM_PIXEL_FORMAT_RGBA32:   return 4;
        case CAM_PIXEL_FORMAT_BGRA32:   return 4;
        case CAM_PIXEL_FORMAT_ARGB32:   return 4;
        case CAM_PIXEL_FORMAT_XRGB8888: return 4;
        case CAM_PIXEL_FORMAT_RGB565:   return 2;
        case CAM_PIXEL_FORMAT_XRGB1555: return 2;
        /* planar / compressed */
        default:                        return 0;
    }
}

CAMERAC_API const char *cam_pixel_format_name(camPixelFormat fmt)
{
    switch (fmt) {
        case CAM_PIXEL_FORMAT_UNKNOWN:  return "UNKNOWN";
        case CAM_PIXEL_FORMAT_RGB24:    return "RGB24";
        case CAM_PIXEL_FORMAT_BGR24:    return "BGR24";
        case CAM_PIXEL_FORMAT_RGBA32:   return "RGBA32";
        case CAM_PIXEL_FORMAT_BGRA32:   return "BGRA32";
        case CAM_PIXEL_FORMAT_ARGB32:   return "ARGB32";
        case CAM_PIXEL_FORMAT_XRGB8888: return "XRGB8888";
        case CAM_PIXEL_FORMAT_RGB565:   return "RGB565";
        case CAM_PIXEL_FORMAT_XRGB1555: return "XRGB1555";
        case CAM_PIXEL_FORMAT_NV12:     return "NV12";
        case CAM_PIXEL_FORMAT_NV21:     return "NV21";
        case CAM_PIXEL_FORMAT_YUY2:     return "YUY2";
        case CAM_PIXEL_FORMAT_UYVY:     return "UYVY";
        case CAM_PIXEL_FORMAT_YVYU:     return "YVYU";
        case CAM_PIXEL_FORMAT_YV12:     return "YV12";
        case CAM_PIXEL_FORMAT_IYUV:     return "IYUV";
        case CAM_PIXEL_FORMAT_P010:     return "P010";
        case CAM_PIXEL_FORMAT_MJPEG:    return "MJPEG";
        default:                        return "?";
    }
}

CAMERAC_API const char *cam_position_name(camPosition pos)
{
    switch (pos) {
        case CAM_POSITION_FRONT_FACING: return "front";
        case CAM_POSITION_BACK_FACING:  return "back";
        default:                        return "unknown";
    }
}
