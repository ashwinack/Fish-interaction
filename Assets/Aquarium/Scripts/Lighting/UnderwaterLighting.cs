using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aquarium
{
    /// <summary>
    /// Sun through the water surface (soft shadows + animated caustics in every aquarium shader),
    /// underwater fog and ambient, tunnel floodlights, pulsing bioluminescent lights and a global
    /// post-processing volume shared by all three cameras.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class UnderwaterLighting : MonoBehaviour
    {
        struct PulseLight
        {
            public Light Light;
            public float BaseIntensity;
            public float Speed;
            public float Phase;
        }

        AquariumDirector m_Director;
        Light m_Sun;
        Quaternion m_SunBase;
        readonly List<PulseLight> m_Pulses = new List<PulseLight>();
        VolumeProfile m_Profile;
        Cubemap m_Reflection;

        public Light Sun => m_Sun;
        public Vector3 SunDirection => m_Sun != null ? m_Sun.transform.forward : Vector3.down;

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            var w = director.water;

            // Sun
            var sunGo = new GameObject("Sun (through surface)");
            sunGo.transform.SetParent(transform, false);
            m_SunBase = Quaternion.Euler(w.sunAngles.x, w.sunAngles.y, 0f);
            sunGo.transform.rotation = m_SunBase;
            m_Sun = sunGo.AddComponent<Light>();
            m_Sun.type = LightType.Directional;
            m_Sun.color = w.sunColor;
            m_Sun.intensity = w.sunIntensity;
            m_Sun.shadows = LightShadows.Soft;
            m_Sun.shadowStrength = 0.85f;
            m_Sun.shadowBias = 0.05f;
            m_Sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = m_Sun;

            // Fog, ambient and reflections
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = w.fogColor;
            RenderSettings.fogDensity = w.fogDensity;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = w.ambientTop;
            RenderSettings.ambientEquatorColor = w.ambientEquator;
            RenderSettings.ambientGroundColor = w.ambientBottom;
            RenderSettings.ambientProbe = BuildAmbientProbe(w);
            m_Reflection = BuildReflectionCubemap(w);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = m_Reflection;
            RenderSettings.reflectionIntensity = 1f;

            Shader.SetGlobalVector("_AquaCausticParams", new Vector4(w.causticScale, w.causticSpeed, w.causticIntensity, director.tank.surfaceHeight));
            Shader.SetGlobalVector("_AquaWaterParams", new Vector4(w.absorption, 0f, 0f, 0f));
            Shader.SetGlobalVector("_AquaScatter", new Vector4(w.sunScatter, w.surfaceGlow, director.tank.seabedDepth, 6f));
            // The aquarium shaders compute fog themselves (independent of URP fog keywords, which builds can strip).
            Shader.SetGlobalVector("_AquaFogParams", new Vector4(w.fogDensity, 1f, 0f, 0f));

            if (w.floodLights) BuildFloodLights(director);
            if (w.postProcessing) BuildPostProcessing();
        }

        static SphericalHarmonicsL2 BuildAmbientProbe(WaterSettings w)
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(w.ambientEquator);
            sh.AddDirectionalLight(Vector3.up, w.ambientTop - w.ambientEquator * 0.5f, 1.2f);
            sh.AddDirectionalLight(Vector3.down, w.ambientBottom, 0.6f);
            return sh;
        }

        static Cubemap BuildReflectionCubemap(WaterSettings w)
        {
            const int size = 16;
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, false) { name = "Underwater Reflection" };
            Color top = w.ambientTop * 2.2f, mid = w.fogColor * 1.3f, bottom = w.ambientBottom * 0.6f;
            foreach (CubemapFace face in System.Enum.GetValues(typeof(CubemapFace)))
            {
                if (face == CubemapFace.Unknown) continue;
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector3 d = CubeDirection(face, (x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    float up = d.y;
                    px[y * size + x] = up > 0f ? Color.Lerp(mid, top, Mathf.Pow(up, 0.7f)) : Color.Lerp(mid, bottom, -up);
                }
                cube.SetPixels(px, face);
            }
            cube.Apply(false, false);
            return cube;
        }

        static Vector3 CubeDirection(CubemapFace face, float u, float v)
        {
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, -v, -u).normalized;
                case CubemapFace.NegativeX: return new Vector3(-1f, -v, u).normalized;
                case CubemapFace.PositiveY: return new Vector3(u, 1f, v).normalized;
                case CubemapFace.NegativeY: return new Vector3(u, -1f, -v).normalized;
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f).normalized;
                default: return new Vector3(-u, -v, -1f).normalized;
            }
        }

        void BuildFloodLights(AquariumDirector director)
        {
            // Spot lights along the base of the tunnel, shining out into the tank like an aquarium's
            // display lighting. They light fish passing close to the glass.
            float hw = director.tunnel.width * 0.5f + director.tank.glassMargin + 0.2f;
            float[] zs = { -director.tunnel.length * 0.45f, director.tunnel.length * 0.45f };
            foreach (float side in new[] { -1f, 1f })
            foreach (float z in zs)
            {
                var go = new GameObject("Tunnel Floodlight");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(side * hw, -0.4f, z);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(side, -0.35f, -Mathf.Sign(z) * 0.25f));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 85f;
                l.innerSpotAngle = 40f;
                l.range = 16f;
                l.intensity = 9f;
                l.color = new Color(0.62f, 0.86f, 1f);
                l.shadows = LightShadows.None;
            }
        }

        /// <summary>Pulsing point light, used by the environment for bioluminescent coral.</summary>
        public void AddPulseLight(Vector3 position, Color color, float intensity, float range, float speed, float phase)
        {
            var go = new GameObject("Bioluminescence");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            m_Pulses.Add(new PulseLight { Light = l, BaseIntensity = intensity, Speed = speed, Phase = phase });
        }

        void BuildPostProcessing()
        {
            var go = new GameObject("Underwater Post Processing");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.name = "Underwater Profile";

            var bloom = m_Profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(0.75f, 0.92f, 1f));

            var tone = m_Profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var color = m_Profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.4f);
            color.contrast.Override(12f);
            color.saturation.Override(12f);
            color.colorFilter.Override(new Color(0.94f, 1f, 1f));

            var lgg = m_Profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(0.98f, 1.0f, 1.03f, 0f));

            // No vignette / chromatic aberration / lens effects: they would break the seams between screens.
            volume.sharedProfile = m_Profile;
        }

        void Update()
        {
            if (m_Sun == null) return;
            float t = Time.time;
            var w = m_Director.water;

            // Surface waves gently sway the sun beam and its intensity: moving shadows and caustics.
            m_Sun.transform.rotation = m_SunBase * Quaternion.Euler(Mathf.Sin(t * 0.21f) * 2.2f, Mathf.Sin(t * 0.13f + 1.3f) * 3.5f, 0f);
            m_Sun.intensity = w.sunIntensity * (1f + Mathf.Sin(t * 0.9f) * 0.04f + Mathf.Sin(t * 2.3f + 0.7f) * 0.025f);
            Shader.SetGlobalVector("_AquaCausticParams", new Vector4(w.causticScale, w.causticSpeed, w.causticIntensity, m_Director.tank.surfaceHeight));
            Shader.SetGlobalVector("_AquaWaterParams", new Vector4(w.absorption, t, 0f, 0f));
            Shader.SetGlobalVector("_AquaScatter", new Vector4(w.sunScatter, w.surfaceGlow, m_Director.tank.seabedDepth, 6f));
            Shader.SetGlobalVector("_AquaFogParams", new Vector4(RenderSettings.fogDensity, RenderSettings.fog ? 1f : 0f, 0f, 0f));

            foreach (var p in m_Pulses)
            {
                float s = 0.5f + 0.5f * Mathf.Sin(t * p.Speed + p.Phase);
                p.Light.intensity = p.BaseIntensity * (0.35f + 0.65f * s * s);
            }
        }

        void OnDestroy()
        {
            if (m_Profile != null) Destroy(m_Profile);
            if (m_Reflection != null) Destroy(m_Reflection);
        }
    }
}
