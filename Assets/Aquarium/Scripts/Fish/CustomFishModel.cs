using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>
    /// Lets any fish model (e.g. from the Unity Asset Store) replace a procedural species.
    /// The model is normalised to unit length along +Z (the fish transform is then scaled to the species length):
    ///  - Rigged / animated models (Animator, Animation or SkinnedMeshRenderer) keep their own animation and
    ///    materials; animation speed follows swimming speed. Built-in "Standard" materials are converted to URP Lit.
    ///  - Static meshes are baked into one mesh and rendered with the aquarium fish shader using the model's
    ///    texture, so they get the same body-wave swimming, bending and escape animation as procedural fish.
    /// </summary>
    public static class CustomFishModel
    {
        public sealed class Template
        {
            public GameObject Prefab;
            public bool Animated;
            public Quaternion Rotation;
            public Vector3 Center;     // after rotation, in model units
            public float Scale;        // 1 / model length
            public Mesh BakedMesh;     // static models only
            public Material[] BakedMaterials;
            public readonly List<Object> Owned = new List<Object>();
        }

        public sealed class Instance
        {
            public GameObject Root;
            public Animator Animator;
            public Animation Legacy;
            public Renderer[] Renderers;
            public bool UsesSwimShader;
        }

        static readonly Dictionary<Material, Material> s_Converted = new Dictionary<Material, Material>();

        public static Quaternion ForwardRotation(FishSpecies s) => ForwardRotation(s.modelForward, s.modelRotationOffset);

        public static Quaternion ForwardRotation(ModelForwardAxis forward, Vector3 rotationOffset)
        {
            Quaternion axis;
            switch (forward)
            {
                case ModelForwardAxis.NegativeZ: axis = Quaternion.Euler(0f, 180f, 0f); break;
                case ModelForwardAxis.PositiveX: axis = Quaternion.Euler(0f, -90f, 0f); break;
                case ModelForwardAxis.NegativeX: axis = Quaternion.Euler(0f, 90f, 0f); break;
                case ModelForwardAxis.PositiveY: axis = Quaternion.Euler(90f, 0f, 0f); break;
                case ModelForwardAxis.NegativeY: axis = Quaternion.Euler(-90f, 0f, 0f); break;
                default: axis = Quaternion.identity; break;
            }
            return Quaternion.Euler(rotationOffset) * axis;
        }

        public static Template Prepare(FishSpecies s, Material swimMaterialTemplate)
        {
            return s.customModel == null ? null : Prepare(s, s.customModel, ForwardRotation(s), swimMaterialTemplate);
        }

        /// <summary>All looks of a species: its model variants, or its single custom model.</summary>
        public static List<Template> PrepareAll(FishSpecies s, Material swimMaterialTemplate)
        {
            var list = new List<Template>();
            if (s.modelVariants != null && s.modelVariants.Count > 0)
            {
                foreach (var v in s.modelVariants)
                {
                    if (v == null || v.model == null) continue;
                    var t = Prepare(s, v.model, ForwardRotation(v.forward, v.rotationOffset + s.modelRotationOffset), swimMaterialTemplate);
                    if (t != null) list.Add(t);
                }
            }
            else if (s.customModel != null)
            {
                var t = Prepare(s, swimMaterialTemplate);
                if (t != null) list.Add(t);
            }
            return list;
        }

        public static Template Prepare(FishSpecies s, GameObject model, Quaternion rotation, Material swimMaterialTemplate)
        {
            var t = new Template { Prefab = model, Rotation = rotation };

            // Measure the model in "fish space" (head along +Z).
            var pivot = new GameObject("Measure " + s.name);
            var inst = Object.Instantiate(model, pivot.transform, false);
            inst.transform.localRotation = t.Rotation;
            inst.transform.localPosition = Vector3.zero;
            var renderers = inst.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[Aquarium] Model '{model.name}' for '{s.name}' has no renderers; skipped.");
                Object.DestroyImmediate(pivot);
                return null;
            }
            var bounds = new Bounds();
            bool first = true;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            float length = Mathf.Max(bounds.size.z, 1e-4f);
            t.Scale = 1f / length;
            t.Center = bounds.center;
            t.Animated = inst.GetComponentInChildren<Animator>(true) != null
                         || inst.GetComponentInChildren<Animation>(true) != null
                         || inst.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

            if (bounds.size.x > length * 1.5f || bounds.size.y > length * 1.5f)
                Debug.LogWarning($"[Aquarium] Model '{model.name}' for '{s.name}' is longer across than along its forward axis - check 'Forward' (size {bounds.size}).");

            if (!t.Animated && s.swimDeformStaticModels)
                Bake(t, inst, s, swimMaterialTemplate);

            Object.DestroyImmediate(pivot);
            return t;
        }

        /// <summary>Combine all static meshes into one normalised mesh and give it swim-shader materials.</summary>
        static void Bake(Template t, GameObject inst, FishSpecies s, Material template)
        {
            var filters = inst.GetComponentsInChildren<MeshFilter>(true);
            var combine = new List<CombineInstance>();
            var materials = new List<Material>();
            Matrix4x4 norm = Matrix4x4.Scale(Vector3.one * t.Scale) * Matrix4x4.Translate(-t.Center);
            foreach (var mf in filters)
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                {
                    combine.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sub, transform = norm * mf.transform.localToWorldMatrix });
                    var src = sub < mr.sharedMaterials.Length ? mr.sharedMaterials[sub] : null;
                    var m = new Material(template) { name = s.name + " (imported)" };
                    Texture tex = GetMainTexture(src);
                    if (tex != null) m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseTint", GetMainColor(src));
                    m.SetFloat("_UseBaseMap", 1f);
                    m.SetFloat("_EyeRadius", 0f);
                    materials.Add(m);
                    t.Owned.Add(m);
                }
            }
            if (combine.Count == 0) return;
            var mesh = new Mesh { name = s.name + " (imported, normalised)" };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(combine.ToArray(), false, true);
            mesh.RecalculateBounds();
            t.BakedMesh = mesh;
            t.BakedMaterials = materials.ToArray();
            t.Owned.Add(mesh);
        }

        /// <summary>Create one fish body under the given parent (the parent is the moving fish transform).</summary>
        public static Instance Create(Template t, Transform parent, int layer)
        {
            var result = new Instance();
            if (t.BakedMesh != null)
            {
                var go = new GameObject("Model");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = t.BakedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = t.BakedMaterials;
                mr.shadowCastingMode = ShadowCastingMode.On;
                result.Root = go;
                result.Renderers = new Renderer[] { mr };
                result.UsesSwimShader = true;
                return result;
            }

            // Animated (or not baked): normalising pivot -> model instance
            var pivot = new GameObject("Model");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localScale = Vector3.one * t.Scale;
            pivot.transform.localPosition = -t.Center * t.Scale;
            var model = Object.Instantiate(t.Prefab, pivot.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = t.Rotation;
            foreach (var tr in model.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
            result.Root = pivot;
            result.Renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (var r in result.Renderers) FixMaterials(r);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = false;
            result.Animator = model.GetComponentInChildren<Animator>(true);
            result.Legacy = model.GetComponentInChildren<Animation>(true);
            if (result.Animator != null) result.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return result;
        }

        /// <summary>Built-in pipeline materials render pink in URP: swap them for URP Lit copies.</summary>
        public static void FixMaterials(Renderer r)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null) continue;
                string n = m.shader.name;
                bool builtin = n == "Standard" || n == "Standard (Specular setup)" || n.StartsWith("Legacy Shaders/") || n.StartsWith("Mobile/") || !m.shader.isSupported;
                if (!builtin) continue;
                if (!s_Converted.TryGetValue(m, out var urp))
                {
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit == null) continue;
                    urp = new Material(lit) { name = m.name + " (URP)" };
                    var tex = GetMainTexture(m);
                    if (tex != null) urp.SetTexture("_BaseMap", tex);
                    urp.SetColor("_BaseColor", GetMainColor(m));
                    if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null)
                    {
                        urp.SetTexture("_BumpMap", m.GetTexture("_BumpMap"));
                        urp.EnableKeyword("_NORMALMAP");
                    }
                    if (m.HasProperty("_Glossiness")) urp.SetFloat("_Smoothness", m.GetFloat("_Glossiness"));
                    s_Converted[m] = urp;
                }
                mats[i] = urp;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }

        static Texture GetMainTexture(Material m)
        {
            if (m == null) return null;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) return m.GetTexture("_MainTex");
            return null;
        }

        static Color GetMainColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }
    }
}
