/*
 * camerac - Cross-platform camera capture library
 *
 * Supports: macOS, iOS, Windows, Linux, Android, Emscripten
 *
 * Inspired by SDL3's camera subsystem.
 * License: MIT
 */

#ifndef CAMERAC_H_
#define CAMERAC_H_

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------ */
/* Platform & export macros                                            */
/* ------------------------------------------------------------------ */

#if defined(_WIN32) || defined(__CYGWIN__)
  #ifdef CAMERAC_BUILDING_DLL
    #define CAMERAC_API __declspec(dllexport)
  #else
    #define CAMERAC_API __declspec(dllimport)
  #endif
#elif defined(__GNUC__) || defined(__clang__)
  #define CAMERAC_API __attribute__((visibility("default")))
#else
  #define CAMERAC_API
#endif

/* ------------------------------------------------------------------ */
/* Pixel formats                                                       */
/* ------------------------------------------------------------------ */

typedef enum camPixelFormat {
    CAM_PIXEL_FORMAT_UNKNOWN = 0,

    /* Packed RGB */
    CAM_PIXEL_FORMAT_RGB24,      /* 8-8-8, no alpha              */
    CAM_PIXEL_FORMAT_BGR24,      /* 8-8-8, BGR order             */
    CAM_PIXEL_FORMAT_RGBA32,     /* 8-8-8-8                      */
    CAM_PIXEL_FORMAT_BGRA32,     /* 8-8-8-8, BGRA order          */
    CAM_PIXEL_FORMAT_ARGB32,     /* 8-8-8-8, ARGB order          */
    CAM_PIXEL_FORMAT_XRGB8888,   /* 32-bit, top byte unused      */
    CAM_PIXEL_FORMAT_RGB565,     /* 5-6-5                        */
    CAM_PIXEL_FORMAT_XRGB1555,   /* 1-5-5-5, top bit unused      */

    /* YUV / planar */
    CAM_PIXEL_FORMAT_NV12,       /* YUV 4:2:0 bi-planar          */
    CAM_PIXEL_FORMAT_NV21,       /* YUV 4:2:0 bi-planar (VU)     */
    CAM_PIXEL_FORMAT_YUY2,       /* YUV 4:2:2 packed             */
    CAM_PIXEL_FORMAT_UYVY,       /* YUV 4:2:2 packed (UYVY)      */
    CAM_PIXEL_FORMAT_YVYU,       /* YUV 4:2:2 packed (YVYU)      */
    CAM_PIXEL_FORMAT_YV12,       /* YUV 4:2:0 planar             */
    CAM_PIXEL_FORMAT_IYUV,       /* YUV 4:2:0 planar (IYUV/I420) */
    CAM_PIXEL_FORMAT_P010,       /* YUV 4:2:0 10-bit bi-planar   */

    /* Compressed */
    CAM_PIXEL_FORMAT_MJPEG,      /* Motion JPEG                  */

    CAM_PIXEL_FORMAT_COUNT
} camPixelFormat;

/* ------------------------------------------------------------------ */
/* Camera position (front / back)                                      */
/* ------------------------------------------------------------------ */

typedef enum camPosition {
    CAM_POSITION_UNKNOWN = 0,
    CAM_POSITION_FRONT_FACING,
    CAM_POSITION_BACK_FACING
} camPosition;

/* ------------------------------------------------------------------ */
/* Permission state                                                    */
/* ------------------------------------------------------------------ */

typedef enum camPermission {
    CAM_PERMISSION_UNKNOWN = 0,
    CAM_PERMISSION_PENDING,
    CAM_PERMISSION_APPROVED,
    CAM_PERMISSION_DENIED
} camPermission;

/* ------------------------------------------------------------------ */
/* Spec / format descriptor                                            */
/* ------------------------------------------------------------------ */

typedef struct camSpec {
    int            width;
    int            height;
    int            fps_numerator;    /* e.g. 30 */
    int            fps_denominator;  /* e.g. 1  */
    camPixelFormat format;
} camSpec;

/* ------------------------------------------------------------------ */
/* Device info                                                         */
/* ------------------------------------------------------------------ */

typedef struct camDeviceInfo {
    char          name[256];       /* Human-readable device name   */
    char          device_id[256];  /* Platform-specific identifier */
    camPosition   position;
    const camSpec *specs;          /* All supported specs          */
    int            num_specs;      /* Number of specs              */
} camDeviceInfo;

/* ------------------------------------------------------------------ */
/* Frame                                                               */
/* ------------------------------------------------------------------ */

