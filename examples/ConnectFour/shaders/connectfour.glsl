@ctype mat4 mat44_t
@ctype vec3 vec3_t
@ctype vec4 vec4_t

// -----------------------------------------------------------------------
// Connect4 Board shader – flat Phong-lit board surface
// -----------------------------------------------------------------------
@vs board_vs
layout(binding=0) uniform board_vs_params {
    mat4 mvp;
    mat4 model;
};

in vec3 position;
in vec3 normal;

out vec3 frag_pos;
out vec3 frag_normal;

void main() {
    vec4 world_pos = model * vec4(position, 1.0);
    frag_pos    = world_pos.xyz;
    frag_normal = normalize(mat3(model) * normal);
    gl_Position = mvp * vec4(position, 1.0);
}
@end

@fs board_fs
layout(binding=1) uniform board_fs_params {
    vec3 light_pos;
    float _pad0;
    vec3 light_color;
    float _pad1;
    vec3 base_color;
    float _pad2;
    vec3 view_pos;
    float _pad3;
};

in vec3 frag_pos;
in vec3 frag_normal;
out vec4 frag_color;

void main() {
    float ambient_strength = 0.35;
    vec3 ambient = ambient_strength * light_color;

    vec3 light_dir = normalize(light_pos - frag_pos);
    float diff = max(dot(frag_normal, light_dir), 0.0);
    vec3 diffuse = diff * light_color;

    float spec_strength = 0.2;
    vec3 view_dir  = normalize(view_pos - frag_pos);
    vec3 half_dir  = normalize(light_dir + view_dir);
    float spec = pow(max(dot(frag_normal, half_dir), 0.0), 32.0);
    vec3 specular = spec_strength * spec * light_color;

    vec3 result = (ambient + diffuse + specular) * base_color;
    frag_color = vec4(result, 1.0);
}
@end

@program board board_vs board_fs

// -----------------------------------------------------------------------
// Connect4 Disc shader – Phong-lit colored disc
// -----------------------------------------------------------------------
@vs disc_vs
layout(binding=0) uniform disc_vs_params {
    mat4 mvp;
    mat4 model;
};

in vec3 position;
in vec3 normal;

out vec3 frag_pos;
out vec3 frag_normal;

void main() {
    vec4 world_pos = model * vec4(position, 1.0);
    frag_pos    = world_pos.xyz;
    frag_normal = normalize(mat3(model) * normal);
    gl_Position = mvp * vec4(position, 1.0);
}
@end

@fs disc_fs
layout(binding=1) uniform disc_fs_params {
    vec3 light_pos;
    float _pad0;
    vec3 light_color;
    float _pad1;
    vec3 disc_color;
    float _pad2;
    vec3 view_pos;
    float _pad3;
};

in vec3 frag_pos;
in vec3 frag_normal;
out vec4 frag_color;

void main() {
    float ambient_strength = 0.3;
    vec3 ambient = ambient_strength * light_color;

    vec3 light_dir = normalize(light_pos - frag_pos);
    float diff = max(dot(frag_normal, light_dir), 0.0);
    vec3 diffuse = diff * light_color;

    float spec_strength = 0.6;
    vec3 view_dir  = normalize(view_pos - frag_pos);
    vec3 half_dir  = normalize(light_dir + view_dir);
    float spec = pow(max(dot(frag_normal, half_dir), 0.0), 64.0);
    vec3 specular = spec_strength * spec * light_color;

    vec3 result = (ambient + diffuse + specular) * disc_color;
    frag_color = vec4(result, 1.0);
}
@end

@program disc disc_vs disc_fs
