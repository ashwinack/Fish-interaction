using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Aquarium
{
    /// <summary>
    /// Operator control panel on the main monitor (Display 1). The three projection screens run on their own
    /// displays; this panel shows live previews and status, routes each screen to a display, and adjusts the
    /// aquarium live. Settings are saved automatically and restored on the next start.
    /// Drawn with IMGUI, which always renders on the main display. F1 hides / shows it.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class ControlPanel : MonoBehaviour
    {
        const float RefHeight = 1080f;
        static readonly string[] s_ScreenNames = { "Left wall", "Floor", "Right wall" };

        AquariumDirector m_Director;
        OperatorSettings m_Settings;
        Camera m_Background;
        readonly RenderTexture[] m_Previews = new RenderTexture[3];
        int m_PreviewCursor;
        bool m_Visible = true;
        float m_SaveAt = -1f;
        bool m_RestartPending;
        bool m_ConfirmQuit;
        float m_FpsSmoothed = 60f;
        Vector2 m_SpeciesScroll;
        string m_Toast;
        float m_ToastUntil;

        // Identify-display overlays (one uGUI canvas per secondary display)
        readonly List<GameObject> m_IdentifyCanvases = new List<GameObject>();
        float m_IdentifyUntil;

        // Styles
        bool m_StylesReady;
        GUIStyle m_Panel, m_Header, m_Title, m_Label, m_Small, m_Value, m_Button, m_BigButton, m_Accent, m_Toggle, m_ToggleOn, m_Warn;
        Texture2D m_PanelTex, m_ButtonTex, m_ButtonHoverTex, m_AccentTex, m_HeaderTex;

        public void Build(AquariumDirector director, OperatorSettings settings)
        {
            m_Director = director;
            m_Settings = settings;

            // Something must clear the operator monitor: a camera that renders nothing but a dark background.
            var go = new GameObject("Control Panel Background");
            go.transform.SetParent(transform, false);
            m_Background = go.AddComponent<Camera>();
            m_Background.clearFlags = CameraClearFlags.SolidColor;
            m_Background.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
            m_Background.cullingMask = 0;
            m_Background.depth = -50;
            m_Background.targetDisplay = 0;

            for (int i = 0; i < 3; i++)
            {
                var screen = director.Rig.Screens[i];
                int h = 240;
                int w = Mathf.Clamp(Mathf.RoundToInt(h * screen.Aspect), 96, 640);
                m_Previews[i] = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Preview " + screen.Id };
                m_Previews[i].Create();
            }
        }

        void Update()
        {
            m_FpsSmoothed = Mathf.Lerp(m_FpsSmoothed, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) m_Visible = !m_Visible;

            if (m_SaveAt > 0f && Time.unscaledTime > m_SaveAt)
            {
                m_Settings.Save();
                m_SaveAt = -1f;
            }
            if (m_IdentifyUntil > 0f && Time.unscaledTime > m_IdentifyUntil)
            {
                foreach (var c in m_IdentifyCanvases) if (c != null) c.SetActive(false);
                m_IdentifyUntil = -1f;
            }
        }

        void LateUpdate()
        {
            // One low-resolution preview per frame, round-robin (each screen refreshes at 1/3 frame rate).
            if (!m_Visible || !m_Settings.previews || m_Director.Rig == null) return;
            var screen = m_Director.Rig.Screens[m_PreviewCursor];
            var rt = m_Previews[m_PreviewCursor];
            m_PreviewCursor = (m_PreviewCursor + 1) % 3;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(screen.Camera, request))
                RenderPipeline.SubmitRenderRequest(screen.Camera, request);
        }

        void MarkChanged()
        {
            m_Settings.ApplyLive(m_Director);
            m_SaveAt = Time.unscaledTime + 1f;
        }

        void Toast(string message)
        {
            m_Toast = message;
            m_ToastUntil = Time.unscaledTime + 3f;
        }

        // -------------------------------------------------------------------------------------------
        // GUI
        // -------------------------------------------------------------------------------------------
        void OnGUI()
        {
            if (m_Director == null || m_Director.Rig == null) return;
            EnsureStyles();
            float scale = Mathf.Max(0.5f, Screen.height / RefHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float W = Screen.width / scale, H = RefHeight;

            if (!m_Visible)
            {
                GUI.Label(new Rect(16, H - 40, 600, 30), "Control panel hidden (F1)", m_Small);
                return;
            }

            // Header
            GUI.Box(new Rect(0, 0, W, 64), GUIContent.none, m_Header);
            GUI.Label(new Rect(24, 12, 600, 40), "AQUARIUM  CONTROL", m_Title);
            string status = $"{m_FpsSmoothed:0} fps     {m_Director.Fish.FishCount} fish     {Display.displays.Length} display(s) connected     {System.DateTime.Now:HH:mm}";
            GUI.Label(new Rect(W - 760, 20, 740, 30), status, m_Value);

            float top = 84f;
            float leftW = 540f;
            DrawScreens(new Rect(20, top, leftW, 330));
            DrawActions(new Rect(20, top + 346, leftW, 250));
            DrawStatus(new Rect(20, top + 612, leftW, H - top - 632));

            float rx = leftW + 40f, rw = W - rx - 20f;
            DrawPreviews(new Rect(rx, top, rw, 330));
            float colW = (rw - 20f) / 2f;
            DrawLookAndInteraction(new Rect(rx, top + 346, colW, 420));
            DrawFish(new Rect(rx + colW + 20f, top + 346, colW, 420));
            DrawFooter(new Rect(rx, top + 782, rw, H - top - 802));

            if (m_Toast != null && Time.unscaledTime < m_ToastUntil)
                GUI.Label(new Rect(W / 2 - 300, H - 70, 600, 40), m_Toast, m_Accent);
        }

        void DrawScreens(Rect r)
        {
            GUILayout.BeginArea(r, m_Panel);
            GUILayout.Label("Screens → displays", m_Label);
            GUILayout.Space(6);
            var rig = m_Director.Rig;
            for (int i = 0; i < 3; i++)
            {
                var id = (ScreenId)i;
                int current = rig.Screens[i].Camera.targetDisplay;
                GUILayout.BeginHorizontal();
                GUILayout.Label(s_ScreenNames[i], m_Label, GUILayout.Width(120));
                if (GUILayout.Button("◀", m_Button, GUILayout.Width(44))) SetDisplay(id, Mathf.Max(1, current - 1));
                GUILayout.Label(DisplayLabel(current), m_Value, GUILayout.Width(230));
                if (GUILayout.Button("▶", m_Button, GUILayout.Width(44))) SetDisplay(id, Mathf.Min(7, current + 1));
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Identify displays", m_Button)) IdentifyDisplays();
            if (GUILayout.Button("Swap left / right", m_Button))
            {
                int l = rig.LeftWall.Camera.targetDisplay, rr = rig.RightWall.Camera.targetDisplay;
                SetDisplay(ScreenId.LeftWall, rr);
                SetDisplay(ScreenId.RightWall, l);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(Application.isEditor
                ? "Editor: open Game views for Displays 2-4 (Aquarium > Open Game Views)."
                : "Display 1 is this monitor. Windows numbers the others in Settings > Display.", m_Small);
            GUILayout.EndArea();
        }

        string DisplayLabel(int index)
        {
            string label = "Display " + (index + 1);
            if (index < Display.displays.Length)
            {
                var d = Display.displays[index];
                label += $"  ({d.systemWidth}×{d.systemHeight})";
            }
            else if (!Application.isEditor) label += "  (not connected)";
            return label;
        }

        void SetDisplay(ScreenId id, int index)
        {
            m_Director.Rig.SetScreenDisplay(id, index);
            switch (id)
            {
                case ScreenId.LeftWall: m_Settings.leftWallDisplay = index; break;
                case ScreenId.RightWall: m_Settings.rightWallDisplay = index; break;
                default: m_Settings.floorDisplay = index; break;
            }
            MarkChanged();
            Toast($"{s_ScreenNames[(int)id]} → Display {index + 1}");
        }

        void DrawActions(Rect r)
        {
            GUILayout.BeginArea(r, m_Panel);
            GUILayout.Label("Actions", m_Label);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Feed all screens", m_BigButton)) FeedAll();
            if (GUILayout.Button("Startle all fish", m_BigButton)) StartleAll();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_RestartPending ? "Restart aquarium  •" : "Restart aquarium", m_RestartPending ? m_Warn : m_BigButton)) Restart();
            if (!m_ConfirmQuit)
            {
                if (GUILayout.Button("Quit", m_BigButton)) m_ConfirmQuit = true;
            }
            else
            {
                if (GUILayout.Button("Really quit?", m_Warn)) Quit();
                if (GUILayout.Button("Cancel", m_BigButton, GUILayout.Width(110))) m_ConfirmQuit = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label("F1 hides this panel.  Restart applies fish size / species changes.", m_Small);
            GUILayout.EndArea();
        }

        void DrawStatus(Rect r)
        {
            GUILayout.BeginArea(r, m_Panel);
            GUILayout.Label("Live status", m_Label);
            var fish = m_Director.Fish;
            var input = m_Director.Input;
            GUILayout.Label($"Fish visible     left {fish.VisibleCount(ScreenId.LeftWall)}    floor {fish.VisibleCount(ScreenId.Floor)}    right {fish.VisibleCount(ScreenId.RightWall)}", m_Value);
            GUILayout.Label($"Curious fish     left {fish.CuriousCount(ScreenId.LeftWall)}    floor {fish.CuriousCount(ScreenId.Floor)}    right {fish.CuriousCount(ScreenId.RightWall)}", m_Value);
            GUILayout.Label($"Active touches   {input.ActivePointerCount}      last touched: {(input.LastTouchedScreen.HasValue ? s_ScreenNames[(int)input.LastTouchedScreen.Value] : "-")}", m_Value);
            GUILayout.Label($"Food flakes      {(m_Director.Food != null ? m_Director.Food.Count : 0)}", m_Value);
            GUILayout.EndArea();
        }

        void DrawPreviews(Rect r)
        {
            GUI.Box(r, GUIContent.none, m_Panel);
            GUI.Label(new Rect(r.x + 16, r.y + 10, 300, 28), "Screens (live)", m_Label);
            bool on = m_Settings.previews;
            if (GUI.Button(new Rect(r.xMax - 190, r.y + 8, 180, 32), (on ? "■  " : "□  ") + "Previews", on ? m_ToggleOn : m_Toggle)) on = !on;
            if (on != m_Settings.previews) { m_Settings.previews = on; MarkChanged(); }

            // Left wall | floor | right wall, scaled to fit at their true aspect ratios
            float avail = r.width - 64f, h = r.height - 90f;
            float total = 0f;
            foreach (var s in m_Director.Rig.Screens) total += s.Aspect;
            h = Mathf.Min(h, avail / total);
            float x = r.x + 16f;
            int[] order = { 0, 1, 2 };
            foreach (int i in order)
            {
                var screen = m_Director.Rig.Screens[i];
                float w = h * screen.Aspect;
                var box = new Rect(x, r.y + 48, w, h);
                if (m_Settings.previews) GUI.DrawTexture(box, m_Previews[i], ScaleMode.StretchToFill, false);
                else GUI.Box(box, "previews off", m_Small);
                GUI.Label(new Rect(x, box.yMax + 6, w + 40, 26), $"{s_ScreenNames[i]}  ·  Display {screen.Camera.targetDisplay + 1}", m_Small);
                x += w + 16f;
            }
        }

        void DrawLookAndInteraction(Rect r)
        {
            GUILayout.BeginArea(r, m_Panel);
            GUILayout.Label("Look", m_Label);
            var s = m_Settings;
            float v;
            if ((v = Slider("Sunlight", s.sunIntensity, 0.3f, 4f, "0.00")) != s.sunIntensity) { s.sunIntensity = v; MarkChanged(); }
            if ((v = Slider("Water haze (fog)", s.fogDensity, 0.005f, 0.12f, "0.000")) != s.fogDensity) { s.fogDensity = v; MarkChanged(); }
            if ((v = Slider("God rays", s.godRays, 0f, 1.5f, "0.00")) != s.godRays) { s.godRays = v; MarkChanged(); }
            if ((v = Slider("Touch water effect", s.waterLayerOpacity, 0.1f, 1f, "0.00")) != s.waterLayerOpacity) { s.waterLayerOpacity = v; MarkChanged(); }
            if ((v = Slider("Ripple strength", s.rippleStrength, 0.2f, 3f, "0.00")) != s.rippleStrength) { s.rippleStrength = v; MarkChanged(); }
            GUILayout.Space(10);
            GUILayout.Label("Interaction & viewer", m_Label);
            if ((v = Slider("Scare radius (m)", s.fleeRadius, 0.8f, 6f, "0.0")) != s.fleeRadius) { s.fleeRadius = v; MarkChanged(); }
            if ((v = Slider("Viewer eye height (m)", s.eyeHeight, 0.8f, 2.2f, "0.00")) != s.eyeHeight) { s.eyeHeight = v; MarkChanged(); }
            GUILayout.BeginHorizontal();
            bool hover = ToggleRow(s.mouseHover, "Mouse hover interacts");
            if (hover != s.mouseHover) { s.mouseHover = hover; MarkChanged(); }
            bool feed = ToggleRow(s.feeding, "Feeding on long press");
            if (feed != s.feeding) { s.feeding = feed; MarkChanged(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawFish(Rect r)
        {
            GUILayout.BeginArea(r, m_Panel);
            GUILayout.Label("Fish  (applied on restart)", m_Label);
            float size = Slider("Fish size", m_Settings.fishSizeMultiplier, 0.5f, 4f, "0.0×");
            if (!Mathf.Approximately(size, m_Settings.fishSizeMultiplier))
            {
                m_Settings.fishSizeMultiplier = size;
                m_RestartPending = true;
                m_SaveAt = Time.unscaledTime + 1f;
            }
            GUILayout.Space(6);
            m_SpeciesScroll = GUILayout.BeginScrollView(m_SpeciesScroll);
            foreach (var sp in m_Director.species)
            {
                if (sp == null) continue;
                bool on = !m_Settings.disabledSpecies.Contains(sp.name);
                bool now = ToggleRow(on, $"{sp.name}   ({sp.count})");
                if (now != on)
                {
                    if (now) m_Settings.disabledSpecies.Remove(sp.name);
                    else m_Settings.disabledSpecies.Add(sp.name);
                    m_RestartPending = true;
                    m_SaveAt = Time.unscaledTime + 1f;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawFooter(Rect r)
        {
            GUILayout.BeginArea(r, m_Panel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save now", m_Button, GUILayout.Width(160))) { m_Settings.Save(); Toast("Settings saved"); }
            if (GUILayout.Button("Reset to scene defaults", m_Button, GUILayout.Width(260)))
            {
                OperatorSettings.Delete();
                Toast("Saved settings removed - restarting with scene defaults");
                Restart(save: false);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Settings file: " + OperatorSettings.FilePath, m_Small);
            GUILayout.EndArea();
        }

        /// <summary>A clearly visible checkbox row (filled square = on).</summary>
        bool ToggleRow(bool value, string label)
        {
            return GUILayout.Button((value ? "■  " : "□  ") + label, value ? m_ToggleOn : m_Toggle) ? !value : value;
        }

        float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, m_Label, GUILayout.Width(210));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(24));
            GUILayout.Label(v.ToString(format), m_Value, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
            return v;
        }

        // -------------------------------------------------------------------------------------------
        // Actions
        // -------------------------------------------------------------------------------------------
        void FeedAll()
        {
            var rig = m_Director.Rig;
            var food = m_Director.Food;
            if (food == null) return;
            foreach (var s in rig.Screens)
            {
                Vector3 glass = s.PointAt(new Vector2(0.5f, 0.55f));
                Vector3 dir = (glass - rig.Eye).normalized;
                food.Drop(glass + dir * (m_Director.tank.glassMargin + 1.8f), 25);
            }
            Toast("Food dropped on all screens");
        }

        void StartleAll()
        {
            for (int i = 0; i < 3; i++)
                m_Director.Input.InjectTouch((ScreenId)i, new Vector2(0.5f, 0.5f), 70000 + i);
            Toast("Startled the fish on all screens");
        }

        void Restart(bool save = true)
        {
            if (save) m_Settings.Save();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().buildIndex : 0);
        }

        void Quit()
        {
            m_Settings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Show a large label on every secondary display for a few seconds.</summary>
        void IdentifyDisplays()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            int count = Application.isEditor ? 8 : Display.displays.Length;
            while (m_IdentifyCanvases.Count < count) m_IdentifyCanvases.Add(null);
            for (int i = 1; i < count; i++)
            {
                if (m_IdentifyCanvases[i] == null)
                {
                    var go = new GameObject("Identify Display " + (i + 1));
                    go.transform.SetParent(transform, false);
                    var canvas = go.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.targetDisplay = i;
                    canvas.sortingOrder = 1000;
                    go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    var bg = new GameObject("Backdrop").AddComponent<Image>();
                    bg.transform.SetParent(go.transform, false);
                    bg.color = new Color(0f, 0f, 0f, 0.6f);
                    var rt = bg.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                    var text = new GameObject("Label").AddComponent<Text>();
                    text.transform.SetParent(go.transform, false);
                    text.font = font;
                    text.fontSize = 120;
                    text.alignment = TextAnchor.MiddleCenter;
                    text.color = Color.white;
                    var trt = text.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
                    m_IdentifyCanvases[i] = go;
                }
                string role = "(unused)";
                foreach (var s in m_Director.Rig.Screens)
                    if (s.Camera.targetDisplay == i) role = s_ScreenNames[(int)s.Id].ToUpperInvariant();
                m_IdentifyCanvases[i].GetComponentInChildren<Text>().text = $"DISPLAY {i + 1}\n{role}";
                m_IdentifyCanvases[i].SetActive(true);
            }
            m_IdentifyUntil = Time.unscaledTime + 5f;
            Toast("Each display now shows its number for 5 seconds");
        }

        // -------------------------------------------------------------------------------------------
        // Styling
        // -------------------------------------------------------------------------------------------
        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (m_StylesReady) return;
            m_PanelTex = Solid(new Color(0.07f, 0.11f, 0.16f, 0.96f));
            m_HeaderTex = Solid(new Color(0.04f, 0.2f, 0.3f, 1f));
            m_ButtonTex = Solid(new Color(0.12f, 0.24f, 0.33f, 1f));
            m_ButtonHoverTex = Solid(new Color(0.16f, 0.36f, 0.48f, 1f));
            m_AccentTex = Solid(new Color(0.85f, 0.45f, 0.1f, 1f));

            m_Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12) };
            m_Panel.normal.background = m_PanelTex;
            m_Header = new GUIStyle(GUI.skin.box);
            m_Header.normal.background = m_HeaderTex;
            m_Title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            m_Title.normal.textColor = new Color(0.8f, 0.95f, 1f);
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
            m_Label.normal.textColor = new Color(0.75f, 0.9f, 1f);
            m_Value = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            m_Value.normal.textColor = Color.white;
            m_Small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            m_Small.normal.textColor = new Color(0.65f, 0.75f, 0.82f);
            m_Button = new GUIStyle(GUI.skin.button) { fontSize = 18, fixedHeight = 40 };
            m_Button.normal.background = m_ButtonTex;
            m_Button.hover.background = m_ButtonHoverTex;
            m_Button.active.background = m_AccentTex;
            m_Button.normal.textColor = m_Button.hover.textColor = Color.white;
            m_BigButton = new GUIStyle(m_Button) { fontSize = 19, fixedHeight = 52 };
            m_Warn = new GUIStyle(m_BigButton);
            m_Warn.normal.background = m_AccentTex;
            m_Accent = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            m_Accent.normal.background = m_AccentTex;
            m_Accent.normal.textColor = Color.white;
            m_Toggle = new GUIStyle(GUI.skin.label) { fontSize = 18, padding = new RectOffset(4, 4, 4, 4) };
            m_Toggle.normal.textColor = new Color(0.6f, 0.68f, 0.75f);
            m_Toggle.hover.textColor = Color.white;
            m_ToggleOn = new GUIStyle(m_Toggle);
            m_ToggleOn.normal.textColor = new Color(1f, 0.72f, 0.35f);
            m_ToggleOn.hover.textColor = new Color(1f, 0.85f, 0.6f);
            m_StylesReady = true;
        }

        void OnDestroy()
        {
            foreach (var rt in m_Previews) if (rt != null) rt.Release();
            foreach (var t in new[] { m_PanelTex, m_HeaderTex, m_ButtonTex, m_ButtonHoverTex, m_AccentTex })
                if (t != null) Destroy(t);
        }
    }
}
