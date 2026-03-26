@ctype mat4 mat44_t

// -----------------------------------------------------------------------
// Chess Piece shader – 2D textured sprite (used for piece PNG rendering)
// -----------------------------------------------------------------------
@vs piece_vs
layout(binding=0) uniform piece_vs_params {
    mat4 mvp;
    float tint_r;
    float tint_g;
    float tint_b;
    float tint_a;
};

in vec2 position;
in vec2 texcoord;

out vec2 uv;
out vec4 tint;

void main() {
    gl_Position = mvp * vec4(position, 0.0, 1.0);
    uv   = texcoord;
    tint = vec4(tint_r, tint_g, tint_b, tint_a);
}
@end

@fs piece_fs
layout(binding=0) uniform texture2D tex;
layout(binding=0) uniform sampler smp;

in vec2 uv;
in vec4 tint;
out vec4 frag_color;

void main() {
    vec4 c = texture(sampler2D(tex, smp), uv);
    // Premultiply alpha then apply tint
    frag_color = vec4(c.rgb * tint.rgb, c.a * tint.a);
}
@end

@program piece piece_vs piece_fs
