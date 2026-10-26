using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.Editor
{
    /// <summary>Play-mode helpers: a stitched screenshot of all three screens and a scripted tap test.</summary>
    public static class AquariumDebugTools
    {
        [MenuItem("Aquarium/Capture Three-Screen Screenshot", priority = 40)]
        public static void CaptureMenu()
        {
            string path = Capture(Path.Combine(Directory.GetCurrentDirectory(), "Screenshots"), 540);
            if (path != null) EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Aquarium/Capture Three-Screen Screenshot", true)]
        static bool CaptureValidate() => Application.isPlaying;

        /// <summary>Renders left wall | floor | right wall at their true aspect ratios into one PNG.</summary>
        public static string Capture(string directory, int height)
        {
            var director = Object.FindAnyObjectByType<AquariumDirector>();
            if (director == null || director.Rig == null)
            {
                Debug.LogWarning("[Aquarium] Enter Play mode with an AquariumDirector in the scene to capture.");
                return null;
            }
            var shots = new List<Texture2D>();
            int totalWidth = 0;
            foreach (var screen in director.Rig.Screens)
            {
                int w = Mathf.RoundToInt(height * screen.Aspect);
                shots.Add(Render(screen.Camera, w, height));
                totalWidth += w;
            }
            const int gap = 4;
            totalWidth += gap * (shots.Count - 1);
            var sheet = new Texture2D(totalWidth, height, TextureFormat.RGB24, false);
            var black = new Color[totalWidth * height];
            sheet.SetPixels(black);
            int x = 0;
            foreach (var shot in shots)
            {
                sheet.SetPixels(x, 0, shot.width, shot.height, shot.GetPixels());
                x += shot.width + gap;
                Object.DestroyImmediate(shot);
            }
            sheet.Apply();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"Aquarium_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Debug.Log($"[Aquarium] Saved {path}");
            return path;
        }

        static Texture2D Render(Camera cam, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Rect rect = cam.rect;
            Matrix4x4 projection = cam.projectionMatrix;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.projectionMatrix = projection;
            RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
            cam.rect = rect;
            cam.projectionMatrix = projection;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        // ------------------------------------------------------------------------------------------
        // Tap test: aims a simulated touch at the nearest visible fish behind a screen and reports how
        // far the fish around the touch ray moved away from it.
        // ------------------------------------------------------------------------------------------
        static readonly List<(Transform fish, float distance)> s_Tracked = new List<(Transform, float)>();
        static Vector3 s_RayOrigin, s_RayDir;

        [MenuItem("Aquarium/Debug/Simulate Tap On Left Wall", priority = 60)]
        public static void TapLeftWall() => Tap(ScreenId.LeftWall);

        [MenuItem("Aquarium/Debug/Simulate Tap On Floor", priority = 61)]
        public static void TapFloor() => Tap(ScreenId.Floor);

        [MenuItem("Aquarium/Debug/Simulate Tap On Right Wall", priority = 62)]
        public static void TapRightWall() => Tap(ScreenId.RightWall);

        [MenuItem("Aquarium/Debug/Simulate Tap On Left Wall", true)]
        [MenuItem("Aquarium/Debug/Simulate Tap On Floor", true)]
        [MenuItem("Aquarium/Debug/Simulate Tap On Right Wall", true)]
        static bool TapValidate() => Application.isPlaying;

        public static string Tap(ScreenId id)
        {
            var director = Object.FindAnyObjectByType<AquariumDirector>();
            if (director == null || director.Rig == null) return "No running aquarium.";
            var screen = director.Rig.Screens[(int)id];
            Vector3 eye = director.Rig.Eye;
            var fishRoot = director.Fish.transform;

            // Aim at the closest fish visible through this screen.
            Transform target = null;
            float best = float.MaxValue;
            Vector2 bestUv = new Vector2(0.5f, 0.5f);
            Vector3 n = screen.Normal;
            foreach (Transform species in fishRoot)
            foreach (Transform fish in species)
            {
                Vector3 d = fish.position - eye;
                float denom = Vector3.Dot(d, -n);
                if (denom <= 0.01f) continue;
                float t = Vector3.Dot(screen.BottomLeft - eye, -n) / denom;
                if (t >= 1f) continue; // fish must be behind the glass
                Vector3 hit = eye + d * t;
                Vector3 local = hit - screen.BottomLeft;
                var uv = new Vector2(Vector3.Dot(local, screen.Right) / screen.Width, Vector3.Dot(local, screen.Up) / screen.Height);
                if (uv.x < 0.1f || uv.x > 0.9f || uv.y < 0.1f || uv.y > 0.9f) continue;
                if (d.magnitude < best)
                {
                    best = d.magnitude;
                    target = fish;
                    bestUv = uv;
                }
            }

            s_RayOrigin = screen.PointAt(bestUv);
            s_RayDir = (s_RayOrigin - eye).normalized;
            s_Tracked.Clear();
            foreach (Transform species in fishRoot)
            foreach (Transform fish in species)
            {
                float dist = DistanceToRay(fish.position);
                if (dist < 2.5f && director.Rig.ScreenIndexForPoint(fish.position) == (int)id) s_Tracked.Add((fish, dist));
            }
            director.Input.InjectTouch(id, bestUv, 9000 + (int)id);
            string msg = target != null
                ? $"Tapped {id} at uv {bestUv:F2}, aiming at a {target.parent.name} {best:F1} m from the eye. Tracking {s_Tracked.Count} fish within 2.5 m of the touch ray."
                : $"Tapped {id} at the centre (no fish in view). Tracking {s_Tracked.Count} fish.";
            Debug.Log("[Aquarium] " + msg);
            return msg;
        }

        /// <summary>Call about a second after Tap: reports how the tracked fish moved relative to the touch ray.</summary>
        [MenuItem("Aquarium/Debug/Report Tap Reaction", priority = 63)]
        public static string ReportReaction()
        {
            if (s_Tracked.Count == 0) return "Nothing tracked. Run a Simulate Tap first.";
            int movedAway = 0;
            float sumBefore = 0f, sumAfter = 0f;
            foreach (var (fish, before) in s_Tracked)
            {
                if (fish == null) continue;
                float after = DistanceToRay(fish.position);
                sumBefore += before;
                sumAfter += after;
                if (after > before + 0.3f) movedAway++;
            }
            string msg = $"{movedAway}/{s_Tracked.Count} fish moved away from the touch; mean distance to the touch ray {sumBefore / s_Tracked.Count:F2} m -> {sumAfter / s_Tracked.Count:F2} m.";
            var director = Object.FindAnyObjectByType<AquariumDirector>();
            if (director != null && director.Fish != null)
                msg += $" Escaping by touched display: Left={director.Fish.EscapingCount(ScreenId.LeftWall)}, " +
                       $"Floor={director.Fish.EscapingCount(ScreenId.Floor)}, Right={director.Fish.EscapingCount(ScreenId.RightWall)}.";
            Debug.Log("[Aquarium] " + msg);
            return msg;
        }

        [MenuItem("Aquarium/Debug/Simulate Drag Across Floor", priority = 64)]
        public static void DragFloorMenu() => SimulateDrag(ScreenId.Floor, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.7f), 0.8f);

        [MenuItem("Aquarium/Debug/Simulate Drag Across Floor", true)]
        static bool DragValidate() => Application.isPlaying;

        /// <summary>Feeds a moving touch every frame from 'from' to 'to' (screen uv) over 'seconds'.</summary>
        public static void SimulateDrag(ScreenId id, Vector2 from, Vector2 to, float seconds)
        {
            var director = Object.FindAnyObjectByType<AquariumDirector>();
            if (director == null || director.Input == null) return;
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                float u = (float)((EditorApplication.timeSinceStartup - start) / seconds);
                if (!Application.isPlaying || u > 1f || director == null)
                {
                    EditorApplication.update -= tick;
                    return;
                }
                director.Input.InjectTouch(id, Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u)), 9100 + (int)id);
            };
            EditorApplication.update += tick;
        }

        static float DistanceToRay(Vector3 p)
        {
            float along = Mathf.Clamp(Vector3.Dot(p - s_RayOrigin, s_RayDir), 0f, 14f);
            return (p - (s_RayOrigin + s_RayDir * along)).magnitude;
        }
    }
}
