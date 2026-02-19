// CGltfModel.cs — replaces SharpGltfModel.cs.
// Loads GLTF/GLB via the cgltf C library.  No SharpGLTF dependency.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using static Sokol.CGltf;
using static Sokol.SG;
using static Sokol.SLog;

namespace Sokol
{
    // -------------------------------------------------------------------------
    // Light data extracted from KHR_lights_punctual nodes during model load.
    // -------------------------------------------------------------------------
    public struct ModelLightInfo
    {
        public cgltf_light_type LightType;
        public Vector3 Color;
        public float Intensity;
        public float Range;
        public float InnerConeAngle;  // degrees
        public float OuterConeAngle;  // degrees
        public Matrix4x4 WorldTransform;
        public string NodeName;
        public CGltfNode? WrapperNode;  // Matching render-node (for animation)
    }

    // -------------------------------------------------------------------------
    // AlphaMode enum matching SharpGLTF naming so Frame_* files compile unchanged
    // -------------------------------------------------------------------------
    public enum AlphaMode { OPAQUE, MASK, BLEND }

    // -------------------------------------------------------------------------
    // CGltfModel — the main model container, owns cgltf_data* lifetime.
    // -------------------------------------------------------------------------
    public unsafe class CGltfModel : IDisposable
    {
        // ---------------------------------------------------------- public API
        public List<AnimatedCharacter> Characters  { get; private set; } = new();
        public List<Mesh>             StaticMeshes { get; private set; } = new();
        public int                    TotalBoneCount => Characters.Sum(c => c.BoneCount);

        // Legacy / compatibility
        public List<Mesh>             Meshes          { get; private set; } = new();
        public List<CGltfNode>        Nodes           { get; private set; } = new();
        public Dictionary<string, BoneInfo> BoneInfoMap  { get; private set; } = new();
        public int                    BoneCounter     = 0;
        public List<CGltfAnimation>   Animations      { get; private set; } = new();
        public int                    CurrentAnimationIndex = 0;
        public bool                   HasAnimations   => Animations.Count > 0 || Characters.Count > 0;
        public bool                   AnimationsReady { get; private set; } = false;
        public Dictionary<int, List<Mesh>> MaterialToMeshMap { get; private set; } = new();

        // Lights
        public List<ModelLightInfo>   ModelLights     { get; private set; } = new();

        // ---------------------------------------------------------- private
        private cgltf_data* _data;
        private bool        _disposed = false;
        private HashSet<string> _skinnedNodeNames = new();
        // Keeps all pinned byte[] alive until this model is disposed.
        private List<GCHandle> _pinnedBuffers = new();
        // Temporary: pre-fetched external texture bytes, used during ProcessModel, cleared after.
        private Dictionary<string, byte[]>? _preloadedTextures;

        // ====================================================================
        //  ParsedGltf — intermediate state between Phase 1 (parse) and Phase 3 (finish)
        // ====================================================================
        /// <summary>
        /// Holds results of a successful cgltf_parse call.  External .bin buffers and
        /// embedded GLB data pointers are maintained through GCHandle pins so that
        /// cgltf_accessor_unpack_floats (called during FinishLoad) can safely access them.
        /// </summary>
        public unsafe class ParsedGltf : IDisposable
        {
            internal cgltf_data*    Data;
            internal string         Path;
            internal string         BaseDir;
            internal GCHandle       MainBufferPin;  // pins the main gltf/glb byte[]
            // Additional pins for fetched external .bin buffers (freed after FinishLoad)
            internal List<GCHandle> ExternalPins = new();
            // External image URIs that need async prefetch; populated by BeginParse
            public  List<(int idx, string uri)> ExternalImageUris = new();
            private bool            _disposed;

            internal ParsedGltf(cgltf_data* data, string path, GCHandle pin)
            {
                Data          = data;
                Path          = path;
                BaseDir       = System.IO.Path.GetDirectoryName(path) ?? "";
                MainBufferPin = pin;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (Data != null) { cgltf_free(Data); Data = null; }
                if (MainBufferPin.IsAllocated) MainBufferPin.Free();
                foreach (var h in ExternalPins) if (h.IsAllocated) h.Free();
                ExternalPins.Clear();
            }
        }

        // ====================================================================
        //  Phase 1 — parse from memory buffer, discover external .bin URIs
        // ====================================================================
        /// <summary>
        /// Parses the raw gltf/glb bytes.  Returns a ParsedGltf containing:
        ///   • The live cgltf_data* (with buffer->data already set for GLB/embedded buffers)
        ///   • A list of (bufferIndex, uri) pairs for external .bin files that must be
        ///     fetched asynchronously before calling FinishLoad.
        /// Returns null on parse failure.
        /// </summary>
        public static unsafe (ParsedGltf parsed, List<(int idx, string uri)> externalUris)?
            BeginParse(byte[] buffer, string path)
        {
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            cgltf_options options = default;
            cgltf_data*   data    = null;

            cgltf_result r = cgltf_parse(options, (void*)pin.AddrOfPinnedObject(),
                                         (nuint)buffer.Length, out data);
            if (r != cgltf_result.cgltf_result_success)
            {
                pin.Free();
                Error($"cgltf_parse failed ({r}) for '{path}'", "CGltf");
                return null;
            }

            Info($"cgltf_parse ok for '{path}' ({buffer.Length} bytes)", "CGltf");

            // For GLB the binary chunk pointer is already set by cgltf_parse and points
            // into our pinned buffer — safe as long as the pin is held.
            // For .gltf + external .bin: locate URIs that need async fetch.
            var externalUris = new List<(int idx, string uri)>();
            for (int i = 0; i < (int)data->buffers_count; i++)
            {
                cgltf_buffer* buf = &data->buffers[i];
                // buf->data != null  →  already embedded (GLB) or data-URI; skip
                if (buf->data != null) continue;

                string? uri = PtrToStr(buf->uri);
                if (uri == null) continue;

                // data: URIs (base64) would need cgltf_load_buffer_base64 — handle inline
                if (uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    Warning($"data-URI buffer {i} in '{path}' — base64 not yet supported without cgltf_load_buffers", "CGltf");
                    continue;
                }

                externalUris.Add((i, uri));
            }

            // GLB: inject the binary chunk into buffers[0].data.
            // cgltf_parse sets data->bin but NOT buffers[0].data — that's normally done by
            // cgltf_load_buffers.  We must do it manually so that accessor reads and embedded
            // texture buffer_view lookups work without calling cgltf_load_buffers.
            if (data->file_type == cgltf_file_type.cgltf_file_type_glb &&
                data->buffers_count > 0 && data->buffers[0].data == null && data->bin != null)
            {
                data->buffers[0].data             = data->bin;
                data->buffers[0].data_free_method = cgltf_data_free_method.cgltf_data_free_method_none;
                Info($"GLB: injected bin ({(long)data->bin_size} bytes) into buffers[0]", "CGltf");
            }

            var parsed = new ParsedGltf(data, path, pin);

            // Collect external image URIs for async prefetch.
            // Embedded buffer-view images work automatically once buffers[0].data is set above.
            for (int i = 0; i < (int)data->images_count; i++)
            {
                cgltf_image* img = &data->images[i];
                if (img->buffer_view != null) continue;   // embedded — no file fetch needed
                string? imgUri = PtrToStr(img->uri);
                if (imgUri == null || imgUri.StartsWith("data:", StringComparison.Ordinal)) continue;
                parsed.ExternalImageUris.Add((i, imgUri));
            }
            if (parsed.ExternalImageUris.Count > 0)
                Info($"Found {parsed.ExternalImageUris.Count} external texture(s) to prefetch", "CGltf");

            return (parsed, externalUris);
        }

