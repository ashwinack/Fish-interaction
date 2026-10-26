using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aquarium.Editor
{
    /// <summary>
    /// Adds "Sea fish - underwater world" (Assets/fish) to the aquarium:
    ///  1. converts its Built-in "Standard" materials to URP Lit (keeping transparency, normal maps, smoothness),
    ///  2. makes the swim/idle clips loop and builds an Animator Controller (swim / idle / eat) per fish,
    ///  3. adds the fish as behaviour groups (each school uses one real species),
    ///  4. places the pack's rock column as reef pillars in the tank.
    /// </summary>
    public static class SeaFishPackSetup
    {
        const string PackRoot = "Assets/fish";
        const string FishFolder = "Assets/fish/models/fish";
        const string ControllerFolder = "Assets/Aquarium/Generated/SeaFish";

        // Models whose head points along -Z (all others point along +Z). Filled in from a side-view render.
        static readonly HashSet<string> s_FacingNegativeZ = new HashSet<string>();

        static readonly string[] s_Angelfish = { "french_angelfish", "pomacanthus_imperator", "zanclus_cornutus", "plectorhinchus", "cown_triggerfish" };
        static readonly string[] s_Tangs = { "regal_tang", "zebrasoma", "zebrasoma_2" };
        static readonly string[] s_Butterfly = { "chaetodon_collare", "chelmon_rostratus", "longnose", "coral_beauty", "flame_angel", "fire_mouth_cichilid", "golden_fish" };
        static readonly string[] s_Small = { "lyretail_anthias", "gramma_loreto" };
        static readonly string[] s_Mandarin = { "mandarin_fish" };
        static readonly string[] s_Perch = { "perch" };

        // Species these replace (disabled, not removed).
        static readonly string[] s_Replaced = { "Cartoon Reef Fish", "Cartoon Angelfish", "Cartoon Racer", "Cartoon Squid", "Clownfish", "Blue Tang", "Yellow Tang", "Queen Angelfish" };

        [MenuItem("Aquarium/Add Sea Fish Pack (underwater world)", priority = 31)]
        public static void AddMenu()
        {
            var director = Object.FindAnyObjectByType<AquariumDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Aquarium] Open the aquarium scene first (no AquariumDirector found).");
                return;
            }
            Debug.Log("[Aquarium] " + Apply(director));
        }

        [MenuItem("Aquarium/Add Sea Fish Pack (underwater world)", true)]
        static bool AddValidate() => !Application.isPlaying && AssetDatabase.IsValidFolder(PackRoot);

        public static string Apply(AquariumDirector director)
        {
            int converted = ConvertMaterials();
            int controllers = SetupAnimations();

            Undo.RecordObject(director, "Add Sea Fish Pack");
            var groups = new List<FishSpecies>
            {
                Angelfish(Variants(s_Angelfish)),
                Tangs(Variants(s_Tangs)),
                Butterfly(Variants(s_Butterfly)),
                SmallSchool(Variants(s_Small)),
                Mandarin(Variants(s_Mandarin)),
                Perch(Variants(s_Perch, "Assets/fish/models/perch_old")),
            };
            groups.RemoveAll(g => g.modelVariants.Count == 0);
            foreach (var g in groups)
            {
                director.species.RemoveAll(s => s != null && s.name == g.name);
                director.species.Add(g);
            }
            int disabled = 0;
            foreach (var s in director.species)
                if (s != null && s_Replaced.Contains(s.name) && s.enabled) { s.enabled = false; disabled++; }

            // Reef pillars from the pack's rock column
            var column = AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + "/models/envariment/prefab/column.prefab");
            if (column != null)
            {
                director.environmentProps.RemoveAll(p => p != null && p.prefab == column);
                director.environmentProps.Add(new EnvironmentProp { prefab = column, count = 6, placement = PropPlacement.BehindWalls, minScale = 1.6f, maxScale = 2.6f });
                director.environmentProps.Add(new EnvironmentProp { prefab = column, count = 2, placement = PropPlacement.UnderFloor, minScale = 0.8f, maxScale = 1.1f });
            }

            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            EditorSceneManager.SaveScene(director.gameObject.scene);
            int fish = groups.Sum(g => g.count);
            return $"Added Sea Fish Pack: {groups.Sum(g => g.modelVariants.Count)} species models as {groups.Count} groups ({fish} fish: " +
                   string.Join(", ", groups.Select(g => $"{g.name} x{g.count}")) + $"). Converted {converted} materials to URP, built {controllers} animator controllers, " +
                   $"added rock columns, disabled {disabled} species they replace.";
        }

        // -------------------------------------------------------------------------------------------
        // 1. Materials: Built-in Standard -> URP Lit
        // -------------------------------------------------------------------------------------------
        public static int ConvertMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PackRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) continue;
                string sn = m.shader.name;
                if (sn != "Standard" && sn != "Standard (Specular setup)") continue;

                // Read Built-in values before switching shaders.
                Texture mainTex = m.GetTexture("_MainTex");
                Vector2 scale = m.GetTextureScale("_MainTex"), offset = m.GetTextureOffset("_MainTex");
                Color color = m.GetColor("_Color");
                float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f;
                float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
                Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                float bumpScale = m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;
                Texture metalMap = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
                float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
                float gloss = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
                float glossScale = m.HasProperty("_GlossMapScale") ? m.GetFloat("_GlossMapScale") : 1f;
                float smoothCh = m.HasProperty("_SmoothnessTextureChannel") ? m.GetFloat("_SmoothnessTextureChannel") : 0f;
                Texture occl = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
                float occlStr = m.HasProperty("_OcclusionStrength") ? m.GetFloat("_OcclusionStrength") : 1f;
                Texture emisMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
                Color emis = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                bool emissive = m.IsKeywordEnabled("_EMISSION");

                m.shader = lit;
                m.SetTexture("_BaseMap", mainTex);
                m.SetTextureScale("_BaseMap", scale);
                m.SetTextureOffset("_BaseMap", offset);
                m.SetColor("_BaseColor", color);
                m.SetTexture("_BumpMap", bump);
                m.SetFloat("_BumpScale", bumpScale);
                m.SetTexture("_MetallicGlossMap", metalMap);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", metalMap != null ? glossScale : gloss);
                m.SetFloat("_SmoothnessTextureChannel", smoothCh);
                m.SetTexture("_OcclusionMap", occl);
                m.SetFloat("_OcclusionStrength", occlStr);
                m.SetTexture("_EmissionMap", emisMap);
                m.SetColor("_EmissionColor", emissive ? emis : Color.black);

                // Rendering mode: 0 opaque, 1 cutout, 2 fade, 3 transparent
                m.SetFloat("_Surface", mode >= 2f ? 1f : 0f);
                m.SetFloat("_Blend", 0f); // alpha
                m.SetFloat("_BlendModePreserveSpecular", mode >= 3f ? 1f : 0f);
                m.SetFloat("_AlphaClip", mode == 1f ? 1f : 0f);
                m.SetFloat("_Cutoff", cutoff);
                m.SetFloat("_Cull", 2f); // back faces culled (fins are modelled double-sided where needed)
                BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords, null);
                // Transparent fins draw before the aquarium's water layer (like the procedural fins).
                if (mode >= 2f && path.StartsWith(FishFolder)) m.renderQueue = 2600;
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            return n;
        }

        // -------------------------------------------------------------------------------------------
        // 2. Animation: looping clips and a swim / idle / eat controller per model
        // -------------------------------------------------------------------------------------------
        public static int SetupAnimations()
        {
            Directory.CreateDirectory(ControllerFolder);
            int built = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PackRoot + "/models" }))
            {
                var fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer == null) continue;
                var clipsCfg = (importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations).ToArray();
                if (clipsCfg.Length == 0) continue;

                bool changed = false;
                foreach (var c in clipsCfg)
                {
                    bool shouldLoop = c.name.Contains("swim") || c.name.Contains("idle");
                    if (c.loopTime != shouldLoop) { c.loopTime = shouldLoop; changed = true; }
                }
                if (changed)
                {
                    importer.clipAnimations = clipsCfg;
                    importer.SaveAndReimport();
                }

                var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                AnimationClip swim = clips.FirstOrDefault(c => c.name.Contains("swim"));
                AnimationClip idle = clips.FirstOrDefault(c => c.name.Contains("idle"));
                AnimationClip eat = clips.FirstOrDefault(c => c.name.Contains("eat"));
                if (swim == null) continue;

                string modelName = Path.GetFileNameWithoutExtension(fbxPath);
                string ctrlPath = $"{ControllerFolder}/{modelName}.controller";
                AssetDatabase.DeleteAsset(ctrlPath);
                var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
                var sm = ctrl.layers[0].stateMachine;
                var swimState = sm.AddState("swim");
                swimState.motion = swim;
                sm.defaultState = swimState;
                if (idle != null) sm.AddState("idle").motion = idle;
                if (eat != null)
                {
                    var eatState = sm.AddState("eat");
                    eatState.motion = eat;
                    var back = eatState.AddTransition(swimState);
                    back.hasExitTime = true;
                    back.exitTime = 0.9f;
                    back.duration = 0.2f;
                }

                // Assign the controller to every prefab built from this model.
                foreach (var pguid in AssetDatabase.FindAssets("t:Prefab", new[] { PackRoot + "/models" }))
                {
                    var ppath = AssetDatabase.GUIDToAssetPath(pguid);
                    if (!AssetDatabase.GetDependencies(ppath).Contains(fbxPath)) continue;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ppath);
                    var anim = prefab.GetComponentInChildren<Animator>(true);
                    if (anim == null) continue;
                    anim.runtimeAnimatorController = ctrl;
                    anim.applyRootMotion = false;
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                }
                built++;
            }
            AssetDatabase.SaveAssets();
            return built;
        }

        // -------------------------------------------------------------------------------------------
        // 3. Species groups
        // -------------------------------------------------------------------------------------------
        static List<FishModelVariant> Variants(string[] names, string folder = FishFolder)
        {
            var list = new List<FishModelVariant>();
            foreach (var n in names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/prefab/{n}.prefab");
                if (prefab == null) continue;
                list.Add(new FishModelVariant
                {
                    model = prefab,
                    forward = s_FacingNegativeZ.Contains(n) ? ModelForwardAxis.NegativeZ : ModelForwardAxis.PositiveZ,
                });
            }
            return list;
        }

        static FishSpecies Base(string name, List<FishModelVariant> variants)
        {
            return new FishSpecies
            {
                name = name,
                modelVariants = variants,
                modelAnimationSpeed = 1f,
                modelAnimationState = "swim",
                swimDeformStaticModels = true,
            };
        }

        // Large, graceful angelfish, moorish idols, sweetlips and triggerfish; in small groups, love the glass.
        static FishSpecies Angelfish(List<FishModelVariant> v)
        {
            var s = Base("Reef Angelfish", v);
            s.count = v.Count * 3; s.schoolSize = 3;
            s.length = 0.34f; s.lengthVariance = 0.12f;
            s.modelAnimationSpeed = 0.8f;
            s.turnRate = 110f; s.burstCoast = 0.3f;
            s.cruiseSpeed = 0.5f; s.burstSpeed = 3f; s.maxAcceleration = 3f; s.banking = 12f;
            s.neighbourRadius = 2f; s.separationDistance = 0.6f; s.alignmentWeight = 0.4f; s.cohesionWeight = 0.5f;
            s.migrationWeight = 0f; s.wanderWeight = 1f;
            s.depthRange = new Vector2(-4.5f, 3f); s.homeHalfLength = 12f;
            s.glassAffinity = 0.85f; s.preferredGlassDistance = 1.5f; s.curiosity = 1f; s.fleeStrength = 10f;
            return s;
        }

        // Tangs school and cruise along the tunnel.
        static FishSpecies Tangs(List<FishModelVariant> v)
        {
            var s = Base("Reef Tangs", v);
            s.count = v.Count * 10; s.schoolSize = 10;
            s.length = 0.28f; s.lengthVariance = 0.12f;
            s.turnRate = 170f; s.burstCoast = 0.2f;
            s.cruiseSpeed = 0.9f; s.burstSpeed = 4.5f; s.maxAcceleration = 4.5f;
            s.neighbourRadius = 1.8f; s.separationDistance = 0.45f; s.alignmentWeight = 1.3f; s.cohesionWeight = 0.9f;
            s.migrationWeight = 0.45f; s.wanderWeight = 0.4f;
            s.depthRange = new Vector2(-4f, 3.5f);
            s.glassAffinity = 0.65f; s.preferredGlassDistance = 2f; s.curiosity = 0.9f; s.fleeStrength = 12f;
            return s;
        }

        // Butterflyfish, dwarf angels and friends: pairs and small groups around the reef, very curious.
        static FishSpecies Butterfly(List<FishModelVariant> v)
        {
            var s = Base("Butterflyfish", v);
            s.count = v.Count * 4; s.schoolSize = 4;
            s.length = 0.2f; s.lengthVariance = 0.15f;
            s.turnRate = 190f; s.burstCoast = 0.4f;
            s.cruiseSpeed = 0.6f; s.burstSpeed = 3.8f; s.maxAcceleration = 4f;
            s.neighbourRadius = 1.4f; s.separationDistance = 0.35f; s.alignmentWeight = 0.7f; s.cohesionWeight = 0.8f;
            s.migrationWeight = 0f; s.wanderWeight = 0.8f;
            s.depthRange = new Vector2(-5f, 2.5f); s.homeHalfLength = 14f;
            s.glassAffinity = 0.75f; s.preferredGlassDistance = 1.6f; s.curiosity = 0.95f; s.fleeStrength = 12f;
            return s;
        }

        // Anthias and royal grammas: small, bright schools low over the reef (seen well through the floor).
        static FishSpecies SmallSchool(List<FishModelVariant> v)
        {
            var s = Base("Anthias & Grammas", v);
            s.count = v.Count * 16; s.schoolSize = 16;
            s.length = 0.13f; s.lengthVariance = 0.15f;
            s.modelAnimationSpeed = 1.2f;
            s.turnRate = 230f; s.burstCoast = 0.3f;
            s.cruiseSpeed = 0.6f; s.burstSpeed = 3.5f; s.maxAcceleration = 5f;
            s.neighbourRadius = 1.2f; s.separationDistance = 0.22f; s.alignmentWeight = 1.2f; s.cohesionWeight = 1.1f;
            s.migrationWeight = 0f; s.wanderWeight = 0.6f;
            s.depthRange = new Vector2(-5.2f, -1f); s.homeHalfLength = 10f; s.homeMaxX = 7f;
            s.glassAffinity = 0f; s.curiosity = 0.8f; s.fleeStrength = 13f;
            return s;
        }

        // Mandarin fish hover slowly just above the seabed.
        static FishSpecies Mandarin(List<FishModelVariant> v)
        {
            var s = Base("Mandarin Fish", v);
            s.count = 6; s.schoolSize = 1;
            s.length = 0.12f; s.lengthVariance = 0.1f;
            s.modelAnimationSpeed = 0.7f;
            s.turnRate = 120f; s.burstCoast = 0.7f; s.burstCoastRate = 0.5f;
            s.cruiseSpeed = 0.25f; s.burstSpeed = 2f; s.maxAcceleration = 2f;
            s.neighbourRadius = 1f; s.separationDistance = 0.5f; s.alignmentWeight = 0f; s.cohesionWeight = 0f;
            s.migrationWeight = 0f; s.wanderWeight = 1f;
            s.depthRange = new Vector2(-5.8f, -4.5f); s.homeHalfLength = 7f; s.homeMaxX = 5f;
            s.glassAffinity = 0f; s.curiosity = 0.7f; s.fleeStrength = 8f;
            return s;
        }

        // Perch: a tight silver-green school in open water.
        static FishSpecies Perch(List<FishModelVariant> v)
        {
            var s = Base("Perch", v);
            s.count = 24; s.schoolSize = 12;
            s.length = 0.22f; s.lengthVariance = 0.12f;
            s.turnRate = 200f; s.burstCoast = 0.1f;
            s.cruiseSpeed = 1.1f; s.burstSpeed = 4.5f; s.maxAcceleration = 5.5f;
            s.neighbourRadius = 1.6f; s.separationDistance = 0.3f; s.alignmentWeight = 1.8f; s.cohesionWeight = 1.3f;
            s.migrationWeight = 0.5f; s.wanderWeight = 0.3f;
            s.depthRange = new Vector2(-3f, 5f);
            s.glassAffinity = 0.4f; s.preferredGlassDistance = 3f; s.curiosity = 0.4f; s.fleeStrength = 14f;
            return s;
        }
    }
}
