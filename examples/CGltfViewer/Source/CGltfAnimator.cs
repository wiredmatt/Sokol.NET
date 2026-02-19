// CGltfAnimator.cs — replaces SharpGltfAnimator.cs.
// Drives per-frame bone transforms using CGltfAnimation/CGltfBone (no SharpGLTF dependency).
using System;
using System.Collections.Generic;
using System.Numerics;
using static Sokol.SLog;

namespace Sokol
{
    public class CGltfAnimator
    {
        private Matrix4x4[] _finalBoneMatrices;
        private CGltfAnimation? _currentAnimation;
        private float _currentTime;

        private Dictionary<string, Matrix4x4> _nodeGlobalTransforms = new();
        private Dictionary<int, List<Mesh>> _materialToMeshMap;
        private Dictionary<int, float[]> _animatedMorphWeights = new();

        // Fast lookup: node name → list of CGltfNodes (multiple primitives per glTF node)
        // _nodesByName: non-skinned nodes for TRS animation
        // _allNodesByName: ALL nodes (including skinned) for morph weight lookup
        private Dictionary<string, List<CGltfNode>> _nodesByName = new();
        private Dictionary<string, List<CGltfNode>> _allNodesByName = new();
        private List<CGltfNode> _allNodes;

        private Dictionary<string, BoneInfo>? _characterBoneInfoMap;

        public float PlaybackSpeed { get; set; } = 1.0f;

        public CGltfAnimator(
            CGltfAnimation? animation,
            Dictionary<int, List<Mesh>> materialToMeshMap,
            List<CGltfNode> nodes,
            int boneCount,
            Dictionary<string, BoneInfo>? characterBoneInfoMap = null)
        {
            _currentTime = 0f;
            _currentAnimation = animation;
            _materialToMeshMap = materialToMeshMap;
            _characterBoneInfoMap = characterBoneInfoMap;
            _allNodes = nodes;

            _finalBoneMatrices = new Matrix4x4[Math.Max(1, boneCount)];
            BuildNodeLookup(nodes);
            Array.Fill(_finalBoneMatrices, Matrix4x4.Identity);

            if (_currentAnimation != null)
            {
                ref CGltfNodeData rootNode = ref _currentAnimation.GetRootNode();
                CalculateBoneTransform(rootNode, Matrix4x4.Identity);
            }
        }

        private void BuildNodeLookup(List<CGltfNode> nodes)
        {
            _nodesByName.Clear();
            _allNodesByName.Clear();
            foreach (var node in nodes)
            {
                if (!string.IsNullOrEmpty(node.NodeName))
                {
                    // All-nodes map (for morph weights — morphs exist on skinned meshes too)
                    if (!_allNodesByName.TryGetValue(node.NodeName, out var allList))
                        _allNodesByName[node.NodeName] = allList = new List<CGltfNode>();
                    allList.Add(node);

                    // Non-skinned map (for TRS node animation)
                    if (!node.IsSkinned)
                    {
                        if (!_nodesByName.TryGetValue(node.NodeName, out var list))
                            _nodesByName[node.NodeName] = list = new List<CGltfNode>();
                        list.Add(node);
                    }
                }
            }
            Info($"CGltfAnimator: built node lookup, {_nodesByName.Count} TRS names, {_allNodesByName.Count} total names", "CGltf");
        }

        public void SetAnimation(CGltfAnimation? animation)
        {
            _currentAnimation = animation;
            _currentTime = 0f;
            Array.Fill(_finalBoneMatrices, Matrix4x4.Identity);

            if (_currentAnimation != null)
            {
                // Rebuild lookup when animation changes (different set of bones may be present)
                BuildNodeLookup(_allNodes);
                ref CGltfNodeData rootNode = ref _currentAnimation.GetRootNode();
                CalculateBoneTransform(rootNode, Matrix4x4.Identity);
            }
        }

        public void UpdateAnimation(float dt)
        {
            if (_currentAnimation == null) return;

            _currentTime += _currentAnimation.GetTicksPerSecond() * dt * PlaybackSpeed;
            _currentTime = _currentTime % _currentAnimation.GetDuration();

            // Batch-update all bones
            foreach (var bone in _currentAnimation.GetBones())
                bone.Update(_currentTime);

            ref CGltfNodeData rootNode = ref _currentAnimation.GetRootNode();
            CalculateBoneTransform(rootNode, Matrix4x4.Identity);

            ApplyAnimationToNodes();
            UpdateMaterialPropertyAnimations(_currentTime);
            UpdateMorphWeightAnimations(_currentTime);
        }

