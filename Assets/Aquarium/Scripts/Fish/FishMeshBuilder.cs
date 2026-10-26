using UnityEngine;

namespace Aquarium
{
    /// <summary>
    /// Builds unit-length fish meshes (nose at z = +0.5, tail tip at z = -0.5) from a species description.
    /// Submesh 0 = body (opaque), submesh 1 = fins (translucent).
    /// uv0.x runs 0 (tail tip) .. 1 (nose), uv0.y 0 (belly) .. 1 (back).
    /// uv1 = (fin mask, fin type: 1 caudal, 2 dorsal/anal, 3 pectoral).
    /// uv2 = (fin coordinate from root 0 .. edge 1, coordinate along the fin 0..1, 0).
    /// </summary>
    public static class FishMeshBuilder
    {
        public struct Result
        {
            public Mesh Mesh;
            public Vector3 EyePosition;
            public float EyeRadius;
            public float HalfSpan; // rays: half wing span (object space)
        }

        const int Rings = 30;
        const int Segments = 20;

        const float FinCaudal = 1f, FinDorsal = 2f, FinPectoral = 3f;

        public static Result Build(FishSpecies s)
        {
            return s.bodyType == FishBodyType.Ray ? BuildRay(s) : BuildFish(s);
        }

        // -----------------------------------------------------------------------------------------
        // Fish: body of revolution with an elliptical cross-section plus subdivided fins.
        // -----------------------------------------------------------------------------------------
        static float BodyLength(FishSpecies s) => 1f - s.tailLength;

        static float Profile(FishSpecies s, float t)
        {
            float p = Mathf.Clamp(s.thickestPoint, 0.1f, 0.8f);
            if (t < p)
            {
                float u = t / p;
                float e = 1f - (1f - u) * (1f - u);
                return Mathf.Pow(Mathf.Max(e, 0f), Mathf.Lerp(0.5f, 1.1f, s.noseSharpness));
            }
            float v = (t - p) / (1f - p);
            // Smooth taper into a slim caudal peduncle
            float taper = 0.5f + 0.5f * Mathf.Cos(Mathf.PI * Mathf.Pow(v, 0.9f));
            return s.peduncle + (1f - s.peduncle) * taper;
        }

        /// <summary>Surface point for body parameter t (0 nose .. 1 tail base) and angle theta (0 = top).</summary>
        static Vector3 BodyPoint(FishSpecies s, float t, float theta)
        {
            float r = Profile(s, t);
            float z = 0.5f - t * BodyLength(s);
            float halfH = s.bodyHeight * 0.5f * r;
            float halfW = s.bodyWidth * 0.5f * r;
            float c = Mathf.Cos(theta), sn = Mathf.Sin(theta);
            // Back arches behind the head, belly is fuller and a little flatter; the snout sits slightly low.
            float arch = Mathf.Sin(Mathf.PI * t) * s.bodyHeight * 0.05f;
            float snout = (1f - Mathf.Clamp01(t / 0.12f)) * -s.bodyHeight * 0.06f;
            float y = c * halfH * (c > 0f ? 1f : 0.92f) + arch + snout;
            // Slight gill bulge just behind the head and a flatter, more laterally compressed back
            float gill = 1f + 0.06f * Mathf.Exp(-Mathf.Pow((t - 0.2f) / 0.05f, 2f));
            float x = sn * halfW * gill * (1f - 0.15f * Mathf.Max(0f, c) * Mathf.Max(0f, c));
            return new Vector3(x, y, z);
        }

        static float UvX(float z) => Mathf.Clamp01(z + 0.5f);

