using System;
using Sokol;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Numerics;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SG.sg_vertex_format;
using static Sokol.SG.sg_index_type;
using static Sokol.SG.sg_cull_mode;
using static Sokol.SG.sg_compare_func;
using static Sokol.Utils;
using System.Diagnostics;
using static Sokol.SLog;
using static Sokol.SDebugUI;
using static Sokol.SImgui;
using Imgui;
using static Imgui.ImguiNative;
using static manifold_sapp_shader_cs.Shaders;
using static Manifold.Manifoldc;

public static unsafe class ManifoldappApp
{
    // ---- Constants ----
    // 500k verts * 6 floats/vert, 1000k tris * 3 uints/tri
    const int MAX_VERTS_FLOATS = 3_000_000;
    const int MAX_INDICES      = 3_000_000;

    static readonly string[] SampleNames =
    {
        "Tetrahedron",
        "Cube",
        "Sphere",
        "Cylinder",
        "CSG: Union",
        "CSG: Difference",
        "CSG: Intersection",
        "Perforated Cube",
        "Wireframe Cube",
        "Torus",
        "Rounded Frame",
        "Twisted Column",
        "Menger Sponge",
        "Gyroid",
        "Auger",
        "Heart",
        "Torus Knot",
        "Scallop",
        "Stretchy Bracelet",
    };

    static readonly Vector4[] SampleColors =
    {
        new Vector4(0.90f, 0.40f, 0.35f, 1),
        new Vector4(0.30f, 0.70f, 0.95f, 1),
        new Vector4(0.30f, 0.90f, 0.55f, 1),
        new Vector4(0.95f, 0.75f, 0.30f, 1),
        new Vector4(0.70f, 0.50f, 0.95f, 1),
        new Vector4(0.95f, 0.90f, 0.30f, 1),
        new Vector4(0.95f, 0.55f, 0.75f, 1),
        new Vector4(0.50f, 0.85f, 0.85f, 1),
        new Vector4(0.75f, 0.75f, 0.75f, 1),
        new Vector4(0.85f, 0.55f, 0.30f, 1),  // Torus
        new Vector4(0.40f, 0.80f, 0.55f, 1),  // Rounded Frame
        new Vector4(0.65f, 0.45f, 0.90f, 1),  // Twisted Column
        new Vector4(0.90f, 0.65f, 0.30f, 1),  // Menger Sponge
        new Vector4(0.35f, 0.85f, 0.85f, 1),  // Gyroid
        new Vector4(0.80f, 0.40f, 0.20f, 1),  // Auger
        new Vector4(0.95f, 0.30f, 0.45f, 1),  // Heart
        new Vector4(0.30f, 0.55f, 0.95f, 1),  // Torus Knot
        new Vector4(0.90f, 0.75f, 0.55f, 1),  // Scallop
        new Vector4(0.55f, 0.85f, 0.70f, 1),  // Stretchy Bracelet
    };

    // ---- State ----
    struct _state
    {
        public sg_pipeline pip;
        public sg_bindings bind;
        public sg_pass_action pass_action;
        public sg_buffer vbuf;
        public sg_buffer ibuf;
        public int    current_sample;
        public float  rotation_y;
        public int    index_count;
        public float  normalize_scale;
        public bool   needs_rebuild;
    }

    static _state state = new _state();

    // ---- Manifold C API helpers ----

    static IntPtr A() => manifold_alloc_manifold();

    static void D(IntPtr m)
    {
        if (m != IntPtr.Zero) manifold_delete_manifold(m);
    }

    static IntPtr MCube(IntPtr mem, double x, double y, double z, bool center = true) =>
        manifold_cube((void*)mem, x, y, z, center ? 1 : 0);

    static IntPtr MSphere(IntPtr mem, double r, int segs = 64) =>
        manifold_sphere((void*)mem, r, segs);

    static IntPtr MCylinder(IntPtr mem, double h, double r, int segs = 36, bool center = true) =>
        manifold_cylinder((void*)mem, h, r, -1.0, segs, center ? 1 : 0);

    static IntPtr MTetrahedron(IntPtr mem) =>
        manifold_tetrahedron((void*)mem);

    static IntPtr MBoolean(IntPtr mem, IntPtr a, IntPtr b, ManifoldOpType op) =>
        manifold_boolean((void*)mem, a, b, op);

    static IntPtr MTranslate(IntPtr mem, IntPtr m, double x, double y, double z) =>
        manifold_translate((void*)mem, m, x, y, z);

    static IntPtr MRotate(IntPtr mem, IntPtr m, double rx, double ry, double rz) =>
        manifold_rotate((void*)mem, m, rx, ry, rz);

    // Extracts mesh data from a manifold into a GPU-ready interleaved vertex array and index array.
    // Vertex layout: 6 floats per vertex — [x, y, z, nx, ny, nz].
    //
    // Normals are computed with crease-angle smoothing (threshold ~60°):
    //   - Adjacent faces within the threshold → smooth shared normal (good for spheres, cylinders)
    //   - Adjacent faces beyond the threshold → hard split vertex (good for cube edges, CSG seams)
    //
    // Returns a normalize_scale so the mesh fits within ~1.5 units from origin.
    static float ExtractMesh(IntPtr m, out float[] vertices, out uint[] indices)
    {
        IntPtr meshMem = manifold_alloc_meshgl();
        IntPtr mesh    = manifold_get_meshgl((void*)meshMem, m);

        int numVerts = (int)manifold_meshgl_num_vert(mesh);
        int numTri   = (int)manifold_meshgl_num_tri(mesh);
        int propLen  = (int)manifold_meshgl_vert_properties_length(mesh);
        int triLen   = (int)manifold_meshgl_tri_length(mesh);

        float[] props = new float[propLen];
        fixed (float* pP = props)
            manifold_meshgl_vert_properties((void*)pP, mesh);

        uint[] rawIdx = new uint[triLen];
        fixed (uint* pI = rawIdx)
            manifold_meshgl_tri_verts((void*)pI, mesh);

        manifold_delete_meshgl(mesh);

        // numProp = total floats per vertex in vertProperties (3 for plain primitives = xyz only)
        int numProp = numVerts > 0 ? propLen / numVerts : 3;
        Vector3 P(int v) => new(props[v * numProp], props[v * numProp + 1], props[v * numProp + 2]);

        // Flip winding: Manifold outputs CCW in right-hand coords, but after MVP transform
        // the triangles appear CW from the camera. Swap index 1 and 2 of every triangle
        // so they become CCW in clip space (matching Metal/Sokol SG_CULLMODE_BACK expectation).
        // for (int t = 0; t < numTri; t++)
        // {
        //     (rawIdx[t*3+1], rawIdx[t*3+2]) = (rawIdx[t*3+2], rawIdx[t*3+1]);
        // }

        // --- Per-face normals (cross product matches the flipped winding order) ---
        var faceNorm = new Vector3[numTri];
        for (int t = 0; t < numTri; t++)
        {
            Vector3 p0 = P((int)rawIdx[t*3]), p1 = P((int)rawIdx[t*3+1]), p2 = P((int)rawIdx[t*3+2]);
            var n = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            faceNorm[t] = float.IsNaN(n.X) ? Vector3.UnitY : n;
        }

        // --- Vertex → triangle adjacency ---
        var vtris = new List<int>[numVerts];
        for (int v = 0; v < numVerts; v++) vtris[v] = new List<int>();
        for (int t = 0; t < numTri; t++)
            for (int k = 0; k < 3; k++) vtris[(int)rawIdx[t*3+k]].Add(t);

        // --- Crease-angle normal smoothing ---
        // cos(60°) = 0.5 — faces sharper than 60° apart get hard edges (split vertices).
        // This gives flat shading on cube faces and smooth shading on sphere/cylinder curves.
        const float CreaseDot = 0.5f;
        var outV    = new List<float>(numVerts * 6);
        var emitted = new Dictionary<(int origVert, int tri), int>();
        indices = new uint[numTri * 3];

        for (int t = 0; t < numTri; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int ov = (int)rawIdx[t * 3 + k];
                if (emitted.TryGetValue((ov, t), out int eidx)) { indices[t*3+k] = (uint)eidx; continue; }

                // Collect smooth group: all tris at this vertex within the crease angle
                Vector3 refN = faceNorm[t];
                var group = new List<int>();
                Vector3 acc = Vector3.Zero;
                foreach (int at in vtris[ov])
                {
                    if (Vector3.Dot(refN, faceNorm[at]) >= CreaseDot)
                    {
                        group.Add(at);
                        // Area-weighted accumulation (cross product magnitude = 2× triangle area)
                        Vector3 q0 = P((int)rawIdx[at*3]), q1 = P((int)rawIdx[at*3+1]), q2 = P((int)rawIdx[at*3+2]);
                        acc += Vector3.Cross(q1 - q0, q2 - q0);
                    }
                }
                var norm = Vector3.Normalize(acc);
                if (float.IsNaN(norm.X)) norm = Vector3.UnitY;

                int newIdx = outV.Count / 6;
                Vector3 pos = P(ov);
                outV.Add(pos.X); outV.Add(pos.Y); outV.Add(pos.Z);
                outV.Add(norm.X); outV.Add(norm.Y); outV.Add(norm.Z);

                // Register all tris in this smooth group so they reuse this vertex index
                foreach (int st in group) emitted[(ov, st)] = newIdx;
                indices[t*3+k] = (uint)newIdx;
            }
        }

