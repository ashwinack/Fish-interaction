using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aquarium.Editor
{
    public static class AquariumSceneMenu
    {
        const string ScenePath = "Assets/Aquarium/Scenes/Aquarium.unity";

        [MenuItem("Aquarium/Create Aquarium Scene", priority = 0)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Aquarium");
            var director = go.AddComponent<AquariumDirector>();
            SetupCameras(director);
            Selection.activeGameObject = go;

            System.IO.Directory.CreateDirectory("Assets/Aquarium/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Make it the first scene in the build.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            ApplyProjectSettings();
            Debug.Log($"[Aquarium] Created {ScenePath} with three display cameras. Use Aquarium > Open Game Views For Displays 1-4 to see all of them in the Editor.");
        }

        [MenuItem("Aquarium/Setup Three Display Cameras", priority = 1)]
        public static void SetupCamerasMenu()
        {
            var director = Object.FindAnyObjectByType<AquariumDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Aquarium] No AquariumDirector in the open scene. Use Aquarium > Create Aquarium Scene.");
                return;
            }
            SetupCameras(director);
            ApplyProjectSettings();
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            EditorSceneManager.SaveScene(director.gameObject.scene);
        }

        /// <summary>
        /// Creates (or reuses) one camera per physical screen under the director:
        /// Display 1 = operator monitor (control panel), Left Wall -> Display 2, Right Wall -> Display 3, Floor -> Display 4.
        /// </summary>
        public static void SetupCameras(AquariumDirector director)
        {
            var ds = director.display;
            var eye = director.tunnel.eyePosition;
            ds.leftWallCamera = EnsureCamera(director, ds.leftWallCamera, "Left Wall Camera (Display 2)", 1, eye,
                Quaternion.LookRotation(Vector3.left, Vector3.up));
            ds.rightWallCamera = EnsureCamera(director, ds.rightWallCamera, "Right Wall Camera (Display 3)", 2, eye,
                Quaternion.LookRotation(Vector3.right, Vector3.up));
            ds.floorCamera = EnsureCamera(director, ds.floorCamera, "Floor Camera (Display 4)", 3, eye,
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            ds.layout = DisplayLayoutMode.MultiDisplay;

            // Exactly one audio listener in the scene.
            if (Object.FindAnyObjectByType<AudioListener>() == null)
                Undo.AddComponent<AudioListener>(ds.leftWallCamera.gameObject);

            EditorUtility.SetDirty(director);
            Debug.Log("[Aquarium] Screen cameras ready: Display 1 = control panel, Left Wall -> Display 2, Right Wall -> Display 3, Floor -> Display 4. " +
                      "Change a camera's Target Display in its Inspector to re-route it.");
        }

        static Camera EnsureCamera(AquariumDirector director, Camera existing, string name, int display, Vector3 eye, Quaternion rotation)
        {
            Camera cam = existing;
            if (cam == null)
            {
                var t = director.transform.Find(name);
                if (t != null) cam = t.GetComponent<Camera>();
            }
            if (cam == null)
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Create Aquarium Camera");
                go.transform.SetParent(director.transform, false);
                cam = go.AddComponent<Camera>();
            }
            Undo.RecordObject(cam, "Configure Aquarium Camera");
            Undo.RecordObject(cam.transform, "Configure Aquarium Camera");
            cam.name = name;
            cam.targetDisplay = display;
            // Edit-time pose; at runtime the rig applies the exact off-axis projection every frame.
            cam.transform.localPosition = eye;
            cam.transform.localRotation = rotation;
            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = director.tunnel.farClip;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = director.water.fogColor;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = director.water.postProcessing;
            data.requiresColorOption = CameraOverrideOption.On;
            data.requiresDepthOption = CameraOverrideOption.On;
            EditorUtility.SetDirty(cam);
            return cam;
        }

        /// <summary>
        /// Installation-friendly player settings, and a full-resolution opaque texture so the aquarium
        /// seen through the water layer stays sharp.
        /// </summary>
        public static void ApplyProjectSettings()
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.visibleInBackground = true;

            var assets = new System.Collections.Generic.HashSet<RenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.count; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i);
                if (a != null) assets.Add(a);
            }
            foreach (var asset in assets.OfType<UniversalRenderPipelineAsset>())
            {
                var so = new SerializedObject(asset);
                var down = so.FindProperty("m_OpaqueDownsampling");
                var opaque = so.FindProperty("m_RequireOpaqueTexture");
                bool changed = false;
                if (down != null && down.intValue != 0) { down.intValue = 0; changed = true; }
                if (opaque != null && !opaque.boolValue) { opaque.boolValue = true; changed = true; }
                if (changed)
                {
                    so.ApplyModifiedProperties();
                    Debug.Log($"[Aquarium] {asset.name}: opaque texture enabled at full resolution (for the water layer refraction).");
                }
            }
        }

        [MenuItem("Aquarium/Open Game Views For Displays 1-4", priority = 2)]
        public static void OpenGameViews()
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                Debug.LogWarning("[Aquarium] Could not find the Game view type. Open Window > General > Game three times and set Display 1/2/3.");
                return;
            }
            var existing = Resources.FindObjectsOfTypeAll(gameViewType).Cast<EditorWindow>().ToList();
            string[] roles = { "Control Panel", "Left Wall", "Right Wall", "Floor" };
            for (int display = 0; display < 4; display++)
            {
                EditorWindow view = display < existing.Count ? existing[display] : (EditorWindow)ScriptableObject.CreateInstance(gameViewType);
                if (display >= existing.Count) view.Show();
                view.titleContent = new GUIContent($"Display {display + 1} - {roles[display]}");
                if (!SetTargetDisplay(view, display))
                    Debug.LogWarning($"[Aquarium] Set the '{view.titleContent.text}' Game view's display dropdown to Display {display + 1} manually.");
                view.Repaint();
            }
            Debug.Log("[Aquarium] Game views: Display 1 = Control Panel, Display 2 = Left Wall, Display 3 = Right Wall, Display 4 = Floor. Dock them, then press Play.");
        }

        static bool SetTargetDisplay(EditorWindow view, int display)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var t = view.GetType(); t != null; t = t.BaseType)
            {
                var prop = t.GetProperty("targetDisplay", flags);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(view, display);
                    return true;
                }
                var field = t.GetField("m_TargetDisplay", flags);
                if (field != null)
                {
                    field.SetValue(view, display);
                    return true;
                }
            }
            return false;
        }

        [MenuItem("Aquarium/Select Director", priority = 20)]
        public static void SelectDirector()
        {
            var d = Object.FindAnyObjectByType<AquariumDirector>();
            if (d != null) Selection.activeGameObject = d.gameObject;
            else Debug.LogWarning("[Aquarium] No AquariumDirector in the open scene. Use Aquarium > Create Aquarium Scene.");
        }
    }
}