        static Result BuildFish(FishSpecies s)
        {
            var mb = new MeshBuilder();
            var white = Color.white;
            const float eps = 0.002f;

            // Body rings (seam duplicated so uvs stay continuous)
            for (int k = 0; k < Rings; k++)
            {
                float t = Mathf.Lerp(0.003f, 1f, Mathf.Pow(k / (float)(Rings - 1), 1.1f));
                for (int j = 0; j <= Segments; j++)
                {
                    float theta = j / (float)Segments * Mathf.PI * 2f;
                    Vector3 p = BodyPoint(s, t, theta);
                    // Derivatives scaled by 1/eps: Vector3.normalized returns zero for tiny vectors.
                    Vector3 dT = (BodyPoint(s, Mathf.Min(t + eps, 1f), theta) - BodyPoint(s, Mathf.Max(t - eps, 0f), theta)) / eps;
                    Vector3 dA = (BodyPoint(s, t, theta + eps) - BodyPoint(s, t, theta - eps)) / eps;
                    Vector3 n = Vector3.Cross(dA, dT).normalized;
                    Vector3 axis = new Vector3(0f, (BodyPoint(s, t, 0f).y + BodyPoint(s, t, Mathf.PI).y) * 0.5f, p.z);
                    if (Vector3.Dot(n, p - axis) < 0f) n = -n;
                    if (n.sqrMagnitude < 0.5f) n = new Vector3(0f, 0f, 1f);
                    mb.AddVertex(p, n, new Vector2(UvX(p.z), 0.5f + 0.5f * Mathf.Cos(theta)), Vector2.zero, white);
                }
            }
            int stride = Segments + 1;
            for (int k = 0; k < Rings - 1; k++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = k * stride + j, b = a + 1, c = a + stride, d = c + 1;
                    mb.AddTriangleOriented(a, c, b);
                    mb.AddTriangleOriented(b, c, d);
                }
            }
            // Cap behind the last ring (hidden by the tail fin)
            {
                int lastRing = (Rings - 1) * stride;
                Vector3 center = Vector3.zero;
                for (int j = 0; j < Segments; j++) center += mb.Positions[lastRing + j];
                center /= Segments;
                int ci = mb.AddVertex(center, Vector3.back, new Vector2(UvX(center.z), 0.5f));
                for (int j = 0; j < Segments; j++)
                    mb.AddTriangleOriented(ci, lastRing + j, lastRing + j + 1);
            }

            float zBase = 0.5f - BodyLength(s);
            float pedHalf = s.bodyHeight * 0.5f * s.peduncle;

            // --- Caudal fin: a grid so it bends smoothly with the body wave -------------------------
            {
                const int nA = 11, nL = 6;
                float topH = s.tailHeight * 0.5f * (1f + s.tailAsymmetry);
                float botH = s.tailHeight * 0.5f * (1f - s.tailAsymmetry);
                float tailLen = 0.5f + zBase;
                int start = mb.VertexCount;
                for (int j = 0; j < nL; j++)
                {
                    float l = j / (float)(nL - 1);
                    for (int i = 0; i < nA; i++)
                    {
                        float a = Mathf.Lerp(-1f, 1f, i / (float)(nA - 1));
                        float aa = Mathf.Abs(a);
                        // Trailing edge: forked (notch in the middle) or rounded for low fork values
                        float trail = -0.5f + tailLen * s.tailFork * (1f - Mathf.Pow(aa, 1.4f))
                                      - tailLen * (1f - s.tailFork) * 0.08f * (1f - aa * aa);
                        float z = Mathf.Lerp(zBase + 0.01f, trail, l);
                        float spanH = a > 0f ? topH : botH;
                        float y = Mathf.Lerp(a * pedHalf, a * spanH, Mathf.Pow(l, 0.75f));
                        AddFinVertex(mb, new Vector3(0f, y, z), Vector3.right, FinCaudal, l, (a + 1f) * 0.5f);
                    }
                }
                AddFinGrid(mb, start, nA, nL);
            }

            // --- Dorsal fin: tapered strip along the back, swept back --------------------------------
            if (s.dorsalHeight > 0.001f)
                AddMedianFin(mb, s, s.dorsalRange.x, s.dorsalRange.y, 0f, s.dorsalHeight);

            // --- Anal fin along the belly --------------------------------------------------------
            if (s.analHeight > 0.001f)
            {
                float t0 = Mathf.Max(0.5f, s.dorsalRange.x + 0.15f), t1 = Mathf.Min(0.92f, s.dorsalRange.y + 0.05f);
                AddMedianFin(mb, s, t0, t1, Mathf.PI, -s.analHeight);
            }

