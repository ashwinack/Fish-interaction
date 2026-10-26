using System;
using UnityEngine;

namespace Aquarium
{
    public enum DisplayLayoutMode
    {
        /// <summary>Multi-display in a player with 3+ displays, otherwise the single-window preview.</summary>
        Auto,
        /// <summary>Each screen camera renders to its own physical display.</summary>
        MultiDisplay,
        /// <summary>All three views side by side in one window (for development).</summary>
        SingleWindowPreview,
    }

    public enum ScreenId
    {
        LeftWall = 0,
        Floor = 1,
        RightWall = 2,
    }

    /// <summary>
    /// Physical geometry of the installation, in metres. The tunnel runs along world +Z with the floor
    /// screen at y = 0 and the viewer's eye above it. All three cameras are driven from this geometry
    /// using off-axis projection, which is what makes the views meet seamlessly at the corners.
    /// </summary>
    [Serializable]
    public class TunnelSettings
    {
        [Tooltip("Distance between the left and right wall screens. Also the width of the floor screen.")]
        public float width = 4f;
        [Tooltip("Height of the visible area of each wall screen, measured from the floor.")]
        public float wallHeight = 3f;
        [Tooltip("Length of the tunnel, i.e. the horizontal size of the wall screens and the depth of the floor screen.")]
        public float length = 6f;
        [Tooltip("Viewer eye position relative to the centre of the floor. Move it at runtime for head tracking.")]
        public Vector3 eyePosition = new Vector3(0f, 1.6f, 0f);
        public float farClip = 160f;
    }

    [Serializable]
    public class DisplaySettings
    {
        [Tooltip("MultiDisplay: each camera renders to its own physical display. In the Editor, open one Game view per display " +
                 "(Aquarium > Open Game Views For Displays 1-4). F2 switches to the single-window preview at runtime.")]
        public DisplayLayoutMode layout = DisplayLayoutMode.MultiDisplay;

        [Header("Operator control panel")]
        [Tooltip("Show the operator control panel on the main monitor (Display 1).")]
        public bool controlPanel = true;
        [Tooltip("Live previews of the three screens in the control panel (costs a little GPU time).")]
        public bool controlPanelPreviews = true;

        [Header("Scene cameras (created by Aquarium > Setup Three Display Cameras)")]
        [Tooltip("When assigned, this camera's own Target Display is used.")]
        public Camera leftWallCamera;
        public Camera rightWallCamera;
        public Camera floorCamera;

        [Header("Fallback display indices (only used for cameras that are not assigned above)")]
        [Tooltip("0-based display index (Unity 'Display 1' = 0). Display 1 is the operator monitor.")]
        public int leftWallDisplay = 1;
        public int rightWallDisplay = 2;
        public int floorDisplay = 3;
        [Tooltip("Call Display.Activate on the secondary displays at startup (player builds only).")]
        public bool activateDisplays = true;
        [Tooltip("First of three consecutive unused layers reserved for each screen's ripple glass.")]
        [Range(8, 29)] public int glassLayerStart = 29;
        public SmaaQuality smaaQuality = SmaaQuality.High;
    }

    public enum SmaaQuality { Off, Medium, High }

    public enum PropPlacement { BehindWalls, UnderFloor, Anywhere }

    /// <summary>An imported environment model (rock, coral, wreck...) scattered on the seabed.</summary>
    [Serializable]
    public class EnvironmentProp
    {
        public GameObject prefab;
        [Min(0)] public int count = 4;
        public PropPlacement placement = PropPlacement.BehindWalls;
        public float minScale = 1f;
        public float maxScale = 2f;
        [Tooltip("Fish steer around it.")]
        public bool obstacle = true;
    }

    [Serializable]
    public class TankSettings
    {
        [Tooltip("Half of the tank width across the tunnel (X).")]
        public float halfWidth = 14f;
        [Tooltip("Half of the tank length along the tunnel (Z). Fish wrap around at the ends, far outside the view.")]
        public float halfLength = 30f;
        [Tooltip("Average seabed height (the floor screen is at y = 0).")]
        public float seabedDepth = -6f;
        public float surfaceHeight = 7f;
        [Tooltip("Minimum distance fish keep from the tunnel glass.")]
        public float glassMargin = 0.5f;
    }

    [Serializable]
    public class WaterSettings
    {
        public Color fogColor = new Color(0.015f, 0.16f, 0.24f);
        [Range(0f, 0.2f)] public float fogDensity = 0.042f;
        public Color sunColor = new Color(0.78f, 0.93f, 1f);
        public float sunIntensity = 1.9f;
        public Vector2 sunAngles = new Vector2(70f, -25f);
        public float causticScale = 0.16f;
        public float causticSpeed = 0.55f;
        public float causticIntensity = 1.25f;

        [Header("Sunlight through the surface")]
        [Tooltip("How strongly the water glows when looking toward the sun (forward scattering).")]
        [Range(0f, 3f)] public float sunScatter = 1f;
        [Tooltip("How much brighter the water gets toward the surface.")]
        [Range(0f, 3f)] public float surfaceGlow = 0.9f;
        [Range(0f, 2f)] public float godRayIntensity = 0.4f;
        [Range(0, 60)] public int godRayCount = 30;
        [Tooltip("Light absorption coefficient per metre of water depth.")]
        public float absorption = 0.03f;
        [Tooltip("Base current (m/s) in world XZ used by the fluid solver.")]
        public Vector2 ambientCurrent = new Vector2(0.02f, 0.12f);
        public Color ambientTop = new Color(0.1f, 0.32f, 0.42f);
        public Color ambientEquator = new Color(0.04f, 0.17f, 0.25f);
        public Color ambientBottom = new Color(0.02f, 0.06f, 0.09f);
        public bool postProcessing = true;
        public bool floodLights = true;
    }