        // ====================================================================
        //  (Between phases) — called once per external .bin fetch completion
        // ====================================================================
        /// <summary>
        /// Pins the fetched bytes and sets buffer->data in the live cgltf_data.
        /// data_free_method is set to none so cgltf_free won't touch our managed memory.
        /// </summary>
        public static unsafe void InjectExternalBuffer(ParsedGltf parsed, int bufferIndex, byte[] bytes)
        {
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            parsed.ExternalPins.Add(pin);

            cgltf_buffer* buf = &parsed.Data->buffers[bufferIndex];
            buf->data             = (void*)pin.AddrOfPinnedObject();
            buf->data_free_method = cgltf_data_free_method.cgltf_data_free_method_none;

            Info($"Injected external buffer[{bufferIndex}]: {bytes.Length} bytes", "CGltf");
        }

        // ====================================================================
        //  Phase 3 — complete model construction (all buffers present)
        // ====================================================================
        /// <summary>
        /// Constructs the final CGltfModel.  Takes ownership of <paramref name="parsed"/>
        /// (which must NOT be disposed by the caller afterwards).
        /// The pins are transferred to the new model and released when it is disposed.
        /// </summary>
        public static unsafe CGltfModel? FinishLoad(ParsedGltf parsed, Dictionary<string, byte[]>? preloadedTextures = null)
        {
            Info($"cgltf FinishLoad '{parsed.Path}'", "CGltf");
            var model = new CGltfModel(parsed, preloadedTextures);
            return model;
        }

        // ====================================================================
        //  Constructor (private) — takes ownership of ParsedGltf
        // ====================================================================
        private CGltfModel(ParsedGltf parsed, Dictionary<string, byte[]>? preloadedTextures = null)
        {
            _data = parsed.Data;
            // Transfer pin ownership: keep main buffer and all .bin buffers pinned
            // until this model is disposed (cgltf_accessor_unpack_floats needs them).
            _pinnedBuffers.Add(parsed.MainBufferPin);
            _pinnedBuffers.AddRange(parsed.ExternalPins);
            // Null out cgltf_data in parsed so its Dispose won't double-free
            parsed.Data = null;
            // Clear ExternalPins list so parsed.Dispose won't free them again.
            // We cannot zero the GCHandle struct but we can clear the list.
            parsed.ExternalPins.Clear();
            // Mark the MainBufferPin in parsed as freed by replacing it with default.
            // (Not strictly needed since parsed.Data is null and it won't call cgltf_free)

            _preloadedTextures = preloadedTextures;
            ProcessModel(_data, parsed.BaseDir);
            _preloadedTextures = null;  // clear after processing — bytes are owned by caller
        }

        // ====================================================================
        //  ProcessModel
        // ====================================================================
        private void ProcessModel(cgltf_data* data, string baseDir)
        {
            Info($"CGltfModel: {data->meshes_count} meshes, {data->nodes_count} nodes, {data->skins_count} skins, {data->animations_count} animations", "CGltf");

            // -----------------------------------------------------------------
            // Step 1 – skins → per-character bone info maps
            // -----------------------------------------------------------------
            var skinDataList = new List<(int skinIndex, string skinName,
                Dictionary<string, BoneInfo> boneInfoMap, int boneCount,
                HashSet<string> jointNames)>();

            _skinnedNodeNames.Clear();
            for (int si = 0; si < (int)data->skins_count; si++)
            {
                cgltf_skin* skin = &data->skins[si];
                var (bim, bc, jn) = ProcessSkin(skin, si);
                string sname = PtrToStr(skin->name) ?? $"Skin_{si}";
                skinDataList.Add((si, sname, bim, bc, jn));
                foreach (var n in jn) _skinnedNodeNames.Add(n);
                if (si == 0) { BoneInfoMap = bim; BoneCounter = bc; }
                Info($"  Skin {si} '{sname}': {bc} bones", "CGltf");
            }

            // -----------------------------------------------------------------
            // Step 2 – meshes → build Mesh objects, remember cgltf_mesh* index
            // -----------------------------------------------------------------
            // Map: cgltf_mesh* (as IntPtr) → first Mesh index in Meshes list
            var meshPtrToStart = new Dictionary<IntPtr, int>();
            for (int mi = 0; mi < (int)data->meshes_count; mi++)
            {
                cgltf_mesh* m = &data->meshes[mi];
                meshPtrToStart[(IntPtr)m] = Meshes.Count;
                for (int pi = 0; pi < (int)m->primitives_count; pi++)
                    ProcessMesh(&m->primitives[pi], baseDir);   // SkinIndex set later in step 3
            }

            // -----------------------------------------------------------------
            // Step 3 – scene node tree → CGltfNode, assign SkinIndex, collect lights
            // -----------------------------------------------------------------
            cgltf_scene* scene = data->scene != null ? data->scene
                               : data->scenes_count > 0 ? data->scenes : null;
            if (scene != null)
            {
                for (int ni = 0; ni < (int)scene->nodes_count; ni++)
                    ProcessNodeWithParent(scene->nodes[ni], null, data, meshPtrToStart);
            }

            // -----------------------------------------------------------------
            // Step 4 – group meshes by skin → StaticMeshes
            // -----------------------------------------------------------------
            var meshesBySkin = Meshes
                .Where(m => m.SkinIndex >= 0)
                .GroupBy(m => m.SkinIndex)
                .ToDictionary(g => g.Key, g => g.ToList());
            StaticMeshes = Meshes.Where(m => m.SkinIndex < 0).ToList();

            // -----------------------------------------------------------------
            // Step 5 – create AnimatedCharacter per skin
            // -----------------------------------------------------------------
            foreach (var (skinIndex, skinName, bim, bc, jn) in skinDataList)
            {
                if (!meshesBySkin.TryGetValue(skinIndex, out var skinMeshes))
                {
                    Warning($"Skin {skinIndex} '{skinName}' has no meshes — skipped", "CGltf");
                    continue;
                }
                var charAnims = ProcessAnimationsForCharacter(skinIndex, skinName, bim, data);
                if (charAnims.Count == 0)
                {
                    Warning($"No animations for character '{skinName}' — skipped", "CGltf");
                    continue;
                }
                var character = new AnimatedCharacter(
                    skinIndex, skinName, charAnims, skinMeshes,
                    MaterialToMeshMap, Nodes, bc, bim);
                Characters.Add(character);
                Info($"  Character '{skinName}': {bc} bones, {skinMeshes.Count} meshes, {charAnims.Count} anims", "CGltf");
            }

            // -----------------------------------------------------------------
            // Step 6 – node-only animations (no skins), or legacy compat
            // -----------------------------------------------------------------
            if (Characters.Count == 0 && data->animations_count > 0)
            {
                var nodeAnims = ProcessNodeAnimations(data);
                Animations.AddRange(nodeAnims);
                Info($"  Node animations: {nodeAnims.Count}", "CGltf");
            }
            else if (Characters.Count > 0 && Characters[0].Animation != null)
            {
                Animations.Add(Characters[0].Animation!);
            }

            // -----------------------------------------------------------------
            // Step 7 – cache animation flags; extract lights
            // -----------------------------------------------------------------
            CacheAnimationInfo();
            AnimationsReady = true;
            ExtractLightNodes(data);

            Info($"CGltfModel ready: {Nodes.Count} nodes, {Meshes.Count} meshes ({Characters.Count} chars, {StaticMeshes.Count} static), {TotalBoneCount} bones, {ModelLights.Count} lights", "CGltf");
        }

