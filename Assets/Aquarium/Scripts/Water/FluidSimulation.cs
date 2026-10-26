using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>
    /// GPU stable-fluids solver for the horizontal water currents of the whole tank. Touches push water
    /// outward, drags stir it, big animals leave wakes. The velocity field is
    ///  - bound globally as _AquaFlowTex so kelp, anemones and seagrass bend with the current,
    ///  - read back asynchronously to the CPU so fish and particles drift with it.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class FluidSimulation : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Splat
        {
            public Vector2 Position; // grid cells
            public Vector2 Force;
            public float Radius;     // cells
            public float Radial;
            public Vector2 Pad;
        }

        const int MaxSplats = 64;
        const int JacobiIterations = 24;

        [Tooltip("Cells across the tank width. The length resolution follows from the tank aspect ratio.")]
        public int resolutionX = 64;
        public float dissipation = 0.35f;
        public float vorticity = 2.5f;
        public float ambientRelax = 0.25f;

        AquariumDirector m_Director;
        ComputeShader m_Cs;
        int m_Advect, m_Curl, m_Forces, m_Divergence, m_Jacobi, m_Subtract, m_Clear;
        RenderTexture m_VelA, m_VelB, m_PressA, m_PressB, m_Div, m_CurlTex;
        ComputeBuffer m_SplatBuffer;
        readonly List<Splat> m_Pending = new List<Splat>();
        Vector2Int m_Grid;
        float m_CellSize;
        Vector2 m_Origin; // world XZ of the grid corner
        Vector2[] m_Cpu;
        bool m_CpuValid;
        bool m_ReadbackPending;
        bool m_Enabled;

        static readonly int s_VelIn = Shader.PropertyToID("_VelocityIn");
        static readonly int s_VelOut = Shader.PropertyToID("_VelocityOut");
        static readonly int s_CurlIn = Shader.PropertyToID("_CurlIn");
        static readonly int s_CurlOut = Shader.PropertyToID("_CurlOut");
        static readonly int s_DivIn = Shader.PropertyToID("_DivergenceIn");
        static readonly int s_DivOut = Shader.PropertyToID("_DivergenceOut");
        static readonly int s_PressIn = Shader.PropertyToID("_PressureIn");
        static readonly int s_PressOut = Shader.PropertyToID("_PressureOut");
        static readonly int s_Splats = Shader.PropertyToID("_Splats");

        public Vector2 AmbientCurrent => m_Director != null ? m_Director.water.ambientCurrent : Vector2.zero;

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            var tank = director.tank;
            m_CellSize = tank.halfWidth * 2f / resolutionX;
            m_Grid = new Vector2Int(resolutionX, Mathf.CeilToInt(tank.halfLength * 2f / m_CellSize));
            m_Origin = new Vector2(-tank.halfWidth, -tank.halfLength);

            Shader.SetGlobalVector("_AquaFlowBounds", new Vector4(m_Origin.x, m_Origin.y, 1f / (m_Grid.x * m_CellSize), 1f / (m_Grid.y * m_CellSize)));
            Shader.SetGlobalTexture("_AquaFlowTex", Texture2D.blackTexture);
            Shader.SetGlobalVector("_AquaFlowParams", Vector4.zero);

            if (!director.Resources.ComputeSupported) return;
            m_Cs = director.Resources.FluidSim;
            m_Advect = m_Cs.FindKernel("Advect");
            m_Curl = m_Cs.FindKernel("Curl");
            m_Forces = m_Cs.FindKernel("Forces");
            m_Divergence = m_Cs.FindKernel("Divergence");
            m_Jacobi = m_Cs.FindKernel("Jacobi");
            m_Subtract = m_Cs.FindKernel("SubtractGradient");
            m_Clear = m_Cs.FindKernel("Clear");

            m_VelA = CreateRT("Flow Velocity A", RenderTextureFormat.RGFloat);
            m_VelB = CreateRT("Flow Velocity B", RenderTextureFormat.RGFloat);
            m_PressA = CreateRT("Flow Pressure A", RenderTextureFormat.RFloat);
            m_PressB = CreateRT("Flow Pressure B", RenderTextureFormat.RFloat);
            m_Div = CreateRT("Flow Divergence", RenderTextureFormat.RFloat);
            m_CurlTex = CreateRT("Flow Curl", RenderTextureFormat.RFloat);
            m_SplatBuffer = new ComputeBuffer(MaxSplats, Marshal.SizeOf<Splat>());
            m_Cpu = new Vector2[m_Grid.x * m_Grid.y];

            m_Cs.SetInts("_GridSize", m_Grid.x, m_Grid.y);
            foreach (var (vel, press) in new[] { (m_VelA, m_PressA), (m_VelB, m_PressB) })
            {
                m_Cs.SetTexture(m_Clear, s_VelOut, vel);
                m_Cs.SetTexture(m_Clear, s_PressOut, press);
                Dispatch(m_Clear);
            }
            m_Enabled = true;
        }

        RenderTexture CreateRT(string name, RenderTextureFormat format)
        {
            var rt = new RenderTexture(m_Grid.x, m_Grid.y, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// Add force to the water around a world position. force: directional acceleration (m/s^2) in
        /// world XZ; radial: outward push (m/s^2); radius in metres.
        /// </summary>
        public void AddSplat(Vector3 worldPosition, Vector2 force, float radiusMetres, float radial)
        {
            if (!m_Enabled || m_Pending == null || m_Pending.Count >= MaxSplats) return;
            m_Pending.Add(new Splat
            {
                Position = new Vector2((worldPosition.x - m_Origin.x) / m_CellSize, (worldPosition.z - m_Origin.y) / m_CellSize),
                Force = force,
                Radius = Mathf.Max(radiusMetres / m_CellSize, 0.5f),
                Radial = radial,
            });
        }

        /// <summary>Current velocity (m/s, world XZ) at a world position, from the latest GPU readback.</summary>
        public Vector2 SampleVelocity(Vector3 worldPosition)
        {
            if (!m_CpuValid || m_Cpu == null) return AmbientCurrent;
            float gx = (worldPosition.x - m_Origin.x) / m_CellSize - 0.5f;
            float gz = (worldPosition.z - m_Origin.y) / m_CellSize - 0.5f;
            int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
            float fx = gx - x0, fz = gz - z0;
            Vector2 a = Cell(x0, z0), b = Cell(x0 + 1, z0), c = Cell(x0, z0 + 1), d = Cell(x0 + 1, z0 + 1);
            return Vector2.Lerp(Vector2.Lerp(a, b, fx), Vector2.Lerp(c, d, fx), fz);
        }

        Vector2 Cell(int x, int z)
        {
            x = Mathf.Clamp(x, 0, m_Grid.x - 1);
            z = Mathf.Clamp(z, 0, m_Grid.y - 1);
            return m_Cpu[z * m_Grid.x + x];
        }

        void Update()
        {
            // GPU resources do not survive a script hot-reload during Play mode.
            if (!m_Enabled || m_SplatBuffer == null || m_VelA == null || !m_VelA.IsCreated()) return;
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 1f / 30f);

            m_Cs.SetInts("_GridSize", m_Grid.x, m_Grid.y);
            m_Cs.SetFloat("_CellSize", m_CellSize);
            m_Cs.SetFloat("_DeltaTime", dt);
            m_Cs.SetFloat("_Dissipation", dissipation);
            m_Cs.SetFloat("_Vorticity", vorticity);
            m_Cs.SetVector("_AmbientCurrent", AmbientCurrent);
            m_Cs.SetFloat("_AmbientRelax", ambientRelax);
            m_Cs.SetFloat("_SimTime", Time.time);

            int splatCount = Mathf.Min(m_Pending.Count, MaxSplats);
            if (splatCount > 0) m_SplatBuffer.SetData(m_Pending, 0, 0, splatCount);
            m_Pending.Clear();
            m_Cs.SetInt("_SplatCount", splatCount);

            // 1. Advect A -> B
            m_Cs.SetTexture(m_Advect, s_VelIn, m_VelA);
            m_Cs.SetTexture(m_Advect, s_VelOut, m_VelB);
            Dispatch(m_Advect);

            // 2. Curl of B
            m_Cs.SetTexture(m_Curl, s_VelIn, m_VelB);
            m_Cs.SetTexture(m_Curl, s_CurlOut, m_CurlTex);
            Dispatch(m_Curl);

            // 3. Forces B -> A (vorticity confinement, ambient current, splats)
            m_Cs.SetTexture(m_Forces, s_VelIn, m_VelB);
            m_Cs.SetTexture(m_Forces, s_CurlIn, m_CurlTex);
            m_Cs.SetTexture(m_Forces, s_VelOut, m_VelA);
            m_Cs.SetBuffer(m_Forces, s_Splats, m_SplatBuffer);
            Dispatch(m_Forces);

            // 4. Divergence of A
            m_Cs.SetTexture(m_Divergence, s_VelIn, m_VelA);
            m_Cs.SetTexture(m_Divergence, s_DivOut, m_Div);
            Dispatch(m_Divergence);

            // 5. Pressure solve (warm-started from last frame)
            m_Cs.SetTexture(m_Jacobi, s_DivIn, m_Div);
            for (int i = 0; i < JacobiIterations; i++)
            {
                m_Cs.SetTexture(m_Jacobi, s_PressIn, m_PressA);
                m_Cs.SetTexture(m_Jacobi, s_PressOut, m_PressB);
                Dispatch(m_Jacobi);
                (m_PressA, m_PressB) = (m_PressB, m_PressA);
            }

            // 6. Project A -> B, then swap so A holds the divergence-free result
            m_Cs.SetTexture(m_Subtract, s_VelIn, m_VelA);
            m_Cs.SetTexture(m_Subtract, s_PressIn, m_PressA);
            m_Cs.SetTexture(m_Subtract, s_VelOut, m_VelB);
            Dispatch(m_Subtract);
            (m_VelA, m_VelB) = (m_VelB, m_VelA);

            Shader.SetGlobalTexture("_AquaFlowTex", m_VelA);
            Shader.SetGlobalVector("_AquaFlowParams", new Vector4(1f, 0f, 0f, 0f));

            if (!m_ReadbackPending && SystemInfo.supportsAsyncGPUReadback)
            {
                m_ReadbackPending = true;
                AsyncGPUReadback.Request(m_VelA, 0, TextureFormat.RGFloat, OnReadback);
            }
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            m_ReadbackPending = false;
            if (request.hasError || m_Cpu == null) return;
            NativeArray<Vector2> data = request.GetData<Vector2>();
            if (data.Length != m_Cpu.Length) return;
            data.CopyTo(m_Cpu);
            m_CpuValid = true;
        }

        void Dispatch(int kernel)
        {
            m_Cs.Dispatch(kernel, Mathf.CeilToInt(m_Grid.x / 8f), Mathf.CeilToInt(m_Grid.y / 8f), 1);
        }

        void OnDestroy()
        {
            m_Enabled = false;
            foreach (var rt in new[] { m_VelA, m_VelB, m_PressA, m_PressB, m_Div, m_CurlTex })
                if (rt != null) rt.Release();
            m_SplatBuffer?.Release();
        }
    }
}
