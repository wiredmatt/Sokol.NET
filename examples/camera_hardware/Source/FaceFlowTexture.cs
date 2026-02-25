
using static Sokol.SG;
using static Sokol.StbImage;
using static Sokol.SBasisu;

namespace Sokol
{
    public unsafe class FaceFlowTexture : IDisposable
    {
        public sg_image Image { get; private set; }
        public sg_view View { get; private set; }
        public sg_sampler Sampler { get; private set; }
        public bool IsValid => Image.id != 0;

        // Properties for MediaPipe compatibility
        public int width { get; private set; }
        public int height { get; private set; }
        public sg_pixel_format format { get; private set; }
        public int bitsPerPixel { get; private set; }

        private bool disposed;
        private string? _cacheKey; // Track the cache key for removal on dispose

        private SharedBuffer? _pixelDataBuffer; // Store pixel data for GetRawTextureData

        public unsafe FaceFlowTexture(void* pixels, int width, int height, string label, sg_pixel_format format, bool stream_update = true, SamplerSettings? samplerSettings = null)
        {
            _cacheKey = label; // Use label as cache key
            this.width = width;
            this.height = height;
            this.format = format;
            this.bitsPerPixel = 4; // Assume RGBA for regular constructor

            samplerSettings ??= new SamplerSettings(); // Use defaults if null

            // Create image with mipmaps
            // Setting num_mipmaps = 0 tells Sokol to auto-calculate the mip count based on dimensions
            // and auto-generate the mipmap chain on the GPU

            sg_image_data image_data = default;

            if (pixels != null)
            {
                image_data.mip_levels[0] = new sg_range { ptr = pixels, size = (nuint)(width * height * 4) };
            }

            var img_desc = new sg_image_desc
            {
                width = width,
                height = height,
                pixel_format = format,
                num_mipmaps = 1,  // 0 = auto-calculate and generate mipmaps
                data = image_data,
                label = label
            };

            // in case the texture should be updated very frequently , it should be true
            img_desc.usage.stream_update = stream_update;

            Image = sg_make_image(img_desc);

            // Create view
            View = sg_make_view(new sg_view_desc
            {
                texture = new sg_texture_view_desc { image = Image },
                label = $"{label}-view"
            });
            // ViewTracker.TrackViewCreation(View, $"{label}-view");

            // Create sampler with proper settings from glTF
            Sampler = sg_make_sampler(new sg_sampler_desc
            {
                min_filter = samplerSettings.MinFilter,
                mag_filter = samplerSettings.MagFilter,
                mipmap_filter = samplerSettings.MipmapFilter,
                wrap_u = samplerSettings.WrapU,
                wrap_v = samplerSettings.WrapV,
                label = $"{label}-sampler"
            });
        }

        private FaceFlowTexture() { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                // Remove from cache if we have a cache key
                if (_cacheKey != null)
                {
                    // TextureCache.Instance.Remove(_cacheKey);
                }

                // Dispose pixel data buffer if exists
                if (_pixelDataBuffer != null)
                {
                    SharedBuffer.Dispose(_pixelDataBuffer);
                    _pixelDataBuffer = null;
                }

                // Destroy sokol graphics resources
                if (Image.id != 0)
                {
                    // ViewTracker.TrackViewDestruction(View);
                    sg_destroy_sampler(Sampler);
                    sg_destroy_view(View);
                    sg_destroy_image(Image);
                    Image = default;
                    View = default;
                    Sampler = default;
                }

                disposed = true;
            }
        }

        ~FaceFlowTexture()
        {
            Dispose(false);
        }



    /// <summary>
        /// Get raw texture data as SharedBuffer. Creates and caches the buffer on first call.
        /// The buffer will be managed by the Texture2D and disposed when the texture is disposed.
        /// </summary>
        public SharedBuffer GetRawTextureData<T>() where T : unmanaged
        {
            // If buffer already exists, return it
            if (_pixelDataBuffer != null)
                return _pixelDataBuffer;
            
            // Calculate buffer size (assuming RGBA format)
            uint bufferSize = (uint)(width * height * 4);
            _pixelDataBuffer = SharedBuffer.Create(bufferSize);
            
            // Note: In a real implementation, you would need to read back the pixel data from the GPU
            // This is a simplified version that just allocates the buffer
            // sg_read_image would be needed if Sokol supports it, or you'd need to keep original pixel data
            
            return _pixelDataBuffer;
        }

    }
}