        // ====================================================================
        //  ProcessSkin
        // ====================================================================
        private (Dictionary<string, BoneInfo> bim, int count, HashSet<string> names) ProcessSkin(cgltf_skin* skin, int skinIndex)
        {
            var boneInfoMap = new Dictionary<string, BoneInfo>();
            var jointNames  = new HashSet<string>();
            int counter     = 0;

            for (int ji = 0; ji < (int)skin->joints_count; ji++)
            {
                cgltf_node* joint = skin->joints[ji];
                string name = PtrToStr(joint->name) ?? $"Joint_{skinIndex}_{ji}";
                jointNames.Add(name);
                if (boneInfoMap.ContainsKey(name)) continue;

                Matrix4x4 ibm = Matrix4x4.Identity;
                if (skin->inverse_bind_matrices != null)
                {
                    float* m = stackalloc float[16];
                    cgltf_accessor_read_float(*skin->inverse_bind_matrices, (nuint)ji, m, 16);
                    ibm = new Matrix4x4(
                        m[0], m[1], m[2], m[3],
                        m[4], m[5], m[6], m[7],
                        m[8], m[9], m[10], m[11],
                        m[12], m[13], m[14], m[15]);
                }
                boneInfoMap[name] = new BoneInfo { Id = counter++, Offset = ibm };
            }
            return (boneInfoMap, counter, jointNames);
        }

        // ====================================================================
        //  ProcessMesh  (SkinIndex assigned during ProcessNodeWithParent)
        // ====================================================================
        private void ProcessMesh(cgltf_primitive* prim, string baseDir)
        {
            if (prim->type != cgltf_primitive_type.cgltf_primitive_type_triangles)
            {
                Warning($"Skipping non-triangle primitive ({prim->type})", "CGltf");
                return;
            }

            // --- find attribute accessors ---
            cgltf_accessor* posAcc = null, normAcc = null, tanAcc = null;
            cgltf_accessor* tc0Acc = null, tc1Acc = null, col0Acc = null;
            cgltf_accessor* jnt0Acc = null, wgt0Acc = null;

            for (int ai = 0; ai < (int)prim->attributes_count; ai++)
            {
                var a = &prim->attributes[ai];
                switch (a->type)
                {
                    case cgltf_attribute_type.cgltf_attribute_type_position: posAcc  = a->data; break;
                    case cgltf_attribute_type.cgltf_attribute_type_normal:   normAcc = a->data; break;
                    case cgltf_attribute_type.cgltf_attribute_type_tangent:  tanAcc  = a->data; break;
                    case cgltf_attribute_type.cgltf_attribute_type_texcoord:
                        if (a->index == 0) tc0Acc = a->data;
                        else if (a->index == 1) tc1Acc = a->data;
                        break;
                    case cgltf_attribute_type.cgltf_attribute_type_color:
                        if (a->index == 0) col0Acc = a->data;
                        break;
                    case cgltf_attribute_type.cgltf_attribute_type_joints:
                        if (a->index == 0) jnt0Acc = a->data;
                        break;
                    case cgltf_attribute_type.cgltf_attribute_type_weights:
                        if (a->index == 0) wgt0Acc = a->data;
                        break;
                }
            }

            if (posAcc == null) { Warning("Skipping primitive without POSITION", "CGltf"); return; }

            int  vc         = (int)posAcc->count;
            bool hasSkin    = jnt0Acc != null && wgt0Acc != null;
            bool hasMorphs  = prim->targets_count > 0;
            bool need32bit  = vc > 65535;

            // --- batch-read attribute data into float arrays ---
            float[] posData  = UnpackAccessorFloats(posAcc,  vc * 3,  3);
            float[] normData = normAcc != null ? UnpackAccessorFloats(normAcc, vc * 3, 3)  : Array.Empty<float>();
            float[] tanData  = tanAcc  != null ? UnpackAccessorFloats(tanAcc,  vc * 4, 4)  : Array.Empty<float>();
            float[] tc0Data  = tc0Acc  != null ? UnpackAccessorFloats(tc0Acc,  vc * 2, 2)  : Array.Empty<float>();
            float[] tc1Data  = tc1Acc  != null ? UnpackAccessorFloats(tc1Acc,  vc * 2, 2)  : Array.Empty<float>();
            float[] colData  = col0Acc != null ? UnpackAccessorFloats(col0Acc, vc * 4, 4)  : Array.Empty<float>();
            float[] jntData  = jnt0Acc != null ? UnpackAccessorFloats(jnt0Acc, vc * 4, 4)  : Array.Empty<float>();
            float[] wgtData  = wgt0Acc != null ? UnpackAccessorFloats(wgt0Acc, vc * 4, 4)  : Array.Empty<float>();

            // --- read indices ---
            var indexList = new List<uint>();
            if (prim->indices != null)
            {
                int ic = (int)prim->indices->count;
                for (int i = 0; i < ic; i++)
                    indexList.Add((uint)cgltf_accessor_read_index(*prim->indices, (nuint)i));
            }
            else
            {
                for (int i = 0; i < vc; i++) indexList.Add((uint)i);
            }

            // --- calculate tangents if absent ---
            float[]? calcTanData = null;
            if (tanAcc == null && tc0Acc != null && normAcc != null)
            {
                var posArr  = Floats3ToVec3(posData,  vc);
                var normArr = Floats3ToVec3Array(normData, vc);
                var tc0Arr  = Floats2ToVec2(tc0Data,  vc);
                var calcTan = CalculateTangents(posArr, normArr, tc0Arr, indexList);
                calcTanData = new float[vc * 4];
                for (int i = 0; i < vc; i++)
                {
                    calcTanData[i * 4 + 0] = calcTan[i].X;
                    calcTanData[i * 4 + 1] = calcTan[i].Y;
                    calcTanData[i * 4 + 2] = calcTan[i].Z;
                    calcTanData[i * 4 + 3] = calcTan[i].W;
                }
                tanData = calcTanData;
            }

            // --- build Vertex array ---
            var vertices = new Vertex[vc];
            for (int i = 0; i < vc; i++)
            {
                ref Vertex v = ref vertices[i];
                v.Position  = new Vector3(posData[i*3], posData[i*3+1], posData[i*3+2]);
                v.Normal    = normData.Length > 0 ? new Vector3(normData[i*3], normData[i*3+1], normData[i*3+2]) : Vector3.UnitY;
                v.Tangent   = tanData.Length > 0  ? new Vector4(tanData[i*4], tanData[i*4+1], tanData[i*4+2], tanData[i*4+3]) : new Vector4(1, 0, 0, 1);
                v.TexCoord0 = tc0Data.Length > 0  ? new Vector2(tc0Data[i*2], tc0Data[i*2+1]) : Vector2.Zero;
                v.TexCoord1 = tc1Data.Length > 0  ? new Vector2(tc1Data[i*2], tc1Data[i*2+1]) : Vector2.Zero;
                v.Color     = colData.Length > 0  ? new Vector4(colData[i*4], colData[i*4+1], colData[i*4+2], colData[i*4+3]) : Vector4.One;
                v.Joints    = jntData.Length > 0  ? new Vector4(jntData[i*4], jntData[i*4+1], jntData[i*4+2], jntData[i*4+3]) : Vector4.Zero;
                v.BoneWeights = wgtData.Length > 0 ? new Vector4(wgtData[i*4], wgtData[i*4+1], wgtData[i*4+2], wgtData[i*4+3]) : Vector4.Zero;
            }

            // --- create Mesh ---
            Mesh mesh;
            if (need32bit)
                mesh = new Mesh(vertices, indexList.ToArray(), hasSkin);
            else
                mesh = new Mesh(vertices, indexList.Select(u => (ushort)u).ToArray(), hasSkin);

            // --- morph targets ---
            if (hasMorphs)
            {
                mesh.HasMorphTargets = true;
                mesh.CgltfPrimitive  = prim;   // keep pointer alive (CGltfModel owns _data)
                mesh.MorphTargetCount = (int)prim->targets_count;
            }

            // --- material ---
            if (prim->material != null)
                ProcessMaterial(prim->material, mesh, baseDir);

            Meshes.Add(mesh);
        }

