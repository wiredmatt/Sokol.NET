/*
 * camerac_internal.h  –  internal structures shared across backends
 */

#ifndef CAMERAC_INTERNAL_H_
#define CAMERAC_INTERNAL_H_

#include "../include/camerac.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>

/* ------------------------------------------------------------------ */
/* Thread / mutex abstraction                                          */
/* ------------------------------------------------------------------ */

#if defined(_WIN32)
  #include <windows.h>
  typedef CRITICAL_SECTION camMutex;
  #define cam_mutex_init(m)    InitializeCriticalSection(m)
  #define cam_mutex_destroy(m) DeleteCriticalSection(m)
  #define cam_mutex_lock(m)    EnterCriticalSection(m)
  #define cam_mutex_unlock(m)  LeaveCriticalSection(m)
#else
  #include <pthread.h>
  typedef pthread_mutex_t camMutex;
  #define cam_mutex_init(m)    pthread_mutex_init(m, NULL)
  #define cam_mutex_destroy(m) pthread_mutex_destroy(m)
  #define cam_mutex_lock(m)    pthread_mutex_lock(m)
  #define cam_mutex_unlock(m)  pthread_mutex_unlock(m)
#endif

/* ------------------------------------------------------------------ */
/* Error handling                                                      */
/* ------------------------------------------------------------------ */

/* Sets the per-thread error string and returns false. */
bool  cam_set_error(const char *fmt, ...);
void  cam_clear_error(void);

/* ------------------------------------------------------------------ */
/* Backend frame-result enum (same semantics as SDL3)                  */
/* ------------------------------------------------------------------ */

typedef enum camFrameResult {
    CAM_FRAME_ERROR = -1,  /* fatal error – close device              */
    CAM_FRAME_SKIP  =  0,  /* no frame available yet – try again      */
    CAM_FRAME_READY =  1   /* frame->data/pitch/timestamp_ns is valid  */
} camFrameResult;

/* ------------------------------------------------------------------ */
/* Backend driver interface                                            */
/* ------------------------------------------------------------------ */

typedef struct camDriverImpl {
    /* Enumerate devices and call cam_add_device() for each one found. */
    void (*DetectDevices)(void);

    /* Open device for capture.  spec is the *requested* spec.
       Store private data in device->hidden. */
    bool (*OpenDevice)(camDevice_t *device, const camSpec *spec);

    /* Stop capture and free device->hidden. */
    void (*CloseDevice)(camDevice_t *device);

    /** Wait (blocking) until a frame is available.
     *  Return false to signal the thread should exit. */
    bool (*WaitDevice)(camDevice_t *device);

    /** Acquire the next frame.  Fill frame->data/pitch/timestamp_ns/rotation.
     *  Memory for frame->data is managed by the backend and must remain valid
     *  until ReleaseFrame() is called. */
    camFrameResult (*AcquireFrame)(camDevice_t *device, camFrame *frame);

    /** Release memory / buffers claimed by AcquireFrame(). */
    void (*ReleaseFrame)(camDevice_t *device, camFrame *frame);

    /** Free the platform handle stored during DetectDevices. */
    void (*FreeDeviceHandle)(camDevice_t *device);

    /** Called at shutdown. */
    void (*Deinitialize)(void);

    /** If true the backend drives its own thread; the library will not
     *  create one for them. */
    bool ProvidesOwnCallbackThread;
} camDriverImpl;

typedef struct camBootstrap {
    const char *name;
    const char *desc;
    bool (*init)(camDriverImpl *impl);
    bool demand_only;
} camBootstrap;

/* ------------------------------------------------------------------ */
/* Internal device structure (camDevice_t)                             */
/* ------------------------------------------------------------------ */

typedef struct camPendingPermission {
    struct camPendingPermission *next;
    camDevice_t                 *device;
    camPermission                result;
} camPendingPermission;

struct camDevice_t {
    camMutex      lock;

    char          name[256];
    char          device_id[256];
    camPosition   position;

    camSpec       requested_spec;   /* what the user asked for     */
    camSpec       actual_spec;      /* what the backend delivers   */

    camSpec      *all_specs;        /* all formats the device supports */
    int           num_specs;

    camPermission permission;

    camFrameCallback    frame_cb;
    void               *frame_userdata;

    camPermissionCallback perm_cb;
    void                 *perm_userdata;

    /* backend-private data */
    void                 *hidden;

    /* platform device handle (from DetectDevices) */
    void                 *handle;

    /* capture thread */
#if defined(_WIN32)
    HANDLE thread;
#else
    pthread_t thread;
#endif
    volatile int shutdown;   /* set to 1 to request thread exit */
    volatile int running;    /* 1 while capture thread is alive */

    /* linked list node */
    struct camDevice_t *next;
};

/* ------------------------------------------------------------------ */
/* Driver state (global singleton)                                     */
/* ------------------------------------------------------------------ */

typedef struct camDriver {
    const char   *name;
    const char   *desc;
    camDriverImpl impl;

    /* list of detected/opened devices */
    camDevice_t  *devices;
    int           device_count;
    camMutex      devices_lock;

    /* pending permission results to dispatch on main thread */
    camPendingPermission *pending_perms;
    camMutex              pending_perms_lock;
} camDriver;

extern camDriver g_cam_driver;

/* ------------------------------------------------------------------ */
/* Functions called by backends                                        */
/* ------------------------------------------------------------------ */

/** Called by backends during DetectDevices() for each device found.
 *  name / position / specs are copied internally.
 *  handle is stored verbatim (freed via FreeDeviceHandle). */
camDevice_t *cam_add_device(const char       *name,
                             const char       *device_id,
                             camPosition       position,
                             int               num_specs,
                             const camSpec    *specs,
                             void             *handle);

/** Backend calls this to deliver permission outcome to the main thread. */
void cam_permission_outcome(camDevice_t *device, camPermission result);

/** Backend calls this to deliver a frame to the registered callback. */
void cam_deliver_frame(camDevice_t *device, const camFrame *frame);

/* ------------------------------------------------------------------ */
/* Internal capture thread                                             */
/* ------------------------------------------------------------------ */

void cam_capture_thread_run(camDevice_t *device);

/* ------------------------------------------------------------------ */
/* Utility                                                             */
/* ------------------------------------------------------------------ */

#define CAM_LOG(fmt, ...) fprintf(stderr, "[camerac] " fmt "\n", ##__VA_ARGS__)

#ifndef CAM_UNUSED
  #define CAM_UNUSED(x) (void)(x)
#endif

#endif /* CAMERAC_INTERNAL_H_ */
