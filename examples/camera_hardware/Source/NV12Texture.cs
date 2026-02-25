using System;
using static Sokol.SG;
using static System.Hardware.CameraC;   

namespace Sokol
{
    public unsafe class NV12Texture : IDisposable
    {
        public FaceFlowTexture YFaceFlowTexture { get; private set; }
        public FaceFlowTexture UvFaceFlowTexture { get; private set; }
        public bool IsValid => YFaceFlowTexture?.IsValid == true && UvFaceFlowTexture?.IsValid == true;

        public int width       { get; private set; }  // actual camera pixels wide
        public int height      { get; private set; }  // actual camera pixels tall
        public int pitchBytes  { get; private set; }  // Y bytes-per-row
        public int pitch2Bytes { get; private set; }  // UV bytes-per-row
        public float uvScaleX => pitchBytes  > 0 ? (float)width / pitchBytes  : 1f;

        private bool disposed;

        public NV12Texture(camFrame frame)
        {
            this.width       = frame.width;
            this.height      = frame.height;
            this.pitchBytes  = frame.pitch;
            this.pitch2Bytes = frame.pitch2;

            // Y  plane: R8,  pitch  × height      (1 byte/pixel → pitch pixels wide)
            // UV plane: RG8, (pitch2/2) × (height/2)  (2 bytes/pixel → pitch2/2 pixels wide)
            YFaceFlowTexture = new FaceFlowTexture(
                null,
                frame.pitch,
                frame.height,
                "camera-y-plane",
                sg_pixel_format.SG_PIXELFORMAT_R8,
                stream_update: true,
                samplerSettings: new SamplerSettings()
            );

            UvFaceFlowTexture = new FaceFlowTexture(
                null,
                frame.pitch2 / 2,
                frame.height / 2,
                "camera-uv-plane",
                sg_pixel_format.SG_PIXELFORMAT_RG8,
                stream_update: true,
                samplerSettings: new SamplerSettings()
            );

            Console.WriteLine($"NV12 Texture: Y={frame.pitch}x{frame.height} UV={frame.pitch2/2}x{frame.height/2}  (camera {frame.width}x{frame.height}, uvScaleX={uvScaleX:F3})");

            // Populate with the first frame's data
            UpdateTexture(frame);
        }

        public void UpdateTexture(camFrame frame)
        {
            if (frame.width != width || frame.height != height)
            {
                Console.WriteLine($"WARNING: Camera frame dimensions changed from {width}x{height} to {frame.width}x{frame.height}");
                return;
            }

            // SDL reference layout: Y at data, UV at data + pitch * height
            byte* yPlane  = (byte*)frame.data;
            byte* uvPlane = (byte*)frame.data2;

            sg_image_data y_data = default;
            y_data.mip_levels[0] = new sg_range { ptr = yPlane,  size = (nuint)(frame.pitch  * frame.height) };
            sg_update_image(YFaceFlowTexture.Image, y_data);

            sg_image_data uv_data = default;
            uv_data.mip_levels[0] = new sg_range { ptr = uvPlane, size = (nuint)(frame.pitch2 * (frame.height / 2)) };
            sg_update_image(UvFaceFlowTexture.Image, uv_data);
        }

        
        // public NV12Texture(SDL_Surface* cameraSurface)
        // {
        //     if (cameraSurface == null)
        //         throw new ArgumentNullException(nameof(cameraSurface));

        //     if (cameraSurface->format != SDL_PIXELFORMAT_NV12 &&
        //         cameraSurface->format != SDL_PIXELFORMAT_NV21)
        //     {
        //         throw new ArgumentException("Surface must be NV12 or NV21 format");
        //     }

        //     this.width = cameraSurface->w;
        //     this.height = cameraSurface->h;

        //     // NV12 layout: Y plane (w×h) followed by interleaved UV plane (w/2×h/2 with 2 bytes per pixel)
        //     byte* yPlane = (byte*)cameraSurface->pixels;
        //     byte* uvPlane = yPlane + (cameraSurface->pitch * cameraSurface->h);

        //     // Create Y texture: R8 format, full resolution
        //     YFaceFlowTexture = new FaceFlowTexture(
        //         yPlane,
        //         cameraSurface->w,
        //         cameraSurface->h,
        //         "camera-y-plane",
        //         sg_pixel_format.SG_PIXELFORMAT_R8,
        //         stream_update: true,
        //         samplerSettings: new SamplerSettings()
        //     );

        //     // Create UV texture: RG8 format (U in R channel, V in G channel), half resolution
        //     // In NV12, UV is interleaved as UVUVUV... which is perfect for RG8
        //     UvFaceFlowTexture = new FaceFlowTexture(
        //         uvPlane,
        //         cameraSurface->w / 2,  // Half width (each UV pair covers 2 pixels horizontally)
        //         cameraSurface->h / 2,  // Half height (UV is subsampled vertically)
        //         "camera-uv-plane",
        //         sg_pixel_format.SG_PIXELFORMAT_RG8,
        //         stream_update: true,
        //         samplerSettings: new SamplerSettings()
        //     );

        //     Console.WriteLine($"NV12 Texture created: Y={cameraSurface->w}x{cameraSurface->h}, UV={cameraSurface->w / 2}x{cameraSurface->h / 2}");
        // }

        // public void UpdateFromCameraSurface(SDL_Surface* cameraSurface)
        // {
        //     if (cameraSurface == null) return;

        //     if (cameraSurface->w != width || cameraSurface->h != height)
        //     {
        //         Console.WriteLine($"WARNING: Camera surface dimensions changed from {width}x{height} to {cameraSurface->w}x{cameraSurface->h}");
        //         return;
        //     }

        //     byte* yPlane = (byte*)cameraSurface->pixels;
        //     byte* uvPlane = yPlane + (cameraSurface->pitch * cameraSurface->h);

        //     // Update Y plane
        //     sg_image_data y_data = default;
        //     y_data.mip_levels[0] = new sg_range()
        //     {
        //         ptr = yPlane,
        //         size = (nuint)(cameraSurface->pitch * cameraSurface->h)
        //     };
        //     sg_update_image(YFaceFlowTexture.Image, y_data);

        //     // Update UV plane
        //     sg_image_data uv_data = default;
        //     uv_data.mip_levels[0] = new sg_range()
        //     {
        //         ptr = uvPlane,
        //         size = (nuint)(cameraSurface->pitch * cameraSurface->h / 2)
        //     };
        //     sg_update_image(UvFaceFlowTexture.Image, uv_data);
        // }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    YFaceFlowTexture?.Dispose();
                    UvFaceFlowTexture?.Dispose();
                }
                disposed = true;
            }
        }

        ~NV12Texture()
        {
            Dispose(false);
        }
    }
}
