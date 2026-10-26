using UnityEngine;

namespace Aquarium
{
    /// <summary>
    /// Shaders and compute shaders loaded from Assets/Aquarium/Resources/AquariumShaders so they are
    /// always included in player builds.
    /// </summary>
    public sealed class AquariumResources
    {
        public Shader Fish;
        public Shader Environment;
        public Shader Kelp;
        public Shader WaterSurface;
        public Shader LightShaft;
        public Shader Particle;
        public Shader Glass;
        public Shader Backdrop;
        public ComputeShader FluidSim;
        public ComputeShader Ripple;
        public ComputeShader ScreenFluid;

        public bool ComputeSupported => SystemInfo.supportsComputeShaders && FluidSim != null && Ripple != null && ScreenFluid != null;

        public static AquariumResources Load()
        {
            var r = new AquariumResources
            {
                Fish = LoadShader("Fish"),
                Environment = LoadShader("Environment"),
                Kelp = LoadShader("Kelp"),
                WaterSurface = LoadShader("WaterSurface"),
                LightShaft = LoadShader("LightShaft"),
                Particle = LoadShader("Particle"),
                Glass = LoadShader("Glass"),
                Backdrop = LoadShader("Backdrop"),
                FluidSim = Resources.Load<ComputeShader>("AquariumShaders/FluidSim"),
                Ripple = Resources.Load<ComputeShader>("AquariumShaders/Ripple"),
                ScreenFluid = Resources.Load<ComputeShader>("AquariumShaders/ScreenFluid"),
            };
            if (r.FluidSim == null || r.Ripple == null || r.ScreenFluid == null)
                Debug.LogError("[Aquarium] Compute shaders missing from Resources/AquariumShaders.");
            if (!SystemInfo.supportsComputeShaders)
                Debug.LogWarning("[Aquarium] Compute shaders are not supported; fluid currents and touch ripples are disabled.");
            return r;
        }

        static Shader LoadShader(string name)
        {
            var shader = Resources.Load<Shader>("AquariumShaders/" + name);
            if (shader == null)
            {
                Debug.LogError($"[Aquarium] Shader '{name}' not found in Resources/AquariumShaders.");
                return Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (!shader.isSupported)
                Debug.LogWarning($"[Aquarium] Shader '{name}' is not supported on this GPU.");
            return shader;
        }
    }
}