typedef struct camFrame {
    void          *data;          /* Pixel data – plane 0 (or packed)  */
    int            pitch;         /* Bytes per row – plane 0           */
    void          *data2;         /* Plane 1 for bi-planar (e.g. UV)   */
    int            pitch2;        /* Bytes per row – plane 1           */
    int            width;
    int            height;
    camPixelFormat format;
    uint64_t       timestamp_ns;  /* Capture time in nanoseconds       */
    float          rotation;      /* Degrees clockwise to make upright */
} camFrame;

/* ------------------------------------------------------------------ */
/* Opaque device handle                                                */
/* ------------------------------------------------------------------ */

typedef struct camDevice_t camDevice_t;
typedef camDevice_t *camDevice;

/* ------------------------------------------------------------------ */
/* Callbacks                                                           */
/* ------------------------------------------------------------------ */

/** Called on the capture thread each time a new frame is ready.
 *  The frame is only valid for the duration of this call; copy
 *  frame->data if you need it later. */
typedef void (*camFrameCallback)(camDevice device, const camFrame *frame, void *userdata);

/** Called on the main thread when camera permission is resolved. */
typedef void (*camPermissionCallback)(camDevice device, camPermission result, void *userdata);

/* ------------------------------------------------------------------ */
/* Library init / shutdown                                             */
/* ------------------------------------------------------------------ */

/** Initialize the camera subsystem.
 *  @return true on success. */
CAMERAC_API bool cam_init(void);

/** Shut down the camera subsystem, release all resources. */
CAMERAC_API void cam_shutdown(void);

/** Returns the name of the active backend driver (e.g. "coremedia"). */
CAMERAC_API const char *cam_get_backend(void);

/* ------------------------------------------------------------------ */
/* Device enumeration                                                  */
/* ------------------------------------------------------------------ */

/** Returns the number of camera devices currently available. */
CAMERAC_API int cam_get_device_count(void);

/** Fill *out_info with information about device at index.
 *  Call cam_free_device_info() when done.
 *  @return true on success. */
CAMERAC_API bool cam_get_device_info(int index, camDeviceInfo *out_info);

/** Free memory allocated inside *info by cam_get_device_info(). */
CAMERAC_API void cam_free_device_info(camDeviceInfo *info);

/* ------------------------------------------------------------------ */
/* Open / close                                                        */
/* ------------------------------------------------------------------ */

/** Open a camera device.
 *  @param device_index  Index from cam_get_device_info().
 *  @param requested     Desired format/resolution/fps (may be NULL for default).
 *  @param frame_cb      Called for every captured frame (may be NULL).
 *  @param userdata      Passed through to the callback.
 *  @return Handle, or NULL on failure. */
CAMERAC_API camDevice cam_open(int                device_index,
                                const camSpec      *requested,
                                camFrameCallback    frame_cb,
                                void               *userdata);

/** Close a camera device and free all associated resources. */
CAMERAC_API void cam_close(camDevice device);

/* ------------------------------------------------------------------ */
/* Permissions                                                         */
/* ------------------------------------------------------------------ */

/** Query the current permission state for an open device. */
CAMERAC_API camPermission cam_get_permission(camDevice device);

/** Set a callback that fires when permission is resolved.
 *  On platforms where permission is always granted this fires immediately. */
CAMERAC_API void cam_set_permission_callback(camDevice            device,
                                              camPermissionCallback cb,
                                               void                *userdata);

/* ------------------------------------------------------------------ */
/* Device queries                                                      */
/* ------------------------------------------------------------------ */

/** @return The actual spec the camera is running at after open. */
CAMERAC_API bool cam_get_actual_spec(camDevice device, camSpec *out_spec);

/** @return Human-readable name of the opened device. */
CAMERAC_API const char *cam_get_device_name(camDevice device);

/** @return Facing position of the opened device. */
CAMERAC_API camPosition cam_get_device_position(camDevice device);

/* ------------------------------------------------------------------ */
/* Main-thread update                                                  */
/* ------------------------------------------------------------------ */

/** Must be called once per frame from the main thread.
 *  Dispatches pending permission callbacks and processes events. */
CAMERAC_API void cam_update(void);

/* ------------------------------------------------------------------ */
/* Utility                                                             */
/* ------------------------------------------------------------------ */

/** @return Bytes per pixel for packed formats, 0 for planar/compressed. */
CAMERAC_API int cam_bytes_per_pixel(camPixelFormat format);

/** @return A printable name for the format. */
CAMERAC_API const char *cam_pixel_format_name(camPixelFormat format);

/** @return A printable name for the position. */
CAMERAC_API const char *cam_position_name(camPosition position);

/** @return The last error message string (thread-safe, per-thread). */
CAMERAC_API const char *cam_get_error(void);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* CAMERAC_H_ */