        // ====================================================================
        //  ProcessMaterial
        // ====================================================================
        private void ProcessMaterial(cgltf_material* mat, Mesh mesh, string baseDir)
        {
            int matIndex = (int)cgltf_material_index(*_data, *mat);

            // PBR Metallic-Roughness
            if (mat->has_pbr_metallic_roughness != 0)
            {
                ref var pbr = ref mat->pbr_metallic_roughness;
                mesh.BaseColorFactor  = new Vector4(pbr.base_color_factor[0], pbr.base_color_factor[1],
                                                    pbr.base_color_factor[2], pbr.base_color_factor[3]);
                mesh.MetallicFactor   = pbr.metallic_factor;
                mesh.RoughnessFactor  = pbr.roughness_factor;
                LoadTexture(pbr.base_color_texture,        mesh, 0, true,  baseDir); // BaseColor (sRGB)
                LoadTexture(pbr.metallic_roughness_texture, mesh, 1, false, baseDir); // MetallicRoughness (linear)
            }
            else
            {
                mesh.BaseColorFactor = Vector4.One;
                mesh.MetallicFactor  = 0.0f;
                mesh.RoughnessFactor = 0.5f;
                mesh.Textures.Add(null); // slot 0
                mesh.Textures.Add(null); // slot 1
            }

            // Normal texture (slot 2)
            mesh.NormalMapScale = mat->normal_texture.scale != 0.0f ? mat->normal_texture.scale : 1.0f;
            LoadTexture(mat->normal_texture,    mesh, 2, false, baseDir);

            // Occlusion texture (slot 3)
            mesh.OcclusionStrength = mat->occlusion_texture.scale;
            if (mesh.OcclusionStrength == 0f) mesh.OcclusionStrength = 1.0f;
            LoadTexture(mat->occlusion_texture, mesh, 3, false, baseDir);

            // Emissive (slot 4)
            mesh.EmissiveFactor = new Vector3(mat->emissive_factor[0], mat->emissive_factor[1], mat->emissive_factor[2]);
            LoadTexture(mat->emissive_texture,  mesh, 4, true,  baseDir); // Emissive (sRGB)

            // KHR_materials_emissive_strength
            if (mat->has_emissive_strength != 0)
                mesh.EmissiveStrength = mat->emissive_strength.emissive_strength;
            else
                mesh.EmissiveStrength = 1.0f;

            // KHR_materials_ior
            if (mat->has_ior != 0) mesh.IOR = mat->ior.ior;
            else                   mesh.IOR = 1.5f;

            // KHR_materials_transmission
            if (mat->has_transmission != 0)
            {
                mesh.TransmissionFactor = mat->transmission.transmission_factor;
                if (mat->transmission.transmission_texture.texture != null)
                {
                    mesh.TransmissionTextureIndex = mesh.Textures.Count;
                    LoadTexture(mat->transmission.transmission_texture, mesh, mesh.Textures.Count, false, baseDir);
                }
                else mesh.TransmissionTextureIndex = -1;
            }
            else
            {
                mesh.TransmissionFactor       = 0f;
                mesh.TransmissionTextureIndex = -1;
            }

            // KHR_materials_volume
            if (mat->has_volume != 0)
            {
                mesh.ThicknessFactor  = mat->volume.thickness_factor;
                mesh.AttenuationDistance = mat->volume.attenuation_distance > 0
                    ? mat->volume.attenuation_distance : float.MaxValue;
                mesh.AttenuationColor = new Vector3(mat->volume.attenuation_color[0],
                                                    mat->volume.attenuation_color[1],
                                                    mat->volume.attenuation_color[2]);
                if (mat->volume.thickness_texture.texture != null)
                {
                    mesh.ThicknessTexCoord = mat->volume.thickness_texture.texcoord;
                    mesh.ThicknessTextureIndex = mesh.Textures.Count;
                    LoadTexture(mat->volume.thickness_texture, mesh, mesh.Textures.Count, false, baseDir);
                }
                else mesh.ThicknessTextureIndex = -1;
            }
            else if (mesh.TransmissionFactor > 0f)
            {
                mesh.ThicknessFactor      = 0f;
                mesh.AttenuationDistance  = float.MaxValue;
                mesh.AttenuationColor     = Vector3.One;
                mesh.ThicknessTextureIndex = -1;
            }
            else
            {
                mesh.ThicknessFactor       = 0f;
                mesh.AttenuationDistance   = float.MaxValue;
                mesh.AttenuationColor      = Vector3.One;
                mesh.ThicknessTextureIndex = -1;
            }

            // KHR_materials_clearcoat
            if (mat->has_clearcoat != 0)
            {
                mesh.ClearcoatFactor    = mat->clearcoat.clearcoat_factor;
                mesh.ClearcoatRoughness = mat->clearcoat.clearcoat_roughness_factor;
            }

            // Texture transforms — extract for each loaded texture view
            if (mat->has_pbr_metallic_roughness != 0)
            {
                ref var pbr = ref mat->pbr_metallic_roughness;
                ExtractTexTransform(pbr.base_color_texture, ref mesh.BaseColorTexOffset,
                    ref mesh.BaseColorTexRotation, ref mesh.BaseColorTexScale, ref mesh.HasBaseColorTexTransform);
                int pbrTC0 = pbr.base_color_texture.texcoord;
                int pbrTC1 = pbr.metallic_roughness_texture.texcoord;
                mesh.BaseColorTexCoord         = pbrTC0;
                mesh.MetallicRoughnessTexCoord = pbrTC1;
                ExtractTexTransform(pbr.metallic_roughness_texture, ref mesh.MetallicRoughnessTexOffset,
                    ref mesh.MetallicRoughnessTexRotation, ref mesh.MetallicRoughnessTexScale, ref mesh.HasMetallicRoughnessTexTransform);
            }
            ExtractTexTransform(mat->normal_texture, ref mesh.NormalTexOffset,
                ref mesh.NormalTexRotation, ref mesh.NormalTexScale, ref mesh.HasNormalTexTransform);
            mesh.NormalTexCoord = mat->normal_texture.texcoord;
            ExtractTexTransform(mat->occlusion_texture, ref mesh.OcclusionTexOffset,
                ref mesh.OcclusionTexRotation, ref mesh.OcclusionTexScale, ref mesh.HasOcclusionTexTransform);
            mesh.OcclusionTexCoord = mat->occlusion_texture.texcoord;
            ExtractTexTransform(mat->emissive_texture, ref mesh.EmissiveTexOffset,
                ref mesh.EmissiveTexRotation, ref mesh.EmissiveTexScale, ref mesh.HasEmissiveTexTransform);
            mesh.EmissiveTexCoord = mat->emissive_texture.texcoord;
            if (mat->has_transmission != 0)
                mesh.TransmissionTexCoord = mat->transmission.transmission_texture.texcoord;

            // Alpha mode and cutoff
            mesh.AlphaMode   = mat->alpha_mode switch
            {
                cgltf_alpha_mode.cgltf_alpha_mode_mask  => AlphaMode.MASK,
                cgltf_alpha_mode.cgltf_alpha_mode_blend => AlphaMode.BLEND,
                _                                       => AlphaMode.OPAQUE
            };
            mesh.AlphaCutoff = mat->alpha_cutoff;
            mesh.DoubleSided = mat->double_sided != 0;

            // Material-to-mesh map
            if (!MaterialToMeshMap.TryGetValue(matIndex, out var mml))
                MaterialToMeshMap[matIndex] = mml = new List<Mesh>();
            mml.Add(mesh);
        }

