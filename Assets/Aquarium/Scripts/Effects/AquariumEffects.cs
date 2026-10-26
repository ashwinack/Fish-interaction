using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>
    /// Particle effects: rising bubble streams from seabed vents, drifting marine snow, bubble bursts
    /// where people touch the glass and sand clouds on the seabed below the floor screen. Particles are
    /// carried by the fluid current. All systems face the shared eye point, so they match across screens.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class AquariumEffects : MonoBehaviour
    {
        AquariumDirector m_Director;
        ParticleSystem m_Snow, m_TouchBubbles, m_Sand;
        readonly List<ParticleSystem> m_VentSystems = new List<ParticleSystem>();
        ParticleSystem.Particle[] m_Buffer = new ParticleSystem.Particle[4096];
        Texture2D m_BubbleTex, m_DotTex;
        readonly List<Material> m_Materials = new List<Material>();

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            m_BubbleTex = TextureFactory.Bubble(64);
            m_DotTex = TextureFactory.SoftDot(32);
            var tank = director.tank;

            var bubbleMat = CreateMaterial("Bubbles", m_BubbleTex, additive: false, boost: 1.4f);
            var snowMat = CreateMaterial("Marine Snow", m_DotTex, additive: true, boost: 1f);
            var sandMat = CreateMaterial("Sand Cloud", m_DotTex, additive: false, boost: 1f);

            // --- Marine snow filling the visible water volume ------------------------------------
            m_Snow = CreateSystem("Marine Snow", snowMat, 3000);
            {
                var main = m_Snow.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.055f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.92f, 1f, 0.45f), new Color(0.9f, 1f, 0.95f, 0.8f));
                main.gravityModifier = 0.002f;
                main.prewarm = true;
                var emission = m_Snow.emission;
                emission.rateOverTime = 160f;
                var shape = m_Snow.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                float height = tank.surfaceHeight - tank.seabedDepth;
                shape.position = new Vector3(0f, (tank.surfaceHeight + tank.seabedDepth) * 0.5f, 0f);
                shape.scale = new Vector3(tank.halfWidth * 1.6f, height, 44f);
                var noise = m_Snow.noise;
                noise.enabled = true;
                noise.strength = 0.06f;
                noise.frequency = 0.15f;
                noise.scrollSpeed = 0.05f;
                FadeInOut(m_Snow);
            }

            // --- Touch bubbles (emitted from script) -------------------------------------------
            m_TouchBubbles = CreateSystem("Touch Bubbles", bubbleMat, 1500);
            {
                var main = m_TouchBubbles.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
                main.gravityModifier = -0.12f; // buoyancy
                var emission = m_TouchBubbles.emission;
                emission.enabled = false;
                var noise = m_TouchBubbles.noise;
                noise.enabled = true;
                noise.strength = 0.35f;
                noise.frequency = 1.2f;
                var limit = m_TouchBubbles.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 1.6f;
                limit.dampen = 0.08f;
                var sol = m_TouchBubbles.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 1.25f)));
                FadeInOut(m_TouchBubbles, 0.02f, 0.85f);
            }

            // --- Sand clouds -----------------------------------------------------------------
            m_Sand = CreateSystem("Sand Clouds", sandMat, 800);
            {
                var main = m_Sand.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                main.gravityModifier = 0.01f;
                var emission = m_Sand.emission;
                emission.enabled = false;
                var limit = m_Sand.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 0.3f;
                limit.dampen = 0.15f;
                var sol = m_Sand.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 2.2f)));
                FadeInOut(m_Sand, 0.1f, 0.5f);
            }

            // --- Bubble vents on the seabed ---------------------------------------------------
            var rng = new System.Random(director.seed + 99);
            var ventPositions = new List<Vector3>();
            for (int i = 0; i < 7; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = i == 6 ? 0.6f : side * Mathf.Lerp(director.tunnel.width * 0.5f + 1f, tank.halfWidth * 0.55f, (float)rng.NextDouble());
                float z = Mathf.Lerp(-16f, 16f, (float)rng.NextDouble());
                ventPositions.Add(new Vector3(x, Seabed.Height(x, z), z));
            }
            foreach (var pos in ventPositions)
            {
                var vent = CreateSystem("Bubble Vent", bubbleMat, 300);
                vent.transform.position = pos;
                vent.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                var main = vent.main;
                float rise = tank.surfaceHeight - pos.y;
                main.startLifetime = new ParticleSystem.MinMaxCurve(rise / 1.2f, rise / 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.1f);
                main.prewarm = true;
                var emission = vent.emission;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(6f, 14f);
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 12, 0, Mathf.Lerp(2f, 5f, (float)rng.NextDouble())) });
                var shape = vent.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 6f;
                shape.radius = 0.12f;
                var noise = vent.noise;
                noise.enabled = true;
                noise.strength = 0.25f;
                noise.frequency = 0.9f;
                var sol = vent.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.4f)));
                FadeInOut(vent, 0.03f, 0.95f);
                vent.Play();
                m_VentSystems.Add(vent);
            }

            m_Snow.Play();
        }

        Material CreateMaterial(string name, Texture tex, bool additive, float boost)
        {
            var m = new Material(m_Director.Resources.Particle) { name = name };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Boost", boost);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m_Materials.Add(m);
            return m;
        }

        ParticleSystem CreateSystem(string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var shape = ps.shape;
            shape.enabled = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            // Face the camera position (shared eye) rather than the view plane: identical across screens.
            r.alignment = ParticleSystemRenderSpace.Facing;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 5f;
            return ps;
        }

        static void FadeInOut(ParticleSystem ps, float fadeIn = 0.15f, float fadeOutStart = 0.8f)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        public void EmitTouchBubbles(Vector3 position, Vector3 direction, int count)
        {
            if (m_TouchBubbles == null) return;
            if (!m_TouchBubbles.isPlaying) m_TouchBubbles.Play();
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector3 jitter = Random.insideUnitSphere;
                ep.position = position + jitter * 0.12f;
                ep.velocity = direction * Random.Range(0.3f, 1.4f) + jitter * 0.6f + Vector3.up * Random.Range(0.1f, 0.5f);
                ep.startSize = Random.Range(0.015f, 0.08f);
                ep.startLifetime = Random.Range(2.5f, 5.5f);
                ep.startColor = new Color(0.85f, 0.97f, 1f, Random.Range(0.6f, 0.95f));
                m_TouchBubbles.Emit(ep, 1);
            }
        }

        public void EmitSandPuff(Vector3 position, int count)
        {
            if (m_Sand == null) return;
            if (!m_Sand.isPlaying) m_Sand.Play();
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector2 c = Random.insideUnitCircle;
                ep.position = position + new Vector3(c.x, 0.05f, c.y) * 0.4f;
                ep.velocity = new Vector3(c.x * 1.2f, Random.Range(0.15f, 0.6f), c.y * 1.2f);
                ep.startSize = Random.Range(0.3f, 0.8f);
                ep.startLifetime = Random.Range(2.5f, 4.5f);
                ep.startColor = new Color(0.62f, 0.58f, 0.46f, Random.Range(0.18f, 0.35f));
                m_Sand.Emit(ep, 1);
            }
        }

        void LateUpdate()
        {
            if (m_Director == null) return;
            float dt = Time.deltaTime;
            var fluid = m_Director.Fluid;
            float surface = m_Director.tank.surfaceHeight - 0.05f;
            Advect(m_Snow, fluid, dt, 0.9f, float.MaxValue);
            Advect(m_TouchBubbles, fluid, dt, 0.6f, surface);
            Advect(m_Sand, fluid, dt, 0.7f, float.MaxValue);
            foreach (var v in m_VentSystems) Advect(v, fluid, dt, 0.5f, surface);
        }

        /// <summary>Move particles with the fluid current and pop bubbles at the surface.</summary>
        void Advect(ParticleSystem ps, FluidSimulation fluid, float dt, float influence, float killAboveY)
        {
            if (ps == null || fluid == null) return;
            int count = ps.particleCount;
            if (count == 0) return;
            if (m_Buffer.Length < count) m_Buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            count = ps.GetParticles(m_Buffer, count);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = m_Buffer[i].position;
                Vector2 flow = fluid.SampleVelocity(p);
                p.x += flow.x * influence * dt;
                p.z += flow.y * influence * dt;
                m_Buffer[i].position = p;
                if (p.y > killAboveY) m_Buffer[i].remainingLifetime = 0f;
            }
            ps.SetParticles(m_Buffer, count);
        }

        void OnDestroy()
        {
            foreach (var m in m_Materials) if (m != null) Destroy(m);
            if (m_BubbleTex != null) Destroy(m_BubbleTex);
            if (m_DotTex != null) Destroy(m_DotTex);
        }
    }

    /// <summary>Procedural particle textures (no texture assets needed).</summary>
    public static class TextureFactory
    {
        public static Texture2D Bubble(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Bubble", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float rim = Mathf.SmoothStep(0.6f, 0.9f, r) * (1f - Mathf.SmoothStep(0.9f, 1f, r));
                float body = (1f - Mathf.SmoothStep(0.85f, 1f, r)) * 0.12f;
                float hx = u + 0.35f, hy = v - 0.35f;
                float highlight = Mathf.Exp(-(hx * hx + hy * hy) * 40f);
                float a = Mathf.Clamp01(rim * 0.9f + body + highlight);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        public static Texture2D SoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Soft Dot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Exp(-(u * u + v * v) * 4.5f) * (1f - Mathf.SmoothStep(0.85f, 1f, Mathf.Sqrt(u * u + v * v)));
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