            // --- Pectoral fins: small fans behind the gills (flap in the shader) ----------------------
            if (s.pectoralSize > 0.001f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    const int nB = 5, nR = 4;
                    float theta = side * Mathf.PI * 0.62f;
                    Vector3 root0 = BodyPoint(s, 0.25f, theta), root1 = BodyPoint(s, 0.33f, theta);
                    float ps = s.pectoralSize;
                    Vector3 dir = new Vector3(side * 0.55f, -0.35f, -0.75f).normalized;
                    Vector3 n = Vector3.Cross(root1 - root0, dir).normalized;
                    int start = mb.VertexCount;
                    for (int r = 0; r < nR; r++)
                    {
                        float e = r / (float)(nR - 1);
                        for (int b = 0; b < nB; b++)
                        {
                            float u = b / (float)(nB - 1);
                            float reach = ps * (1f - 0.45f * Mathf.Pow(Mathf.Abs(u - 0.35f) * 1.5f, 2f));
                            Vector3 p = Vector3.Lerp(root0, root1, u) + dir * reach * e;
                            AddFinVertex(mb, p, n, FinPectoral, e, u);
                        }
                    }
                    AddFinGrid(mb, start, nB, nR);
                }
            }

            // Eye on the upper flank just behind the snout
            Vector3 eye = BodyPoint(s, 0.1f, Mathf.PI * 0.36f);
            eye.x = Mathf.Abs(eye.x);