        private void ApplyAnimationToNodes()
        {
            if (_currentAnimation == null || _nodesByName.Count == 0) return;

            foreach (var bone in _currentAnimation.GetBones())
            {
                if (_nodesByName.TryGetValue(bone.Name, out var renderNodes))
                {
                    bone.GetAnimatedChannels(out bool hasT, out bool hasR, out bool hasS,
                                             out Vector3 t, out Quaternion r, out Vector3 s);
                    foreach (var node in renderNodes)
                    {
                        Vector3 ft = hasT ? t : node.Position;
                        Quaternion fr = hasR ? r : node.Rotation;
                        Vector3 fs = hasS ? s : node.Scale;
                        node.SetLocalTransform(ft, fr, fs);
                    }
                }
            }
        }

        public void PlayAnimation(CGltfAnimation animation)
        {
            _currentAnimation = animation;
            _currentTime = 0f;
        }

        private void CalculateBoneTransform(CGltfNodeData node, Matrix4x4 parentTransform)
        {
            Matrix4x4 nodeTransform = node.Transformation;

            var bone = _currentAnimation?.FindBone(node.Name);
            if (bone != null)
                nodeTransform = bone.GetLocalTransform();

            Matrix4x4 globalTransformation = nodeTransform * parentTransform;
            _nodeGlobalTransforms[node.Name] = globalTransformation;

            var boneInfoMap = _characterBoneInfoMap ?? _currentAnimation?.GetBoneIDMap();
            if (boneInfoMap != null && boneInfoMap.TryGetValue(node.Name, out var boneInfo))
            {
                int index = boneInfo.Id;
                if (index >= 0 && index < _finalBoneMatrices.Length)
                    _finalBoneMatrices[index] = boneInfo.Offset * globalTransformation;
            }

            for (int i = 0; i < node.ChildrenCount; i++)
                CalculateBoneTransform(node.Children[i], globalTransformation);
        }

        public Matrix4x4[] GetFinalBoneMatrices() => _finalBoneMatrices;
        public float GetCurrentTime() => _currentTime;
        public CGltfAnimation? GetCurrentAnimation() => _currentAnimation;

        public bool TryGetNodeGlobalTransform(string nodeName, out Matrix4x4 globalTransform)
            => _nodeGlobalTransforms.TryGetValue(nodeName, out globalTransform);

        private void UpdateMaterialPropertyAnimations(float t)
        {
            if (_currentAnimation?.MaterialAnimations.Count == 0) return;
            if (_currentAnimation == null) return;
            foreach (var matAnim in _currentAnimation.MaterialAnimations)
            {
                if (matAnim.IsFloatType)
                    ApplyMaterialFloatProperty(matAnim.MaterialIndex, matAnim.Target, matAnim.SampleFloatAtTime(t));
                else
                    ApplyMaterialVector2Property(matAnim.MaterialIndex, matAnim.Target, matAnim.SampleVector2AtTime(t));
            }
        }

        private void ApplyMaterialFloatProperty(int matIdx, MaterialAnimationTarget target, float value)
        {
            if (_materialToMeshMap == null || !_materialToMeshMap.TryGetValue(matIdx, out var meshes)) return;
            foreach (var mesh in meshes)
            {
                switch (target)
                {
                    case MaterialAnimationTarget.NormalTextureRotation: mesh.NormalTexRotation = value; break;
                }
            }
        }

        private void ApplyMaterialVector2Property(int matIdx, MaterialAnimationTarget target, Vector2 value)
        {
            if (_materialToMeshMap == null || !_materialToMeshMap.TryGetValue(matIdx, out var meshes)) return;
            foreach (var mesh in meshes)
            {
                switch (target)
                {
                    case MaterialAnimationTarget.NormalTextureOffset: mesh.NormalTexOffset = value; break;
                    case MaterialAnimationTarget.NormalTextureScale:  mesh.NormalTexScale  = value; break;
                }
            }
        }

        private void UpdateMorphWeightAnimations(float t)
        {
            if (_currentAnimation?.MorphAnimations.Count == 0) return;
            if (_currentAnimation == null) return;
            foreach (var morphAnim in _currentAnimation.MorphAnimations)
            {
                var weights = morphAnim.SampleWeightsAtTime(t);
                // Use name-based lookup so all primitives sharing the same node get the weights.
                if (!string.IsNullOrEmpty(morphAnim.NodeName) &&
                    _allNodesByName.TryGetValue(morphAnim.NodeName, out var matchingNodes))
                {
                    foreach (var node in matchingNodes)
                        _animatedMorphWeights[node.NodeIndex] = weights;
                }
                else if (morphAnim.NodeIndex >= 0)
                {
                    // Fallback: use the stored index directly
                    _animatedMorphWeights[morphAnim.NodeIndex] = weights;
                }
            }
        }

        public float[]? GetAnimatedMorphWeights(int nodeIndex)
            => _animatedMorphWeights.TryGetValue(nodeIndex, out var w) ? w : null;
    }
}
