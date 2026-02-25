//------------------------------------------------------------------------------
//  Shader code for camera texture rendering (fullscreen quad)
//------------------------------------------------------------------------------

@vs vs
in vec2 position;
in vec2 texcoord0;
out vec2 uv;

void main() {
    gl_Position = vec4(position, 0.0, 1.0);
    uv = texcoord0;
}
@end

@fs fs
layout(binding=0) uniform texture2D tex_y;
layout(binding=0) uniform sampler smp_y;
layout(binding=1) uniform texture2D tex_uv;
layout(binding=1) uniform sampler smp_uv;

in vec2 uv;
out vec4 frag_color;

void main() {
    // Sample Y from first texture
    float y = texture(sampler2D(tex_y, smp_y), uv).r;
    
    // Sample UV from second texture (RG8 format: U in R channel, V in G channel)
    vec2 uv_sample = texture(sampler2D(tex_uv, smp_uv), uv).rg;
    float u = uv_sample.r - 0.5;
    float v = uv_sample.g - 0.5;
    
    // YUV to RGB conversion (BT.601 standard, same as reference)
    float r = y + 1.13983 * v;
    float g = y - 0.39465 * u - 0.58060 * v;
    float b = y + 2.03211 * u;
    
    frag_color = vec4(clamp(r, 0.0, 1.0), clamp(g, 0.0, 1.0), clamp(b, 0.0, 1.0), 1.0);
}
@end

@program camera_texture vs fs

//------------------------------------------------------------------------------
// RGBA32 variant (single-plane, browser getUserMedia output)
//------------------------------------------------------------------------------

@fs fs_rgba
layout(binding=0) uniform texture2D tex_rgba;
layout(binding=0) uniform sampler smp_rgba;

in vec2 uv;
out vec4 frag_color;

void main() {
    frag_color = texture(sampler2D(tex_rgba, smp_rgba), uv);
}
@end

@program camera_texture_rgba vs fs_rgba
