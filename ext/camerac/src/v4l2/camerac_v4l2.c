/*
 * camerac_v4l2.c  –  Linux backend using Video4Linux2
 *
 * Requires: linux/videodev2.h (Linux kernel headers)
 */

#include "../camerac_internal.h"

#if defined(__linux__) && !defined(__ANDROID__)

#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <unistd.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <linux/videodev2.h>

/* ------------------------------------------------------------------ */
/* Format helpers                                                      */
/* ------------------------------------------------------------------ */

static camPixelFormat v4l2_to_pixel_format(uint32_t pixfmt)
{
    switch (pixfmt) {
        case V4L2_PIX_FMT_RGB24:  return CAM_PIXEL_FORMAT_RGB24;
        case V4L2_PIX_FMT_BGR24:  return CAM_PIXEL_FORMAT_BGR24;
        case V4L2_PIX_FMT_RGB565: return CAM_PIXEL_FORMAT_RGB565;
        case V4L2_PIX_FMT_YUYV:   return CAM_PIXEL_FORMAT_YUY2;
        case V4L2_PIX_FMT_UYVY:   return CAM_PIXEL_FORMAT_UYVY;
        case V4L2_PIX_FMT_NV12:   return CAM_PIXEL_FORMAT_NV12;
        case V4L2_PIX_FMT_NV21:   return CAM_PIXEL_FORMAT_NV21;
        case V4L2_PIX_FMT_YVU420: return CAM_PIXEL_FORMAT_YV12;
        case V4L2_PIX_FMT_YUV420: return CAM_PIXEL_FORMAT_IYUV;
        case V4L2_PIX_FMT_MJPEG:  return CAM_PIXEL_FORMAT_MJPEG;
        default:                  return CAM_PIXEL_FORMAT_UNKNOWN;
    }
}

static int xioctl(int fd, unsigned long request, void *arg)
{
    int r;
    do { r = ioctl(fd, request, arg); } while (r == -1 && errno == EINTR);
    return r;
}

/* ------------------------------------------------------------------ */
/* mmap buffer                                                         */
/* ------------------------------------------------------------------ */

#define V4L2_BUFFER_COUNT 4

typedef struct {
    void   *start;
    size_t  length;
} V4L2Buffer;

/* ------------------------------------------------------------------ */
/* Device handle                                                       */
/* ------------------------------------------------------------------ */

typedef struct V4L2DeviceHandle {
    char path[256];
} V4L2DeviceHandle;

/* ------------------------------------------------------------------ */
/* Private per-device data                                             */
/* ------------------------------------------------------------------ */

typedef struct V4L2PrivateData {
    int         fd;
    V4L2Buffer  buffers[V4L2_BUFFER_COUNT];
    int         buf_count;
    void       *frame_copy;
    size_t      frame_bytes;
} V4L2PrivateData;

/* ------------------------------------------------------------------ */
/* DetectDevices                                                       */
/* ------------------------------------------------------------------ */