    /// <summary>The touch-reactive water layer drawn on every screen.</summary>
    [Serializable]
    public class WaterLayerSettings
    {
        [Tooltip("Speed at which ripples travel across the glass (m/s).")]
        [Range(0.1f, 2f)] public float waveSpeed = 0.55f;
        [Tooltip("How quickly ripples die out (per second).")]
        [Range(0f, 5f)] public float damping = 0.9f;
        [Tooltip("Smooths very small waves (surface-tension-like).")]
        [Range(0f, 0.01f)] public float viscosity = 0.0015f;
        [Tooltip("Wave simulation resolution in texels per metre of screen.")]
        [Range(24, 160)] public int texelsPerMetre = 96;

        [Header("Liquid (swirling fluid that follows the finger)")]
        [Tooltip("Liquid simulation resolution in texels per metre of screen.")]
        [Range(16, 96)] public int liquidTexelsPerMetre = 40;
        [Tooltip("How strongly a moving finger drags the liquid along.")]
        [Range(0f, 3f)] public float liquidForce = 1.2f;
        [Tooltip("How long the liquid keeps moving (lower = flows longer).")]
        [Range(0f, 5f)] public float liquidVelocityDissipation = 0.8f;
        [Tooltip("How quickly the disturbed liquid fades (lower = trails last longer).")]
        [Range(0f, 5f)] public float liquidDyeDissipation = 0.9f;
        [Tooltip("Keeps small swirls spinning.")]
        [Range(0f, 40f)] public float liquidVorticity = 14f;
        [Range(4, 60)] public int liquidPressureIterations = 24;
        [Tooltip("How much the liquid layer bends the scene behind it.")]
        [Range(0f, 0.1f)] public float liquidRefraction = 0.025f;

        [Header("Look")]
        [Tooltip("How far the scene behind is bent by the waves.")]
        [Range(0f, 0.1f)] public float refraction = 0.03f;
        [Tooltip("Maximum opacity of the water layer. Below 1 the aquarium always shows through.")]
        [Range(0.1f, 1f)] public float maxOpacity = 0.6f;
        public Color waterTint = new Color(0.55f, 0.85f, 1f, 1f);
        public Color highlightColor = new Color(0.85f, 0.97f, 1f, 1f);
        [Range(0f, 1f)] public float foam = 0.35f;
        [Range(0f, 1f)] public float dispersion = 0.35f;
    }

    [Serializable]
    public class InteractionSettings
    {
        [Tooltip("Radius (m) around the touch ray inside which fish react.")]
        public float fleeRadius = 2.6f;
        [Tooltip("How far into the water (m) a touch scares fish.")]
        public float reach = 14f;
        [Tooltip("How long (s) the scare lingers after a finger is lifted.")]
        public float lingerTime = 0.7f;
        [Tooltip("How fast the startle spreads through the water from the touch point (m/s). Farther fish react later.")]
        public float reactionWaveSpeed = 14f;
        [Tooltip("Random extra reaction time per fish (s), so a school scatters organically.")]
        public float reactionJitter = 0.08f;
        [Tooltip("Duration (s) of a full-strength escape burst before the fish glides back to cruising.")]
        public float escapeDuration = 1.4f;
        public float rippleStrength = 1f;
        [Tooltip("Outward push injected into the fluid solver on a new touch (m/s^2).")]
        public float fluidPush = 55f;
        [Tooltip("Force multiplier for dragging a finger through the water.")]
        public float fluidDrag = 6f;
        public int bubblesPerTap = 28;
        [Tooltip("Ignore the mouse while any finger is on a touchscreen (Windows also promotes touches to mouse events).")]
        public bool ignoreMouseWhileTouching = true;

        [Header("Curious fish and feeding")]
        [Tooltip("A finger (or cursor) held still this long (s) stops scaring and starts attracting curious fish.")]
        public float attractDelay = 0.5f;
        [Tooltip("Fingers moving slower than this (m/s) keep attracting: fish follow the finger along the glass.")]
        public float followSpeed = 0.35f;
        [Tooltip("Fingers moving faster than this (m/s) scare fish again.")]
        public float scareSpeed = 0.9f;
        [Tooltip("Radius (m) from which curious fish come to a finger.")]
        public float attractRadius = 7f;
        [Tooltip("Holding a finger still this long (s) sprinkles food flakes that fish come to eat.")]
        public float feedDelay = 1.2f;
        [Tooltip("Food flakes dropped per second while feeding.")]
        public float foodRate = 4f;

        [Header("Mouse hover")]
        [Tooltip("The mouse interacts just by moving over a screen (no click needed). Clicking gives the full effect.")]
        public bool mouseHover = true;
        [Tooltip("Strength of hover interaction relative to a click / touch.")]
        [Range(0f, 1f)] public float hoverStrength = 0.6f;
        [Tooltip("A still cursor keeps curious fish gathered for this long (s), then they lose interest.")]
        public float hoverIdleTime = 6f;

        [Header("Plants")]
        [Tooltip("How far kelp, seagrass and anemones bend away from a touch.")]
        [Range(0f, 3f)] public float plantPush = 1f;
        [Tooltip("Radius (m) around the touch ray in which plants bend.")]
        public float plantPushRadius = 1.8f;
    }
}
