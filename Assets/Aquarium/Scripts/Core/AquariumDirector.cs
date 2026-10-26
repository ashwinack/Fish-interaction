using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aquarium
{
    /// <summary>
    /// Entry point of the three-screen aquarium. Add this to an empty GameObject at the world origin and
    /// press Play: it builds the cameras, lighting, environment, fluid simulation, fish and effects.
    /// Use the menu "Aquarium > Create Aquarium Scene" to set up a ready-made scene.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class AquariumDirector : MonoBehaviour
    {
        [Tooltip("Random seed for the environment layout and the fish spawn positions.")]
        public int seed = 20260925;

        public TunnelSettings tunnel = new TunnelSettings();
        public DisplaySettings display = new DisplaySettings();
        public TankSettings tank = new TankSettings();
        public WaterSettings water = new WaterSettings();
        public InteractionSettings interaction = new InteractionSettings();
        public WaterLayerSettings waterLayer = new WaterLayerSettings();
        [Header("Fish")]
        [Tooltip("Scales every species (size, spacing and speed). Bigger fish read much better on large screens.")]
        [Range(0.5f, 4f)] public float fishSizeMultiplier = 2f;
        public List<FishSpecies> species = FishSpecies.CreateDefaultSet();

        [Header("Environment")]
        [Tooltip("Imported environment models scattered on the seabed (e.g. rocks from an Asset Store pack).")]
        public List<EnvironmentProp> environmentProps = new List<EnvironmentProp>();

        [Header("Debug")]
        [Tooltip("F1 toggles the overlay, F2 toggles multi-display / single-window preview.")]
        public bool showOverlay = false;

        public AquariumResources Resources { get; private set; }
        public UnderwaterLighting Lighting { get; private set; }
        public AquariumEnvironment Environment { get; private set; }
        public FluidSimulation Fluid { get; private set; }
        public FishSchool Fish { get; private set; }
        public AquariumEffects Effects { get; private set; }
        public FoodSystem Food { get; private set; }
        public DisplayRig Rig { get; private set; }
        public AquariumInput Input { get; private set; }
        public ControlPanel Panel { get; private set; }
        public OperatorSettings Operator { get; private set; }

        float m_FpsSmoothed = 60f;

        void Awake()
        {
            Resources = AquariumResources.Load();

            // Operator settings (saved from the control panel) override the scene values before anything is built.
            if (display.controlPanel)
            {
                Operator = OperatorSettings.LoadOrCapture(this);
                Operator.ApplyBeforeBuild(this);
            }

            Seabed.Configure(tank, seed);
            UnityEngine.Random.InitState(seed);

            // Order matters: lighting before the environment (pulse lights), environment before fish
            // (obstacles), rig before the ripple glass.
            Lighting = CreateSystem<UnderwaterLighting>("Lighting");
            Lighting.Build(this);

            Environment = CreateSystem<AquariumEnvironment>("Environment");
            Environment.Build(this);

            Fluid = CreateSystem<FluidSimulation>("Fluid Simulation");
            Fluid.Build(this);

            Fish = CreateSystem<FishSchool>("Fish");
            Fish.Build(this);

            Effects = CreateSystem<AquariumEffects>("Effects");
            Effects.Build(this);

            Food = CreateSystem<FoodSystem>("Food");
            Food.Build(this);

            Rig = CreateSystem<DisplayRig>("Display Rig");
            Rig.Build(this);

            if (Resources.ComputeSupported)
            {
                foreach (var screen in Rig.Screens)
                {
                    var go = new GameObject($"Ripple Glass ({screen.Id})");
                    go.transform.SetParent(Rig.transform, false);
                    var ripple = go.AddComponent<RippleSurface>();
                    ripple.Initialize(screen, Resources, waterLayer);
                    screen.Ripple = ripple;
                }
            }

            Input = CreateSystem<AquariumInput>("Input");
            Input.Build(this);

            if (display.controlPanel && Operator != null)
            {
                Operator.ApplyLive(this);
                Panel = CreateSystem<ControlPanel>("Control Panel (Display 1)");
                Panel.Build(this, Operator);
            }

            Debug.Log($"[Aquarium] Ready: {Fish.FishCount} fish, layout = {(Rig.IsPreview ? "single-window preview" : "multi-display")}, displays connected = {Display.displays.Length}.");
        }

        T CreateSystem<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        void Update()
        {
            m_FpsSmoothed = Mathf.Lerp(m_FpsSmoothed, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame && Panel == null) showOverlay = !showOverlay;
            if (kb.f2Key.wasPressedThisFrame && Rig != null) Rig.ApplyLayout(!Rig.IsPreview);
        }

        void OnGUI()
        {
            if (!showOverlay || Rig == null || Panel != null) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            style.normal.textColor = Color.white;
            GUILayout.BeginArea(new Rect(12, 12, 520, 200), GUI.skin.box);
            GUILayout.Label($"Aquarium  {m_FpsSmoothed:0} fps", style);
            GUILayout.Label($"Layout: {(Rig.IsPreview ? "single-window preview" : "multi-display")}   (F2 to switch)", style);
            GUILayout.Label($"Displays connected: {Display.displays.Length}   Left=Display {Rig.LeftWall.Camera.targetDisplay + 1}  " +
                            $"Right=Display {Rig.RightWall.Camera.targetDisplay + 1}  Floor=Display {Rig.Floor.Camera.targetDisplay + 1}", style);
            GUILayout.Label($"Fish visible  L:{Fish.VisibleCount(ScreenId.LeftWall)}  F:{Fish.VisibleCount(ScreenId.Floor)}  R:{Fish.VisibleCount(ScreenId.RightWall)}   " +
                            $"Active touches: {Input.ActivePointerCount}   Last touch: {Input.LastTouchedScreen}", style);
            GUILayout.Label($"Compute (fluid + ripples): {(Resources.ComputeSupported ? "on" : "off")}", style);
            GUILayout.EndArea();
        }

        void OnValidate()
        {
            tunnel.width = Mathf.Max(0.5f, tunnel.width);
            tunnel.wallHeight = Mathf.Max(0.5f, tunnel.wallHeight);
            tunnel.length = Mathf.Max(0.5f, tunnel.length);
            tunnel.eyePosition.y = Mathf.Max(0.2f, tunnel.eyePosition.y);
            tank.halfWidth = Mathf.Max(tunnel.width * 0.5f + 4f, tank.halfWidth);
        }
    }
}
