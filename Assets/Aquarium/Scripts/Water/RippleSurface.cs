using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>
    /// Per-screen interactive water layer, drawn by a semi-transparent refractive quad placed exactly on the
    /// physical screen. Two GPU simulations run per screen:
    ///  - a wave equation (height + velocity) for rings and wakes,
    ///  - a 2D stable-fluids solver (velocity + dye) for smooth, swirling liquid that flows with the finger.
    /// Each screen has its own simulations, so a touch only disturbs the display that was touched.
    /// </summary>
    public sealed class RippleSurface : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RippleSplat
        {
            public Vector2 Uv;
            public float Radius;
            public float Strength;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct FluidSplat
        {
            public Vector2 Uv;
            public Vector2 Force;  // texels / s
            public float Radius;   // texels
            public float Dye;
            public Vector2 Pad;
        }

        const int MaxSplats = 64;
        const float IdleTimeout = 12f;
        const int MaxSubsteps = 8;

        WaterLayerSettings m_Settings;
        Material m_Material;
        MeshRenderer m_Renderer;
        float m_LastActivity = -999f;
        bool m_Active;

        // Waves
        ComputeShader m_Wave;
        int m_WaveStep, m_WaveClear;
        RenderTexture m_WaveA, m_WaveB;
        ComputeBuffer m_WaveSplatBuffer;
        readonly List<RippleSplat> m_WavePending = new List<RippleSplat>();
        Vector2Int m_WaveSize;
        Vector2 m_WaveMetresPerTexel;

        // Liquid
        ComputeShader m_Fluid;
        int m_FAdvect, m_FSplat, m_FCurl, m_FVort, m_FDiv, m_FJacobi, m_FGrad, m_FClear;
        RenderTexture m_VelA, m_VelB, m_DyeA, m_DyeB, m_PressA, m_PressB, m_Div, m_Curl;
        ComputeBuffer m_FluidSplatBuffer;
        readonly List<FluidSplat> m_FluidPending = new List<FluidSplat>();
        Vector2Int m_FluidSize;
        Vector2 m_FluidMetresPerTexel;

        public ScreenId Screen { get; private set; }

        static readonly int s_State = Shader.PropertyToID("_State");
        static readonly int s_Next = Shader.PropertyToID("_Next");
        static readonly int s_Splats = Shader.PropertyToID("_Splats");
        static readonly int s_SizeId = Shader.PropertyToID("_Size");
        static readonly int s_Mpt = Shader.PropertyToID("_MetresPerTexel");
        static readonly int s_Dt = Shader.PropertyToID("_Dt");
        static readonly int s_WaveSpeed = Shader.PropertyToID("_WaveSpeed");
        static readonly int s_Damping = Shader.PropertyToID("_Damping");
        static readonly int s_Viscosity = Shader.PropertyToID("_Viscosity");
        static readonly int s_SplatCount = Shader.PropertyToID("_SplatCount");
        static readonly int s_RippleTex = Shader.PropertyToID("_RippleTex");
        static readonly int s_VelIn = Shader.PropertyToID("_VelIn");
        static readonly int s_VelOut = Shader.PropertyToID("_VelOut");
        static readonly int s_DyeIn = Shader.PropertyToID("_DyeIn");
        static readonly int s_DyeOut = Shader.PropertyToID("_DyeOut");
        static readonly int s_CurlIn = Shader.PropertyToID("_CurlIn");
        static readonly int s_CurlOut = Shader.PropertyToID("_CurlOut");
        static readonly int s_DivIn = Shader.PropertyToID("_DivIn");
        static readonly int s_DivOut = Shader.PropertyToID("_DivOut");
        static readonly int s_PIn = Shader.PropertyToID("_PIn");
        static readonly int s_POut = Shader.PropertyToID("_POut");

        public void Initialize(ProjectionScreen screen, AquariumResources res, WaterLayerSettings settings)
        {
            Screen = screen.Id;
            m_Settings = settings;

            // --- Wave simulation -------------------------------------------------------------------
            m_Wave = res.Ripple;
            m_WaveStep = m_Wave.FindKernel("Step");
            m_WaveClear = m_Wave.FindKernel("Clear");
            float tpm = Mathf.Max(8, settings.texelsPerMetre);
            m_WaveSize = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(screen.Width * tpm), 32, 1024), Mathf.Clamp(Mathf.RoundToInt(screen.Height * tpm), 32, 1024));
            m_WaveMetresPerTexel = new Vector2(screen.Width / m_WaveSize.x, screen.Height / m_WaveSize.y);
            m_WaveA = CreateRT($"Waves {screen.Id} A", m_WaveSize, RenderTextureFormat.RGFloat);
            m_WaveB = CreateRT($"Waves {screen.Id} B", m_WaveSize, RenderTextureFormat.RGFloat);
            m_WaveSplatBuffer = new ComputeBuffer(MaxSplats, Marshal.SizeOf<RippleSplat>());

            // --- Liquid simulation -----------------------------------------------------------------
            m_Fluid = res.ScreenFluid;
            m_FAdvect = m_Fluid.FindKernel("Advect");
            m_FSplat = m_Fluid.FindKernel("Splat");
            m_FCurl = m_Fluid.FindKernel("Curl");
            m_FVort = m_Fluid.FindKernel("Vorticity");
            m_FDiv = m_Fluid.FindKernel("Divergence");
            m_FJacobi = m_Fluid.FindKernel("Jacobi");
            m_FGrad = m_Fluid.FindKernel("Gradient");
            m_FClear = m_Fluid.FindKernel("Clear");
            float ftpm = Mathf.Max(8, settings.liquidTexelsPerMetre);
            m_FluidSize = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(screen.Width * ftpm), 16, 512), Mathf.Clamp(Mathf.RoundToInt(screen.Height * ftpm), 16, 512));
            m_FluidMetresPerTexel = new Vector2(screen.Width / m_FluidSize.x, screen.Height / m_FluidSize.y);
            m_VelA = CreateRT($"Liquid Vel {screen.Id} A", m_FluidSize, RenderTextureFormat.RGFloat);
            m_VelB = CreateRT($"Liquid Vel {screen.Id} B", m_FluidSize, RenderTextureFormat.RGFloat);
            m_DyeA = CreateRT($"Liquid Dye {screen.Id} A", m_FluidSize, RenderTextureFormat.RFloat);
            m_DyeB = CreateRT($"Liquid Dye {screen.Id} B", m_FluidSize, RenderTextureFormat.RFloat);
            m_PressA = CreateRT($"Liquid P {screen.Id} A", m_FluidSize, RenderTextureFormat.RFloat);
            m_PressB = CreateRT($"Liquid P {screen.Id} B", m_FluidSize, RenderTextureFormat.RFloat);
            m_Div = CreateRT($"Liquid Div {screen.Id}", m_FluidSize, RenderTextureFormat.RFloat);
            m_Curl = CreateRT($"Liquid Curl {screen.Id}", m_FluidSize, RenderTextureFormat.RFloat);
            m_FluidSplatBuffer = new ComputeBuffer(MaxSplats, Marshal.SizeOf<FluidSplat>());
            ClearAll();

            // --- Quad on the screen plane (2 cm behind the glass, just past the near plane) ---------
            Vector3 offset = -screen.Normal * 0.02f;
            var mesh = new Mesh { name = $"Water Layer {screen.Id}" };
            mesh.SetVertices(new List<Vector3>
            {
                screen.BottomLeft + offset,
                screen.BottomRight + offset,
                screen.TopLeft + offset,
                screen.TopLeft + (screen.BottomRight - screen.BottomLeft) + offset,
            });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Only this screen's camera renders this layer (see DisplayRig culling masks).
            gameObject.layer = screen.GlassLayer;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            m_Renderer = gameObject.AddComponent<MeshRenderer>();
            m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.lightProbeUsage = LightProbeUsage.Off;
            m_Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_Material = new Material(res.Glass) { name = $"Water Layer {screen.Id}" };
            m_Material.SetVector(s_Mpt, m_WaveMetresPerTexel);
            m_Material.SetVector("_FluidTexel", new Vector4(1f / m_FluidSize.x, 1f / m_FluidSize.y, 0f, 0f));
            BindTextures();
            ApplyMaterialSettings();
            m_Renderer.sharedMaterial = m_Material;
            m_Renderer.enabled = false;
        }

        static RenderTexture CreateRT(string name, Vector2Int size, RenderTextureFormat format)
        {
            var rt = new RenderTexture(size.x, size.y, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        void BindTextures()
        {
            m_Material.SetTexture(s_RippleTex, m_WaveA);
            m_Material.SetTexture("_FluidVel", m_VelA);
            m_Material.SetTexture("_FluidDye", m_DyeA);
        }

        void ApplyMaterialSettings()
        {
            m_Material.SetFloat("_Refraction", m_Settings.refraction);
            m_Material.SetFloat("_LiquidRefraction", m_Settings.liquidRefraction);
            m_Material.SetFloat("_MaxOpacity", m_Settings.maxOpacity);
            m_Material.SetColor("_WaterTint", m_Settings.waterTint);
            m_Material.SetColor("_HighlightColor", m_Settings.highlightColor);
            m_Material.SetFloat("_Foam", m_Settings.foam);
            m_Material.SetFloat("_Dispersion", m_Settings.dispersion);
        }

        void Wake()
        {
            m_LastActivity = Time.time;
            if (!m_Active)
            {
                m_Active = true;
                m_Renderer.enabled = true;
            }
        }

        /// <summary>
        /// Push the wave surface. uv is 0..1 across the screen, radius in metres, strength is a velocity
        /// impulse in m/s (negative presses the surface down, like a fingertip).
        /// </summary>
        public void AddSplat(Vector2 uv, float radiusMetres, float strength)
        {
            if (m_Wave == null || m_WavePending.Count >= MaxSplats) return;
            m_WavePending.Add(new RippleSplat { Uv = uv, Radius = radiusMetres, Strength = strength });
            Wake();
        }

        /// <summary>
        /// Stir the liquid. velocity is the finger velocity on the screen plane in metres per second
        /// (x = screen right, y = screen up); dye is how much disturbed water to add (0..1).
        /// </summary>
        public void AddLiquid(Vector2 uv, Vector2 velocity, float radiusMetres, float dye)
        {
            if (m_Fluid == null || m_FluidPending.Count >= MaxSplats) return;
            m_FluidPending.Add(new FluidSplat
            {
                Uv = uv,
                Force = new Vector2(velocity.x / m_FluidMetresPerTexel.x, velocity.y / m_FluidMetresPerTexel.y) * m_Settings.liquidForce,
                Radius = Mathf.Max(radiusMetres / Mathf.Min(m_FluidMetresPerTexel.x, m_FluidMetresPerTexel.y), 0.75f),
                Dye = dye,
            });
            Wake();
        }

        void Update()
        {
            // GPU resources do not survive a script hot-reload during Play mode.
            if (!m_Active || m_Wave == null || m_WaveSplatBuffer == null || m_WaveA == null || m_FluidSplatBuffer == null) return;

            if (Time.time - m_LastActivity > IdleTimeout)
            {
                // Everything has settled: stop simulating and hide the layer.
                m_Active = false;
                m_Renderer.enabled = false;
                ClearAll();
                return;
            }

            ApplyMaterialSettings(); // live-tweakable in the Inspector
            float frameDt = Mathf.Min(Time.deltaTime, 1f / 20f);
            StepWaves(frameDt);
            StepLiquid(frameDt);
            BindTextures();
        }

        void StepWaves(float frameDt)
        {
            // Substep to keep the solver stable (CFL: c * dt / dx <= 0.5).
            float dx = Mathf.Min(m_WaveMetresPerTexel.x, m_WaveMetresPerTexel.y);
            float maxDt = 0.5f * dx / Mathf.Max(m_Settings.waveSpeed, 0.01f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(frameDt / maxDt), 1, MaxSubsteps);
            float dt = Mathf.Min(frameDt / steps, maxDt);

            m_Wave.SetInts(s_SizeId, m_WaveSize.x, m_WaveSize.y);
            m_Wave.SetVector(s_Mpt, m_WaveMetresPerTexel);
            m_Wave.SetFloat(s_Dt, dt);
            m_Wave.SetFloat(s_WaveSpeed, m_Settings.waveSpeed);
            m_Wave.SetFloat(s_Damping, m_Settings.damping);
            m_Wave.SetFloat(s_Viscosity, m_Settings.viscosity);

            int splatCount = Mathf.Min(m_WavePending.Count, MaxSplats);
            if (splatCount > 0) m_WaveSplatBuffer.SetData(m_WavePending, 0, 0, splatCount);
            m_WavePending.Clear();

            for (int s = 0; s < steps; s++)
            {
                m_Wave.SetInt(s_SplatCount, s == 0 ? splatCount : 0);
                m_Wave.SetBuffer(m_WaveStep, s_Splats, m_WaveSplatBuffer);
                m_Wave.SetTexture(m_WaveStep, s_State, m_WaveA);
                m_Wave.SetTexture(m_WaveStep, s_Next, m_WaveB);
                Dispatch(m_Wave, m_WaveStep, m_WaveSize);
                (m_WaveA, m_WaveB) = (m_WaveB, m_WaveA);
            }
        }

        void StepLiquid(float dt)
        {
            var f = m_Fluid;
            f.SetInts(s_SizeId, m_FluidSize.x, m_FluidSize.y);
            f.SetFloat(s_Dt, dt);
            f.SetFloat("_VelDissipation", m_Settings.liquidVelocityDissipation);
            f.SetFloat("_DyeDissipation", m_Settings.liquidDyeDissipation);
            f.SetFloat("_Vorticity", m_Settings.liquidVorticity);

            // Advect velocity and dye (A -> B)
            f.SetTexture(m_FAdvect, s_VelIn, m_VelA);
            f.SetTexture(m_FAdvect, s_DyeIn, m_DyeA);
            f.SetTexture(m_FAdvect, s_VelOut, m_VelB);
            f.SetTexture(m_FAdvect, s_DyeOut, m_DyeB);
            Dispatch(f, m_FAdvect, m_FluidSize);

            // Splats (B -> A)
            int splatCount = Mathf.Min(m_FluidPending.Count, MaxSplats);
            if (splatCount > 0) m_FluidSplatBuffer.SetData(m_FluidPending, 0, 0, splatCount);
            m_FluidPending.Clear();
            f.SetInt(s_SplatCount, splatCount);
            f.SetBuffer(m_FSplat, s_Splats, m_FluidSplatBuffer);
            f.SetTexture(m_FSplat, s_VelIn, m_VelB);
            f.SetTexture(m_FSplat, s_DyeIn, m_DyeB);
            f.SetTexture(m_FSplat, s_VelOut, m_VelA);
            f.SetTexture(m_FSplat, s_DyeOut, m_DyeA);
            Dispatch(f, m_FSplat, m_FluidSize);

            // Vorticity confinement keeps the swirls alive (A -> B)
            f.SetTexture(m_FCurl, s_VelIn, m_VelA);
            f.SetTexture(m_FCurl, s_CurlOut, m_Curl);
            Dispatch(f, m_FCurl, m_FluidSize);
            f.SetTexture(m_FVort, s_VelIn, m_VelA);
            f.SetTexture(m_FVort, s_CurlIn, m_Curl);
            f.SetTexture(m_FVort, s_VelOut, m_VelB);
            Dispatch(f, m_FVort, m_FluidSize);

            // Pressure projection -> divergence-free flow (B -> A)
            f.SetTexture(m_FDiv, s_VelIn, m_VelB);
            f.SetTexture(m_FDiv, s_DivOut, m_Div);
            Dispatch(f, m_FDiv, m_FluidSize);
            f.SetTexture(m_FJacobi, s_DivIn, m_Div);
            for (int i = 0; i < m_Settings.liquidPressureIterations; i++)
            {
                f.SetTexture(m_FJacobi, s_PIn, m_PressA);
                f.SetTexture(m_FJacobi, s_POut, m_PressB);
                Dispatch(f, m_FJacobi, m_FluidSize);
                (m_PressA, m_PressB) = (m_PressB, m_PressA);
            }
            f.SetTexture(m_FGrad, s_VelIn, m_VelB);
            f.SetTexture(m_FGrad, s_PIn, m_PressA);
            f.SetTexture(m_FGrad, s_VelOut, m_VelA);
            Dispatch(f, m_FGrad, m_FluidSize);
        }

        static void Dispatch(ComputeShader cs, int kernel, Vector2Int size)
        {
            cs.Dispatch(kernel, Mathf.CeilToInt(size.x / 8f), Mathf.CeilToInt(size.y / 8f), 1);
        }

        void ClearAll()
        {
            if (m_Wave != null)
            {
                m_Wave.SetInts(s_SizeId, m_WaveSize.x, m_WaveSize.y);
                foreach (var rt in new[] { m_WaveA, m_WaveB })
                {
                    if (rt == null) continue;
                    m_Wave.SetTexture(m_WaveClear, s_Next, rt);
                    Dispatch(m_Wave, m_WaveClear, m_WaveSize);
                }
            }
            if (m_Fluid != null && m_VelA != null)
            {
                m_Fluid.SetInts(s_SizeId, m_FluidSize.x, m_FluidSize.y);
                foreach (var (vel, dye, p) in new[] { (m_VelA, m_DyeA, m_PressA), (m_VelB, m_DyeB, m_PressB) })
                {
                    m_Fluid.SetTexture(m_FClear, s_VelOut, vel);
                    m_Fluid.SetTexture(m_FClear, s_DyeOut, dye);
                    m_Fluid.SetTexture(m_FClear, s_POut, p);
                    Dispatch(m_Fluid, m_FClear, m_FluidSize);
                }
            }
            m_WavePending.Clear();
            m_FluidPending.Clear();
        }

        void OnDestroy()
        {
            foreach (var rt in new[] { m_WaveA, m_WaveB, m_VelA, m_VelB, m_DyeA, m_DyeB, m_PressA, m_PressB, m_Div, m_Curl })
                if (rt != null) rt.Release();
            m_WaveSplatBuffer?.Release();
            m_FluidSplatBuffer?.Release();
            if (m_Material != null) Destroy(m_Material);
        }
    }
}
