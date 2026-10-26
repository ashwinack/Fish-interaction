using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>Growable vertex/index lists that turn into a Mesh. Used by all procedural geometry.</summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uv0 = new List<Vector2>();
        public readonly List<Vector2> Uv1 = new List<Vector2>();
        public readonly List<Vector3> Uv2 = new List<Vector3>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int> Indices = new List<int>();
        /// <summary>Optional second submesh (used for translucent fish fins).</summary>
        public readonly List<int> Indices1 = new List<int>();

        public int VertexCount => Positions.Count;

        public void AddTriangle1(int a, int b, int c)
        {
            Indices1.Add(a);
            Indices1.Add(b);
            Indices1.Add(c);
        }

        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv0, Vector2 uv1, Color color)
        {
            Positions.Add(p);
            Normals.Add(n);
            Uv0.Add(uv0);
            Uv1.Add(uv1);
            Uv2.Add(Vector3.zero);
            Colors.Add(color);
            return Positions.Count - 1;
        }

        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv0) => AddVertex(p, n, uv0, Vector2.zero, Color.white);

        public void AddTriangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        /// <summary>Adds a triangle wound so its geometric normal agrees with the vertex normals (front face outwards).</summary>
        public void AddTriangleOriented(int a, int b, int c)
        {
            Vector3 pa = Positions[a], pb = Positions[b], pc = Positions[c];
            Vector3 geo = Vector3.Cross(pb - pa, pc - pa);
            Vector3 n = Normals[a] + Normals[b] + Normals[c];
            if (Vector3.Dot(geo, n) < 0f) AddTriangle(a, c, b);
            else AddTriangle(a, b, c);
        }

        public void Append(MeshBuilder other, Matrix4x4 transform)
        {
            int offset = Positions.Count;
            Matrix4x4 normalMatrix = transform.inverse.transpose;
            for (int i = 0; i < other.Positions.Count; i++)
            {
                Positions.Add(transform.MultiplyPoint3x4(other.Positions[i]));
                Normals.Add(normalMatrix.MultiplyVector(other.Normals[i]).normalized);
                Uv0.Add(other.Uv0[i]);
                Uv1.Add(other.Uv1[i]);
                Uv2.Add(other.Uv2[i]);
                Colors.Add(other.Colors[i]);
            }
            foreach (int idx in other.Indices) Indices.Add(idx + offset);
        }

        public Mesh ToMesh(string name, bool recalculateNormals = false)
        {
            var mesh = new Mesh { name = name };
            if (Positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetUVs(2, Uv2);
            mesh.SetColors(Colors);
            mesh.subMeshCount = Indices1.Count > 0 ? 2 : 1;
            mesh.SetTriangles(Indices, 0);
            if (Indices1.Count > 0) mesh.SetTriangles(Indices1, 1);
            if (recalculateNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }
    }

    public static class Icosphere
    {
        /// <summary>Unit icosphere with shared vertices (smooth normals). subdivisions 0..4.</summary>
        public static void Build(int subdivisions, List<Vector3> vertices, List<int> triangles)
        {
            vertices.Clear();
            triangles.Clear();
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] baseVerts =
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            foreach (var v in baseVerts) vertices.Add(v.normalized);
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            triangles.AddRange(faces);

            var cache = new Dictionary<long, int>();
            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(triangles.Count * 4);
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Midpoint(a, b, vertices, cache);
                    int bc = Midpoint(b, c, vertices, cache);
                    int ca = Midpoint(c, a, vertices, cache);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles.Clear();
                triangles.AddRange(next);
            }
        }

        static int Midpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            idx = verts.Count - 1;
            cache[key] = idx;
            return idx;
        }
    }
}