            return new Result
            {
                Mesh = mb.ToMesh(s.name + " Mesh"),
                EyePosition = eye,
                EyeRadius = Mathf.Clamp(s.bodyHeight * 0.11f, 0.012f, 0.05f),
                HalfSpan = s.bodyWidth * 0.5f,
            };
        }

        /// <summary>Dorsal (theta 0, height &gt; 0) or anal (theta PI, height &lt; 0) fin as a subdivided strip.</summary>
        static void AddMedianFin(MeshBuilder mb, FishSpecies s, float t0, float t1, float theta, float height)
        {
            const int nB = 10, nH = 4;
            int start = mb.VertexCount;
            for (int r = 0; r < nH; r++)
            {
                float e = r / (float)(nH - 1);
                for (int b = 0; b < nB; b++)
                {
                    float u = b / (float)(nB - 1);
                    Vector3 root = BodyPoint(s, Mathf.Lerp(t0, t1, u), theta);
                    root.y -= Mathf.Sign(height) * 0.004f;
                    // Tallest in the front third, curving back toward the tail
                    float profile = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.65f))), 0.8f);
                    float h = height * profile;
                    Vector3 tip = root + new Vector3(0f, h, -Mathf.Abs(h) * 0.55f);
                    AddFinVertex(mb, Vector3.Lerp(root, tip, e), Vector3.right, FinDorsal, e, u);
                }
            }
            AddFinGrid(mb, start, nB, nH);
        }

        static void AddFinVertex(MeshBuilder mb, Vector3 p, Vector3 n, float type, float edge, float along)
        {
            int i = mb.AddVertex(p, n, new Vector2(UvX(p.z), 0.5f), new Vector2(1f, type), Color.white);
            mb.Uv2[i] = new Vector3(edge, along, 0f);
        }

        static void AddFinGrid(MeshBuilder mb, int start, int nx, int ny)
        {
            for (int y = 0; y < ny - 1; y++)
            for (int x = 0; x < nx - 1; x++)
            {
                int a = start + y * nx + x, b = a + 1, c = a + nx, d = c + 1;
                mb.AddTriangle1(a, c, b);
                mb.AddTriangle1(b, c, d);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Ray: flat lens-shaped wing disc (top and bottom surfaces) with a whip tail.
        // -----------------------------------------------------------------------------------------
        const float RayBodyFront = 0.5f;
        const float RayBodyBack = -0.1f;

        static Vector3 RayPoint(FishSpecies s, float u, float v, bool top)
        {
            float halfSpan = s.raySpan * 0.5f;
            float width = halfSpan * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01(v), 0.75f))), 0.85f);
            float x = u * width;
            // Wings sweep back toward the tips
            float z = Mathf.Lerp(RayBodyFront, RayBodyBack, v) - Mathf.Abs(u) * Mathf.Abs(u) * 0.18f;
            float thickness = s.bodyHeight * Mathf.Max(0f, 1f - u * u) * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v))), 0.6f);
            float y = top ? thickness : -thickness * 0.55f;
            return new Vector3(x, y, z);
        }

        static Result BuildRay(FishSpecies s)
        {
            var mb = new MeshBuilder();
            const int nu = 24, nv = 20;
            const float e = 0.002f;
            for (int surface = 0; surface < 2; surface++)
            {
                bool top = surface == 0;
                int start = mb.VertexCount;
                for (int iv = 0; iv <= nv; iv++)
                {
                    float v = Mathf.Lerp(0.01f, 0.99f, iv / (float)nv);
                    for (int iu = 0; iu <= nu; iu++)
                    {
                        float u = Mathf.Lerp(-1f, 1f, iu / (float)nu);
                        Vector3 p = RayPoint(s, u, v, top);
                        Vector3 du = (RayPoint(s, Mathf.Min(u + e, 1f), v, top) - RayPoint(s, Mathf.Max(u - e, -1f), v, top)) / e;
                        Vector3 dv = (RayPoint(s, u, Mathf.Min(v + e, 1f), top) - RayPoint(s, u, Mathf.Max(v - e, 0f), top)) / e;
                        Vector3 n = Vector3.Cross(dv, du).normalized;
                        if ((top && n.y < 0f) || (!top && n.y > 0f)) n = -n;
                        if (n.sqrMagnitude < 0.5f) n = top ? Vector3.up : Vector3.down;
                        mb.AddVertex(p, n, new Vector2(UvX(p.z), top ? 1f : 0f), Vector2.zero, Color.white);
                    }
                }
                int row = nu + 1;
                for (int iv = 0; iv < nv; iv++)
                {
                    for (int iu = 0; iu < nu; iu++)
                    {
                        int a = start + iv * row + iu, b = a + 1, c = a + row, d = c + 1;
                        mb.AddTriangleOriented(a, c, b);
                        mb.AddTriangleOriented(b, c, d);
                    }
                }
            }

            // Cephalic lobes ("horns") at the front
            for (int side = -1; side <= 1; side += 2)
            {
                int start = mb.VertexCount;
                Vector3 a = new Vector3(side * 0.08f, 0.01f, RayBodyFront - 0.02f);
                Vector3 b = new Vector3(side * 0.16f, 0.0f, RayBodyFront - 0.06f);
                Vector3 c = new Vector3(side * 0.1f, -0.02f, RayBodyFront + 0.12f);
                AddFinVertex(mb, a, Vector3.up, FinDorsal, 0f, 0f);
                AddFinVertex(mb, b, Vector3.up, FinDorsal, 0f, 1f);
                AddFinVertex(mb, c, Vector3.up, FinDorsal, 1f, 0.5f);
                mb.AddTriangle1(start, start + 1, start + 2);
            }

            // Whip tail
            {
                int start = mb.VertexCount;
                AddFinVertex(mb, new Vector3(0f, 0.015f, RayBodyBack + 0.05f), Vector3.right, FinCaudal, 0f, 1f);
                AddFinVertex(mb, new Vector3(0f, -0.015f, RayBodyBack + 0.05f), Vector3.right, FinCaudal, 0f, 0f);
                AddFinVertex(mb, new Vector3(0f, 0f, -0.5f), Vector3.right, FinCaudal, 0.3f, 0.5f);
                mb.AddTriangle1(start, start + 1, start + 2);
            }

            Vector3 eye = RayPoint(s, 0.3f, 0.12f, true);
            eye.x = Mathf.Abs(eye.x);
            eye.y += 0.005f;

            return new Result
            {
                Mesh = mb.ToMesh(s.name + " Mesh"),
                EyePosition = eye,
                EyeRadius = 0.025f,
                HalfSpan = s.raySpan * 0.5f,
            };
        }
    }
}