        // ====================================================================
        //  LoadTexture
        // ====================================================================
        private void LoadTexture(cgltf_texture_view view, Mesh mesh, int slotIndex,
                                  bool isSRGB, string baseDir)
        {
            if (view.texture == null)
            {
                mesh.Textures.Add(null);
                return;
            }

            cgltf_image* img = view.texture->image;
            if (img == null)
            {
                mesh.Textures.Add(null);
                return;
            }

            byte[]? imageData = null;

            if (img->buffer_view != null)
            {
                // Embedded buffer
                byte* ptr = cgltf_buffer_view_data(*img->buffer_view);
                if (ptr == null) { mesh.Textures.Add(null); return; }
                int   sz   = (int)img->buffer_view->size;
                imageData  = new byte[sz];
                Marshal.Copy((IntPtr)ptr, imageData, 0, sz);
            }
            else if (img->uri != IntPtr.Zero)
            {
                // External file
                string? uri = PtrToStr(img->uri);
                if (uri == null) { mesh.Textures.Add(null); return; }
                string fullPath = Path.Combine(baseDir, uri);
                // Prefer pre-fetched bytes (required on Web/iOS/Android where File I/O is unavailable)
                if (_preloadedTextures != null && _preloadedTextures.TryGetValue(fullPath, out var preloaded))
                {
                    imageData = preloaded;
                }
                else if (File.Exists(fullPath))
                {
                    imageData = File.ReadAllBytes(fullPath);
                }
                else
                {
                    Warning($"Texture file not found: '{fullPath}'", "CGltf");
                    mesh.Textures.Add(null);
                    return;
                }
            }

            if (imageData == null) { mesh.Textures.Add(null); return; }

            // Build sampler settings
            var samplerSettings = ExtractSamplerSettings(view.texture->sampler);
            sg_pixel_format fmt = isSRGB ? sg_pixel_format.SG_PIXELFORMAT_SRGB8A8
                                         : sg_pixel_format.SG_PIXELFORMAT_RGBA8;

            // Cache key includes sampler hash so identical images with different samplers are distinct
            string cacheKey = $"img_{(IntPtr)img:X}_{samplerSettings.GetHashCode()}";
            var texture = TextureCache.Instance.GetOrCreate(cacheKey, imageData, fmt, samplerSettings);
            mesh.Textures.Add(texture);
        }

        // ====================================================================
        //  ProcessNodeWithParent  — recursive walk of the scene node tree
        // ====================================================================
        private void ProcessNodeWithParent(cgltf_node* node, CGltfNode? parentRenderNode,
                                            cgltf_data* data,
                                            Dictionary<IntPtr, int> meshPtrToStart)
        {
            // Decompose local transform
            Vector3    pos   = Vector3.Zero;
            Quaternion rot   = Quaternion.Identity;
            Vector3    scale = Vector3.One;

            if (node->has_matrix != 0)
            {
                var mat = new Matrix4x4(
                    node->matrix[0],  node->matrix[1],  node->matrix[2],  node->matrix[3],
                    node->matrix[4],  node->matrix[5],  node->matrix[6],  node->matrix[7],
                    node->matrix[8],  node->matrix[9],  node->matrix[10], node->matrix[11],
                    node->matrix[12], node->matrix[13], node->matrix[14], node->matrix[15]);
                Matrix4x4.Decompose(mat, out scale, out rot, out pos);
            }
            else
            {
                if (node->has_translation != 0) pos   = new Vector3(node->translation[0], node->translation[1], node->translation[2]);
                if (node->has_rotation    != 0) rot   = new Quaternion(node->rotation[0], node->rotation[1], node->rotation[2], node->rotation[3]);
                if (node->has_scale       != 0) scale = new Vector3(node->scale[0], node->scale[1], node->scale[2]);
            }

            string? nodeName = PtrToStr(node->name);
            bool isSkinned   = nodeName != null && _skinnedNodeNames.Contains(nodeName);
            int nodeListIdx  = Nodes.Count; // index this node will occupy in Nodes

            CGltfNode? currentRenderNode = null;

            // Determine skin index for this node
            int skinIndex = -1;
            if (node->skin != null)
                skinIndex = (int)cgltf_skin_index(*data, *node->skin);

            // Morph weights from mesh (default weights)
            IReadOnlyList<float>? meshMorphWeights = null;
            if (node->mesh != null && node->mesh->weights_count > 0)
            {
                var mw = new float[(int)node->mesh->weights_count];
                for (int wi = 0; wi < mw.Length; wi++) mw[wi] = node->mesh->weights[wi];
                meshMorphWeights = mw;
            }

            // Node-level morph weights
            IReadOnlyList<float>? nodeMorphWeights = null;
            if (node->weights_count > 0)
            {
                var nw = new float[(int)node->weights_count];
                for (int wi = 0; wi < nw.Length; wi++) nw[wi] = node->weights[wi];
                nodeMorphWeights = nw;
            }

            if (node->mesh != null && meshPtrToStart.TryGetValue((IntPtr)node->mesh, out int meshStart))
            {
                int primCount = (int)node->mesh->primitives_count;
                for (int pi = 0; pi < primCount; pi++)
                {
                    int meshIdx = meshStart + pi;
                    if (meshIdx >= Meshes.Count) continue;

                    Meshes[meshIdx].SkinIndex = skinIndex;

                    var rn = new CGltfNode
                    {
                        Position         = pos,
                        Rotation         = rot,
                        Scale            = scale,
                        MeshIndex        = meshIdx,
                        NodeName         = nodeName,
                        HasAnimation     = false,
                        IsSkinned        = isSkinned,
                        Parent           = parentRenderNode,
                        NodeIndex        = nodeListIdx + pi,
                        NodeMorphWeights = nodeMorphWeights,
                        MeshMorphWeights = meshMorphWeights
                    };
                    Nodes.Add(rn);
                    if (currentRenderNode == null) currentRenderNode = rn;
                }
            }
            else
            {
                // Non-mesh node (skeleton, transform, light, camera)
                bool mightBeAnimated = nodeName != null;
                bool hasChildren     = node->children_count > 0;
                if (mightBeAnimated || hasChildren)
                {
                    currentRenderNode = new CGltfNode
                    {
                        Position     = pos,
                        Rotation     = rot,
                        Scale        = scale,
                        MeshIndex    = -1,
                        NodeName     = nodeName,
                        HasAnimation = false,
                        IsSkinned    = isSkinned,
                        Parent       = parentRenderNode,
                        NodeIndex    = nodeListIdx
                    };
                    Nodes.Add(currentRenderNode);
                }
            }

            // Children
            for (int ci = 0; ci < (int)node->children_count; ci++)
                ProcessNodeWithParent(node->children[ci], currentRenderNode ?? parentRenderNode, data, meshPtrToStart);
        }

