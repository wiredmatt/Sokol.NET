// ObjLoader.cs — Simple WaveFront OBJ loader for Reversi disc meshes.
// Parses position + normal vertices and face indices, split by material.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Reversi
{
    /// <summary>Single vertex with position and normal.</summary>
    public struct ObjVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
    }

    /// <summary>A sub-mesh sharing one material, with its own vertex and index arrays.</summary>
    public class ObjSubMesh
    {
        public string MaterialName = "";
        public ObjVertex[] Vertices = Array.Empty<ObjVertex>();
        public ushort[]    Indices  = Array.Empty<ushort>();
    }

    /// <summary>Loaded OBJ mesh — may contain multiple sub-meshes.</summary>
    public class ObjMesh
    {
        public ObjSubMesh[] SubMeshes = Array.Empty<ObjSubMesh>();
    }

    public static class ObjLoader
    {
        /// <summary>Parse an OBJ file from raw text bytes.</summary>
        public static ObjMesh Load(byte[] data)
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            return ParseObjText(text);
        }

        private static ObjMesh ParseObjText(string text)
        {
            var positions = new List<Vector3>();
            var normals   = new List<Vector3>();

            // Temporary per-material face storage: material name → list of (posIdx, normIdx)
            var groups = new List<(string mat, List<(int p, int n)[]> faces)>();
            string currentMat = "default";
            List<(int p, int n)[]>? currentFaces = null;

            void EnsureGroup(string mat)
            {
                if (currentFaces == null || currentMat != mat)
                {
                    currentMat = mat;
                    currentFaces = new List<(int, int)[]>();
                    groups.Add((mat, currentFaces));
                }
            }

            // Ensure the default group exists
            EnsureGroup(currentMat);

            var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    positions.Add(ParseVec3(line, 2));
                }
                else if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    normals.Add(ParseVec3(line, 3));
                }
                else if (line.StartsWith("usemtl ", StringComparison.Ordinal))
                {
                    EnsureGroup(line.Substring(7).Trim());
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    if (currentFaces == null) EnsureGroup(currentMat);
                    var face = ParseFace(line.Substring(2));
                    if (face != null) currentFaces!.Add(face);
                }
            }

            // Build sub-meshes
            var subMeshes = new List<ObjSubMesh>();
            foreach (var (mat, faces) in groups)
            {
                if (faces.Count == 0) continue;
                subMeshes.Add(BuildSubMesh(mat, faces, positions, normals));
            }

            return new ObjMesh { SubMeshes = subMeshes.ToArray() };
        }

        private static ObjSubMesh BuildSubMesh(
            string mat,
            List<(int p, int n)[]> faces,
            List<Vector3> positions,
            List<Vector3> normals)
        {
            // Deduplicate vertices
            var vertMap = new Dictionary<(int, int), ushort>();
            var verts   = new List<ObjVertex>();
            var indices = new List<ushort>();

            foreach (var face in faces)
            {
                // Fan triangulation for polygons
                int triCount = face.Length - 2;
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = 0, i1 = t + 1, i2 = t + 2;
                    indices.Add(GetOrAdd(face[i0], vertMap, verts, positions, normals));
                    indices.Add(GetOrAdd(face[i1], vertMap, verts, positions, normals));
                    indices.Add(GetOrAdd(face[i2], vertMap, verts, positions, normals));
                }
            }

            return new ObjSubMesh
            {
                MaterialName = mat,
                Vertices = verts.ToArray(),
                Indices  = indices.ToArray()
            };
        }

        private static ushort GetOrAdd(
            (int p, int n) key,
            Dictionary<(int, int), ushort> map,
            List<ObjVertex> verts,
            List<Vector3> positions,
            List<Vector3> normals)
        {
            if (map.TryGetValue(key, out ushort idx)) return idx;

            var v = new ObjVertex
            {
                Position = key.p >= 0 && key.p < positions.Count ? positions[key.p] : Vector3.Zero,
                Normal   = key.n >= 0 && key.n < normals.Count   ? normals[key.n]   : Vector3.UnitY
            };
            idx = (ushort)verts.Count;
            verts.Add(v);
            map[key] = idx;
            return idx;
        }

        // Parse face tokens like "1/1/1" or "1//1" or just "1"
        private static (int p, int n)[]? ParseFace(string s)
        {
            var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3) return null;
            var result = new (int p, int n)[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                var parts = tokens[i].Split('/');
                int p = parts.Length > 0 && int.TryParse(parts[0], out int pv) ? pv - 1 : 0;
                int n = parts.Length > 2 && int.TryParse(parts[2], out int nv) ? nv - 1 : 0;
                result[i] = (p, n);
            }
            return result;
        }

        private static Vector3 ParseVec3(string line, int startToken)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float x = startToken     < parts.Length ? ParseF(parts[startToken])     : 0;
            float y = startToken + 1 < parts.Length ? ParseF(parts[startToken + 1]) : 0;
            float z = startToken + 2 < parts.Length ? ParseF(parts[startToken + 2]) : 0;
            return new Vector3(x, y, z);
        }

        private static float ParseF(string s) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }
}
