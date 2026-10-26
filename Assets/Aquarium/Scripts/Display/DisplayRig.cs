using UnityEngine;

namespace Aquarium
{
    /// <summary>
    /// Creates the three screen cameras (left wall, floor, right wall), routes them to physical displays
    /// or to a side-by-side preview, and updates all three frusta together every frame from one shared
    /// eye point so the views stay in sync.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class DisplayRig : MonoBehaviour
    {
        AquariumDirector m_Director;
        Camera m_PreviewBackground;

        public ProjectionScreen LeftWall { get; private set; }
        public ProjectionScreen Floor { get; private set; }
        public ProjectionScreen RightWall { get; private set; }
        public ProjectionScreen[] Screens { get; private set; }
        public bool IsPreview { get; private set; }

        /// <summary>Viewer eye in world space. Driven by TunnelSettings.eyePosition (can be head-tracked).</summary>
        public Vector3 Eye => m_Director.transform.TransformPoint(m_Director.tunnel.eyePosition);

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            var t = director.tunnel;
            var ds = director.display;
            float hw = t.width * 0.5f, hl = t.length * 0.5f, h = t.wallHeight;
            Transform root = director.transform;

            int glassBase = Mathf.Clamp(ds.glassLayerStart, 8, 29);
            int allGlass = (1 << glassBase) | (1 << (glassBase + 1)) | (1 << (glassBase + 2));

            // Corners are given as the viewer sees each screen: bottom-left, bottom-right, top-left.
            LeftWall = CreateScreen(ScreenId.LeftWall, "Left Wall Camera", ds.leftWallCamera,
                root.TransformPoint(new Vector3(-hw, 0f, -hl)), root.TransformPoint(new Vector3(-hw, 0f, hl)), root.TransformPoint(new Vector3(-hw, h, -hl)),
                ds.leftWallDisplay, glassBase, allGlass);
            Floor = CreateScreen(ScreenId.Floor, "Floor Camera", ds.floorCamera,
                root.TransformPoint(new Vector3(-hw, 0f, -hl)), root.TransformPoint(new Vector3(hw, 0f, -hl)), root.TransformPoint(new Vector3(-hw, 0f, hl)),
                ds.floorDisplay, glassBase + 1, allGlass);
            RightWall = CreateScreen(ScreenId.RightWall, "Right Wall Camera", ds.rightWallCamera,
                root.TransformPoint(new Vector3(hw, 0f, hl)), root.TransformPoint(new Vector3(hw, 0f, -hl)), root.TransformPoint(new Vector3(hw, h, hl)),
                ds.rightWallDisplay, glassBase + 2, allGlass);
            Screens = new[] { LeftWall, Floor, RightWall };

            for (int i = 0; i < Screens.Length; i++)
                for (int j = i + 1; j < Screens.Length; j++)
                    if (Screens[i].Camera.targetDisplay == Screens[j].Camera.targetDisplay)
                        Debug.LogWarning($"[Aquarium] {Screens[i].Id} and {Screens[j].Id} both target Display {Screens[i].Camera.targetDisplay + 1}. " +
                                         "Give every screen camera its own Target Display.");

            bool preview = ds.layout == DisplayLayoutMode.SingleWindowPreview ||
                           (ds.layout == DisplayLayoutMode.Auto && (Application.isEditor || Display.displays.Length < 3));
            ApplyLayout(preview);
            SyncCameras();
        }

        ProjectionScreen CreateScreen(ScreenId id, string name, Camera sceneCamera, Vector3 bl, Vector3 br, Vector3 tl, int display, int glassLayer, int allGlassMask)
        {
            GameObject go;
            if (sceneCamera != null)
            {
                // Use the camera placed in the scene; its Target Display decides where it renders.
                go = sceneCamera.gameObject;
                display = sceneCamera.targetDisplay;
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(transform, false);
            }
            var screen = go.GetComponent<ProjectionScreen>();
            if (screen == null) screen = go.AddComponent<ProjectionScreen>();
            // Each camera sees everything except the other screens' glass layers.
            int mask = ~allGlassMask | (1 << glassLayer);
            var w = m_Director.water;
            screen.Initialize(id, bl, br, tl, display, glassLayer, mask, w.fogColor, w.postProcessing, m_Director.display.smaaQuality);
            return screen;
        }