        // ====================================================================
        //  ProcessAnimationsForCharacter  (filtered animation set per skin)
        // ====================================================================
        private List<CGltfAnimation> ProcessAnimationsForCharacter(
            int skinIndex, string characterName,
            Dictionary<string, BoneInfo> boneInfoMap,
            cgltf_data* data)
        {
            var result = new List<CGltfAnimation>();
            if (data->animations_count == 0) return result;

            var rootNode = BuildRootNodeHierarchy(data);

            for (int ai = 0; ai < (int)data->animations_count; ai++)
            {
                cgltf_animation* anim = &data->animations[ai];
                string animName = PtrToStr(anim->name) ?? $"Animation_{ai}";
                float  duration = ComputeAnimDuration(anim);
                float  tps      = 1.0f; // cgltf uses seconds

                var animation  = new CGltfAnimation(duration, tps, rootNode, boneInfoMap);
                animation.Name = animName;

                int boneChannelCount = 0, nodeChannelCount = 0;
                for (int ci = 0; ci < (int)anim->channels_count; ci++)
                {
                    cgltf_animation_channel* ch = &anim->channels[ci];
                    if (ch->target_node == null) continue;

                    string? targetName = PtrToStr(ch->target_node->name);
                    if (targetName == null) continue;

                    // Morph weight channels
                    if (ch->target_path == cgltf_animation_path_type.cgltf_animation_path_type_weights)
                    {
                        ParseMorphWeightChannel(ch, targetName, animation, data);
                        continue;
                    }

                    bool isBone      = boneInfoMap.ContainsKey(targetName);
                    bool isNodeAnim  = !_skinnedNodeNames.Contains(targetName);
                    if (!isBone && !isNodeAnim) continue;

                    int boneId = isBone ? boneInfoMap[targetName].Id : -1;

                    var bone = animation.FindBone(targetName);
                    if (bone == null)
                    {
                        bone = new CGltfBone(targetName, boneId);
                        animation.AddBone(bone);
                    }

                    var interp = ConvertInterpolation(ch->sampler->interpolation);
                    ParseBoneChannel(ch, bone, interp);

                    if (isBone) boneChannelCount++;
                    else        nodeChannelCount++;
                }

                if (animation.GetBones().Count > 0 || animation.MorphAnimations.Count > 0)
                {
                    result.Add(animation);
                    Info($"  Anim '{animName}' [char '{characterName}']: {boneChannelCount} bone ch, {nodeChannelCount} node ch, {duration:F2}s", "CGltf");
                }
            }
            return result;
        }

        // ====================================================================
        //  ProcessNodeAnimations  (no skins)
        // ====================================================================
        private List<CGltfAnimation> ProcessNodeAnimations(cgltf_data* data)
        {
            var result   = new List<CGltfAnimation>();
            var emptyBim = new Dictionary<string, BoneInfo>();
            var rootNode = BuildRootNodeHierarchy(data);

            for (int ai = 0; ai < (int)data->animations_count; ai++)
            {
                cgltf_animation* anim = &data->animations[ai];
                string animName = PtrToStr(anim->name) ?? $"Animation_{ai}";
                float  duration = ComputeAnimDuration(anim);

                var animation  = new CGltfAnimation(duration, 1.0f, rootNode, emptyBim);
                animation.Name = animName;

                for (int ci = 0; ci < (int)anim->channels_count; ci++)
                {
                    cgltf_animation_channel* ch = &anim->channels[ci];
                    if (ch->target_node == null) continue;

                    string? targetName = PtrToStr(ch->target_node->name);
                    if (targetName == null) continue;

                    if (ch->target_path == cgltf_animation_path_type.cgltf_animation_path_type_weights)
                    {
                        ParseMorphWeightChannel(ch, targetName, animation, data);
                        continue;
                    }

                    var bone = animation.FindBone(targetName);
                    if (bone == null)
                    {
                        bone = new CGltfBone(targetName, -1);
                        animation.AddBone(bone);
                    }
                    ParseBoneChannel(ch, bone, ConvertInterpolation(ch->sampler->interpolation));
                }

                if (animation.GetBones().Count > 0 || animation.MorphAnimations.Count > 0)
                {
                    result.Add(animation);
                    Info($"  Node anim '{animName}': {animation.GetBones().Count} animated nodes, {duration:F2}s", "CGltf");
                }
            }
            return result;
        }

        // ====================================================================
        //  ParseBoneChannel  — fills a CGltfBone from a translation/rotation/scale channel
        // ====================================================================
        private void ParseBoneChannel(cgltf_animation_channel* ch, CGltfBone bone, CGltfInterpolation interp)
        {
            cgltf_animation_sampler* samp = ch->sampler;
            int nKeys = (int)samp->input->count;
            if (nKeys == 0) return;

            float[] times = UnpackAccessorFloats(samp->input, nKeys, 1);

            switch (ch->target_path)
            {
                case cgltf_animation_path_type.cgltf_animation_path_type_translation:
                {
                    int valCount = interp == CGltfInterpolation.CubicSpline ? nKeys * 3 : nKeys;
                    float[] raw = UnpackAccessorFloats(samp->output, valCount * 3, 3);
                    var vals = new Vector3[valCount];
                    for (int i = 0; i < valCount; i++)
                        vals[i] = new Vector3(raw[i*3], raw[i*3+1], raw[i*3+2]);
                    bone.SetTranslationKeys(times, vals, interp);
                    break;
                }
                case cgltf_animation_path_type.cgltf_animation_path_type_rotation:
                {
                    int valCount = interp == CGltfInterpolation.CubicSpline ? nKeys * 3 : nKeys;
                    float[] raw = UnpackAccessorFloats(samp->output, valCount * 4, 4);
                    var vals = new Quaternion[valCount];
                    for (int i = 0; i < valCount; i++)
                        vals[i] = new Quaternion(raw[i*4], raw[i*4+1], raw[i*4+2], raw[i*4+3]);
                    bone.SetRotationKeys(times, vals, interp);
                    break;
                }
                case cgltf_animation_path_type.cgltf_animation_path_type_scale:
                {
                    int valCount = interp == CGltfInterpolation.CubicSpline ? nKeys * 3 : nKeys;
                    float[] raw = UnpackAccessorFloats(samp->output, valCount * 3, 3);
                    var vals = new Vector3[valCount];
                    for (int i = 0; i < valCount; i++)
                        vals[i] = new Vector3(raw[i*3], raw[i*3+1], raw[i*3+2]);
                    bone.SetScaleKeys(times, vals, interp);
                    break;
                }
            }
        }