        vertices = outV.ToArray();
        int outVerts = vertices.Length / 6;

        // Normalize scale to fit within ~1.5 units from origin
        float maxExtent = 0.001f;
        for (int v = 0; v < outVerts; v++)
        {
            float e = MathF.Max(MathF.Abs(vertices[v*6]), MathF.Max(MathF.Abs(vertices[v*6+1]), MathF.Abs(vertices[v*6+2])));
            if (e > maxExtent) maxExtent = e;
        }
        return 1.5f / maxExtent;
    }

    // ---- Sample builders ----

    static (float[] verts, uint[] indices, float scale) BuildSample(int idx) => idx switch
    {
        0 => BuildTetrahedron(),
        1 => BuildCube(),
        2 => BuildSphere(),
        3 => BuildCylinder(),
        4 => BuildCSGUnion(),
        5 => BuildCSGDifference(),
        6 => BuildCSGIntersection(),
        7 => BuildPerforatedCube(),
        8 => BuildWireframeCube(),
        9 => BuildTorus(),
        10 => BuildRoundedFrame(),
        11 => BuildTwistedColumn(),
        12 => BuildMengerSponge(),
        13 => BuildGyroid(),
        14 => BuildAuger(),
        15 => BuildHeart(),
        16 => BuildTorusKnot(),
        17 => BuildScallop(),
        18 => BuildStretchyBracelet(),
        _ => BuildCube(),
    };

    static (float[], uint[], float) BuildTetrahedron()
    {
        IntPtr mem = A(); IntPtr m = MTetrahedron(mem);
        float s = ExtractMesh(m, out var v, out var idx);
        D(m);
        return (v, idx, s);
    }

    static (float[], uint[], float) BuildCube()
    {
        IntPtr mem = A(); IntPtr m = MCube(mem, 1, 1, 1);
        float s = ExtractMesh(m, out var v, out var idx);
        D(m);
        return (v, idx, s);
    }

    static (float[], uint[], float) BuildSphere()
    {
        IntPtr mem = A(); IntPtr m = MSphere(mem, 1.0, 64);
        float s = ExtractMesh(m, out var v, out var idx);
        D(m);
        return (v, idx, s);
    }

    static (float[], uint[], float) BuildCylinder()
    {
        IntPtr mem = A(); IntPtr m = MCylinder(mem, 2.0, 0.6, 36);
        float s = ExtractMesh(m, out var v, out var idx);
        D(m);
        return (v, idx, s);
    }

    // CSG Union: cube and offset sphere merged together
    static (float[], uint[], float) BuildCSGUnion()
    {
        IntPtr aMem = A(); IntPtr a = MCube(aMem, 1.2, 1.2, 1.2);
        IntPtr bMem = A(); IntPtr b = MSphere(bMem, 0.8, 48);
        IntPtr tMem = A(); IntPtr bt = MTranslate(tMem, b, 0.4, 0.4, 0.0);
        D(b);
        IntPtr rMem = A(); IntPtr r = MBoolean(rMem, a, bt, ManifoldOpType.MANIFOLD_ADD);
        D(a); D(bt);
        float s = ExtractMesh(r, out var v, out var idx);
        D(r);
        return (v, idx, s);
    }

    // CSG Difference: cube with sphere carved out
    static (float[], uint[], float) BuildCSGDifference()
    {
        IntPtr aMem = A(); IntPtr a = MCube(aMem, 1.6, 1.6, 1.6);
        IntPtr bMem = A(); IntPtr b = MSphere(bMem, 1.05, 48);
        IntPtr rMem = A(); IntPtr r = MBoolean(rMem, a, b, ManifoldOpType.MANIFOLD_SUBTRACT);
        D(a); D(b);
        float s = ExtractMesh(r, out var v, out var idx);
        D(r);
        return (v, idx, s);
    }

    // CSG Intersection: rounded cube (cube intersected with sphere)
    static (float[], uint[], float) BuildCSGIntersection()
    {
        IntPtr aMem = A(); IntPtr a = MCube(aMem, 1.6, 1.6, 1.6);
        IntPtr bMem = A(); IntPtr b = MSphere(bMem, 1.05, 48);
        IntPtr rMem = A(); IntPtr r = MBoolean(rMem, a, b, ManifoldOpType.MANIFOLD_INTERSECT);
        D(a); D(b);
        float s = ExtractMesh(r, out var v, out var idx);
        D(r);
        return (v, idx, s);
    }

    // Perforated Cube: 3x3x3 cube with 3 axis-aligned bars subtracted (level-1 Menger sponge)
    static (float[], uint[], float) BuildPerforatedCube()
    {
        IntPtr cuMem = A(); IntPtr cu   = MCube(cuMem, 3.0, 3.0, 3.0);
        IntPtr zMem  = A(); IntPtr zBar = MCube(zMem,  1.01, 1.01, 3.01);
        IntPtr xMem  = A(); IntPtr xBar = MCube(xMem,  3.01, 1.01, 1.01);
        IntPtr yMem  = A(); IntPtr yBar = MCube(yMem,  1.01, 3.01, 1.01);

        IntPtr r1M = A(); IntPtr r1 = MBoolean(r1M, cu,   zBar, ManifoldOpType.MANIFOLD_SUBTRACT);
        D(cu); D(zBar);
        IntPtr r2M = A(); IntPtr r2 = MBoolean(r2M, r1,   xBar, ManifoldOpType.MANIFOLD_SUBTRACT);
        D(r1); D(xBar);
        IntPtr r3M = A(); IntPtr r3 = MBoolean(r3M, r2,   yBar, ManifoldOpType.MANIFOLD_SUBTRACT);
        D(r2); D(yBar);

        float s = ExtractMesh(r3, out var v, out var idx);
        D(r3);
        return (v, idx, s);
    }

    // Wireframe Cube: 12 edge cylinders + 8 corner spheres, all union'd via batch boolean
    static (float[], uint[], float) BuildWireframeCube()
    {
        const double E   = 1.0;   // half edge length
        const double R   = 0.06;  // cylinder / corner radius
        const int    SEG = 12;    // cylinder segments

        IntPtr vec = manifold_alloc_manifold_vec();

        // X-axis edges (4): positioned at y=±E, z=±E
        foreach (double yOff in new[] { -E, E })
        foreach (double zOff in new[] { -E, E })
        {
            IntPtr cM = A(); IntPtr c  = MCylinder(cM, E * 2 + R * 2, R, SEG);
            IntPtr rM = A(); IntPtr cr = MRotate(rM, c, 0, 90, 0); D(c);
            IntPtr tM = A(); IntPtr ct = MTranslate(tM, cr, 0, yOff, zOff); D(cr);
            manifold_manifold_vec_push_back(vec, ct); D(ct);
        }

        // Y-axis edges (4): positioned at x=±E, z=±E
        foreach (double xOff in new[] { -E, E })
        foreach (double zOff in new[] { -E, E })
        {
            IntPtr cM = A(); IntPtr c  = MCylinder(cM, E * 2 + R * 2, R, SEG);
            IntPtr rM = A(); IntPtr cr = MRotate(rM, c, 90, 0, 0); D(c);
            IntPtr tM = A(); IntPtr ct = MTranslate(tM, cr, xOff, 0, zOff); D(cr);
            manifold_manifold_vec_push_back(vec, ct); D(ct);
        }

        // Z-axis edges (4): positioned at x=±E, y=±E (cylinder is already along Z)
        foreach (double xOff in new[] { -E, E })
        foreach (double yOff in new[] { -E, E })
        {
            IntPtr cM = A(); IntPtr c  = MCylinder(cM, E * 2 + R * 2, R, SEG);
            IntPtr tM = A(); IntPtr ct = MTranslate(tM, c, xOff, yOff, 0); D(c);
            manifold_manifold_vec_push_back(vec, ct); D(ct);
        }

        // Corner spheres (8)
        foreach (double x in new[] { -E, E })
        foreach (double y in new[] { -E, E })
        foreach (double z in new[] { -E, E })
        {
            IntPtr sM = A(); IntPtr sp = MSphere(sM, R * 1.8, 8);
            IntPtr tM = A(); IntPtr st = MTranslate(tM, sp, x, y, z); D(sp);
            manifold_manifold_vec_push_back(vec, st); D(st);
        }

        IntPtr rMem   = A();
        IntPtr result = manifold_batch_boolean((void*)rMem, vec, ManifoldOpType.MANIFOLD_ADD);
        manifold_delete_manifold_vec(vec);

        float s = ExtractMesh(result, out var verts, out var idxArr);
        D(result);
        return (verts, idxArr, s);
    }

    // Torus: revolve a circle (minor radius 0.35) at major radius 0.9 offset from the Z axis
    static (float[], uint[], float) BuildTorus()
    {
        const double majorR = 0.9;
        const double minorR = 0.35;
        const int    segs   = 48;

        IntPtr csMem = manifold_alloc_cross_section();
        IntPtr cs    = manifold_cross_section_circle((void*)csMem, minorR, segs);
        IntPtr tMem  = manifold_alloc_cross_section();
        IntPtr ct    = manifold_cross_section_translate((void*)tMem, cs, majorR, 0.0);
        manifold_delete_cross_section(cs);

        IntPtr pMem  = manifold_alloc_polygons();
        IntPtr polys = manifold_cross_section_to_polygons((void*)pMem, ct);
        manifold_delete_cross_section(ct);

        IntPtr rMem   = A();
        IntPtr result = manifold_revolve((void*)rMem, polys, segs, 360.0);
        manifold_delete_polygons(polys);

        float s = ExtractMesh(result, out var v, out var idx);
        D(result);
        return (v, idx, s);
    }

    // Rounded Frame: 12 edge cylinders with spherical caps at every corner forming a cube frame.
    // Based on rounded-frame.mjs from the Manifold wasm examples.
    // Rounded Frame: the JS example splits the frame with a cube into inside/outside parts.
    // We build with the same approach and perform the split, then union both halves
    // (inside+outside) so the full geometry including the split seam is visible.
    // Matches rounded-frame.mjs: result.split(cube(100, true)).
    static (float[], uint[], float) BuildRoundedFrame()
    {
        // Parameters matching JS: edgeLength=100, radius=10 (scaled to unit space ~÷100)
        const double edgeLen = 1.0;
        const double R       = 0.1;

        // edge = cylinder(edgeLen, R) — along Z axis (not centered)
        // corner = sphere(R)
        // edge1 = union(corner, edge).rotate([-90,0,0]).translate([-edgeLen/2, -edgeLen/2, 0])
        //   → cylinder becomes Y-axis edge, sphere at origin then translated to corner
        IntPtr e0Mem  = A(); IntPtr edge   = MCylinder(e0Mem, edgeLen, R, 32, false);
        IntPtr sp0Mem = A(); IntPtr corner = MSphere(sp0Mem, R, 32);
        IntPtr u0Mem  = A(); IntPtr ue     = MBoolean(u0Mem, corner, edge, ManifoldOpType.MANIFOLD_ADD); D(corner); D(edge);
        IntPtr r0Mem  = A(); IntPtr uer    = MRotate(r0Mem, ue, -90, 0, 0); D(ue);
        IntPtr t0Mem  = A(); IntPtr edge1  = MTranslate(t0Mem, uer, -edgeLen * 0.5, -edgeLen * 0.5, 0); D(uer);

        // edge1 + edge1.rotate([0,0,180]) → two opposite Y-edges with corners
        IntPtr r1Mem  = A(); IntPtr edge1r = MRotate(r1Mem, edge1, 0, 0, 180);
        IntPtr u1Mem  = A(); IntPtr twoY   = MBoolean(u1Mem, edge1, edge1r, ManifoldOpType.MANIFOLD_ADD); D(edge1r);
        D(edge1);

        // + edge.translate([-edgeLen/2, -edgeLen/2, 0]) — a Z-axis edge along the bottom-front
        IntPtr e1Mem  = A(); IntPtr edgeZ  = MCylinder(e1Mem, edgeLen, R, 32, false);
        IntPtr t1Mem  = A(); IntPtr edgeZt = MTranslate(t1Mem, edgeZ, -edgeLen * 0.5, -edgeLen * 0.5, 0); D(edgeZ);
        IntPtr u2Mem  = A(); IntPtr edge2  = MBoolean(u2Mem, twoY, edgeZt, ManifoldOpType.MANIFOLD_ADD); D(twoY); D(edgeZt);

        // edge4 = union(edge2, edge2.rotate([0,0,90])).translate([0,0,-edgeLen/2])
        IntPtr r2Mem  = A(); IntPtr edge2r = MRotate(r2Mem, edge2, 0, 0, 90);
        IntPtr u3Mem  = A(); IntPtr e4pre  = MBoolean(u3Mem, edge2, edge2r, ManifoldOpType.MANIFOLD_ADD); D(edge2r); D(edge2);
        IntPtr t2Mem  = A(); IntPtr edge4  = MTranslate(t2Mem, e4pre, 0, 0, -edgeLen * 0.5); D(e4pre);

        // result = union(edge4, edge4.rotate([180,0,0]))
        IntPtr r3Mem  = A(); IntPtr edge4r = MRotate(r3Mem, edge4, 180, 0, 0);
        IntPtr u4Mem  = A(); IntPtr frame  = MBoolean(u4Mem, edge4, edge4r, ManifoldOpType.MANIFOLD_ADD); D(edge4r); D(edge4);

        // Split frame with cube(edgeLen, center=true) → inside (intersection) and outside (difference)
        IntPtr cubeMem = A(); IntPtr cutter = MCube(cubeMem, edgeLen, edgeLen, edgeLen, true);
        IntPtr inMem   = A();
        IntPtr outMem  = A();
        var pair = manifold_split((void*)inMem, (void*)outMem, frame, cutter);
        D(frame); D(cutter);
        // pair.first = inside (the cross-bar parts), pair.second = outside (the frame corners)
        // Union both back so we can see the full frame with the split seam demonstrated
        IntPtr fullMem = A();
        IntPtr full    = MBoolean(fullMem, pair.first, pair.second, ManifoldOpType.MANIFOLD_ADD);
        D(pair.first); D(pair.second);

        float s = ExtractMesh(full, out var verts, out var idxArr);
        D(full);
        return (verts, idxArr, s);
    }

    // Twisted Column: extrude a square cross-section with 180° twist and taper.
    static (float[], uint[], float) BuildTwistedColumn()
    {
        const double side   = 0.55;
        const double height = 2.5;
        const double twist  = 180.0;
        const int    slices = 64;

        IntPtr csMem = manifold_alloc_cross_section();
        IntPtr cs    = manifold_cross_section_square((void*)csMem, side, side, 1);

        IntPtr pMem  = manifold_alloc_polygons();
        IntPtr polys = manifold_cross_section_to_polygons((void*)pMem, cs);
        manifold_delete_cross_section(cs);

        IntPtr rMem   = A();
        // extrude(polygons, height, slices, twist_deg, scale_x, scale_y)
        IntPtr result = manifold_extrude((void*)rMem, polys, height, slices, twist, 1.0, 1.0);
        manifold_delete_polygons(polys);

        // Smooth the result to make the twist surface look nice
        IntPtr sMem     = A();
        IntPtr smoothed = manifold_smooth_out((void*)sMem, result, 20.0, 0.5);
        D(result);

        float s = ExtractMesh(smoothed, out var v, out var idx);
        D(smoothed);
        return (v, idx, s);
    }

    // Menger Sponge level 3: recursive fractal holes applied on 3 axes.
    // Ported from menger-sponge.mjs in the Manifold wasm examples.
    static (float[], uint[], float) BuildMengerSponge()
    {
        const int n = 3; // recursion depth

        IntPtr cuMem = A();
        IntPtr cube  = MCube(cuMem, 1.0, 1.0, 1.0);

        // Build flat list of scaled+translated hole cubes recursively
        var holes = new System.Collections.Generic.List<IntPtr>();
        MengerFractal(holes, cube, 1.0, 0.0, 0.0, 1, n);

        // Union all holes into one compound shape
        IntPtr vec = manifold_alloc_manifold_vec();
        foreach (var h in holes) manifold_manifold_vec_push_back(vec, h);
        foreach (var h in holes) D(h);
        IntPtr holeMem = A();
        IntPtr hole    = manifold_batch_boolean((void*)holeMem, vec, ManifoldOpType.MANIFOLD_ADD);
        manifold_delete_manifold_vec(vec);

        // Rotate hole for the other two axis pairs
        IntPtr rx90M = A(); IntPtr hole90x = MRotate(rx90M, hole,  90, 0, 0);
        IntPtr ry90M = A(); IntPtr hole90y = MRotate(ry90M, hole, 0, 90, 0);

        // Subtract holes along all three axes
        IntPtr d1M = A(); IntPtr d1 = MBoolean(d1M, cube,  hole,   ManifoldOpType.MANIFOLD_SUBTRACT); D(cube);  D(hole);
        IntPtr d2M = A(); IntPtr d2 = MBoolean(d2M, d1,    hole90x, ManifoldOpType.MANIFOLD_SUBTRACT); D(d1);   D(hole90x);
        IntPtr d3M = A(); IntPtr d3 = MBoolean(d3M, d2,    hole90y, ManifoldOpType.MANIFOLD_SUBTRACT); D(d2);   D(hole90y);

        float s = ExtractMesh(d3, out var v, out var idx);
        D(d3);
        return (v, idx, s);
    }

    // Recursively collect scaled hole cubes at 2D (XY) grid positions.
    static void MengerFractal(System.Collections.Generic.List<IntPtr> holes,
                               IntPtr template, double w, double px, double py,
                               int depth, int maxDepth)
    {
        w /= 3.0;
        // Add a w×w×1 scaled copy at (px, py)
        IntPtr sM = A(); IntPtr scaled = manifold_scale((void*)sM, template, w, w, 1.0);
        IntPtr tM = A(); IntPtr placed = MTranslate(tM, scaled, px, py, 0.0); D(scaled);
        holes.Add(placed);

        if (depth == maxDepth) return;

        // The 8 surrounding positions at the new (reduced) scale
        double[] ox = { -w, -w, -w,  0,  w,  w,  w,  0 };
        double[] oy = { -w,  0,  w,  w,  w,  0, -w, -w };
        for (int k = 0; k < 8; k++)
            MengerFractal(holes, template, w, px + ox[k], py + oy[k], depth + 1, maxDepth);
    }

    // Gyroid SDF — expects coordinates in SIZE units; ctx = pointer to double (1/sc).
    // Divides coords by sc to get natural period units, then applies pi/4 phase offset.
    [UnmanagedCallersOnly]
    static double GyroidSDF(double x, double y, double z, void* ctx)
    {
        double invSc = *(double*)ctx;
        double off = 3.14159265358979 * 0.25;  // pi/4
        double ox = x * invSc - off;
        double oy = y * invSc - off;
        double oz = z * invSc - off;
        return Math.Cos(ox) * Math.Sin(oy)
             + Math.Cos(oy) * Math.Sin(oz)
             + Math.Cos(oz) * Math.Sin(ox);
    }

    // Gyroid modular puzzle piece — ported from gyroid-module.ts.
    // Builds m=4 pyramid of 20 copies, each translated by [(k+i-j)*size,(k-i)*size,(-j)*size],
    // matching the JS reference exactly.
    static (float[], uint[], float) BuildGyroid()
    {
        double period  = 2.0 * Math.PI;
        double n       = 20.0;
        double size    = 20.0;
        double sc      = size / period;          // ≈ 3.185
        double edgeLen = size / n;               // = 1.0 (in size units)
        double rdS     = size * Math.Sqrt(2.0);
        double bound   = rdS * 1.1;

        double invSc   = 1.0 / sc;

        // --- Build the level sets once (in size units, SDF normalises via ctx) ---
        IntPtr boxMem = manifold_alloc_box();
        IntPtr box    = manifold_box((void*)boxMem, -bound, -bound, -bound, bound, bound, bound);

        IntPtr g1Mem = A();
        IntPtr g1    = manifold_level_set((void*)g1Mem, &GyroidSDF, box, edgeLen, -0.4, -1.0, &invSc);
        IntPtr g2Mem = A();
        IntPtr g2    = manifold_level_set((void*)g2Mem, &GyroidSDF, box, edgeLen,  0.4, -1.0, &invSc);
        manifold_delete_box(box);

        // --- Rhombic dodecahedron ---
        IntPtr rb0Mem = A(); IntPtr rb0 = MCube(rb0Mem, rdS, rdS, 2.0 * rdS, true);
        IntPtr rb1Mem = A(); IntPtr rb1 = MRotate(rb1Mem, rb0, 90, 45,  0);
        IntPtr rb2Mem = A(); IntPtr rb2 = MRotate(rb2Mem, rb0, 90, 45, 90);
        IntPtr rb3Mem = A(); IntPtr rb3 = MRotate(rb3Mem, rb0,  0,  0, 45);
        D(rb0);
        IntPtr i1Mem = A(); IntPtr rd12 = MBoolean(i1Mem, rb1, rb2, ManifoldOpType.MANIFOLD_INTERSECT); D(rb1); D(rb2);
        IntPtr i2Mem = A(); IntPtr rd   = MBoolean(i2Mem, rd12, rb3, ManifoldOpType.MANIFOLD_INTERSECT); D(rd12); D(rb3);

        // --- Single module: rd.intersect(g1).subtract(g2) ---
        IntPtr s1Mem = A(); IntPtr step1  = MBoolean(s1Mem, rd,    g1, ManifoldOpType.MANIFOLD_INTERSECT); D(rd);    D(g1);
        IntPtr s2Mem = A(); IntPtr module = MBoolean(s2Mem, step1, g2, ManifoldOpType.MANIFOLD_SUBTRACT);  D(step1); D(g2);

        // --- Replicate m=4 copies with pyramid translations, then batch-union ---
        const int m = 4;
        IntPtr vec = manifold_alloc_manifold_vec();
        for (int i = 0; i < m; i++)
        for (int j = i; j < m; j++)
        for (int k = j; k < m; k++)
        {
            double tx = (k + i - j) * size;
            double ty = (k - i)     * size;
            double tz = (-j)        * size;
            IntPtr tMem = A();
            IntPtr copy = MTranslate(tMem, module, tx, ty, tz);
            manifold_manifold_vec_push_back(vec, copy);
            D(copy);
        }
        D(module);

        IntPtr resMem = A();
        IntPtr result = manifold_batch_boolean((void*)resMem, vec, ManifoldOpType.MANIFOLD_ADD);
        manifold_delete_manifold_vec(vec);

        float s = ExtractMesh(result, out var v, out var idx);
        D(result);
        return (v, idx, s);
    }

    // Auger: 3 revolving beads extruded with a twist into a helical screw shape.
    // Ported from auger.mjs in the Manifold wasm examples.
    static (float[], uint[], float) BuildAuger()
    {
        const double outerR = 0.5;
        const double beadR  = 0.05;
        const double height = 1.5;
        const double twist  = 90.0;

        // Quarter-torus bead: revolve a circle at outerR by 90°
        IntPtr csMem  = manifold_alloc_cross_section();
        IntPtr cs     = manifold_cross_section_circle((void*)csMem, beadR, 32);
        IntPtr ctMem  = manifold_alloc_cross_section();
        IntPtr ct     = manifold_cross_section_translate((void*)ctMem, cs, outerR, 0.0);
        manifold_delete_cross_section(cs);
        IntPtr pMem   = manifold_alloc_polygons();
        IntPtr polys  = manifold_cross_section_to_polygons((void*)pMem, ct);
        manifold_delete_cross_section(ct);
        IntPtr qtMem  = A(); IntPtr qTorus = manifold_revolve((void*)qtMem, polys, 32, 90.0);
        manifold_delete_polygons(polys);

        // Cap sphere at the open end of the quarter-torus, then position at -outerR in Y
        IntPtr capMem = A(); IntPtr cap    = MSphere(capMem, beadR, 16);
        IntPtr capTM  = A(); IntPtr capT   = MTranslate(capTM, cap, outerR, 0.0, 0.0); D(cap);
        IntPtr b1uM   = A(); IntPtr b1u    = MBoolean(b1uM, qTorus, capT, ManifoldOpType.MANIFOLD_ADD); D(qTorus); D(capT);
        IntPtr b1tM   = A(); IntPtr bead1  = MTranslate(b1tM, b1u, 0.0, -outerR, 0.0); D(b1u);

        // Three copies at 0°, 120°, 240°
        IntPtr vec = manifold_alloc_manifold_vec();
        manifold_manifold_vec_push_back(vec, bead1);
        IntPtr r1M = A(); IntPtr b2 = MRotate(r1M, bead1, 0, 0, 120); manifold_manifold_vec_push_back(vec, b2); D(b2);
        IntPtr r2M = A(); IntPtr b3 = MRotate(r2M, bead1, 0, 0, 240); manifold_manifold_vec_push_back(vec, b3); D(b3);
        D(bead1);
        IntPtr beadsMem = A();
        IntPtr beads    = manifold_batch_boolean((void*)beadsMem, vec, ManifoldOpType.MANIFOLD_ADD);
        manifold_delete_manifold_vec(vec);

        // Slice the 3-bead union at z=0 → extrude with twist
        IntPtr sliceMem = manifold_alloc_polygons();
        IntPtr slice    = manifold_slice((void*)sliceMem, beads, 0.0);
        IntPtr augMem   = A();
        IntPtr auger    = manifold_extrude((void*)augMem, slice, height, 50, twist, 1.0, 1.0);
        manifold_delete_polygons(slice);

        // Add the base bead assembly and the top (rotated by twist)
        IntPtr topTM  = A(); IntPtr beadsTop  = MTranslate(topTM,  beads,    0.0, 0.0, height);
        IntPtr topRM  = A(); IntPtr beadsTopR = MRotate(topRM, beadsTop, 0, 0, twist); D(beadsTop);
        IntPtr r3M    = A(); IntPtr r3   = MBoolean(r3M,  auger, beads,     ManifoldOpType.MANIFOLD_ADD); D(auger); D(beads);
        IntPtr r4M    = A(); IntPtr r4   = MBoolean(r4M,  r3,    beadsTopR, ManifoldOpType.MANIFOLD_ADD); D(r3);    D(beadsTopR);

        float s = ExtractMesh(r4, out var v, out var idx);
        D(r4);
        return (v, idx, s);
    }


    [UnmanagedCallersOnly]
    static void HeartWarp(ManifoldVec3* result, double x, double y, double z, void* ctx)
    {
        double x2 = x*x, y2 = y*y, z2 = z*z;
        double a  = x2 + 9.0/4.0*y2 + z2;
        double b  = z*z2*(x2 + 9.0/80.0*y2);
        double a2 = a*a, a3 = a2*a;
        // Newton's method: find r such that (a*r²-1)³ = b*r⁵  →  Taubin heart surface
        double r = 1.5;
        for (int i = 0; i < 100; i++)
        {
            double r2 = r*r, r4 = r2*r2;
            double f  = a3*r4*r2 - b*r4*r - 3.0*a2*r4 + 3.0*a*r2 - 1.0;
            double df = 6.0*a3*r4*r - 5.0*b*r4 - 12.0*a2*r2*r + 6.0*a*r;
            if (Math.Abs(df) < 1e-12) break;
            double dr = f / df;
            r -= dr;
            if (Math.Abs(dr) < 0.0001) break;
        }
        result->x = x*r; result->y = y*r; result->z = z*r;
    }


    static (float[], uint[], float) BuildHeart()
    {
        IntPtr ballMem = A();
        IntPtr ball    = MSphere(ballMem, 1.0, 200);
        IntPtr rMem    = A();
        IntPtr result  = manifold_warp((void*)rMem, ball, &HeartWarp, null);
        D(ball);
        float s = ExtractMesh(result, out var v, out var idx);
        D(result);
        return (v, idx, s);
    }

    // Torus Knot (p=1, q=3 trefoil knot): revolved cylinder warped along a torus-knot path.
    // Ported from torus-knot.mjs in the Manifold wasm examples.
    [UnmanagedCallersOnly]
    static void TorusKnotWarp(ManifoldVec3* result, double x, double y, double z, void* ctx)
    {
        const double majorR  = 25.0;
        const double minorR  = 10.0;
        const double threadR =  3.75;
        const double off     =  2.0;
        const double pk      =  1.0;  // = p / gcd(p,q)
        const double qk      =  3.0;  // = q / gcd(p,q)

        double psi   = qk * Math.Atan2(x, y);
        double theta = psi * pk / qk;
        double x1    = Math.Sqrt(x*x + y*y);
        double phi   = Math.Atan2(x1 - off, z);

        // Local tube cross-section position
        double p3x = threadR * Math.Cos(phi);
        double p3y = 0.0;
        double p3z = threadR * Math.Sin(phi);

        // Align tube with the knot tangent
        double r       = majorR + minorR * Math.Cos(theta);
        double tangAng = -Math.Atan2(pk * minorR, qk * r);
        double cosA = Math.Cos(tangAng), sinA = Math.Sin(tangAng);
        // rotate2D([p3y, p3z], tangAng)
        double t0 = p3y * cosA - p3z * sinA;
        double t1 = p3y * sinA + p3z * cosA;
        p3x += minorR; p3y = t0; p3z = t1;

        // Rotate in the RZ plane by -theta (place on donut surface)
        double cosT = Math.Cos(-theta), sinT = Math.Sin(-theta);
        double q0 = p3x * cosT - p3z * sinT;
        double q1 = p3x * sinT + p3z * cosT;
        double newP3y = p3y;  // y-component (p3y = t0) carries through
        p3x = q0 + majorR; p3y = newP3y; p3z = q1;

        // Final rotation around Z by psi
        double cosP = Math.Cos(psi), sinP = Math.Sin(psi);
        result->x = p3x * cosP - p3y * sinP;
        result->y = p3x * sinP + p3y * cosP;
        result->z = p3z;
    }

    static (float[], uint[], float) BuildTorusKnot()
    {
        const double off     = 2.0;
        const double threadR = 3.75;
        const double majorR  = 25.0;
        const double qk      = 3.0;

        int n = manifold_get_circular_segments(threadR);
        int m = (int)(n * qk * majorR / threadR);

        // Revolve the offset circle to create the torus surface, then warp to knot shape
        IntPtr csMem = manifold_alloc_cross_section();
        IntPtr cs    = manifold_cross_section_circle((void*)csMem, 1.0, n);
        IntPtr ctMem = manifold_alloc_cross_section();
        IntPtr ct    = manifold_cross_section_translate((void*)ctMem, cs, off, 0.0);
        manifold_delete_cross_section(cs);
        IntPtr pMem    = manifold_alloc_polygons();
        IntPtr polys   = manifold_cross_section_to_polygons((void*)pMem, ct);
        manifold_delete_cross_section(ct);
        IntPtr rMem    = A();
        IntPtr revolved = manifold_revolve((void*)rMem, polys, m, 360.0);
        manifold_delete_polygons(polys);

        IntPtr wMem   = A();
        IntPtr result = manifold_warp((void*)wMem, revolved, &TorusKnotWarp, null);

        D(revolved);

        float s = ExtractMesh(result, out var v, out var idx);
        D(result);
        return (v, idx, s);
    }

    // Scallop: fan mesh with selective edge sharpening via manifold_smooth + refine.
    // Ported from scallop.mjs in the Manifold wasm examples.
    static (float[], uint[], float) BuildScallop()
    {
        const double height    = 10.0;
        const double radius    = 30.0;
        const double offset    = 20.0;
        const int    wiggles   = 12;
        const double sharpness = 0.8;
        const int    n         = 50;

        var posList = new System.Collections.Generic.List<float>();
        var triList = new System.Collections.Generic.List<uint>();
        var heList  = new System.Collections.Generic.List<nuint>();
        var smList  = new System.Collections.Generic.List<double>();

        // Vertex 0: center-top (-offset, 0, +height); Vertex 1: center-bottom
        posList.Add(-(float)offset); posList.Add(0f); posList.Add((float)height);
        posList.Add(-(float)offset); posList.Add(0f); posList.Add(-(float)height);

        double delta = Math.PI / wiggles;
        for (int i = 0; i < 2 * wiggles; i++)
        {
            double theta = (i - wiggles) * delta;
            double amp   = 0.5 * height * Math.Max(Math.Cos(0.8 * theta), 0.0);
            posList.Add((float)(radius * Math.Cos(theta)));
            posList.Add((float)(radius * Math.Sin(theta)));
            posList.Add((float)(amp * (i % 2 == 0 ? 1 : -1)));

            int j = (i + 1) % (2 * wiggles);
            double sm = 1.0 - sharpness * Math.Cos((theta + delta * 0.5) / 2.0);

            // Top fan (0, 2+i, 2+j): sharpen the outer rim halfedge (flat index triList.Count+1)
            heList.Add((nuint)(triList.Count + 1));
            smList.Add(sm);
            triList.Add(0); triList.Add((uint)(2 + i)); triList.Add((uint)(2 + j));

            // Bottom fan (1, 2+j, 2+i): same outer rim halfedge index pattern
            heList.Add((nuint)(triList.Count + 1));
            smList.Add(sm);
            triList.Add(1); triList.Add((uint)(2 + j)); triList.Add((uint)(2 + i));
        }

        float[]  vertProps = posList.ToArray();
        uint[]   triVerts  = triList.ToArray();
        nuint[]  heArray   = heList.ToArray();
        double[] smArray   = smList.ToArray();

        nuint nVerts = (nuint)(vertProps.Length / 3);
        nuint nTris  = (nuint)(triVerts.Length / 3);

        IntPtr meshMem = manifold_alloc_meshgl();
        IntPtr mesh    = manifold_meshgl((void*)meshMem, ref vertProps[0], nVerts, 3, ref triVerts[0], nTris);
        IntPtr smem    = A();
        IntPtr smooth  = manifold_smooth((void*)smem, mesh, ref heArray[0], ref smArray[0], (nuint)heArray.Length);
        manifold_delete_meshgl(meshMem);

        IntPtr rmem    = A();
        IntPtr refined = manifold_refine((void*)rmem, smooth, n);
        D(smooth);

        float s = ExtractMesh(refined, out var v, out var idx);
        D(refined);
        return (v, idx, s);
    }

    // Stretchy Bracelet: cylinder with decorative bumps intersected with a stretch-cut polygon.
    // Ported from stretchy-bracelet.mjs in the Manifold wasm examples.
    static IntPtr BraceletBase(
        double width, double radius, double decorRadius, double twistRadius,
        int nDecor, double innerRadius, double outerRadius,
        double cut, int nCut, int nDivision)
    {
        // Base cylinder from z=0 to z=width (not centered)
        IntPtr bMem = A();
        IntPtr b    = MCylinder(bMem, width, radius + twistRadius * 0.5, 64, false);

        // Decor circle polygon: 2*nDivision points of radius decorRadius centered at (twistRadius, 0)
        var cPts     = new ManifoldVec2[2 * nDivision];
        double dPhiStep = Math.PI / nDivision;
        for (int i = 0; i < 2 * nDivision; i++)
        {
            double a = dPhiStep * i;
            cPts[i] = new ManifoldVec2 { x = decorRadius * Math.Cos(a) + twistRadius, y = decorRadius * Math.Sin(a) };
        }
        IntPtr spDecMem = manifold_alloc_simple_polygon();
        fixed (ManifoldVec2* pp = cPts)
            spDecMem = manifold_simple_polygon((void*)spDecMem, pp, (nuint)cPts.Length);
        IntPtr pdecMem = manifold_alloc_polygons();
        {
            IntPtr* arr = stackalloc IntPtr[1];
            arr[0] = spDecMem;
            pdecMem = manifold_polygons((void*)pdecMem, (IntPtr)arr, 1);
        }
        manifold_delete_simple_polygon(spDecMem);

        // Extrude decor circle with 180 degree half-twist, scale Y by 0.5, translate to outer rim
        IntPtr extMem  = A();
        IntPtr extD    = manifold_extrude((void*)extMem, pdecMem, width, nDivision, 180.0, 1.0, 1.0);
        manifold_delete_polygons(pdecMem);
        IntPtr sclMem  = A();
        IntPtr extDs   = manifold_scale((void*)sclMem, extD, 1.0, 0.5, 1.0);
        D(extD);
        IntPtr decMem  = A();
        IntPtr decor0  = MTranslate(decMem, extDs, 0.0, radius, 0.0);
        D(extDs);

        // Union base cylinder with nDecor rotated copies of the decor bump
        IntPtr vec = manifold_alloc_manifold_vec();
        manifold_manifold_vec_push_back(vec, b);
        D(b);
        for (int i = 0; i < nDecor; i++)
        {
            IntPtr rMem = A();
            IntPtr rD   = MRotate(rMem, decor0, 0, 0, 360.0 / nDecor * i);
            manifold_manifold_vec_push_back(vec, rD);
            D(rD);
        }
        D(decor0);
        IntPtr bodyMem = A();
        IntPtr body    = manifold_batch_boolean((void*)bodyMem, vec, ManifoldOpType.MANIFOLD_ADD);
        manifold_delete_manifold_vec(vec);

        // Stretch polygon: nCut * 4 points forming the elastic-cut starburst pattern
        double dPhiRad = 2.0 * Math.PI / nCut;
        var sPts = new ManifoldVec2[nCut * 4];
        for (int i = 0; i < nCut; i++)
        {
            double ang  = dPhiRad * i;
            double cosA = Math.Cos(ang), sinA = Math.Sin(ang);
            // rotate2D for p0=(outerRadius,0), p1=(innerRadius,-cut), p2=(innerRadius,+cut)
            double r0x = outerRadius * cosA,                     r0y = outerRadius * sinA;
            double r1x = innerRadius * cosA - (-cut) * sinA,     r1y = innerRadius * sinA + (-cut) * cosA;
            double r2x = innerRadius * cosA - ( cut) * sinA,     r2y = innerRadius * sinA + ( cut) * cosA;
            sPts[i*4+0] = new ManifoldVec2 { x = r0x, y = r0y };
            sPts[i*4+1] = new ManifoldVec2 { x = r1x, y = r1y };
            sPts[i*4+2] = new ManifoldVec2 { x = r2x, y = r2y };
            sPts[i*4+3] = new ManifoldVec2 { x = r0x, y = r0y };
        }
        IntPtr spStrMem = manifold_alloc_simple_polygon();
        fixed (ManifoldVec2* pp = sPts)
            spStrMem = manifold_simple_polygon((void*)spStrMem, pp, (nuint)sPts.Length);
        IntPtr pstrMem = manifold_alloc_polygons();
        {
            IntPtr* arr = stackalloc IntPtr[1];
            arr[0] = spStrMem;
            pstrMem = manifold_polygons((void*)pstrMem, (IntPtr)arr, 1);
        }
        manifold_delete_simple_polygon(spStrMem);

        IntPtr extSMem = A();
        IntPtr extS    = manifold_extrude((void*)extSMem, pstrMem, width, 0, 0.0, 1.0, 1.0);
        manifold_delete_polygons(pstrMem);

        IntPtr intMem  = A();
        IntPtr result  = MBoolean(intMem, extS, body, ManifoldOpType.MANIFOLD_INTERSECT);
        D(extS);
        D(body);
        return result;
    }

    static (float[], uint[], float) BuildStretchyBracelet()
    {
        const double radius    = 30.0;
        const double height    = 8.0;
        const double width     = 15.0;
        const double thickness = 0.4;
        const int    nDecor    = 20;
        const int    nCut      = 27;
        const int    nDivision = 30;

        double twistRadius  = Math.PI * radius / nDecor;
        double decorRadius  = twistRadius * 1.5;
        double outerRadius  = radius + (decorRadius + twistRadius) * 0.5;
        double innerRadius  = outerRadius - height;
        double cut          = 0.5 * (2.0 * Math.PI * innerRadius / nCut - thickness);
        double adjThickness = 0.5 * thickness * height / cut;

        IntPtr outer = BraceletBase(width, radius, decorRadius, twistRadius, nDecor,
                                    innerRadius + thickness, outerRadius + adjThickness,
                                    cut - adjThickness, nCut, nDivision);
        IntPtr inner = BraceletBase(width, radius - thickness, decorRadius, twistRadius, nDecor,
                                    innerRadius, outerRadius + 3.0 * adjThickness,
                                    cut, nCut, nDivision);

        IntPtr diffMem = A();
        IntPtr diff    = MBoolean(diffMem, outer, inner, ManifoldOpType.MANIFOLD_SUBTRACT);
        D(outer); D(inner);

        // Center the bracelet around z=0 for display
        IntPtr ctrMem  = A();
        IntPtr centered = MTranslate(ctrMem, diff, 0.0, 0.0, -width * 0.5);
        D(diff);

        float s = ExtractMesh(centered, out var v, out var idx);
        D(centered);
        return (v, idx, s);
    }

    // ---- GPU upload ----

    static void LoadSample(int idx)
    {
        state.current_sample = idx;
        var (verts, indices, scale) = BuildSample(idx);
        state.normalize_scale = scale;
        state.index_count     = indices.Length;

        fixed (float* pV = verts)
            sg_update_buffer(state.vbuf, new sg_range
            {
                ptr  = pV,
                size = (nuint)(verts.Length * sizeof(float)),
            });

        fixed (uint* pI = indices)
            sg_update_buffer(state.ibuf, new sg_range
            {
                ptr  = pI,
                size = (nuint)(indices.Length * sizeof(uint)),
            });
    }

    // ---- Sokol callbacks ----

    [UnmanagedCallersOnly]
    private static unsafe void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger      = { func = &slog_func },
        });

        simgui_setup(new simgui_desc_t
        {
            logger = { func = &slog_func },
        });

        state.pass_action = default;
        state.pass_action.colors[0].load_action  = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.colors[0].clear_value  = new sg_color { r = 0.15f, g = 0.15f, b = 0.18f, a = 1.0f };
        state.pass_action.depth.load_action      = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.depth.clear_value      = 1.0f;

        // Vertex buffer — 6 floats per vertex (xyz position + xyz normal), stream updated
        state.vbuf = sg_make_buffer(new sg_buffer_desc
        {
            size  = (nuint)(MAX_VERTS_FLOATS * sizeof(float)),
            usage = new sg_buffer_usage { stream_update = true },
            label = "manifold-vbuf",
        });

        // Index buffer — uint32 indices, stream updated
        state.ibuf = sg_make_buffer(new sg_buffer_desc
        {
            size  = (nuint)(MAX_INDICES * sizeof(uint)),
            usage = new sg_buffer_usage { index_buffer = true, stream_update = true },
            label = "manifold-ibuf",
        });

        // Shader + pipeline
        sg_shader shd = sg_make_shader(manifold_shader_desc(sg_query_backend()));
        var pd = default(sg_pipeline_desc);
        pd.shader = shd;
        pd.layout.attrs[ATTR_manifold_pos].format  = SG_VERTEXFORMAT_FLOAT3;
        pd.layout.attrs[ATTR_manifold_norm].format = SG_VERTEXFORMAT_FLOAT3;
        pd.face_winding          = sg_face_winding.SG_FACEWINDING_CCW;
        pd.index_type              = SG_INDEXTYPE_UINT32;
        pd.cull_mode               = SG_CULLMODE_BACK;
        pd.depth.compare           = SG_COMPAREFUNC_LESS_EQUAL;
        pd.depth.write_enabled     = true;
        pd.label = "manifold-pipeline";
        state.pip = sg_make_pipeline(pd);

        state.bind = new sg_bindings();
        state.bind.vertex_buffers[0] = state.vbuf;
        state.bind.index_buffer      = state.ibuf;

        // Build and upload first sample
        LoadSample(0);
    }

    [UnmanagedCallersOnly]
    private static unsafe void Frame()
    {
        if (state.needs_rebuild)
        {
            state.needs_rebuild = false;
            LoadSample(state.current_sample);
        }

        // Auto-rotate around Y
        float dt = (float)sapp_frame_duration();
        state.rotation_y += dt * 0.6f;

        // Camera + transform matrices
        float aspect = sapp_widthf() / sapp_heightf();
        var proj  = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.1f, 100f);
        var view  = Matrix4x4.CreateLookAt(new Vector3(-5, 1.5f, 5f), Vector3.Zero, Vector3.UnitY);
        var model = Matrix4x4.CreateRotationX(-90 * (MathF.PI / 180f)) * Matrix4x4.CreateRotationY(state.rotation_y)
                  * Matrix4x4.CreateScale(state.normalize_scale);
        var mvp   = model * view * proj;

        var vsParams = new vs_params_t { mvp = mvp, model = model };
        var fsParams = new fs_params_t
        {
            light_dir  = new Vector4(1.2f, 2.0f, 2.5f, 0),
            base_color = SampleColors[state.current_sample],
        };

        // ImGui UI
        simgui_new_frame(new simgui_frame_desc_t
        {
            width      = sapp_width(),
            height     = sapp_height(),
            delta_time = sapp_frame_duration(),
        });

        igSetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(300, 130), ImGuiCond.Always);
        byte winOpen = 1;
        igBegin("Manifold Samples", ref winOpen,
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);
        igText(SampleNames[state.current_sample]);
        igText($"Triangles: {state.index_count / 3:N0}");
        igSeparator();
        if (igButton("< Previous", new Vector2(130, 30)))
        {
            state.current_sample = (state.current_sample - 1 + SampleNames.Length) % SampleNames.Length;
            state.needs_rebuild  = true;
        }
        igSameLine(0, 8);
        if (igButton("Next >", new Vector2(130, 30)))
        {
            state.current_sample = (state.current_sample + 1) % SampleNames.Length;
            state.needs_rebuild  = true;
        }
        igEnd();

        // 3D render pass
        sg_begin_pass(new sg_pass { action = state.pass_action, swapchain = sglue_swapchain() });
        sg_apply_pipeline(state.pip);
        sg_apply_bindings(state.bind);
        sg_apply_uniforms(UB_vs_params, SG_RANGE<vs_params_t>(ref vsParams));
        sg_apply_uniforms(UB_fs_params, SG_RANGE<fs_params_t>(ref fsParams));
        sg_draw(0, (uint)state.index_count, 1);

        simgui_render();
        sg_end_pass();
        sg_commit();
    }

    [UnmanagedCallersOnly]
    private static unsafe void Event(sapp_event* e)
    {
        simgui_handle_event(in *e);
    }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        simgui_shutdown();
        sg_shutdown();

        if (Debugger.IsAttached)
            Environment.Exit(0);
    }

    public static SApp.sapp_desc sokol_main()
    {
        return new SApp.sapp_desc
        {
            init_cb      = &Init,
            frame_cb     = &Frame,
            event_cb     = &Event,
            cleanup_cb   = &Cleanup,
            width        = 1280,
            height       = 720,
            sample_count = 4,
            window_title = "Manifold Samples (Sokol.NET)",
            icon         = { sokol_default = true },
            logger       = { func = &slog_func },
        };
    }
}
