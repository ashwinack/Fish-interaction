using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>Sphere that fish steer around.</summary>
    public struct Obstacle
    {
        public Vector3 Center;
        public float Radius;
    }

    /// <summary>
    /// Procedurally builds the aquarium around the tunnel: sandy seabed, reef walls of rock behind the
    /// wall screens, coral and anemone gardens below the floor screen, kelp forests, seagrass, the
    /// underside of the water surface and god-ray light shafts.
    /// </summary>
    public sealed class AquariumEnvironment : MonoBehaviour
    {
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();

        AquariumDirector m_Director;
        System.Random m_Rng;
        readonly List<Object> m_Owned = new List<Object>();
        Material m_Sand, m_Kelp, m_Seagrass, m_Surface, m_Shaft;
        Material[] m_RockMats, m_CoralMats, m_AnemoneMats;
        Mesh[] m_RockMeshes;

        float Rand() => (float)m_Rng.NextDouble();
        float Range(float a, float b) => Mathf.Lerp(a, b, Rand());

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            m_Rng = new System.Random(director.seed);
            CreateMaterials();
            BuildSeabed();
            BuildRocks();
            BuildCoral();
            BuildKelp();
            BuildSeagrass();
            BuildAnemones();
            BuildSurface();
            BuildLightShafts();
            BuildBackdrop();
            BuildProps();
        }

        // ------------------------------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------------------------------
        Material Mat(Shader shader, string name)
        {
            var m = new Material(shader) { name = name, enableInstancing = true };
            m_Owned.Add(m);
            return m;
        }

        Material EnvMat(string name, Color a, Color b, float noise, float smooth, float ripples, Color emission, float caustics = 1f)
        {
            var m = Mat(m_Director.Resources.Environment, name);
            m.SetColor("_BaseColor", a);
            m.SetColor("_SecondColor", b);
            m.SetFloat("_NoiseScale", noise);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_SandRipples", ripples);
            m.SetFloat("_CausticStrength", caustics);
            m.SetColor("_EmissionColor", emission);
            m.SetVector("_EmissionPulse", new Vector4(Range(0.4f, 1.1f), 0.6f, 0f, 0f));
            return m;
        }

        Material KelpMat(string name, Color root, Color tip, Vector4 sway, float translucency, Color glow, float push)
        {
            var m = Mat(m_Director.Resources.Kelp, name);
            m.SetColor("_BaseColor", root);
            m.SetColor("_TipColor", tip);
            m.SetVector("_Sway", sway);
            m.SetFloat("_Translucency", translucency);
            m.SetColor("_EmissionColor", glow);
            m.SetFloat("_PushResponse", push);
            return m;
        }

        void CreateMaterials()
        {
            m_Sand = EnvMat("Sand", new Color(0.56f, 0.52f, 0.42f), new Color(0.42f, 0.39f, 0.31f), 0.35f, 0.1f, 1f, Color.black, 0.9f);
            m_RockMats = new[]
            {
                EnvMat("Rock Grey", new Color(0.42f, 0.4f, 0.38f), new Color(0.25f, 0.24f, 0.24f), 0.5f, 0.15f, 0f, Color.black),
                EnvMat("Rock Brown", new Color(0.45f, 0.37f, 0.3f), new Color(0.27f, 0.22f, 0.2f), 0.6f, 0.12f, 0f, Color.black),
                EnvMat("Rock Dark", new Color(0.3f, 0.3f, 0.32f), new Color(0.16f, 0.17f, 0.19f), 0.45f, 0.2f, 0f, Color.black),
            };
            m_CoralMats = new[]
            {
                EnvMat("Coral Pink", new Color(1f, 0.42f, 0.55f), new Color(0.8f, 0.25f, 0.4f), 3f, 0.3f, 0f, new Color(0.6f, 0.08f, 0.2f)),
                EnvMat("Coral Purple", new Color(0.62f, 0.35f, 0.95f), new Color(0.4f, 0.2f, 0.7f), 3f, 0.3f, 0f, new Color(0.3f, 0.1f, 0.6f)),
                EnvMat("Coral Orange", new Color(1f, 0.55f, 0.18f), new Color(0.85f, 0.35f, 0.1f), 3f, 0.3f, 0f, new Color(0.5f, 0.18f, 0.02f)),
                EnvMat("Coral Teal", new Color(0.25f, 0.85f, 0.75f), new Color(0.1f, 0.55f, 0.5f), 2.5f, 0.35f, 0f, new Color(0.05f, 0.4f, 0.35f)),
                EnvMat("Brain Coral", new Color(0.75f, 0.8f, 0.35f), new Color(0.45f, 0.5f, 0.2f), 5f, 0.25f, 0f, new Color(0.08f, 0.1f, 0.02f)),
            };
            m_Kelp = KelpMat("Kelp", new Color(0.2f, 0.18f, 0.06f), new Color(0.55f, 0.5f, 0.12f), new Vector4(0.7f, 0.45f, 1.4f, 1.5f), 1f, Color.black, 1.4f);
            m_Seagrass = KelpMat("Seagrass", new Color(0.12f, 0.3f, 0.08f), new Color(0.4f, 0.65f, 0.2f), new Vector4(0.12f, 0.9f, 0.5f, 1.3f), 0.8f, Color.black, 0.32f);
            m_AnemoneMats = new[]
            {
                KelpMat("Anemone Magenta", new Color(0.7f, 0.2f, 0.45f), new Color(1f, 0.6f, 0.85f), new Vector4(0.05f, 1.4f, 0.25f, 1.2f), 1.2f, new Color(0.6f, 0.1f, 0.4f), 0.2f),
                KelpMat("Anemone Green", new Color(0.3f, 0.6f, 0.3f), new Color(0.75f, 1f, 0.55f), new Vector4(0.05f, 1.2f, 0.25f, 1.2f), 1.2f, new Color(0.2f, 0.5f, 0.15f), 0.2f),
            };
            m_Surface = Mat(m_Director.Resources.WaterSurface, "Water Surface");
            m_Shaft = Mat(m_Director.Resources.LightShaft, "Light Shaft");
            m_Shaft.SetFloat("_Intensity", m_Director.water.godRayIntensity);
            m_Shaft.SetColor("_Color", new Color(0.75f, 0.93f, 1f));
        }

        GameObject Spawn(string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Not static: static batching merges meshes and drops the per-blade UV data the plant shader needs.
            return go;
        }

        Mesh Own(Mesh m)
        {
            m_Owned.Add(m);
            return m;
        }

        // ------------------------------------------------------------------------------------------
        // Seabed
        // ------------------------------------------------------------------------------------------
        void BuildSeabed()
        {
            var tank = m_Director.tank;
            float hw = tank.halfWidth + 6f, hl = tank.halfLength + 10f;
            const float step = 0.5f;
            int nx = Mathf.CeilToInt(hw * 2f / step) + 1;
            int nz = Mathf.CeilToInt(hl * 2f / step) + 1;
            var mb = new MeshBuilder();
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float x = -hw + ix * step, z = -hl + iz * step;
                float y = Seabed.Height(x, z);
                float hx = Seabed.Height(x + 0.1f, z) - Seabed.Height(x - 0.1f, z);
                float hz = Seabed.Height(x, z + 0.1f) - Seabed.Height(x, z - 0.1f);
                Vector3 n = new Vector3(-hx / 0.2f, 1f, -hz / 0.2f).normalized;
                // Darker, rockier sand on the slopes; pale sand in the middle.
                float slope = 1f - n.y;
                float patch = Noise.Fbm3(new Vector3(x * 0.15f, 0f, z * 0.15f));
                Color c = Color.Lerp(Color.white, new Color(0.62f, 0.62f, 0.6f), Mathf.Clamp01(slope * 4f + (patch - 0.5f) * 0.8f));
                mb.AddVertex(new Vector3(x, y, z), n, new Vector2(x, z), Vector2.zero, c);
            }
            for (int iz = 0; iz < nz - 1; iz++)
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix, b = a + 1, c = a + nx, d = c + 1;
                mb.AddTriangleOriented(a, c, b);
                mb.AddTriangleOriented(b, c, d);
            }
            var go = Spawn("Seabed", Own(mb.ToMesh("Seabed")), m_Sand, Vector3.zero, Quaternion.identity, Vector3.one, castShadows: false);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------------------------------
        // Rocks and reef walls
        // ------------------------------------------------------------------------------------------
        Mesh BuildRockMesh(int variant)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Icosphere.Build(3, verts, tris);
            var mb = new MeshBuilder();
            Vector3 seed = new Vector3(variant * 13.1f, variant * 7.7f, variant * 3.3f);
            foreach (var v in verts)
            {
                float d = 1f + (Noise.Fbm3(v * 1.4f + seed) - 0.5f) * 0.9f;
                d += (Noise.Value3(v * 5f + seed) - 0.5f) * 0.12f;
                // Ridged strata for some variants
                if (variant % 2 == 1) d += Mathf.Abs(Mathf.Sin(v.y * 9f + Noise.Value3(v * 2f + seed) * 3f)) * 0.06f;
                Vector3 p = v * d;
                if (p.y < -0.35f) p.y = -0.35f + (p.y + 0.35f) * 0.25f; // flat base
                float algae = Mathf.Clamp01(v.y * 1.4f) * Mathf.Clamp01(Noise.Fbm3(v * 3f + seed * 2f) * 2f - 0.4f);
                Color c = Color.Lerp(Color.white, new Color(0.55f, 0.85f, 0.45f), algae);
                mb.AddVertex(p, v, Vector2.zero, Vector2.zero, c);
            }
            for (int i = 0; i < tris.Count; i += 3) mb.AddTriangleOriented(tris[i], tris[i + 1], tris[i + 2]);
            return Own(mb.ToMesh("Rock " + variant, recalculateNormals: true));
        }

        void PlaceRock(Vector3 basePos, float radius, float squash, bool obstacle = true)
        {
            int variant = m_Rng.Next(m_RockMeshes.Length);
            var rot = Quaternion.Euler(Range(-8f, 8f), Range(0f, 360f), Range(-8f, 8f));
            var scale = new Vector3(radius * Range(0.8f, 1.25f), radius * squash, radius * Range(0.8f, 1.25f));
            var pos = basePos + Vector3.up * radius * squash * 0.3f;
            Spawn("Rock", m_RockMeshes[variant], m_RockMats[m_Rng.Next(m_RockMats.Length)], pos, rot, scale);
            if (obstacle)
                Obstacles.Add(new Obstacle { Center = pos, Radius = Mathf.Max(scale.x, scale.z) * 0.95f });
        }

        void BuildRocks()
        {
            m_RockMeshes = new Mesh[6];
            for (int i = 0; i < m_RockMeshes.Length; i++) m_RockMeshes[i] = BuildRockMesh(i);
            var tank = m_Director.tank;
            float glass = m_Director.tunnel.width * 0.5f + 2.5f;

            // Reef walls: large boulders stacked behind each wall screen.
            foreach (float side in new[] { -1f, 1f })
            {
                for (float z = -tank.halfLength; z <= tank.halfLength; z += Range(3f, 5f))
                {
                    float x = side * Range(tank.halfWidth - 2.5f, tank.halfWidth + 1f);
                    PlaceRock(new Vector3(x, Seabed.Height(x, z), z), Range(2.2f, 4.2f), Range(0.7f, 1.2f));
                    if (Rand() < 0.55f)
                    {
                        float x2 = side * Range(tank.halfWidth - 4f, tank.halfWidth - 1.5f);
                        float z2 = z + Range(-1.5f, 1.5f);
                        PlaceRock(new Vector3(x2, Seabed.Height(x2, z2) + Range(1f, 3f), z2), Range(1.2f, 2.2f), Range(0.6f, 1f));
                    }
                }
            }

            // Scattered rocks on the seabed (more of them near the tunnel, where people look).
            for (int i = 0; i < 70; i++)
            {
                float x = Range(-tank.halfWidth + 3f, tank.halfWidth - 3f);
                float z = Rand() < 0.6f ? Range(-12f, 12f) : Range(-tank.halfLength, tank.halfLength);
                float r = Range(0.3f, 1.3f);
                PlaceRock(new Vector3(x, Seabed.Height(x, z) - r * 0.1f, z), r, Range(0.5f, 0.9f), obstacle: r > 0.6f);
            }

            // A rocky outcrop directly beneath the floor screen with coral on top.
            for (int i = 0; i < 5; i++)
            {
                float x = Range(-1.5f, 1.5f), z = Range(-2.5f, 2.5f);
                PlaceRock(new Vector3(x, Seabed.Height(x, z), z), Range(0.8f, 1.5f), Range(0.5f, 0.8f));
            }

            // Keep a free lane close to the glass so fish can pass right in front of visitors.
            Obstacles.RemoveAll(o => Mathf.Abs(o.Center.x) < glass && o.Center.y + o.Radius > -0.5f);
        }

        // ------------------------------------------------------------------------------------------
        // Coral
        // ------------------------------------------------------------------------------------------
        static void AddTube(MeshBuilder mb, Vector3 a, Vector3 b, float ra, float rb, int sides, float tipGlow)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 fwd = Vector3.Cross(axis, side);
            int start = mb.VertexCount;
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 c = ring == 0 ? a : b;
                float r = ring == 0 ? ra : rb;
                for (int s = 0; s <= sides; s++)
                {
                    float ang = s / (float)sides * Mathf.PI * 2f;
                    Vector3 n = side * Mathf.Cos(ang) + fwd * Mathf.Sin(ang);
                    mb.AddVertex(c + n * r, n, new Vector2(s / (float)sides, ring), Vector2.zero, new Color(1f, 1f, 1f, ring == 0 ? 0f : tipGlow));
                }
            }
            int row = sides + 1;
            for (int s = 0; s < sides; s++)
            {
                int i0 = start + s, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                mb.AddTriangleOriented(i0, i2, i1);
                mb.AddTriangleOriented(i1, i2, i3);
            }
            // Rounded tip cap
            int tip = mb.AddVertex(b + axis * rb * 0.8f, axis, new Vector2(0.5f, 1f), Vector2.zero, new Color(1f, 1f, 1f, 1f));
            for (int s = 0; s < sides; s++) mb.AddTriangleOriented(start + row + s, start + row + s + 1, tip);
        }

        void BranchCoral(MeshBuilder mb, Vector3 start, Vector3 dir, float length, float radius, int depth)
        {
            Vector3 end = start + dir * length;
            AddTube(mb, start, end, radius, radius * 0.7f, 6, depth == 0 ? 1f : 0.3f);
            if (depth <= 0) return;
            int children = 2 + m_Rng.Next(2);
            for (int i = 0; i < children; i++)
            {
                Vector3 nd = (dir + new Vector3(Range(-0.7f, 0.7f), Range(0.1f, 0.5f), Range(-0.7f, 0.7f))).normalized;
                BranchCoral(mb, end, nd, length * Range(0.6f, 0.85f), radius * 0.7f, depth - 1);
            }
        }

        Mesh BuildBranchCoralMesh()
        {
            var mb = new MeshBuilder();
            int trunks = 2 + m_Rng.Next(3);
            for (int i = 0; i < trunks; i++)
            {
                Vector3 dir = (Vector3.up + new Vector3(Range(-0.4f, 0.4f), 0f, Range(-0.4f, 0.4f))).normalized;
                BranchCoral(mb, new Vector3(Range(-0.1f, 0.1f), -0.05f, Range(-0.1f, 0.1f)), dir, Range(0.18f, 0.3f), Range(0.035f, 0.055f), 3);
            }
            return Own(mb.ToMesh("Branch Coral"));
        }

        Mesh BuildBrainCoralMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Icosphere.Build(3, verts, tris);
            var mb = new MeshBuilder();
            Vector3 seed = new Vector3(Rand() * 50f, Rand() * 50f, Rand() * 50f);
            foreach (var v in verts)
            {
                // Meandering ridges
                float ridge = Mathf.Abs(Mathf.Sin((v.x * 7f + Noise.Value3(v * 3f + seed) * 6f) + v.z * 5f));
                Vector3 p = v * (1f + ridge * 0.06f);
                if (p.y < 0f) p.y *= 0.2f;
                mb.AddVertex(p, v, Vector2.zero, Vector2.zero, new Color(1f - ridge * 0.35f, 1f - ridge * 0.35f, 1f - ridge * 0.35f, ridge));
            }
            for (int i = 0; i < tris.Count; i += 3) mb.AddTriangleOriented(tris[i], tris[i + 1], tris[i + 2]);
            return Own(mb.ToMesh("Brain Coral", recalculateNormals: true));
        }

        void BuildCoral()
        {
            var branchMeshes = new Mesh[4];
            for (int i = 0; i < branchMeshes.Length; i++) branchMeshes[i] = BuildBranchCoralMesh();
            var brainMeshes = new[] { BuildBrainCoralMesh(), BuildBrainCoralMesh() };
            var tank = m_Director.tank;
            var lighting = m_Director.Lighting;

            int clusters = 34;
            for (int i = 0; i < clusters; i++)
            {
                // Dense garden under the floor screen and along the base of the walls.
                float x, z;
                if (i < 14) { x = Range(-4f, 4f); z = Range(-6f, 6f); }
                else { x = Mathf.Sign(Rand() - 0.5f) * Range(3f, tank.halfWidth - 3f); z = Range(-18f, 18f); }
                Vector3 center = new Vector3(x, Seabed.Height(x, z), z);
                int pieces = 3 + m_Rng.Next(5);
                for (int k = 0; k < pieces; k++)
                {
                    Vector3 p = center + new Vector3(Range(-0.9f, 0.9f), 0f, Range(-0.9f, 0.9f));
                    p.y = Seabed.Height(p.x, p.z) - 0.05f;
                    var rot = Quaternion.Euler(0f, Range(0f, 360f), 0f);
                    if (Rand() < 0.7f)
                    {
                        float s = Range(1.2f, 2.6f);
                        Spawn("Branch Coral", branchMeshes[m_Rng.Next(branchMeshes.Length)], m_CoralMats[m_Rng.Next(4)], p, rot, Vector3.one * s);
                    }
                    else
                    {
                        float s = Range(0.3f, 0.7f);
                        Spawn("Brain Coral", brainMeshes[m_Rng.Next(brainMeshes.Length)], m_CoralMats[4], p, rot, new Vector3(s, s * 0.7f, s));
                    }
                }
                if (lighting != null && i % 4 == 0)
                {
                    Color c = Rand() < 0.5f ? new Color(0.2f, 0.9f, 1f) : new Color(0.95f, 0.3f, 0.9f);
                    lighting.AddPulseLight(center + Vector3.up * 0.6f, c, Range(1.5f, 3f), Range(3f, 4.5f), Range(0.5f, 1.3f), Rand() * 10f);
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // Kelp, seagrass, anemones (all use the flow-reactive Kelp shader)
        // ------------------------------------------------------------------------------------------
        void AddBlade(MeshBuilder mb, Vector3 root, float height, float width, float seed, int segments, float twist, Vector2 lean)
        {
            float heading = seed * Mathf.PI * 2f;
            int start = mb.VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 c = root + new Vector3(lean.x * t * t, t * height, lean.y * t * t);
                float ang = heading + twist * t;
                Vector3 right = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float w = width * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Lerp(0.08f, 0.96f, t))), 0.6f);
                Vector3 tangent = Vector3.up;
                Vector3 n = Vector3.Cross(right, tangent).normalized;
                // uv1 = (random seed, blade length), uv2 = blade root: every blade sways and bends on its own.
                var uv1 = new Vector2(seed, height);
                int v0 = mb.AddVertex(c - right * w * 0.5f, n, new Vector2(0f, t), uv1, Color.white);
                int v1 = mb.AddVertex(c + right * w * 0.5f, n, new Vector2(1f, t), uv1, Color.white);
                mb.Uv2[v0] = root;
                mb.Uv2[v1] = root;
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2, b = a + 1, c = a + 2, d = a + 3;
                mb.AddTriangle(a, c, b);
                mb.AddTriangle(b, c, d);
            }
        }

        void BuildKelp()
        {
            var tank = m_Director.tank;
            for (int i = 0; i < 26; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * Range(m_Director.tunnel.width * 0.5f + 5.5f, tank.halfWidth - 2f);
                float z = Range(-tank.halfLength * 0.6f, tank.halfLength * 0.6f);
                Vector3 root = new Vector3(x, Seabed.Height(x, z) - 0.1f, z);
                float maxHeight = tank.surfaceHeight - 0.6f - root.y;
                var mb = new MeshBuilder();
                int blades = 4 + m_Rng.Next(5);
                for (int b = 0; b < blades; b++)
                {
                    float h = Mathf.Min(maxHeight, Range(4.5f, 11f));
                    Vector3 r = new Vector3(Range(-0.3f, 0.3f), 0f, Range(-0.3f, 0.3f));
                    AddBlade(mb, r, h, Range(0.18f, 0.32f), Rand(), 22, Range(-3f, 3f), new Vector2(Range(-0.6f, 0.6f), Range(-0.6f, 0.6f)));
                }
                Spawn("Kelp", Own(mb.ToMesh("Kelp")), m_Kelp, root, Quaternion.identity, Vector3.one);
            }
        }

        void BuildSeagrass()
        {
            var tank = m_Director.tank;
            for (int i = 0; i < 40; i++)
            {
                float x = i < 16 ? Range(-5f, 5f) : Range(-tank.halfWidth + 3f, tank.halfWidth - 3f);
                float z = i < 16 ? Range(-7f, 7f) : Range(-20f, 20f);
                Vector3 root = new Vector3(x, Seabed.Height(x, z), z);
                var mb = new MeshBuilder();
                int blades = 25 + m_Rng.Next(30);
                for (int b = 0; b < blades; b++)
                {
                    Vector2 o = new Vector2(Range(-0.8f, 0.8f), Range(-0.8f, 0.8f));
                    Vector3 r = new Vector3(o.x, Seabed.Height(x + o.x, z + o.y) - root.y - 0.03f, o.y);
                    AddBlade(mb, r, Range(0.3f, 0.9f), Range(0.03f, 0.06f), Rand(), 5, Range(-1f, 1f), new Vector2(Range(-0.15f, 0.15f), Range(-0.15f, 0.15f)));
                }
                Spawn("Seagrass", Own(mb.ToMesh("Seagrass")), m_Seagrass, root, Quaternion.identity, Vector3.one, castShadows: false);
            }
        }

        void BuildAnemones()
        {
            // Homes for the clownfish, mostly visible through the floor.
            for (int i = 0; i < 10; i++)
            {
                float x = Range(-4.5f, 4.5f), z = Range(-6f, 6f);
                Vector3 root = new Vector3(x, Seabed.Height(x, z), z);
                var mb = new MeshBuilder();
                int tentacles = 40 + m_Rng.Next(30);
                float radius = Range(0.18f, 0.32f);
                for (int t = 0; t < tentacles; t++)
                {
                    float ang = Rand() * Mathf.PI * 2f;
                    float rr = Mathf.Sqrt(Rand()) * radius;
                    Vector3 r = new Vector3(Mathf.Cos(ang) * rr, 0.08f, Mathf.Sin(ang) * rr);
                    Vector2 lean = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Range(0.05f, 0.2f);
                    AddBlade(mb, r, Range(0.2f, 0.38f), Range(0.025f, 0.04f), Rand(), 5, Range(-2f, 2f), lean);
                }
                Spawn("Anemone", Own(mb.ToMesh("Anemone")), m_AnemoneMats[i % m_AnemoneMats.Length], root, Quaternion.identity, Vector3.one, castShadows: false);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Water surface and light shafts
        // ------------------------------------------------------------------------------------------
        /// <summary>Live control of the god-ray brightness (control panel).</summary>
        public void SetGodRayIntensity(float intensity)
        {
            if (m_Shaft != null) m_Shaft.SetFloat("_Intensity", intensity);
        }

        /// <summary>Imported environment models (Director > Environment Props) placed on the seabed.</summary>
        void BuildProps()
        {
            var props = m_Director.environmentProps;
            if (props == null) return;
            var tank = m_Director.tank;
            float wall = m_Director.tunnel.width * 0.5f;
            foreach (var prop in props)
            {
                if (prop == null || prop.prefab == null) continue;
                for (int i = 0; i < prop.count; i++)
                {
                    float x, z;
                    switch (prop.placement)
                    {
                        case PropPlacement.UnderFloor:
                            x = Range(-wall * 0.7f, wall * 0.7f);
                            z = Range(-m_Director.tunnel.length * 0.4f, m_Director.tunnel.length * 0.4f);
                            break;
                        case PropPlacement.BehindWalls:
                            x = (i % 2 == 0 ? -1f : 1f) * Range(wall + 3.5f, tank.halfWidth - 2f);
                            z = Range(-18f, 18f);
                            break;
                        default:
                            x = Range(-tank.halfWidth + 2f, tank.halfWidth - 2f);
                            z = Range(-tank.halfLength * 0.6f, tank.halfLength * 0.6f);
                            break;
                    }
                    var go = Instantiate(prop.prefab, transform);
                    go.name = prop.prefab.name;
                    go.transform.SetPositionAndRotation(new Vector3(x, Seabed.Height(x, z) - 0.15f, z), Quaternion.Euler(0f, Range(0f, 360f), 0f));
                    go.transform.localScale = prop.prefab.transform.localScale * Range(prop.minScale, prop.maxScale);
                    var renderers = go.GetComponentsInChildren<Renderer>();
                    foreach (var r in renderers)
                    {
                        CustomFishModel.FixMaterials(r);
                        r.shadowCastingMode = ShadowCastingMode.On;
                        r.receiveShadows = true;
                    }
                    foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                    // Keep props out of the tunnel volume
                    if (prop.placement != PropPlacement.UnderFloor && Mathf.Abs(x) < wall + 2f)
                    {
                        Destroy(go);
                        continue;
                    }

                    if (!prop.obstacle || renderers.Length == 0) continue;
                    // Stack of spheres up the prop so fish avoid tall pillars along their full height.
                    var b = renderers[0].bounds;
                    foreach (var r in renderers) b.Encapsulate(r.bounds);
                    float radius = Mathf.Max(0.3f, Mathf.Max(b.extents.x, b.extents.z) * 0.9f);
                    for (float y = b.min.y + radius * 0.5f; y < b.max.y; y += radius)
                        Obstacles.Add(new Obstacle { Center = new Vector3(b.center.x, y, b.center.z), Radius = radius });
                }
            }
        }

        void BuildBackdrop()
        {
            // Far sphere showing the sun-scattered water colour (see AquaFogColor).
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Icosphere.Build(2, verts, tris);
            var mb = new MeshBuilder();
            foreach (var v in verts) mb.AddVertex(v, -v, Vector2.zero);
            for (int i = 0; i < tris.Count; i += 3) mb.AddTriangle(tris[i], tris[i + 1], tris[i + 2]);
            var mesh = Own(mb.ToMesh("Backdrop"));
            var mat = Mat(m_Director.Resources.Backdrop, "Backdrop");
            float r = m_Director.tunnel.farClip * 0.85f;
            var go = Spawn("Water Backdrop", mesh, mat, m_Director.transform.TransformPoint(m_Director.tunnel.eyePosition), Quaternion.identity, Vector3.one * r, castShadows: false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.receiveShadows = false;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
        }

        void BuildSurface()
        {
            var tank = m_Director.tank;
            float hw = tank.halfWidth + 40f, hl = tank.halfLength + 60f;
            var mb = new MeshBuilder();
            mb.AddVertex(new Vector3(-hw, 0f, -hl), Vector3.down, Vector2.zero);
            mb.AddVertex(new Vector3(hw, 0f, -hl), Vector3.down, Vector2.zero);
            mb.AddVertex(new Vector3(-hw, 0f, hl), Vector3.down, Vector2.zero);
            mb.AddVertex(new Vector3(hw, 0f, hl), Vector3.down, Vector2.zero);
            mb.AddTriangle(0, 1, 2);
            mb.AddTriangle(1, 3, 2);
            Spawn("Water Surface", Own(mb.ToMesh("Water Surface")), m_Surface, new Vector3(0f, tank.surfaceHeight, 0f), Quaternion.identity, Vector3.one, castShadows: false);
        }

        void BuildLightShafts()
        {
            var tank = m_Director.tank;
            var mb = new MeshBuilder();
            mb.AddVertex(new Vector3(-0.5f, -1f, 0f), Vector3.forward, new Vector2(0f, 0f));
            mb.AddVertex(new Vector3(0.5f, -1f, 0f), Vector3.forward, new Vector2(1f, 0f));
            mb.AddVertex(new Vector3(-0.5f, 0f, 0f), Vector3.forward, new Vector2(0f, 1f));
            mb.AddVertex(new Vector3(0.5f, 0f, 0f), Vector3.forward, new Vector2(1f, 1f));
            mb.AddTriangle(0, 2, 1);
            mb.AddTriangle(1, 2, 3);
            var mesh = Own(mb.ToMesh("Light Shaft"));
            // Billboards are expanded in the vertex shader: make the bounds generous.
            mesh.bounds = new Bounds(new Vector3(0f, -0.5f, 0f), new Vector3(3f, 1.2f, 3f));

            var w = m_Director.water;
            Vector3 toSun = -(Quaternion.Euler(w.sunAngles.x, w.sunAngles.y, 0f) * Vector3.forward);
            for (int i = 0; i < m_Director.water.godRayCount; i++)
            {
                float x = Range(-tank.halfWidth, tank.halfWidth);
                float z = Range(-26f, 26f);
                var pos = new Vector3(x, tank.surfaceHeight + 0.2f, z);
                var rot = Quaternion.FromToRotation(Vector3.up, toSun) * Quaternion.Euler(0f, Range(0f, 360f), 0f);
                float length = Range(13f, 20f);
                var go = Spawn("Light Shaft", mesh, m_Shaft, pos, rot, new Vector3(Range(0.7f, 3f), length, 1f), castShadows: false);
                go.GetComponent<MeshRenderer>().receiveShadows = false;
                go.isStatic = false;
            }
        }

        void OnDestroy()
        {
            foreach (var o in m_Owned)
                if (o != null) Destroy(o);
        }
    }
}