        /// <summary>Switch between one-display-per-screen and the single-window preview at runtime.</summary>
        public void ApplyLayout(bool preview)
        {
            IsPreview = preview;
            var ds = m_Director.display;
            if (!preview)
            {
                foreach (var s in Screens)
                {
                    s.Camera.targetDisplay = s.DisplayIndex;
                    s.Camera.rect = new Rect(0f, 0f, 1f, 1f);
                }
                if (ds.activateDisplays && !Application.isEditor)
                {
                    foreach (var s in Screens)
                    {
                        int i = s.DisplayIndex;
                        if (i > 0 && i < Display.displays.Length && !Display.displays[i].active)
                            Display.displays[i].Activate();
                        if (i >= Display.displays.Length)
                            Debug.LogWarning($"[Aquarium] {s.Id} is assigned to display {i + 1}, but only {Display.displays.Length} display(s) are connected.");
                    }
                }
                if (m_PreviewBackground != null) m_PreviewBackground.enabled = false;
            }
            else
            {
                foreach (var s in Screens) s.Camera.targetDisplay = 0;
                if (m_PreviewBackground == null)
                {
                    var go = new GameObject("Preview Letterbox Camera");
                    go.transform.SetParent(transform, false);
                    m_PreviewBackground = go.AddComponent<Camera>();
                    m_PreviewBackground.clearFlags = CameraClearFlags.SolidColor;
                    m_PreviewBackground.backgroundColor = Color.black;
                    m_PreviewBackground.cullingMask = 0;
                    m_PreviewBackground.depth = -10;
                }
                m_PreviewBackground.enabled = true;
                UpdatePreviewRects();
            }
        }

        /// <summary>Route a screen to a physical display at runtime (activates the display in a player).</summary>
        public void SetScreenDisplay(ScreenId id, int displayIndex)
        {
            var s = Screens[(int)id];
            s.SetDisplay(displayIndex);
            if (!IsPreview) s.Camera.rect = new Rect(0f, 0f, 1f, 1f);
            if (!Application.isEditor && displayIndex > 0 && displayIndex < Display.displays.Length && !Display.displays[displayIndex].active)
                Display.displays[displayIndex].Activate();
            if (IsPreview) s.Camera.targetDisplay = 0;
        }

        /// <summary>Lay the three views out left-to-right at their true aspect ratios (left wall | floor | right wall).</summary>
        void UpdatePreviewRects()
        {
            float windowAspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float total = 0f;
            foreach (var s in Screens) total += s.Aspect;
            const float gap = 0.004f;
            float heightFrac = Mathf.Min(1f, (1f - gap * 2f) * windowAspect / total);
            float x = (1f - (total * heightFrac / windowAspect + gap * 2f)) * 0.5f;
            float y = (1f - heightFrac) * 0.5f;
            foreach (var s in Screens)
            {
                float w = s.Aspect * heightFrac / windowAspect;
                s.Camera.rect = new Rect(x, y, w, heightFrac);
                x += w + gap;
            }
        }

        void LateUpdate()
        {
            if (IsPreview) UpdatePreviewRects();
            SyncCameras();
        }

        /// <summary>All three cameras are posed from the same eye in the same call: they never drift apart.</summary>
        void SyncCameras()
        {
            Vector3 eye = Eye;
            float far = m_Director.tunnel.farClip;
            foreach (var s in Screens) s.UpdateProjection(eye, far);
        }

        /// <summary>
        /// Which screen a world point is seen through from the eye (-1 if none, e.g. above the tunnel).
        /// The three screens surround the eye, so any point is visible on at most one of them.
        /// </summary>
        public int ScreenIndexForPoint(Vector3 point)
        {
            Vector3 eye = Eye;
            Vector3 d = point - eye;
            foreach (var s in Screens)
            {
                Vector3 inward = -s.Normal; // from the viewer towards the water
                float denom = Vector3.Dot(d, inward);
                if (denom <= 1e-4f) continue;
                float t = Vector3.Dot(s.BottomLeft - eye, inward) / denom;
                if (t >= 1f) continue; // the point is on the viewer's side of this glass
                Vector3 local = eye + d * t - s.BottomLeft;
                float u = Vector3.Dot(local, s.Right) / s.Width;
                float v = Vector3.Dot(local, s.Up) / s.Height;
                if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) return (int)s.Id;
            }
            return -1;
        }

        /// <summary>Map a pointer position (pixels on a display) to a screen and its normalised coordinates.</summary>
        public bool TryMapPointer(Vector2 pixel, int displayIndex, out ProjectionScreen screen, out Vector2 uv)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var s in Screens)
                {
                    // First pass honours the display the pointer is on; second pass is a fallback for
                    // platforms that do not report a display index.
                    if (pass == 0 && !IsPreview && s.Camera.targetDisplay != displayIndex) continue;
                    if (pass == 1 && !IsPreview && s.Camera.targetDisplay != 0) continue;
                    Rect r = s.Camera.pixelRect;
                    if (r.width <= 0f || r.height <= 0f || !r.Contains(pixel)) continue;
                    screen = s;
                    uv = new Vector2((pixel.x - r.x) / r.width, (pixel.y - r.y) / r.height);
                    return true;
                }
                if (IsPreview || displayIndex == 0) break;
            }
            screen = null;
            uv = default;
            return false;
        }
    }
}
