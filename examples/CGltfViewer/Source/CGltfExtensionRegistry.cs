// CGltfExtensionRegistry.cs
// Hook system for GLTF extensions that cgltf does not natively parse.
//
// cgltf stores the raw JSON of every unrecognised extension in the
// `extensions[]` array that lives on each cgltf object.  This registry
// lets application code register handlers for specific extension names;
// CGltfModel calls Dispatch* at the right moments so the handlers can
// read the JSON, access keyframe accessors, and populate the model.
//
// Example — handle KHR_animation_pointer on animation channels:
//
//   CGltfExtensionRegistry.RegisterAnimationChannelHandler(
//       "KHR_animation_pointer",
//       ctx => {
//           // ctx.ExtensionJson  == {"pointer":"/materials/0/..."}
//           // ctx.ReadTimes()    == float[] of keyframe times
//           // ctx.ReadOutputFloats(4) == flat RGBA values
//           ctx.Animation.MaterialAnimations.Add(/* build MaterialPropertyAnimation */);
//       });
//
// Registration should happen in Init.cs before any model is loaded.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Sokol.CGltf;

namespace Sokol
{
    // -------------------------------------------------------------------------
    // Context: animation channel with null target_node (KHR_animation_pointer)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Passed to handlers registered via
    /// <see cref="CGltfExtensionRegistry.RegisterAnimationChannelHandler"/>.
    /// Provided for each animation channel whose <c>target_node</c> is null,
    /// i.e. channels that route through an extension such as KHR_animation_pointer.
    /// </summary>
    public unsafe class CGltfAnimationChannelExtContext
    {
        /// <summary>The extension name that triggered this call, e.g. "KHR_animation_pointer".</summary>
        public string ExtensionName { get; }

        /// <summary>Raw JSON string for this extension on the channel, e.g. <c>{"pointer":"/materials/0/..."}</c>.</summary>
        public string ExtensionJson { get; }

        /// <summary>Pointer to the raw cgltf animation channel.</summary>
        public cgltf_animation_channel* Channel { get; }

        /// <summary>Pointer to the full cgltf_data so handlers can resolve material/node indices.</summary>
        public cgltf_data* Data { get; }

        /// <summary>The CGltfAnimation being built; handlers should add results here.</summary>
        public CGltfAnimation Animation { get; }

        /// <summary>Index of the parent animation in <c>data->animations</c>.</summary>
        public int AnimationIndex { get; }

        /// <summary>Index of this channel in the parent animation's channel array.</summary>
        public int ChannelIndex { get; }

        internal CGltfAnimationChannelExtContext(
            string extName, string extJson,
            cgltf_animation_channel* ch, cgltf_data* data,
            CGltfAnimation animation, int animIdx, int channelIdx)
        {
            ExtensionName  = extName;
            ExtensionJson  = extJson;
            Channel        = ch;
            Data           = data;
            Animation      = animation;
            AnimationIndex = animIdx;
            ChannelIndex   = channelIdx;
        }

        // ------------------------------------------------------------------
        // Accessor helpers
        // ------------------------------------------------------------------

        /// <summary>Read the time input accessor of the channel's sampler.</summary>
        public float[] ReadTimes()
        {
            var samp = Channel->sampler;
            if (samp == null || samp->input == null) return Array.Empty<float>();
            int n = (int)samp->input->count;
            return CGltfModel.UnpackAccessorFloats(samp->input, n, 1);
        }

        /// <summary>
        /// Read the output accessor as a flat float array.
        /// Pass <paramref name="componentsPerKey"/> matching the data layout
        /// (e.g. 1 for scalar, 2 for VEC2, 3 for VEC3, 4 for VEC4/colour).
        /// </summary>
        public float[] ReadOutputFloats(int componentsPerKey = 1)
        {
            var samp = Channel->sampler;
            if (samp == null || samp->output == null) return Array.Empty<float>();
            int n = (int)samp->output->count;
            return CGltfModel.UnpackAccessorFloats(samp->output, n * componentsPerKey, componentsPerKey);
        }

        // ------------------------------------------------------------------
        // Convenience: scan channel-level extensions by name
        // ------------------------------------------------------------------