        // ====================================================================
        //  ParseMorphWeightChannel
        // ====================================================================
        private void ParseMorphWeightChannel(cgltf_animation_channel* ch, string targetName,
                                              CGltfAnimation animation, cgltf_data* data)
        {
            cgltf_animation_sampler* samp = ch->sampler;
            int nKeys = (int)samp->input->count;
            if (nKeys == 0) return;

            // Find matching render node by name to get node index
            int nodeIdx = -1;
            for (int ni = 0; ni < Nodes.Count; ni++)
            {
                if (Nodes[ni].NodeName == targetName) { nodeIdx = ni; break; }
            }

            // Derive number of morph targets from output accessor element count
            int totalOutputVals = (int)samp->output->count;
            int numTargets = nKeys > 0 ? totalOutputVals / nKeys : 0;
            if (numTargets == 0) return;

            float[] times  = UnpackAccessorFloats(samp->input,  nKeys, 1);
            float[] values = UnpackAccessorFloats(samp->output, totalOutputVals, 1);

            var morphAnim = new MorphWeightAnimation
            {
                NodeIndex = nodeIdx,
                NodeName  = targetName
            };
            for (int ki = 0; ki < nKeys; ki++)
            {
                var w = new float[numTargets];
                for (int ti = 0; ti < numTargets; ti++)
                    w[ti] = values[ki * numTargets + ti];
                morphAnim.Keyframes.Add((times[ki], w));
            }
            animation.MorphAnimations.Add(morphAnim);
        }

        // ====================================================================
        //  BuildRootNodeHierarchy  — build CGltfNodeData tree for animator
        // ====================================================================
        private CGltfNodeData BuildRootNodeHierarchy(cgltf_data* data)
        {
            var root = new CGltfNodeData
            {
                Name      = "SceneRoot",
                Transformation = Matrix4x4.Identity,
                Children  = new List<CGltfNodeData>(),
                ChildrenCount = 0
            };

            cgltf_scene* scene = data->scene != null ? data->scene
                               : data->scenes_count > 0 ? data->scenes : null;
            if (scene != null)
            {
                for (int ni = 0; ni < (int)scene->nodes_count; ni++)
                {
                    root.Children.Add(BuildNodeData(scene->nodes[ni]));
                    root.ChildrenCount++;
                }
            }
            return root;
        }

        private CGltfNodeData BuildNodeData(cgltf_node* node)
        {
            // Build local transform matrix
            float* m16 = stackalloc float[16];
            cgltf_node_transform_local(*node, m16);
            var localMatrix = new Matrix4x4(
                m16[0], m16[1], m16[2],  m16[3],
                m16[4], m16[5], m16[6],  m16[7],
                m16[8], m16[9], m16[10], m16[11],
                m16[12],m16[13],m16[14], m16[15]);

            string name = PtrToStr(node->name) ?? "";
            var data  = new CGltfNodeData
            {
                Name           = name,
                Transformation = localMatrix,
                Children       = new List<CGltfNodeData>(),
                ChildrenCount  = (int)node->children_count
            };
            for (int ci = 0; ci < (int)node->children_count; ci++)
                data.Children.Add(BuildNodeData(node->children[ci]));
            return data;
        }

        // ====================================================================
        //  ExtractLightNodes   (KHR_lights_punctual)
        // ====================================================================
        private void ExtractLightNodes(cgltf_data* data)
        {
            for (int ni = 0; ni < (int)data->nodes_count; ni++)
            {
                cgltf_node* node = &data->nodes[ni];
                if (node->light == null) continue;

                cgltf_light* light = node->light;

                // World transform
                float* wm = stackalloc float[16];
                cgltf_node_transform_world(*node, wm);
                var worldMat = new Matrix4x4(
                    wm[0], wm[1], wm[2],  wm[3],
                    wm[4], wm[5], wm[6],  wm[7],
                    wm[8], wm[9], wm[10], wm[11],
                    wm[12],wm[13],wm[14], wm[15]);

                string? nname = PtrToStr(node->name);
                var wrapperNode = nname != null ? Nodes.FirstOrDefault(n => n.NodeName == nname) : null;

                var li = new ModelLightInfo
                {
                    LightType       = light->type,
                    Color           = new Vector3(light->color[0], light->color[1], light->color[2]),
                    Intensity       = light->intensity,
                    Range           = light->range > 0 ? light->range : 1.0f,
                    InnerConeAngle  = (float)(light->spot_inner_cone_angle * 180.0 / Math.PI),
                    OuterConeAngle  = (float)(light->spot_outer_cone_angle * 180.0 / Math.PI),
                    WorldTransform  = worldMat,
                    NodeName        = nname ?? "",
                    WrapperNode     = wrapperNode
                };
                ModelLights.Add(li);
            }
        }

        // ====================================================================
        //  CacheAnimationInfo
        // ====================================================================
        private void CacheAnimationInfo()
        {
            var animatedNames = new HashSet<string>();
            foreach (var c in Characters)
            {
                if (c.Animation != null)
                    foreach (var b in c.Animation.GetBones())
                        if (b.Name != null) animatedNames.Add(b.Name);
            }
            if (Animations.Count > 0 && Animations[CurrentAnimationIndex] != null)
                foreach (var b in Animations[CurrentAnimationIndex].GetBones())
                    if (b.Name != null) animatedNames.Add(b.Name);

            foreach (var n in Nodes)
                if (n.NodeName != null)
                    n.HasAnimation = animatedNames.Contains(n.NodeName);
        }

        // ====================================================================
        //  CalculateModelBounds
        // ====================================================================
        public BoundingBox CalculateModelBounds()
        {
            if (Meshes.Count == 0 || Nodes.Count == 0)
                return new BoundingBox(Vector3.Zero, Vector3.Zero);

            bool    ok  = false;
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            foreach (var node in Nodes)
            {
                if (node.MeshIndex < 0 || node.MeshIndex >= Meshes.Count) continue;
                var bb = Meshes[node.MeshIndex].Bounds.Transform(node.WorldTransform);
                if (!ok) { min = bb.Min; max = bb.Max; ok = true; }
                else      { min = Vector3.Min(min, bb.Min); max = Vector3.Max(max, bb.Max); }
            }

            return ok ? new BoundingBox(min, max) : new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        // ====================================================================
        //  Animation navigation helpers (legacy compat)
        // ====================================================================
        public int    GetAnimationCount()       => Animations.Count;
        public string GetCurrentAnimationName() => HasAnimations ? Animations[CurrentAnimationIndex].Name : "None";

        public void SetCurrentAnimation(int index)
        {
            if (index >= 0 && index < Animations.Count)
                CurrentAnimationIndex = index;
        }

        public void NextAnimation()
        {
            if (Animations.Count > 0)
                CurrentAnimationIndex = (CurrentAnimationIndex + 1) % Animations.Count;
        }

        public void PreviousAnimation()
        {
            if (Animations.Count > 0)
                CurrentAnimationIndex = (CurrentAnimationIndex - 1 + Animations.Count) % Animations.Count;
        }

        // ====================================================================
        //  Dispose
        // ====================================================================
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_data != null)
            {
                cgltf_free(_data);
                _data = null;
            }
            // Release all pinned managed buffers AFTER cgltf_free (cgltf must not touch them after free).
            foreach (var h in _pinnedBuffers)
                if (h.IsAllocated) h.Free();
            _pinnedBuffers.Clear();
            foreach (var mesh in Meshes) mesh.Dispose();
            Meshes.Clear();
            foreach (var mesh in StaticMeshes) mesh.Dispose();
            StaticMeshes.Clear();
            foreach (var c in Characters) c.Dispose();
            GC.SuppressFinalize(this);
        }

        ~CGltfModel() => Dispose();

        // ====================================================================
        //  Helper utilities
        // ====================================================================
        private static string? PtrToStr(IntPtr p) =>
            p != IntPtr.Zero ? Marshal.PtrToStringUTF8(p) : null;

        private static float[] UnpackAccessorFloats(cgltf_accessor* acc, int totalFloats, int elemSize)
        {
            if (acc == null || totalFloats <= 0) return Array.Empty<float>();
            var buf = new float[totalFloats];
            fixed (float* pBuf = buf)
                cgltf_accessor_unpack_floats(*acc, pBuf, (nuint)totalFloats);
            return buf;
        }

