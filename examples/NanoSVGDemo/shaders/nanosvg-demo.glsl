//------------------------------------------------------------------------------
//  Shader for NanoSVGDemo: simple 2D textured quad with aspect-ratio scaling
//------------------------------------------------------------------------------

@vs vs
layout(binding=0) uniform vs_params {
    vec4 quad_xform; // xy = scale, zw = unused
};

in vec2 pos;
in vec2 uv0;
out vec2 uv;

void main() {
    gl_Position = vec4(pos.x * quad_xform.x, pos.y * quad_xform.y, 0.0, 1.0);
    uv = uv0;
}
@end

@fs fs
layout(binding=0) uniform texture2D tex;
layout(binding=0) uniform sampler smp;

in vec2 uv;
out vec4 frag_color;

void main() {
    frag_color = texture(sampler2D(tex, smp), uv);
}
@end

@program nanosvg vs fs