static void V4L2_DetectDevices(void)
{
    DIR *dir = opendir("/dev");
    if (!dir) return;

    struct dirent *ent;
    while ((ent = readdir(dir)) != NULL) {
        if (strncmp(ent->d_name, "video", 5) != 0) continue;

        char path[256];
        snprintf(path, sizeof(path), "/dev/%s", ent->d_name);

        int fd = open(path, O_RDWR | O_NONBLOCK);
        if (fd < 0) continue;

        struct v4l2_capability cap;
        memset(&cap, 0, sizeof(cap));
        if (xioctl(fd, VIDIOC_QUERYCAP, &cap) < 0 ||
            !(cap.capabilities & V4L2_CAP_VIDEO_CAPTURE) ||
            !(cap.capabilities & V4L2_CAP_STREAMING)) {
            close(fd);
            continue;
        }

        char name[256];
        strncpy(name, (char *)cap.card, sizeof(name) - 1);

        /* Enumerate frame sizes and formats */
        camSpec *specs = NULL;
        int num_specs = 0;

        struct v4l2_fmtdesc fmtdesc;
        memset(&fmtdesc, 0, sizeof(fmtdesc));
        fmtdesc.type = V4L2_BUF_TYPE_VIDEO_CAPTURE;

        while (xioctl(fd, VIDIOC_ENUM_FMT, &fmtdesc) == 0) {
            camPixelFormat pf = v4l2_to_pixel_format(fmtdesc.pixelformat);
            if (pf != CAM_PIXEL_FORMAT_UNKNOWN) {
                struct v4l2_frmsizeenum frmsize;
                memset(&frmsize, 0, sizeof(frmsize));
                frmsize.pixel_format = fmtdesc.pixelformat;
                frmsize.index = 0;

                while (xioctl(fd, VIDIOC_ENUM_FRAMESIZES, &frmsize) == 0) {
                    uint32_t w, h;
                    if (frmsize.type == V4L2_FRMSIZE_TYPE_DISCRETE) {
                        w = frmsize.discrete.width;
                        h = frmsize.discrete.height;
                    } else {
                        w = frmsize.stepwise.max_width;
                        h = frmsize.stepwise.max_height;
                    }

                    /* Enumerate frame intervals */
                    struct v4l2_frmivalenum frmival;
                    memset(&frmival, 0, sizeof(frmival));
                    frmival.pixel_format = fmtdesc.pixelformat;
                    frmival.width  = w;
                    frmival.height = h;
                    frmival.index  = 0;

                    bool added = false;
                    while (xioctl(fd, VIDIOC_ENUM_FRAMEINTERVALS, &frmival) == 0) {
                        uint32_t fps_n, fps_d;
                        if (frmival.type == V4L2_FRMIVAL_TYPE_DISCRETE) {
                            /* interval is denominator/numerator -> fps = num/denom */
                            fps_n = frmival.discrete.denominator;
                            fps_d = frmival.discrete.numerator;
                        } else {
                            fps_n = frmival.stepwise.min.denominator;
                            fps_d = frmival.stepwise.min.numerator;
                        }
                        if (fps_d == 0) fps_d = 1;

                        specs = (camSpec *)realloc(specs,
                            sizeof(camSpec) * (size_t)(num_specs + 1));
                        camSpec *s = &specs[num_specs++];
                        s->width           = (int)w;
                        s->height          = (int)h;
                        s->fps_numerator   = (int)fps_n;
                        s->fps_denominator = (int)fps_d;
                        s->format          = pf;
                        added = true;
                        frmival.index++;
                        if (frmsize.type != V4L2_FRMSIZE_TYPE_DISCRETE) break;
                    }

                    if (!added) {
                        /* Add with unknown fps */
                        specs = (camSpec *)realloc(specs,
                            sizeof(camSpec) * (size_t)(num_specs + 1));
                        camSpec *s = &specs[num_specs++];
                        s->width           = (int)w;
                        s->height          = (int)h;
                        s->fps_numerator   = 30;
                        s->fps_denominator = 1;
                        s->format          = pf;
                    }

                    frmsize.index++;
                    if (frmsize.type != V4L2_FRMSIZE_TYPE_DISCRETE) break;
                }
            }
            fmtdesc.index++;
        }

        close(fd);

        V4L2DeviceHandle *handle =
            (V4L2DeviceHandle *)calloc(1, sizeof(V4L2DeviceHandle));
        strncpy(handle->path, path, sizeof(handle->path) - 1);

        cam_add_device(name, path, CAM_POSITION_UNKNOWN,
                       num_specs, specs, handle);
        free(specs);
    }
    closedir(dir);
}

/* ------------------------------------------------------------------ */
/* OpenDevice                                                          */
/* ------------------------------------------------------------------ */