        /// <summary>Return the raw JSON data of a different extension on this channel, or null.</summary>
        public string? FindChannelExtensionJson(string name)
        {
            for (int i = 0; i < (int)Channel->extensions_count; i++)
            {
                var ext = &Channel->extensions[i];
                if (CGltfExtensionRegistry.PtrStr(ext->name) == name)
                    return CGltfExtensionRegistry.PtrStr(ext->data);
            }
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Context: node-level extension
    // -------------------------------------------------------------------------

    /// <summary>
    /// Passed to handlers registered via
    /// <see cref="CGltfExtensionRegistry.RegisterNodeHandler"/>.
    /// Provided for each node that carries the registered extension.
    /// </summary>
    public unsafe class CGltfNodeExtContext
    {
        /// <summary>The extension name that triggered this call.</summary>
        public string ExtensionName { get; }

        /// <summary>Raw JSON string for this extension on the node.</summary>
        public string ExtensionJson { get; }

        /// <summary>Pointer to the raw cgltf node.</summary>
        public cgltf_node* Node { get; }

        /// <summary>Pointer to the full cgltf_data.</summary>
        public cgltf_data* Data { get; }

        /// <summary>Index of this node in <c>data->nodes</c>.</summary>
        public int NodeIndex { get; }

        internal CGltfNodeExtContext(
            string extName, string extJson,
            cgltf_node* node, cgltf_data* data, int nodeIdx)
        {
            ExtensionName = extName;
            ExtensionJson = extJson;
            Node      = node;
            Data      = data;
            NodeIndex = nodeIdx;
        }
    }

    // -------------------------------------------------------------------------
    // Context: material-level extension
    // -------------------------------------------------------------------------

    /// <summary>
    /// Passed to handlers registered via
    /// <see cref="CGltfExtensionRegistry.RegisterMaterialHandler"/>.
    /// Provided for each material that carries the registered extension.
    /// </summary>
    public unsafe class CGltfMaterialExtContext
    {
        /// <summary>The extension name that triggered this call.</summary>
        public string ExtensionName { get; }

        /// <summary>Raw JSON string for this extension on the material.</summary>
        public string ExtensionJson { get; }

        /// <summary>Pointer to the raw cgltf material.</summary>
        public cgltf_material* Material { get; }

        /// <summary>Pointer to the full cgltf_data.</summary>
        public cgltf_data* Data { get; }

        /// <summary>Index of this material in <c>data->materials</c>.</summary>
        public int MaterialIndex { get; }

        internal CGltfMaterialExtContext(
            string extName, string extJson,
            cgltf_material* material, cgltf_data* data, int matIdx)
        {
            ExtensionName = extName;
            ExtensionJson = extJson;
            Material      = material;
            Data          = data;
            MaterialIndex = matIdx;
        }
    }

    // -------------------------------------------------------------------------
    // CGltfExtensionRegistry
    // -------------------------------------------------------------------------

    /// <summary>
    /// Static registry for application-level GLTF extension handlers.<br/>
    /// Register handlers before loading any model (typically in <c>Init.cs</c>).
    /// CGltfModel automatically dispatches to registered handlers during parsing.
    /// </summary>
    public static unsafe class CGltfExtensionRegistry
    {
        // -- handler tables --------------------------------------------------

        private static readonly Dictionary<string, List<Action<CGltfAnimationChannelExtContext>>>
            _animChannelHandlers = new();

        private static readonly Dictionary<string, List<Action<CGltfNodeExtContext>>>
            _nodeHandlers = new();

        private static readonly Dictionary<string, List<Action<CGltfMaterialExtContext>>>
            _materialHandlers = new();

        // -- Registration ----------------------------------------------------

        /// <summary>
        /// Register a handler for animation channels whose <c>target_node</c> is null,
        /// e.g. KHR_animation_pointer channels.
        /// The handler is called once per channel per matching extension.
        /// </summary>
        public static void RegisterAnimationChannelHandler(
            string extensionName,
            Action<CGltfAnimationChannelExtContext> handler)
        {
            if (!_animChannelHandlers.TryGetValue(extensionName, out var list))
                _animChannelHandlers[extensionName] = list = new List<Action<CGltfAnimationChannelExtContext>>();
            list.Add(handler);
        }

        /// <summary>
        /// Register a handler called for every node carrying <paramref name="extensionName"/>.
        /// </summary>
        public static void RegisterNodeHandler(
            string extensionName,
            Action<CGltfNodeExtContext> handler)
        {
            if (!_nodeHandlers.TryGetValue(extensionName, out var list))
                _nodeHandlers[extensionName] = list = new List<Action<CGltfNodeExtContext>>();
            list.Add(handler);
        }

        /// <summary>
        /// Register a handler called for every material carrying <paramref name="extensionName"/>.
        /// </summary>
        public static void RegisterMaterialHandler(
            string extensionName,
            Action<CGltfMaterialExtContext> handler)
        {
            if (!_materialHandlers.TryGetValue(extensionName, out var list))
                _materialHandlers[extensionName] = list = new List<Action<CGltfMaterialExtContext>>();
            list.Add(handler);
        }

        /// <summary>Remove all registered handlers.</summary>
        public static void Clear()
        {
            _animChannelHandlers.Clear();
            _nodeHandlers.Clear();
            _materialHandlers.Clear();
        }

        // -- Queries ---------------------------------------------------------

        /// <returns>True if any handler is registered for animation channels with this extension name.</returns>
        public static bool HasAnimationChannelHandler(string name) =>
            _animChannelHandlers.ContainsKey(name);

        /// <returns>True if any handler is registered for nodes with this extension name.</returns>
        public static bool HasNodeHandler(string name) =>
            _nodeHandlers.ContainsKey(name);

        /// <returns>True if any handler is registered for materials with this extension name.</returns>
        public static bool HasMaterialHandler(string name) =>
            _materialHandlers.ContainsKey(name);

        /// <returns>True if any node extension handler is registered (any name).</returns>
        public static bool HasAnyNodeHandler => _nodeHandlers.Count > 0;

        /// <returns>True if any material extension handler is registered (any name).</returns>
        public static bool HasAnyMaterialHandler => _materialHandlers.Count > 0;

        // -- Internal dispatch (called from CGltfModel) ----------------------

        /// <summary>
        /// Called by CGltfModel for every animation channel where target_node is null.
        /// Walks the channel's extensions[] and fires registered handlers.
        /// </summary>
        internal static void DispatchAnimationChannel(
            cgltf_animation_channel* ch, cgltf_data* data,
            CGltfAnimation animation, int animIdx, int channelIdx)
        {
            if (_animChannelHandlers.Count == 0) return;

            for (int ei = 0; ei < (int)ch->extensions_count; ei++)
            {
                var ext  = &ch->extensions[ei];
                string? name = PtrStr(ext->name);
                string? json = PtrStr(ext->data);
                if (name == null) continue;
                if (!_animChannelHandlers.TryGetValue(name, out var handlers)) continue;

                var ctx = new CGltfAnimationChannelExtContext(
                    name, json ?? "", ch, data, animation, animIdx, channelIdx);
                foreach (var h in handlers) h(ctx);
            }
        }

        /// <summary>
        /// Called by CGltfModel for each node after loading.
        /// Fires registered node handlers for any matching extension.
        /// </summary>
        internal static void DispatchNode(cgltf_node* node, cgltf_data* data, int nodeIdx)
        {
            if (_nodeHandlers.Count == 0) return;

            for (int ei = 0; ei < (int)node->extensions_count; ei++)
            {
                var ext  = &node->extensions[ei];
                string? name = PtrStr(ext->name);
                string? json = PtrStr(ext->data);
                if (name == null) continue;
                if (!_nodeHandlers.TryGetValue(name, out var handlers)) continue;

                var ctx = new CGltfNodeExtContext(name, json ?? "", node, data, nodeIdx);
                foreach (var h in handlers) h(ctx);
            }
        }

        /// <summary>
        /// Called by CGltfModel for each material after loading.
        /// Fires registered material handlers for any matching extension.
        /// </summary>
        internal static void DispatchMaterial(cgltf_material* material, cgltf_data* data, int matIdx)
        {
            if (_materialHandlers.Count == 0) return;

            for (int ei = 0; ei < (int)material->extensions_count; ei++)
            {
                var ext  = &material->extensions[ei];
                string? name = PtrStr(ext->name);
                string? json = PtrStr(ext->data);
                if (name == null) continue;
                if (!_materialHandlers.TryGetValue(name, out var handlers)) continue;

                var ctx = new CGltfMaterialExtContext(name, json ?? "", material, data, matIdx);
                foreach (var h in handlers) h(ctx);
            }
        }

        // -- Internal helpers ------------------------------------------------

        /// <summary>Convert a native UTF-8 char* (stored as IntPtr) to a managed string.</summary>
        internal static string? PtrStr(IntPtr p) =>
            p != IntPtr.Zero ? Marshal.PtrToStringUTF8(p) : null;
    }
}
