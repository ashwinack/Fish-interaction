using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>
    /// Food flakes sprinkled where a visitor holds a finger on the glass. Flakes drift down with the water
    /// current; curious fish on the same display chase and eat them.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class FoodSystem : MonoBehaviour
    {
        struct Flake
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float Seed;
            public int Screen;
            public float Size;
        }

        const int MaxFlakes = 200;
        const float Lifetime = 30f;

        AquariumDirector m_Director;
        readonly List<Flake> m_Flakes = new List<Flake>();
        ParticleSystem m_Renderer;
        ParticleSystem.Particle[] m_Particles = new ParticleSystem.Particle[MaxFlakes];
        Material m_Material;
        Texture2D m_Texture;

        public int Count => m_Flakes.Count;

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            m_Texture = TextureFactory.SoftDot(16);
            m_Material = new Material(director.Resources.Particle) { name = "Food Flakes" };
            m_Material.SetTexture("_MainTex", m_Texture);
            m_Material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m_Material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m_Material.SetFloat("_Boost", 1.3f);

            var go = new GameObject("Food Flakes");
            go.transform.SetParent(transform, false);
            m_Renderer = go.AddComponent<ParticleSystem>();
            m_Renderer.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = m_Renderer.main;
            main.maxParticles = MaxFlakes;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;
            var emission = m_Renderer.emission;
            emission.enabled = false;
            var shape = m_Renderer.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m_Material;
            r.alignment = ParticleSystemRenderSpace.Facing;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            m_Renderer.Play();
        }

        /// <summary>Sprinkle flakes around a point in the water (world space).</summary>
        public void Drop(Vector3 position, int count)
        {
            for (int i = 0; i < count && m_Flakes.Count < MaxFlakes; i++)
            {
                m_Flakes.Add(new Flake
                {
                    Position = position + Random.insideUnitSphere * 0.25f,
                    Velocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(-0.05f, 0.1f), Random.Range(-0.15f, 0.15f)),
                    Seed = Random.value * 100f,
                    Size = Random.Range(0.025f, 0.05f) * Mathf.Sqrt(m_Director.fishSizeMultiplier),
                    Screen = -1,
                });
            }
        }

        /// <summary>Nearest flake visible on the given screen within radius.</summary>
        public bool TryGetNearest(Vector3 from, int screen, float radius, out Vector3 flakePosition)
        {
            flakePosition = default;
            float best = radius * radius;
            bool found = false;
            for (int i = 0; i < m_Flakes.Count; i++)
            {
                var f = m_Flakes[i];
                if (f.Screen != screen) continue;
                float d2 = (f.Position - from).sqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    flakePosition = f.Position;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Eat the nearest flake within reach of a mouth. Returns true if one was eaten.</summary>
        public bool TryEat(Vector3 mouth, float reach)
        {
            int bestIndex = -1;
            float best = reach * reach;
            for (int i = 0; i < m_Flakes.Count; i++)
            {
                float d2 = (m_Flakes[i].Position - mouth).sqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    bestIndex = i;
                }
            }
            if (bestIndex < 0) return false;
            m_Flakes.RemoveAt(bestIndex);
            return true;
        }

        void Update()
        {
            if (m_Director == null) return;
            float dt = Time.deltaTime;
            var rig = m_Director.Rig;
            var fluid = m_Director.Fluid;
            for (int i = m_Flakes.Count - 1; i >= 0; i--)
            {
                var f = m_Flakes[i];
                f.Age += dt;
                float bed = Seabed.Height(f.Position.x, f.Position.z) + 0.03f;
                if (f.Age > Lifetime || f.Position.y < bed - 0.1f)
                {
                    m_Flakes.RemoveAt(i);
                    continue;
                }
                // Flakes flutter as they sink and are carried by the current.
                f.Velocity = Vector3.Lerp(f.Velocity, new Vector3(0f, -0.1f, 0f), 1f - Mathf.Exp(-dt * 1.5f));
                Vector3 wobble = new Vector3(Mathf.Sin(Time.time * 2.3f + f.Seed), 0f, Mathf.Cos(Time.time * 1.9f + f.Seed * 1.3f)) * 0.06f;
                Vector2 flow = fluid != null ? fluid.SampleVelocity(f.Position) : Vector2.zero;
                f.Position += (f.Velocity + wobble + new Vector3(flow.x, 0f, flow.y) * 0.8f) * dt;
                if (f.Position.y < bed) f.Position.y = bed; // settle on the sand
                f.Screen = rig != null ? rig.ScreenIndexForPoint(f.Position) : -1;
                m_Flakes[i] = f;
            }

            int n = m_Flakes.Count;
            for (int i = 0; i < n; i++)
            {
                var f = m_Flakes[i];
                float fade = Mathf.Clamp01((Lifetime - f.Age) / 3f) * Mathf.Clamp01(f.Age * 4f);
                m_Particles[i].position = f.Position;
                m_Particles[i].startSize = f.Size;
                m_Particles[i].startColor = new Color(0.95f, 0.72f, 0.38f, 0.95f * fade);
                m_Particles[i].remainingLifetime = 100f;
                m_Particles[i].startLifetime = 1000f;
            }
            m_Renderer.SetParticles(m_Particles, n);
        }

        void OnDestroy()
        {
            if (m_Material != null) Destroy(m_Material);
            if (m_Texture != null) Destroy(m_Texture);
        }
    }
}