static bool V4L2_OpenDevice(camDevice_t *device, const camSpec *spec)
{
    V4L2DeviceHandle *handle = (V4L2DeviceHandle *)device->handle;

    int fd = open(handle->path, O_RDWR);
    if (fd < 0) {
        return cam_set_error("Could not open %s: %s",
                             handle->path, strerror(errno));
    }

    /* Set format */
    struct v4l2_format fmt;
    memset(&fmt, 0, sizeof(fmt));
    fmt.type                = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    fmt.fmt.pix.width       = (uint32_t)(spec ? spec->width  : 640);
    fmt.fmt.pix.height      = (uint32_t)(spec ? spec->height : 480);

    /* Translate pixel format back to v4l2 */
    uint32_t v4l2_fmt = V4L2_PIX_FMT_YUYV; /* safe default */
    if (spec) {
        switch (spec->format) {
            case CAM_PIXEL_FORMAT_RGB24:  v4l2_fmt = V4L2_PIX_FMT_RGB24;  break;
            case CAM_PIXEL_FORMAT_BGR24:  v4l2_fmt = V4L2_PIX_FMT_BGR24;  break;
            case CAM_PIXEL_FORMAT_RGB565: v4l2_fmt = V4L2_PIX_FMT_RGB565; break;
            case CAM_PIXEL_FORMAT_YUY2:   v4l2_fmt = V4L2_PIX_FMT_YUYV;  break;
            case CAM_PIXEL_FORMAT_UYVY:   v4l2_fmt = V4L2_PIX_FMT_UYVY;  break;
            case CAM_PIXEL_FORMAT_NV12:   v4l2_fmt = V4L2_PIX_FMT_NV12;  break;
            case CAM_PIXEL_FORMAT_NV21:   v4l2_fmt = V4L2_PIX_FMT_NV21;  break;
            case CAM_PIXEL_FORMAT_YV12:   v4l2_fmt = V4L2_PIX_FMT_YVU420; break;
            case CAM_PIXEL_FORMAT_IYUV:   v4l2_fmt = V4L2_PIX_FMT_YUV420; break;
            case CAM_PIXEL_FORMAT_MJPEG:  v4l2_fmt = V4L2_PIX_FMT_MJPEG; break;
            default: break;
        }
    }
    fmt.fmt.pix.pixelformat = v4l2_fmt;
    fmt.fmt.pix.field       = V4L2_FIELD_INTERLACED;

    if (xioctl(fd, VIDIOC_S_FMT, &fmt) < 0) {
        close(fd);
        return cam_set_error("VIDIOC_S_FMT failed: %s", strerror(errno));
    }

    /* Update actual spec */
    device->actual_spec.width  = (int)fmt.fmt.pix.width;
    device->actual_spec.height = (int)fmt.fmt.pix.height;
    device->actual_spec.format = v4l2_to_pixel_format(fmt.fmt.pix.pixelformat);

    /* Request mmap buffers */
    struct v4l2_requestbuffers req;
    memset(&req, 0, sizeof(req));
    req.count  = V4L2_BUFFER_COUNT;
    req.type   = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    req.memory = V4L2_MEMORY_MMAP;

    if (xioctl(fd, VIDIOC_REQBUFS, &req) < 0 || req.count < 2) {
        close(fd);
        return cam_set_error("VIDIOC_REQBUFS failed: %s", strerror(errno));
    }

    V4L2PrivateData *priv = (V4L2PrivateData *)calloc(1, sizeof(V4L2PrivateData));
    priv->fd = fd;
    priv->buf_count = (int)req.count;

    for (int i = 0; i < priv->buf_count; i++) {
        struct v4l2_buffer buf;
        memset(&buf, 0, sizeof(buf));
        buf.type   = V4L2_BUF_TYPE_VIDEO_CAPTURE;
        buf.memory = V4L2_MEMORY_MMAP;
        buf.index  = (uint32_t)i;

        xioctl(fd, VIDIOC_QUERYBUF, &buf);

        priv->buffers[i].length = buf.length;
        priv->buffers[i].start  = mmap(NULL, buf.length,
                                        PROT_READ | PROT_WRITE,
                                        MAP_SHARED, fd, (off_t)buf.m.offset);
    }

    /* Enqueue all buffers */
    for (int i = 0; i < priv->buf_count; i++) {
        struct v4l2_buffer buf;
        memset(&buf, 0, sizeof(buf));
        buf.type   = V4L2_BUF_TYPE_VIDEO_CAPTURE;
        buf.memory = V4L2_MEMORY_MMAP;
        buf.index  = (uint32_t)i;
        xioctl(fd, VIDIOC_QBUF, &buf);
    }

    /* Start streaming */
    enum v4l2_buf_type type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    if (xioctl(fd, VIDIOC_STREAMON, &type) < 0) {
        close(fd);
        free(priv);
        return cam_set_error("VIDIOC_STREAMON failed: %s", strerror(errno));
    }

    device->hidden = priv;

    /* V4L2 always has permission */
    cam_permission_outcome(device, CAM_PERMISSION_APPROVED);

    return true;
}

/* ------------------------------------------------------------------ */
/* WaitDevice                                                          */
/* ------------------------------------------------------------------ */

