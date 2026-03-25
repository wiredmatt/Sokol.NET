@ctype mat4 System.Numerics.Matrix4x4
@ctype vec4 System.Numerics.Vector4
@ctype vec3 System.Numerics.Vector3

//=== manifold mesh rendering ===

@vs vs_manifold
layout(binding=0) uniform vs_params {
    mat4 mvp;
    mat4 model;
};

in vec3 pos;
in vec3 norm;

out vec3 v_normal;
out vec3 v_pos;

void main() {
    gl_Position = mvp * vec4(pos, 1.0);
    v_normal = normalize((model * vec4(norm, 0.0)).xyz);
    v_pos = (model * vec4(pos, 1.0)).xyz;
}
@end

@fs fs_manifold
layout(binding=1) uniform fs_params {
    vec4 light_dir;
    vec4 base_color;
};

in vec3 v_normal;
in vec3 v_pos;

out vec4 frag_color;

void main() {
    float ambient = 0.25;
    vec3 ld = normalize(light_dir.xyz);
    float diffuse = max(0.0, dot(v_normal, ld)) * 0.75;
    // slight back-face illumination for better mesh visibility
    float back = max(0.0, dot(-v_normal, ld)) * 0.1;
    float lighting = ambient + diffuse + back;
    frag_color = vec4(base_color.xyz * lighting, 1.0);
}
@end

@program manifold vs_manifold fs_manifold
