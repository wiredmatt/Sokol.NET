@ctype vec4 System.Numerics.Vector4

@vs bg_vs
in vec2 in_pos;
in vec2 in_uv;
out vec2 uv;

void main() {
    gl_Position = vec4(in_pos, 0.5, 1.0);
    uv = in_uv;
}
@end

@fs bg_fs
layout(binding=0) uniform bg_params {
    vec4 color_top;
    vec4 color_bottom;
};

in vec2 uv;
out vec4 frag_color;

void main() {
    frag_color = mix(color_bottom, color_top, uv.y);
}
@end

@program bg bg_vs bg_fs
