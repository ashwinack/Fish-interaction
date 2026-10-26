using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Aquarium
{
    /// <summary>A touch "threat" as seen by the fish: a ray from the glass point into the water.</summary>
    public struct Disturbance
    {
        public Vector3 Origin;    // point on the glass
        public Vector3 Direction; // from the eye through the glass point, into the water
        public float Radius;
        public float Reach;
        public float Strength;    // 0..~1.5
        public ScreenId Screen;
    }

    /// <summary>A calm finger or cursor that curious fish swim toward (and follow).</summary>
    public struct Attractor
    {
        public Vector3 Position;  // in the water just behind the glass
        public ScreenId Screen;
        public float Strength;    // 0..1
        public bool Feeding;
    }

    /// <summary>
    /// Collects input from every touchscreen, pen and the mouse (click or hover) on every display, maps
    /// each pointer to the screen it is on and turns it into fish disturbances, water-layer waves and
    /// liquid, tank currents, bubbles, sand clouds and a smooth push field for the plants.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class AquariumInput : MonoBehaviour
    {
        const int MouseId = -1000;
        const int MaxPushes = 8;

        sealed class PointerState
        {
            public ProjectionScreen Screen;
            public Vector2 Uv;
            public Vector3 GlassPoint;
            public Vector3 Direction;
            public float LastBubbleTime;
            public float LastPulseTime;
            public Vector3 Velocity;
            public float DownTime;
            public float CalmTime;
            public float LastFoodTime;
            public bool Seen;
        }

        struct Echo
        {
            public Disturbance Disturbance;
            public float Born;
        }

        /// <summary>Smoothed plant push per pointer: eases in on contact and out after release.</summary>
        sealed class Push
        {
            public Vector3 Origin, Direction;
            public float Target, Value;
        }

        AquariumDirector m_Director;
        readonly Dictionary<int, PointerState> m_Pointers = new Dictionary<int, PointerState>();
        readonly List<int> m_Released = new List<int>();
        readonly List<Echo> m_Echoes = new List<Echo>();
        readonly Dictionary<int, Push> m_Pushes = new Dictionary<int, Push>();
        readonly List<int> m_DeadPushes = new List<int>();
        readonly Vector4[] m_PushOrigins = new Vector4[MaxPushes];
        readonly Vector4[] m_PushDirs = new Vector4[MaxPushes];
        // External touches: held for a short grace period so sources updating slower than the frame rate do not flicker.
        const float InjectedHoldTime = 0.15f;
        readonly Dictionary<int, (ScreenId screen, Vector2 uv, float time)> m_Injected = new Dictionary<int, (ScreenId, Vector2, float)>();
        readonly List<int> m_ExpiredInjected = new List<int>();

        Vector2 m_LastMousePos;
        float m_LastMouseMoveTime = -999f;
        float m_CurrentStrength = 1f;

        static readonly int s_PushOriginId = Shader.PropertyToID("_AquaPushOrigin");
        static readonly int s_PushDirId = Shader.PropertyToID("_AquaPushDir");
        static readonly int s_PushCountId = Shader.PropertyToID("_AquaPushCount");

        /// <summary>Active disturbances for this frame (held pointers plus fading echoes of released ones).</summary>
        public readonly List<Disturbance> Disturbances = new List<Disturbance>();

        /// <summary>Calm fingers / cursors this frame that curious fish swim toward.</summary>
        public readonly List<Attractor> Attractors = new List<Attractor>();

        public int ActivePointerCount => m_Pointers.Count;

        /// <summary>The screen that received the most recent touch or click.</summary>
        public ScreenId? LastTouchedScreen { get; private set; }

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            Shader.SetGlobalVectorArray(s_PushOriginId, m_PushOrigins);
            Shader.SetGlobalVectorArray(s_PushDirId, m_PushDirs);
            Shader.SetGlobalInt(s_PushCountId, 0);
        }

        /// <summary>
        /// Feed a touch from an external source (depth camera, lidar, TouchDesigner/OSC bridge, tests).
        /// uv is 0..1 across the screen as the viewer sees it. Call every frame while the touch is held;
        /// use a stable pointerId per finger/blob.
        /// </summary>
        public void InjectTouch(ScreenId screenId, Vector2 uv, int pointerId)
        {
            m_Injected[pointerId] = (screenId, uv, Time.unscaledTime);
        }

        void Update()
        {
            if (m_Director == null || m_Director.Rig == null) return;
            var settings = m_Director.interaction;
            Disturbances.Clear();
            Attractors.Clear();
            foreach (var p in m_Pointers.Values) p.Seen = false;

            // --- Every touchscreen (one per physical screen) and every finger on it -------------------
            bool anyTouch = false;
            foreach (var device in InputSystem.devices)
            {
                if (device is Touchscreen ts)
                {
                    foreach (TouchControl touch in ts.touches)
                    {
                        if (!touch.press.isPressed) continue;
                        anyTouch = true;
                        int id = ts.deviceId * 100 + touch.touchId.ReadValue();
                        m_CurrentStrength = 1f;
                        HandlePointer(id, touch.position.ReadValue(), touch.displayIndex.ReadValue());
                    }
                }
                else if (device is Pen pen && pen.tip.isPressed)
                {
                    anyTouch = true;
                    m_CurrentStrength = 1f;
                    HandlePointer(-2000 - pen.deviceId, pen.position.ReadValue(), pen.displayIndex.ReadValue());
                }
            }

            // --- Mouse: click = full effect; hovering over a screen = lighter, interactive effect -------
            var mouse = Mouse.current;
            if (mouse != null && !(anyTouch && settings.ignoreMouseWhileTouching))
            {
                Vector2 pos = mouse.position.ReadValue();
                if ((pos - m_LastMousePos).sqrMagnitude > 0.25f)
                {
                    m_LastMousePos = pos;
                    m_LastMouseMoveTime = Time.time;
                }
                bool pressed = mouse.leftButton.isPressed;
                bool hovering = settings.mouseHover && Application.isFocused && Time.time - m_LastMouseMoveTime < settings.hoverIdleTime && PointerOverGame();
                if (pressed || hovering)
                {
                    m_CurrentStrength = pressed ? 1f : settings.hoverStrength;
                    // Hover and click use separate ids so pressing starts a fresh "contact" (splash + scare).
                    HandlePointer(pressed ? MouseId : MouseId - 1, pos, mouse.displayIndex.ReadValue());
                }
            }

            m_ExpiredInjected.Clear();
            foreach (var kv in m_Injected)
            {
                if (Time.unscaledTime - kv.Value.time > InjectedHoldTime) { m_ExpiredInjected.Add(kv.Key); continue; }
                m_CurrentStrength = 1f;
                var uvIn = kv.Value.uv;
                ProcessPointer(kv.Key, m_Director.Rig.Screens[(int)kv.Value.screen], new Vector2(Mathf.Clamp01(uvIn.x), Mathf.Clamp01(uvIn.y)));
            }
            foreach (int id in m_ExpiredInjected) m_Injected.Remove(id);

            // Released pointers leave a fading echo so even a quick tap scares the fish.
            m_Released.Clear();
            foreach (var kv in m_Pointers)
                if (!kv.Value.Seen) m_Released.Add(kv.Key);
            foreach (int id in m_Released)
            {
                m_Pointers.Remove(id);
                if (m_Pushes.TryGetValue(id, out var push)) push.Target = 0f;
            }

            float linger = Mathf.Max(0.01f, settings.lingerTime);
            for (int i = m_Echoes.Count - 1; i >= 0; i--)
            {
                float age = Time.time - m_Echoes[i].Born;
                if (age > linger)
                {
                    m_Echoes.RemoveAt(i);
                    continue;
                }
                var d = m_Echoes[i].Disturbance;
                d.Strength *= 1f - age / linger;
                Disturbances.Add(d);
            }

            UpdatePlantPush(settings);
        }

        void HandlePointer(int id, Vector2 pixel, int displayIndex)
        {
            displayIndex = ResolveDisplayIndex(displayIndex);
            if (!m_Director.Rig.TryMapPointer(pixel, displayIndex, out var screen, out var uv)) return;
            ProcessPointer(id, screen, uv);
        }

        /// <summary>In the Editor, hover only counts while the cursor is over a Game view.</summary>
        static bool PointerOverGame()
        {
#if UNITY_EDITOR
            var window = UnityEditor.EditorWindow.mouseOverWindow;
            return window != null && window.GetType().Name == "GameView";
#else
            return true;
#endif
        }

        /// <summary>
        /// In a player the Input System reports which display a pointer is on. In the Editor, each display
        /// is shown in its own Game view, so the Game view under the cursor tells us the display instead.
        /// </summary>
        static int ResolveDisplayIndex(int reported)
        {
#if UNITY_EDITOR
            var window = UnityEditor.EditorWindow.mouseOverWindow;
            if (window == null) window = UnityEditor.EditorWindow.focusedWindow;
            if (window != null && window.GetType().Name == "GameView")
            {
                for (var t = window.GetType(); t != null; t = t.BaseType)
                {
                    var prop = t.GetProperty("targetDisplay",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (prop != null && prop.PropertyType == typeof(int)) return (int)prop.GetValue(window);
                }
            }
#endif
            return reported;
        }

        void ProcessPointer(int id, ProjectionScreen screen, Vector2 uv)
        {
            var rig = m_Director.Rig;
            var settings = m_Director.interaction;
            float strength = m_CurrentStrength;
            Vector3 eye = rig.Eye;
            Vector3 glassPoint = screen.PointAt(uv);
            Vector3 dir = (glassPoint - eye).normalized;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            bool isNew = !m_Pointers.TryGetValue(id, out var state) || state.Screen != screen;
            if (isNew)
            {
                state = new PointerState { Screen = screen, Uv = uv, GlassPoint = glassPoint, Direction = dir, LastBubbleTime = -1f, DownTime = Time.time };
                m_Pointers[id] = state;
            }
            state.Seen = true;
            if (strength >= 0.99f) LastTouchedScreen = screen.Id;

            Vector3 rawVelocity = isNew ? Vector3.zero : (glassPoint - state.GlassPoint) / dt;
            // Smoothed finger velocity: mouse and touch sample at different rates, this keeps both fluid.
            state.Velocity = isNew ? Vector3.zero : Vector3.Lerp(state.Velocity, rawVelocity, 1f - Mathf.Exp(-dt * 20f));
            Vector3 glassVelocity = state.Velocity;
            float moved = isNew ? 0f : (glassPoint - state.GlassPoint).magnitude;
            // Hover only acts while the cursor moves, so a parked mouse does not keep scaring fish.
            float activity = strength >= 0.99f ? 1f : Mathf.Clamp01(glassVelocity.magnitude * 1.5f);

            // --- Fish: landing or fast movement scares, a calm finger attracts ------------------------------
            float speed = glassVelocity.magnitude;
            bool pressed = strength >= 0.99f;
            float heldFor = Time.time - state.DownTime;
            if (speed < settings.followSpeed) state.CalmTime += dt; else state.CalmTime = 0f;
            bool landing = pressed && heldFor < 0.2f;
            bool fast = speed > settings.scareSpeed;
            bool attracting = state.CalmTime > settings.attractDelay;

            if (attracting)
            {
                bool feeding = pressed && heldFor > settings.feedDelay;
                Attractors.Add(new Attractor
                {
                    // Far enough behind the glass that the tunnel-avoidance force does not fight the attraction.
                    Position = glassPoint + dir * (m_Director.tank.glassMargin + 1.9f),
                    Screen = screen.Id,
                    Strength = pressed ? 1f : settings.hoverStrength,
                    Feeding = feeding,
                });
                var food = m_Director.Food;
                if (feeding && food != null && Time.time - state.LastFoodTime > 1f / Mathf.Max(settings.foodRate, 0.1f))
                {
                    // Sprinkled a little way out so fish (which keep clear of the glass) can reach it.
                    food.Drop(glassPoint + dir * (m_Director.tank.glassMargin + 1.5f), Random.Range(1, 3));
                    state.LastFoodTime = Time.time;
                }
            }

            if (landing || fast)
            {
                float scare = landing ? 1f : Mathf.Clamp01((speed - settings.scareSpeed) / settings.scareSpeed + 0.4f);
                var disturbance = new Disturbance
                {
                    Origin = glassPoint,
                    Direction = dir,
                    Radius = settings.fleeRadius * (isNew ? 1.2f : 1f) * Mathf.Lerp(0.75f, 1f, strength),
                    Reach = settings.reach,
                    Strength = Mathf.Clamp(1f + speed * 0.25f, 1f, 1.6f) * strength * scare,
                    Screen = screen.Id,
                };
                Disturbances.Add(disturbance);
                if (isNew || fast)
                    m_Echoes.Add(new Echo { Disturbance = disturbance, Born = Time.time });
                if (m_Echoes.Count > 64) m_Echoes.RemoveAt(0);
            }

            // --- Water layer on this screen: waves + liquid ----------------------------------------------
            if (screen.Ripple != null)
            {
                float rs = settings.rippleStrength * strength;
                Vector2 screenVel = new Vector2(Vector3.Dot(glassVelocity, screen.Right), Vector3.Dot(glassVelocity, screen.Up));
                if (isNew && strength >= 0.99f)
                {
                    // Finger lands: press into the surface and push the liquid outward in a ring.
                    screen.Ripple.AddSplat(uv, 0.06f, -0.45f * rs);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k / 6f * Mathf.PI * 2f;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Vector2 offsetUv = new Vector2(d.x * 0.05f / screen.Width, d.y * 0.05f / screen.Height);
                        screen.Ripple.AddLiquid(uv + offsetUv, d * 1.2f, 0.06f, 0.12f);
                    }
                    screen.Ripple.AddLiquid(uv, Vector2.zero, 0.08f, 0.5f);
                    state.LastPulseTime = Time.time;
                }
                else if (moved > 0.002f)
                {
                    // Drag: continuous wake and liquid pulled along the finger path.
                    int n = Mathf.Clamp(Mathf.CeilToInt(moved / 0.015f), 1, 16);
                    float waveImpulse = -Mathf.Clamp(speed * 0.18f, 0.04f, 0.35f) * rs / Mathf.Sqrt(n);
                    float dye = Mathf.Clamp(speed * 0.2f, 0.03f, 0.3f) * strength / n * 2f;
                    for (int k = 1; k <= n; k++)
                    {
                        Vector2 p = Vector2.Lerp(state.Uv, uv, k / (float)n);
                        screen.Ripple.AddSplat(p, 0.045f, waveImpulse);
                        screen.Ripple.AddLiquid(p, screenVel * strength, 0.07f, dye);
                    }
                }
                else if (strength >= 0.99f && Time.time - state.LastPulseTime > 0.35f)
                {
                    // Holding still: gentle pulses.
                    screen.Ripple.AddSplat(uv, 0.05f, -0.15f * rs);
                    screen.Ripple.AddLiquid(uv, Vector2.zero, 0.06f, 0.08f);
                    state.LastPulseTime = Time.time;
                }
            }

            // --- Tank currents ------------------------------------------------------------------------
            var fluid = m_Director.Fluid;
            if (fluid != null && activity > 0.05f)
            {
                Vector3 waterPoint = WaterPoint(screen, glassPoint, dir);
                if (isNew && strength >= 0.99f) fluid.AddSplat(waterPoint, Vector2.zero, 2.2f, settings.fluidPush);
                if (moved > 0.001f)
                {
                    Vector3 v = glassVelocity;
                    // Wall drags that go up/down still stir the water sideways along the tunnel.
                    Vector2 horizontal = new Vector2(v.x, v.z);
                    if (screen.Id != ScreenId.Floor) horizontal += new Vector2(-screen.Normal.x, -screen.Normal.z) * Mathf.Abs(v.y) * 0.3f;
                    fluid.AddSplat(waterPoint, horizontal * settings.fluidDrag * strength, 1.6f, 0f);
                }
            }

            // --- Plants bend away from the touch (smoothed in UpdatePlantPush) --------------------------
            if (!m_Pushes.TryGetValue(id, out var push))
            {
                push = new Push();
                m_Pushes[id] = push;
            }
            push.Origin = glassPoint;
            push.Direction = dir;
            push.Target = strength * Mathf.Max(activity, strength >= 0.99f ? 1f : 0f);

            // --- Bubbles and sand ---------------------------------------------------------------------
            var effects = m_Director.Effects;
            if (effects != null)
            {
                Vector3 behindGlass = glassPoint - screen.Normal * 0.35f;
                if (isNew && strength >= 0.99f)
                {
                    effects.EmitTouchBubbles(behindGlass, dir, settings.bubblesPerTap);
                    if (screen.Id == ScreenId.Floor && TryHitSeabed(glassPoint, dir, out var bed))
                        effects.EmitSandPuff(bed, 40);
                }
                else if (moved > 0.01f && Time.time - state.LastBubbleTime > 0.05f)
                {
                    effects.EmitTouchBubbles(behindGlass, dir, Mathf.Clamp(Mathf.RoundToInt(moved * 60f * strength), 1, 6));
                    state.LastBubbleTime = Time.time;
                }
            }

            state.Uv = uv;
            state.GlassPoint = glassPoint;
            state.Direction = dir;
        }

        /// <summary>Eases each pointer's plant push in and out and uploads them for the Kelp shader.</summary>
        void UpdatePlantPush(InteractionSettings settings)
        {
            float dt = Time.deltaTime;
            int count = 0;
            m_DeadPushes.Clear();
            foreach (var kv in m_Pushes)
            {
                var p = kv.Value;
                if (!m_Pointers.ContainsKey(kv.Key)) p.Target = 0f;
                float rate = p.Target > p.Value ? 10f : 1.6f; // quick bend, slow springy recovery
                p.Value = Mathf.Lerp(p.Value, p.Target, 1f - Mathf.Exp(-dt * rate));
                if (p.Value < 0.002f && p.Target <= 0f)
                {
                    m_DeadPushes.Add(kv.Key);
                    continue;
                }
                if (count < MaxPushes)
                {
                    m_PushOrigins[count] = new Vector4(p.Origin.x, p.Origin.y, p.Origin.z, p.Value * settings.plantPush);
                    m_PushDirs[count] = new Vector4(p.Direction.x, p.Direction.y, p.Direction.z, settings.plantPushRadius);
                    count++;
                }
            }
            foreach (int id in m_DeadPushes) m_Pushes.Remove(id);
            Shader.SetGlobalVectorArray(s_PushOriginId, m_PushOrigins);
            Shader.SetGlobalVectorArray(s_PushDirId, m_PushDirs);
            Shader.SetGlobalInt(s_PushCountId, count);
        }

        Vector3 WaterPoint(ProjectionScreen screen, Vector3 glassPoint, Vector3 dir)
        {
            // Floor touches act on the water below the tunnel; wall touches a few metres out.
            float depth = screen.Id == ScreenId.Floor ? 3.5f : 3f;
            return glassPoint + dir * depth;
        }

        bool TryHitSeabed(Vector3 origin, Vector3 dir, out Vector3 hit)
        {
            Vector3 p = origin;
            for (int i = 0; i < 200; i++)
            {
                p += dir * 0.1f;
                if (p.y <= Seabed.Height(p.x, p.z))
                {
                    hit = p;
                    return true;
                }
            }
            hit = default;
            return false;
        }
    }
}