        private static Vector3[] Floats3ToVec3(float[] data, int count)
        {
            var r = new Vector3[count];
            for (int i = 0; i < count; i++) r[i] = new Vector3(data[i*3], data[i*3+1], data[i*3+2]);
            return r;
        }

        // Alias with different name to avoid duplicate method error
        private static Vector3[] Floats3ToVec3Array(float[] data, int count) => Floats3ToVec3(data, count);

        private static Vector2[] Floats2ToVec2(float[] data, int count)
        {
            var r = new Vector2[count];
            for (int i = 0; i < count; i++) r[i] = new Vector2(data[i*2], data[i*2+1]);
            return r;
        }

        private static void ExtractTexTransform(cgltf_texture_view view,
            ref Vector2 offset, ref float rotation, ref Vector2 scale, ref bool hasTransform)
        {
            if (view.has_transform == 0) return;
            offset = new Vector2(view.transform.offset[0], view.transform.offset[1]);
            rotation = view.transform.rotation;
            scale    = new Vector2(view.transform.scale[0],  view.transform.scale[1]);
            hasTransform = offset != Vector2.Zero || rotation != 0.0f || scale != Vector2.One;
        }

        private static SamplerSettings ExtractSamplerSettings(cgltf_sampler* sampler)
        {
            var s = new SamplerSettings();
            if (sampler == null)
            {
                s.MinFilter    = sg_filter.SG_FILTER_LINEAR;
                s.MagFilter    = sg_filter.SG_FILTER_LINEAR;
                s.MipmapFilter = sg_filter.SG_FILTER_LINEAR;
                s.WrapU        = sg_wrap.SG_WRAP_REPEAT;
                s.WrapV        = sg_wrap.SG_WRAP_REPEAT;
                return s;
            }

            s.MagFilter = sampler->mag_filter switch
            {
                cgltf_filter_type.cgltf_filter_type_nearest => sg_filter.SG_FILTER_NEAREST,
                _                                           => sg_filter.SG_FILTER_LINEAR
            };

            s.MinFilter = sampler->min_filter switch
            {
                cgltf_filter_type.cgltf_filter_type_nearest              => sg_filter.SG_FILTER_NEAREST,
                cgltf_filter_type.cgltf_filter_type_nearest_mipmap_nearest => sg_filter.SG_FILTER_NEAREST,
                cgltf_filter_type.cgltf_filter_type_nearest_mipmap_linear  => sg_filter.SG_FILTER_NEAREST,
                _                                                          => sg_filter.SG_FILTER_LINEAR
            };

            s.MipmapFilter = sampler->min_filter switch
            {
                cgltf_filter_type.cgltf_filter_type_nearest_mipmap_nearest => sg_filter.SG_FILTER_NEAREST,
                cgltf_filter_type.cgltf_filter_type_linear_mipmap_nearest  => sg_filter.SG_FILTER_NEAREST,
                cgltf_filter_type.cgltf_filter_type_nearest_mipmap_linear  => sg_filter.SG_FILTER_LINEAR,
                cgltf_filter_type.cgltf_filter_type_linear_mipmap_linear   => sg_filter.SG_FILTER_LINEAR,
                _                                                           => sg_filter.SG_FILTER_NEAREST
            };

            s.WrapU = sampler->wrap_s switch
            {
                cgltf_wrap_mode.cgltf_wrap_mode_clamp_to_edge    => sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
                cgltf_wrap_mode.cgltf_wrap_mode_mirrored_repeat  => sg_wrap.SG_WRAP_MIRRORED_REPEAT,
                _                                                 => sg_wrap.SG_WRAP_REPEAT
            };

            s.WrapV = sampler->wrap_t switch
            {
                cgltf_wrap_mode.cgltf_wrap_mode_clamp_to_edge    => sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
                cgltf_wrap_mode.cgltf_wrap_mode_mirrored_repeat  => sg_wrap.SG_WRAP_MIRRORED_REPEAT,
                _                                                 => sg_wrap.SG_WRAP_REPEAT
            };

            return s;
        }

        private static float ComputeAnimDuration(cgltf_animation* anim)
        {
            float maxTime = 0f;
            for (int ci = 0; ci < (int)anim->channels_count; ci++)
            {
                var ch  = &anim->channels[ci];
                if (ch->sampler == null || ch->sampler->input == null) continue;
                int cnt  = (int)ch->sampler->input->count;
                if (cnt == 0) continue;
                float* last = stackalloc float[1];
                cgltf_accessor_read_float(*ch->sampler->input, (nuint)(cnt - 1), last, 1);
                if (last[0] > maxTime) maxTime = last[0];
            }
            return maxTime > 0f ? maxTime : 1.0f;
        }

        private static CGltfInterpolation ConvertInterpolation(cgltf_interpolation_type t) => t switch
        {
            cgltf_interpolation_type.cgltf_interpolation_type_step         => CGltfInterpolation.Step,
            cgltf_interpolation_type.cgltf_interpolation_type_cubic_spline => CGltfInterpolation.CubicSpline,
            _                                                               => CGltfInterpolation.Linear
        };

        // ====================================================================
        //  Tangent calculation  (Lengyel method, copied from SharpGltfModel)
        // ====================================================================
        private static List<Vector4> CalculateTangents(
            Vector3[] positions, Vector3[] normals, Vector2[] texCoords, List<uint> indices)
        {
            int vc = positions.Length;
            var tan1 = new Vector3[vc];
            var tan2 = new Vector3[vc];

            for (int ii = 0; ii < indices.Count - 2; ii += 3)
            {
                int  i0 = (int)indices[ii], i1 = (int)indices[ii+1], i2 = (int)indices[ii+2];
                Vector3 p0 = positions[i0], p1 = positions[i1], p2 = positions[i2];
                Vector2 u0 = texCoords[i0], u1 = texCoords[i1], u2 = texCoords[i2];

                float x1 = p1.X - p0.X, x2 = p2.X - p0.X;
                float y1 = p1.Y - p0.Y, y2 = p2.Y - p0.Y;
                float z1 = p1.Z - p0.Z, z2 = p2.Z - p0.Z;
                float s1 = u1.X - u0.X, s2 = u2.X - u0.X;
                float t1 = u1.Y - u0.Y, t2 = u2.Y - u0.Y;
                float denom = s1 * t2 - s2 * t1;
                if (MathF.Abs(denom) < 1e-7f) continue;
                float r  = 1.0f / denom;
                var   sdir = new Vector3((t2*x1 - t1*x2)*r, (t2*y1 - t1*y2)*r, (t2*z1 - t1*z2)*r);
                var   tdir = new Vector3((s1*x2 - s2*x1)*r, (s1*y2 - s2*y1)*r, (s1*z2 - s2*z1)*r);

                tan1[i0] += sdir; tan1[i1] += sdir; tan1[i2] += sdir;
                tan2[i0] += tdir; tan2[i1] += tdir; tan2[i2] += tdir;
            }

            var tangents = new List<Vector4>(vc);
            for (int i = 0; i < vc; i++)
            {
                var n = normals[i];
                var t = tan1[i];
                var ortho  = Vector3.Normalize(t - n * Vector3.Dot(n, t));
                float hand = Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0.0f ? -1.0f : 1.0f;
                tangents.Add(new Vector4(ortho.X, ortho.Y, ortho.Z, hand));
            }
            return tangents;
        }
    }
}
