using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium
{
    public enum FishBodyType { Fish, Ray }

    /// <summary>Which local axis of an imported model points toward the fish's head.</summary>
    public enum ModelForwardAxis { PositiveZ, NegativeZ, PositiveX, NegativeX, PositiveY, NegativeY }

    /// <summary>One look for a species. Each school picks one variant, so similar-looking fish swim together.</summary>
    [Serializable]
    public class FishModelVariant
    {
        public GameObject model;
        [Tooltip("Which axis of this model points toward the head.")]
        public ModelForwardAxis forward = ModelForwardAxis.PositiveZ;
        [Tooltip("Extra rotation (degrees) for this model.")]
        public Vector3 rotationOffset;
    }

    /// <summary>
    /// Everything that defines one species: procedural body shape, colouring, animation and behaviour.
    /// Lengths are relative to body length unless noted; world sizes are in metres.
    /// </summary>
    [Serializable]
    public class FishSpecies
    {
        public string name = "Fish";
        public bool enabled = true;
        [Min(0)] public int count = 20;
        [Tooltip("Fish per school. Fish only align with and cohere to their own school.")]
        [Min(1)] public int schoolSize = 10;

        [Header("Imported 3D model (optional, e.g. from the Asset Store)")]
        [Tooltip("Fish prefab or model that replaces the procedural fish. It is scaled to 'length' automatically. " +
                 "Rigged/animated models keep their own animation (sped up with swimming speed); static meshes get the aquarium swim deformation.")]
        public GameObject customModel;
        [Tooltip("Which axis of the model points toward the head.")]
        public ModelForwardAxis modelForward = ModelForwardAxis.PositiveZ;
        [Tooltip("Extra rotation (degrees) if the model is still not facing forward / upright.")]
        public Vector3 modelRotationOffset;
        [Tooltip("Animator / Animation playback speed at cruise speed.")]
        public float modelAnimationSpeed = 1f;
        [Tooltip("Optional Animator state to play (e.g. \"Swim\"). Empty = default state.")]
        public string modelAnimationState = "";
        [Tooltip("For static (non-animated) meshes: apply the aquarium body-wave swim shader using the model's texture.")]
        public bool swimDeformStaticModels = true;
        [Tooltip("Several looks for this species (used instead of Custom Model when not empty). Schools cycle through them.")]
        public List<FishModelVariant> modelVariants = new List<FishModelVariant>();

        [Header("Body (relative to length)")]
        public FishBodyType bodyType = FishBodyType.Fish;
        [Tooltip("Body length in metres.")]
        public float length = 0.25f;
        [Range(0f, 0.5f)] public float lengthVariance = 0.15f;
        public float bodyHeight = 0.3f;
        public float bodyWidth = 0.14f;
        [Range(0.15f, 0.7f)] public float thickestPoint = 0.38f;
        [Range(0.02f, 0.5f)] public float peduncle = 0.15f;
        [Range(0f, 1f)] public float noseSharpness = 0.2f;
        [Range(0.05f, 0.4f)] public float tailLength = 0.2f;
        public float tailHeight = 0.25f;
        [Range(0f, 1f)] public float tailFork = 0.4f;
        [Range(-0.6f, 0.6f)] public float tailAsymmetry = 0f;
        public float dorsalHeight = 0.1f;
        public Vector2 dorsalRange = new Vector2(0.3f, 0.65f);
        public float analHeight = 0.06f;
        public float pectoralSize = 0.08f;
        [Tooltip("Rays only: wing span relative to body length.")]
        public float raySpan = 2.2f;

        [Header("Colour")]
        public Color topColor = new Color(0.2f, 0.3f, 0.5f);
        public Color bellyColor = new Color(0.9f, 0.9f, 0.9f);
        public Color finColor = new Color(0.7f, 0.7f, 0.75f);
        public Color tailColor = new Color(0.7f, 0.7f, 0.75f);
        [Range(0f, 1f)] public float tailColorStart = 0f;
        public Color stripeColor = Color.white;
        [Min(0)] public int stripeCount = 0;
        [Range(0.01f, 0.45f)] public float stripeWidth = 0.08f;
        public float stripeOffset = 0f;
        [Range(0f, 1f)] public float stripeStrength = 1f;
        [Range(0f, 1f)] public float metallic = 0f;
        [Range(0f, 1f)] public float smoothness = 0.6f;
        [Range(0f, 1f)] public float sheen = 0.2f;

        [Header("Swimming animation")]
        [Tooltip("Tail beats (or wing flaps) per second at cruise speed.")]
        public float swimFrequency = 3f;
        public float swimAmplitude = 0.1f;
        public float bodyWaves = 0.9f;

        [Header("Natural motion")]
        [Tooltip("Maximum turning speed while cruising (degrees per second). Escapes turn up to 4x faster.")]
        public float turnRate = 160f;
        [Tooltip("Burst-and-coast swimming: 0 = steady tail beat, 1 = strong kick-and-glide rhythm (reef fish).")]
        [Range(0f, 1f)] public float burstCoast = 0.4f;
        [Tooltip("Kick-and-glide cycles per second.")]
        public float burstCoastRate = 0.7f;
        [Range(0f, 1f)] public float spots = 0f;
        [Range(0f, 1f)] public float finOpacity = 0.8f;

        [Header("Movement")]
        public float cruiseSpeed = 0.8f;
        public float burstSpeed = 4f;
        public float maxAcceleration = 4f;
        [Tooltip("Degrees of banking per (radian/second) of turning.")]
        public float banking = 12f;

        [Header("Schooling")]
        public float neighbourRadius = 1.5f;
        public float separationDistance = 0.3f;
        public float separationWeight = 2f;
        public float alignmentWeight = 1f;
        public float cohesionWeight = 0.8f;
        [Tooltip("Pull toward swimming along the tunnel. 0 = no migration (fish roam a home area).")]
        public float migrationWeight = 0f;
        public float wanderWeight = 0.6f;

        [Header("Habitat")]
        [Tooltip("Preferred height band in world metres (floor screen is at 0).")]
        public Vector2 depthRange = new Vector2(-4f, 3f);
        [Tooltip("Non-migrating fish stay within this distance of z = 0.")]
        public float homeHalfLength = 12f;
        [Tooltip("Non-migrating fish stay within this |x|. 0 = whole tank.")]
        public float homeMaxX = 0f;
        [Tooltip("How strongly fish stay close to the wall glass, where visitors can see them up close.")]
        [Range(0f, 1f)] public float glassAffinity = 0.5f;
        [Tooltip("Preferred distance (m) behind the wall glass.")]
        public float preferredGlassDistance = 2.5f;

        [Header("Curiosity")]
        [Tooltip("How readily the fish come to a finger held still on the glass, follow a slow finger and come to feed.")]
        [Range(0f, 1f)] public float curiosity = 0.6f;

        [Header("Reactions")]
        public float fleeRadiusMultiplier = 1f;
        public float fleeStrength = 12f;
        [Tooltip("How much the fluid current carries the fish.")]
        [Range(0f, 1.5f)] public float currentInfluence = 0.8f;
        [Tooltip("Smaller fish keep clear of predators.")]
        public bool isPredator = false;
        [Tooltip("Large animals push water around them (feeds the fluid solver).")]
        public bool stirsWater = false;

        public static List<FishSpecies> CreateDefaultSet()
        {
            return new List<FishSpecies>
            {
                new FishSpecies
                {
                    name = "Silver Sardine", count = 240, schoolSize = 60, curiosity = 0.3f, glassAffinity = 0.45f, preferredGlassDistance = 3f, turnRate = 220f, burstCoast = 0f, finOpacity = 0.55f,
                    length = 0.2f, lengthVariance = 0.15f, bodyHeight = 0.2f, bodyWidth = 0.12f, thickestPoint = 0.35f,
                    peduncle = 0.1f, noseSharpness = 0.5f, tailLength = 0.22f, tailHeight = 0.2f, tailFork = 0.7f,
                    dorsalHeight = 0.07f, dorsalRange = new Vector2(0.4f, 0.55f), analHeight = 0.03f, pectoralSize = 0.06f,
                    topColor = new Color(0.2f, 0.36f, 0.5f), bellyColor = new Color(0.9f, 0.93f, 0.96f),
                    finColor = new Color(0.55f, 0.62f, 0.68f), tailColor = new Color(0.4f, 0.5f, 0.58f), tailColorStart = 0.2f,
                    metallic = 0.3f, smoothness = 0.88f, sheen = 0.6f,
                    swimFrequency = 4.5f, swimAmplitude = 0.12f, bodyWaves = 0.9f,
                    cruiseSpeed = 1.5f, burstSpeed = 5.5f, maxAcceleration = 7f, banking = 8f,
                    neighbourRadius = 1.8f, separationDistance = 0.3f, separationWeight = 3f, alignmentWeight = 2f,
                    cohesionWeight = 1.4f, migrationWeight = 0.6f, wanderWeight = 0.25f,
                    depthRange = new Vector2(-3.5f, 5.5f), fleeRadiusMultiplier = 1.25f, fleeStrength = 16f, currentInfluence = 0.8f,
                },
                new FishSpecies
                {
                    name = "Clownfish", count = 24, schoolSize = 4, curiosity = 0.85f, glassAffinity = 0f, turnRate = 200f, burstCoast = 0.65f, burstCoastRate = 0.9f, finOpacity = 0.85f,
                    length = 0.13f, lengthVariance = 0.2f, bodyHeight = 0.36f, bodyWidth = 0.18f, thickestPoint = 0.35f,
                    peduncle = 0.2f, noseSharpness = 0f, tailLength = 0.2f, tailHeight = 0.24f, tailFork = 0.05f,
                    dorsalHeight = 0.12f, dorsalRange = new Vector2(0.3f, 0.78f), analHeight = 0.08f, pectoralSize = 0.09f,
                    topColor = new Color(1f, 0.38f, 0.04f), bellyColor = new Color(1f, 0.52f, 0.14f),
                    finColor = new Color(1f, 0.42f, 0.06f), tailColor = new Color(1f, 0.42f, 0.06f),
                    stripeColor = new Color(1f, 1f, 1f), stripeCount = 3, stripeWidth = 0.075f, stripeOffset = -0.1f, stripeStrength = 1f,
                    smoothness = 0.45f, sheen = 0.1f,
                    swimFrequency = 5f, swimAmplitude = 0.1f,
                    cruiseSpeed = 0.45f, burstSpeed = 3.2f, maxAcceleration = 4f,
                    neighbourRadius = 1.2f, separationDistance = 0.25f, separationWeight = 2f, alignmentWeight = 0.5f,
                    cohesionWeight = 1f, wanderWeight = 1f,
                    depthRange = new Vector2(-5.4f, -1.2f), homeHalfLength = 7f, homeMaxX = 6f,
                    fleeRadiusMultiplier = 0.9f, fleeStrength = 12f, currentInfluence = 1f,
                },
                new FishSpecies
                {
                    name = "Blue Tang", count = 40, schoolSize = 10, curiosity = 0.9f, glassAffinity = 0.65f, preferredGlassDistance = 2f, turnRate = 170f, burstCoast = 0.45f, finOpacity = 0.9f,
                    length = 0.3f, bodyHeight = 0.5f, bodyWidth = 0.12f, thickestPoint = 0.42f,
                    peduncle = 0.14f, noseSharpness = 0.1f, tailLength = 0.2f, tailHeight = 0.34f, tailFork = 0.3f,
                    dorsalHeight = 0.09f, dorsalRange = new Vector2(0.18f, 0.88f), analHeight = 0.08f, pectoralSize = 0.1f,
                    topColor = new Color(0.04f, 0.22f, 0.85f), bellyColor = new Color(0.1f, 0.42f, 0.95f),
                    finColor = new Color(0.03f, 0.08f, 0.35f), tailColor = new Color(1f, 0.82f, 0.08f), tailColorStart = 0.22f,
                    stripeColor = new Color(0.02f, 0.03f, 0.12f), stripeCount = 1, stripeWidth = 0.16f, stripeOffset = 0.05f, stripeStrength = 0.75f,
                    smoothness = 0.45f, sheen = 0.3f,
                    swimFrequency = 3f, swimAmplitude = 0.08f,
                    cruiseSpeed = 0.9f, burstSpeed = 4.5f, maxAcceleration = 4.5f,
                    neighbourRadius = 1.8f, separationDistance = 0.45f, alignmentWeight = 1.2f, cohesionWeight = 0.8f,
                    migrationWeight = 0.45f, wanderWeight = 0.5f,
                    depthRange = new Vector2(-4f, 3.5f), fleeStrength = 12f,
                },
                new FishSpecies
                {
                    name = "Yellow Tang", count = 30, schoolSize = 6, curiosity = 0.9f, glassAffinity = 0.7f, preferredGlassDistance = 1.8f, turnRate = 170f, burstCoast = 0.55f, finOpacity = 0.8f,
                    length = 0.22f, bodyHeight = 0.62f, bodyWidth = 0.1f, thickestPoint = 0.42f,
                    peduncle = 0.15f, noseSharpness = 0.35f, tailLength = 0.18f, tailHeight = 0.3f, tailFork = 0.1f,
                    dorsalHeight = 0.14f, dorsalRange = new Vector2(0.2f, 0.85f), analHeight = 0.12f, pectoralSize = 0.08f,
                    topColor = new Color(1f, 0.82f, 0.03f), bellyColor = new Color(1f, 0.9f, 0.2f),
                    finColor = new Color(1f, 0.8f, 0.05f), tailColor = new Color(1f, 0.85f, 0.1f),
                    smoothness = 0.4f, sheen = 0.15f,
                    swimFrequency = 2.8f, swimAmplitude = 0.08f,
                    cruiseSpeed = 0.7f, burstSpeed = 4f,
                    neighbourRadius = 1.4f, separationDistance = 0.35f, alignmentWeight = 0.9f, cohesionWeight = 0.7f,
                    wanderWeight = 0.8f, depthRange = new Vector2(-4.5f, 2f), homeHalfLength = 11f, homeMaxX = 8f,
                },
                new FishSpecies
                {
                    name = "Queen Angelfish", count = 12, schoolSize = 2, curiosity = 1f, glassAffinity = 0.8f, preferredGlassDistance = 1.6f, turnRate = 110f, burstCoast = 0.6f, burstCoastRate = 0.5f, finOpacity = 0.85f, spots = 0.25f,
                    length = 0.35f, bodyHeight = 0.72f, bodyWidth = 0.1f, thickestPoint = 0.45f,
                    peduncle = 0.15f, noseSharpness = 0.2f, tailLength = 0.16f, tailHeight = 0.32f, tailFork = 0f,
                    dorsalHeight = 0.32f, dorsalRange = new Vector2(0.25f, 0.9f), analHeight = 0.28f, pectoralSize = 0.1f,
                    topColor = new Color(0.08f, 0.42f, 0.75f), bellyColor = new Color(0.95f, 0.78f, 0.1f),
                    finColor = new Color(0.95f, 0.72f, 0.05f), tailColor = new Color(1f, 0.8f, 0.05f), tailColorStart = 0.17f,
                    smoothness = 0.45f, sheen = 0.35f,
                    swimFrequency = 2f, swimAmplitude = 0.06f,
                    cruiseSpeed = 0.5f, burstSpeed = 3f, maxAcceleration = 3f,
                    neighbourRadius = 2f, separationDistance = 0.6f, alignmentWeight = 0.4f, cohesionWeight = 0.5f,
                    wanderWeight = 1f, depthRange = new Vector2(-4.5f, 3f), homeHalfLength = 10f, homeMaxX = 8f,
                },
                new FishSpecies
                {
                    name = "Manta Ray", count = 4, schoolSize = 1, curiosity = 0.35f, glassAffinity = 0.5f, preferredGlassDistance = 3.5f, bodyType = FishBodyType.Ray, turnRate = 30f, burstCoast = 0f,
                    length = 1.6f, lengthVariance = 0.2f, raySpan = 2.2f, bodyHeight = 0.08f,
                    topColor = new Color(0.07f, 0.08f, 0.1f), bellyColor = new Color(0.9f, 0.9f, 0.92f),
                    finColor = new Color(0.07f, 0.08f, 0.1f), tailColor = new Color(0.07f, 0.08f, 0.1f),
                    smoothness = 0.45f, sheen = 0.1f,
                    swimFrequency = 0.35f, swimAmplitude = 0.28f,
                    cruiseSpeed = 0.9f, burstSpeed = 2.4f, maxAcceleration = 0.9f, banking = 25f,
                    neighbourRadius = 4f, separationDistance = 3f, alignmentWeight = 0f, cohesionWeight = 0f,
                    migrationWeight = 0.8f, wanderWeight = 0.35f,
                    depthRange = new Vector2(-4.5f, 1.5f), fleeRadiusMultiplier = 1.4f, fleeStrength = 3.5f,
                    currentInfluence = 0.3f, stirsWater = true,
                },
                new FishSpecies
                {
                    name = "Reef Shark", count = 3, schoolSize = 1, curiosity = 0.15f, glassAffinity = 0.5f, preferredGlassDistance = 3f, turnRate = 45f, burstCoast = 0f, finOpacity = 1f,
                    length = 2f, lengthVariance = 0.15f, bodyHeight = 0.19f, bodyWidth = 0.17f, thickestPoint = 0.33f,
                    peduncle = 0.06f, noseSharpness = 1f, tailLength = 0.25f, tailHeight = 0.32f, tailFork = 0.55f, tailAsymmetry = 0.4f,
                    dorsalHeight = 0.17f, dorsalRange = new Vector2(0.34f, 0.5f), analHeight = 0.04f, pectoralSize = 0.22f,
                    topColor = new Color(0.33f, 0.36f, 0.4f), bellyColor = new Color(0.86f, 0.87f, 0.87f),
                    finColor = new Color(0.25f, 0.27f, 0.3f), tailColor = new Color(0.3f, 0.32f, 0.36f),
                    smoothness = 0.5f, sheen = 0.05f,
                    swimFrequency = 0.8f, swimAmplitude = 0.1f, bodyWaves = 0.7f,
                    cruiseSpeed = 1.2f, burstSpeed = 3.2f, maxAcceleration = 1.2f, banking = 15f,
                    neighbourRadius = 4f, separationDistance = 2.5f, alignmentWeight = 0f, cohesionWeight = 0f,
                    migrationWeight = 0.9f, wanderWeight = 0.4f,
                    depthRange = new Vector2(-3.5f, 5.5f), fleeRadiusMultiplier = 1.2f, fleeStrength = 4.5f,
                    currentInfluence = 0.2f, isPredator = true, stirsWater = true,
                },
            };
        }
    }
}
