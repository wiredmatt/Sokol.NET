// CGltfAnimatedCharacter.cs — replaces SharpGltfAnimatedCharacter.cs.
// All SharpGltf* types renamed to CGltf* equivalents.
using System;
using System.Collections.Generic;
using System.Numerics;
using static Sokol.SG;
using static Sokol.SLog;

namespace Sokol
{
    public class AnimatedCharacter
    {
        public int SkinIndex { get; }
        public string Name { get; }

        private List<CGltfAnimation> Animations { get; }
        public int CurrentAnimationIndex { get; private set; }
        public CGltfAnimation Animation => Animations[CurrentAnimationIndex];

        public CGltfAnimator Animator { get; }
        public List<Mesh> Meshes { get; }
        public int BoneCount { get; }
        public bool UsesTextureSkinning { get; private set; }

        public void SetSkinningMode(SkinningMode mode)
        {
            if (BoneCount >= AnimationConstants.MAX_BONES) { UsesTextureSkinning = true; return; }
            bool newMode = (mode == SkinningMode.TextureBased);
            if (newMode && !UsesTextureSkinning && JointMatrixTexture.id == 0)
            {
                UsesTextureSkinning = true;
                CreateJointMatrixTexture();
            }
            else if (!newMode && UsesTextureSkinning)
            {
                UsesTextureSkinning = false;
            }
            else
            {
                UsesTextureSkinning = newMode;
            }
        }

        public sg_image JointMatrixTexture { get; private set; }
        public sg_view JointMatrixView { get; private set; }
        public sg_sampler JointMatrixSampler { get; private set; }
        public int JointTextureWidth { get; private set; }

        private float[]? jointTextureData;
        private bool textureUpdatedThisFrame;

        public AnimatedCharacter(
            int skinIndex,
            string name,
            List<CGltfAnimation> animations,
            List<Mesh> meshes,
            Dictionary<int, List<Mesh>> materialToMeshMap,
            List<CGltfNode> nodes,
            int boneCount,
            Dictionary<string, BoneInfo> boneInfoMap)
        {
            SkinIndex = skinIndex;
            Name = name;
            Animations = animations;
            CurrentAnimationIndex = 0;
            Meshes = meshes;
            BoneCount = boneCount;

            UsesTextureSkinning = (boneCount >= AnimationConstants.MAX_BONES);

            Animator = new CGltfAnimator(
                animations[0],
                materialToMeshMap,
                nodes,
                boneCount,
                boneInfoMap);

            if (UsesTextureSkinning)
                CreateJointMatrixTexture();
        }

        public void Update(float dt)
        {
            Animator.UpdateAnimation(dt);
            textureUpdatedThisFrame = false;
        }

        public void SetAnimation(CGltfAnimation animation) => Animator.SetAnimation(animation);

        public int GetAnimationCount() => Animations.Count;

        public void NextAnimation()
        {
            if (Animations.Count == 0) return;
            CurrentAnimationIndex = (CurrentAnimationIndex + 1) % Animations.Count;
            Animator.SetAnimation(Animation);
        }

        public void PreviousAnimation()
        {
            if (Animations.Count == 0) return;
            CurrentAnimationIndex = (CurrentAnimationIndex - 1 + Animations.Count) % Animations.Count;
            Animator.SetAnimation(Animation);
        }

        public Matrix4x4[] GetBoneMatrices() => Animator.GetFinalBoneMatrices();

        private void CreateJointMatrixTexture()
        {
            if (BoneCount <= 0) return;

            int width = (int)Math.Ceiling(Math.Sqrt(BoneCount * 8));
            JointTextureWidth = width;

            JointMatrixSampler = sg_make_sampler(new sg_sampler_desc
            {
                min_filter = sg_filter.SG_FILTER_NEAREST,
                mag_filter = sg_filter.SG_FILTER_NEAREST,
                wrap_u = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
                wrap_v = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
                label = $"joint-matrix-sampler-{Name}"
            });

            JointMatrixTexture = sg_make_image(new sg_image_desc
            {
                width = width,
                height = width,
                pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA32F,
                usage = new sg_image_usage { stream_update = true },
                label = $"joint-matrix-texture-{Name}"
            });

            JointMatrixView = sg_make_view(new sg_view_desc
            {
                texture = new sg_texture_view_desc { image = JointMatrixTexture },
                label = $"joint-matrix-view-{Name}"
            });
        }

        public unsafe void UpdateJointMatrixTexture()
        {
            if (!UsesTextureSkinning || JointMatrixTexture.id == 0) return;
            if (textureUpdatedThisFrame) return;
            textureUpdatedThisFrame = true;

            var boneMatrices = GetBoneMatrices();
            int width = JointTextureWidth;
            int texelCount = width * width;

            if (jointTextureData == null || jointTextureData.Length != texelCount * 4)
                jointTextureData = new float[texelCount * 4];

            Array.Clear(jointTextureData, 0, jointTextureData.Length);

            int maxJoints = Math.Min(BoneCount, texelCount / 8);
            for (int i = 0; i < maxJoints; i++)
            {
                CopyMatrix4x4ToFloatArray(boneMatrices[i], jointTextureData, i * 32);
                CopyMatrix4x4ToFloatArray(boneMatrices[i], jointTextureData, i * 32 + 16);
            }

            fixed (float* ptr = jointTextureData)
            {
                var imageData = new sg_image_data();
                imageData.mip_levels[0].ptr = ptr;
                imageData.mip_levels[0].size = (nuint)(jointTextureData.Length * sizeof(float));
                sg_update_image(JointMatrixTexture, in imageData);
            }
        }

        private static void CopyMatrix4x4ToFloatArray(Matrix4x4 mat, float[] arr, int offset)
        {
            arr[offset +  0] = mat.M11; arr[offset +  1] = mat.M12; arr[offset +  2] = mat.M13; arr[offset +  3] = mat.M14;
            arr[offset +  4] = mat.M21; arr[offset +  5] = mat.M22; arr[offset +  6] = mat.M23; arr[offset +  7] = mat.M24;
            arr[offset +  8] = mat.M31; arr[offset +  9] = mat.M32; arr[offset + 10] = mat.M33; arr[offset + 11] = mat.M34;
            arr[offset + 12] = mat.M41; arr[offset + 13] = mat.M42; arr[offset + 14] = mat.M43; arr[offset + 15] = mat.M44;
        }

        public void Dispose()
        {
            if (JointMatrixTexture.id != 0) { sg_destroy_image(JointMatrixTexture); JointMatrixTexture = default; }
            if (JointMatrixView.id != 0)    { sg_destroy_view(JointMatrixView);      JointMatrixView    = default; }
            if (JointMatrixSampler.id != 0) { sg_destroy_sampler(JointMatrixSampler);JointMatrixSampler = default; }
        }
    }
}