static bool V4L2_WaitDevice(camDevice_t *device)
{
    V4L2PrivateData *priv = (V4L2PrivateData *)device->hidden;
    fd_set fds;
    struct timeval tv = { .tv_sec = 2, .tv_usec = 0 };
    FD_ZERO(&fds);
    FD_SET(priv->fd, &fds);
    int r = select(priv->fd + 1, &fds, NULL, NULL, &tv);
    if (r < 0 && errno != EINTR) return false;
    return true;
}

/* ------------------------------------------------------------------ */
/* AcquireFrame                                                        */
/* ------------------------------------------------------------------ */

static camFrameResult V4L2_AcquireFrame(camDevice_t *device, camFrame *frame)
{
    V4L2PrivateData *priv = (V4L2PrivateData *)device->hidden;

    struct v4l2_buffer buf;
    memset(&buf, 0, sizeof(buf));
    buf.type   = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    buf.memory = V4L2_MEMORY_MMAP;

    if (xioctl(priv->fd, VIDIOC_DQBUF, &buf) < 0) {
        if (errno == EAGAIN) return CAM_FRAME_SKIP;
        return CAM_FRAME_ERROR;
    }

    size_t frame_bytes = priv->buffers[buf.index].length;
    if (frame_bytes > priv->frame_bytes) {
        free(priv->frame_copy);
        priv->frame_copy  = malloc(frame_bytes);
        priv->frame_bytes = frame_bytes;
    }
    memcpy(priv->frame_copy, priv->buffers[buf.index].start, frame_bytes);

    /* Re-enqueue buffer immediately */
    xioctl(priv->fd, VIDIOC_QBUF, &buf);

    frame->data         = priv->frame_copy;
    frame->timestamp_ns = (uint64_t)buf.timestamp.tv_sec  * 1000000000ULL
                        + (uint64_t)buf.timestamp.tv_usec * 1000ULL;

    int bpp = cam_bytes_per_pixel(device->actual_spec.format);
    frame->pitch = bpp > 0 ? device->actual_spec.width * bpp : (int)(frame_bytes / (size_t)device->actual_spec.height);

    return CAM_FRAME_READY;
}

/* ------------------------------------------------------------------ */
/* ReleaseFrame                                                        */
/* ------------------------------------------------------------------ */

static void V4L2_ReleaseFrame(camDevice_t *device, camFrame *frame)
{
    CAM_UNUSED(device);
    /* data points into priv->frame_copy; nothing to do until next acquire */
    frame->data  = NULL;
    frame->pitch = 0;
}

/* ------------------------------------------------------------------ */
/* CloseDevice / FreeDeviceHandle / Deinitialize                       */
/* ------------------------------------------------------------------ */

static void V4L2_CloseDevice(camDevice_t *device)
{
    if (!device || !device->hidden) return;
    V4L2PrivateData *priv = (V4L2PrivateData *)device->hidden;

    enum v4l2_buf_type type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    xioctl(priv->fd, VIDIOC_STREAMOFF, &type);

    for (int i = 0; i < priv->buf_count; i++) {
        munmap(priv->buffers[i].start, priv->buffers[i].length);
    }

    close(priv->fd);
    free(priv->frame_copy);
    free(priv);
    device->hidden = NULL;
}

static void V4L2_FreeDeviceHandle(camDevice_t *device)
{
    if (device && device->handle) { free(device->handle); device->handle = NULL; }
}

static void V4L2_Deinitialize(void) {}

/* ------------------------------------------------------------------ */
/* Bootstrap                                                           */
/* ------------------------------------------------------------------ */

static bool V4L2_Init(camDriverImpl *impl)
{
    impl->DetectDevices             = V4L2_DetectDevices;
    impl->OpenDevice                = V4L2_OpenDevice;
    impl->CloseDevice               = V4L2_CloseDevice;
    impl->WaitDevice                = V4L2_WaitDevice;
    impl->AcquireFrame              = V4L2_AcquireFrame;
    impl->ReleaseFrame              = V4L2_ReleaseFrame;
    impl->FreeDeviceHandle          = V4L2_FreeDeviceHandle;
    impl->Deinitialize              = V4L2_Deinitialize;
    impl->ProvidesOwnCallbackThread = false;
    return true;
}

camBootstrap V4L2_bootstrap = {
    "v4l2",
    "Video4Linux2 (Linux)",
    V4L2_Init,
    false
};

#endif /* __linux__ && !__ANDROID__ */
