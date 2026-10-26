using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aquarium.Editor
{
    /// <summary>
    /// Adds the 28 fish of "Cartoon Fish Pack 2" to the aquarium as four behaviour groups (round reef fish,
    /// tall angelfish-style fish, slender racers and a squid). The models are static meshes, so they get the
    /// aquarium swim shader (body wave, turn bending, escape tail beats) with the pack's texture.
    /// </summary>
    public static class CartoonFishPackSetup
    {
        const string PrefabPrefix = "Cartoon_fish_Pack_2_1_";

        // Models whose head points along +X (all others point along -X), found from a side-view contact sheet.
        static readonly HashSet<int> s_FacingPositiveX = new HashSet<int> { 2, 3, 4, 7, 13, 14, 18 };

        static readonly int[] s_Round = { 1, 2, 3, 4, 5, 6, 7, 10, 12, 14 };
        static readonly int[] s_Disc = { 13, 16, 17, 21, 22, 24, 26, 27, 28 };
        static readonly int[] s_Slender = { 8, 9, 11, 15, 18, 19, 20, 25 };
        static readonly int[] s_Squid = { 23 };

        // Procedural species these fish replace (they are disabled, not removed).
        static readonly string[] s_Replaced = { "Clownfish", "Blue Tang", "Yellow Tang", "Queen Angelfish" };

        [MenuItem("Aquarium/Add Cartoon Fish Pack 2", priority = 30)]
        public static void AddMenu()
        {
            var director = Object.FindAnyObjectByType<AquariumDirector>();
            if (director == null)
            {
                Debug.LogWarning("[Aquarium] Open the aquarium scene first (no AquariumDirector found).");
                return;
            }
            string report = Apply(director);
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            EditorSceneManager.SaveScene(director.gameObject.scene);
            Debug.Log("[Aquarium] " + report);
        }

        [MenuItem("Aquarium/Add Cartoon Fish Pack 2", true)]
        static bool AddValidate() => !Application.isPlaying && FindPrefab(1) != null;

        public static string Apply(AquariumDirector director)
        {
            Undo.RecordObject(director, "Add Cartoon Fish Pack 2");
            var groups = new List<FishSpecies>
            {
                Round(Variants(s_Round)),
                Disc(Variants(s_Disc)),
                Slender(Variants(s_Slender)),
                Squid(Variants(s_Squid)),
            };

            int modelCount = groups.Sum(g => g.modelVariants.Count);
            if (modelCount == 0) return "Cartoon Fish Pack 2 prefabs not found.";

            foreach (var g in groups)
            {
                director.species.RemoveAll(s => s != null && s.name == g.name);
                director.species.Add(g);
            }
            int disabled = 0;
            foreach (var s in director.species)
            {
                if (s != null && s_Replaced.Contains(s.name) && s.enabled)
                {
                    s.enabled = false;
                    disabled++;
                }
            }
            EditorUtility.SetDirty(director);
            return $"Added Cartoon Fish Pack 2: {modelCount} models in {groups.Count} species " +
                   $"({string.Join(", ", groups.Select(g => $"{g.name} x{g.count}"))}). Disabled {disabled} procedural reef species they replace.";
        }

        static GameObject FindPrefab(int n)
        {
            string name = PrefabPrefix + n;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        static List<FishModelVariant> Variants(int[] numbers)
        {
            var list = new List<FishModelVariant>();
            foreach (int n in numbers)
            {
                var prefab = FindPrefab(n);
                if (prefab == null) continue;
                list.Add(new FishModelVariant
                {
                    model = prefab,
                    forward = s_FacingPositiveX.Contains(n) ? ModelForwardAxis.PositiveX : ModelForwardAxis.NegativeX,
                });
            }
            return list;
        }

        // ---------------------------------------------------------------------------------------------
        // Behaviour groups. Lengths are before the global Fish Size Multiplier (default 2x).
        // ---------------------------------------------------------------------------------------------
        static FishSpecies Base(string name, List<FishModelVariant> variants)
        {
            return new FishSpecies
            {
                name = name,
                modelVariants = variants,
                swimDeformStaticModels = true,
                smoothness = 0.55f,
                sheen = 0.1f,
                metallic = 0f,
                finOpacity = 1f,
            };
        }

        // Round, colourful reef fish in small schools; very curious, stay near the glass.
        static FishSpecies Round(List<FishModelVariant> v)
        {
            var s = Base("Cartoon Reef Fish", v);
            s.count = Mathf.Max(v.Count, 1) * 6; s.schoolSize = 6;
            s.length = 0.24f; s.lengthVariance = 0.15f;
            s.swimFrequency = 3.5f; s.swimAmplitude = 0.09f; s.bodyWaves = 0.8f;
            s.turnRate = 190f; s.burstCoast = 0.55f; s.burstCoastRate = 0.8f;
            s.cruiseSpeed = 0.7f; s.burstSpeed = 4f; s.maxAcceleration = 4.5f; s.banking = 10f;
            s.neighbourRadius = 1.6f; s.separationDistance = 0.35f; s.separationWeight = 2f;
            s.alignmentWeight = 1f; s.cohesionWeight = 0.9f; s.migrationWeight = 0f; s.wanderWeight = 0.6f;
            s.depthRange = new Vector2(-4.5f, 3.5f); s.homeHalfLength = 14f;
            s.glassAffinity = 0.7f; s.preferredGlassDistance = 1.8f;
            s.curiosity = 0.95f; s.fleeStrength = 12f;
            return s;
        }

        // Tall, disc-shaped angelfish / tang style: slow, graceful, in pairs; love coming to the glass.
        static FishSpecies Disc(List<FishModelVariant> v)
        {
            var s = Base("Cartoon Angelfish", v);
            s.count = Mathf.Max(v.Count, 1) * 3; s.schoolSize = 3;
            s.length = 0.32f; s.lengthVariance = 0.15f;
            s.swimFrequency = 2.2f; s.swimAmplitude = 0.06f; s.bodyWaves = 0.7f;
            s.turnRate = 110f; s.burstCoast = 0.6f; s.burstCoastRate = 0.5f;
            s.cruiseSpeed = 0.5f; s.burstSpeed = 3f; s.maxAcceleration = 3f; s.banking = 12f;
            s.neighbourRadius = 2f; s.separationDistance = 0.6f; s.separationWeight = 2f;
            s.alignmentWeight = 0.4f; s.cohesionWeight = 0.5f; s.migrationWeight = 0f; s.wanderWeight = 1f;
            s.depthRange = new Vector2(-4.5f, 3f); s.homeHalfLength = 12f;
            s.glassAffinity = 0.85f; s.preferredGlassDistance = 1.5f;
            s.curiosity = 1f; s.fleeStrength = 10f;
            return s;
        }

        // Slender, fast fish that cruise along the tunnel in tight schools.
        static FishSpecies Slender(List<FishModelVariant> v)
        {
            var s = Base("Cartoon Racer", v);
            s.count = Mathf.Max(v.Count, 1) * 6; s.schoolSize = 6;
            s.length = 0.3f; s.lengthVariance = 0.12f;
            s.swimFrequency = 4f; s.swimAmplitude = 0.11f; s.bodyWaves = 0.9f;
            s.turnRate = 200f; s.burstCoast = 0.1f;
            s.cruiseSpeed = 1.2f; s.burstSpeed = 5f; s.maxAcceleration = 6f; s.banking = 8f;
            s.neighbourRadius = 1.8f; s.separationDistance = 0.4f; s.separationWeight = 2.5f;
            s.alignmentWeight = 1.8f; s.cohesionWeight = 1.2f; s.migrationWeight = 0.5f; s.wanderWeight = 0.3f;
            s.depthRange = new Vector2(-3.5f, 5f);
            s.glassAffinity = 0.5f; s.preferredGlassDistance = 2.5f;
            s.curiosity = 0.5f; s.fleeStrength = 14f;
            return s;
        }

        // Squid: barely any body wave, moves in jet-like kick-and-glide pulses.
        static FishSpecies Squid(List<FishModelVariant> v)
        {
            var s = Base("Cartoon Squid", v);
            s.count = 5; s.schoolSize = 1;
            s.length = 0.35f; s.lengthVariance = 0.1f;
            s.swimFrequency = 1f; s.swimAmplitude = 0.02f; s.bodyWaves = 0.5f;
            s.turnRate = 90f; s.burstCoast = 1f; s.burstCoastRate = 0.8f;
            s.cruiseSpeed = 0.6f; s.burstSpeed = 3.5f; s.maxAcceleration = 3f; s.banking = 4f;
            s.neighbourRadius = 2f; s.separationDistance = 1f;
            s.alignmentWeight = 0f; s.cohesionWeight = 0f; s.migrationWeight = 0f; s.wanderWeight = 0.8f;
            s.depthRange = new Vector2(-4f, 3f); s.homeHalfLength = 12f;
            s.glassAffinity = 0.6f; s.preferredGlassDistance = 2f;
            s.curiosity = 0.6f; s.fleeStrength = 10f;
            return s;
        }
    }
}
