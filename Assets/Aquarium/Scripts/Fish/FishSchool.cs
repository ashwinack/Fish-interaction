using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium
{
    /// <summary>
    /// Multi-species boids simulation. Each fish combines schooling (separation / alignment / cohesion
    /// within its own school), migration along the tunnel, wander, habitat limits, glass and obstacle
    /// avoidance, predator avoidance, fleeing from touches and drift in the fluid current.
    /// </summary>
    [DefaultExecutionOrder(0)]
    public sealed class FishSchool : MonoBehaviour
    {
        sealed class SpeciesRuntime
        {
            public FishSpecies Def;
            public Mesh Mesh;
            public Material Material;
            public Material FinMaterial;
            public List<CustomFishModel.Template> Models = new List<CustomFishModel.Template>(); // imported looks (optional)
        }

        sealed class Fish
        {
            public int Species;
            public int School;
            public Transform Transform;
            public Renderer[] Renderers;
            public bool UsesSwimShader = true;
            public Animator Animator;
            public Animation Legacy;
            public int AnimState;          // current Animator state hash (imported rigged fish)
            public bool HasIdle, HasEat;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Forward;
            public float Size;
            public float Panic;
            public float Phase;
            public float Bend;
            public float WanderSeed;
            public float MigrateSign;
            public Vector3 Accel;
            public Vector3 SmoothedAccel;
            public float KickPhase;
            public float Thrust = 1f;
            public float Bank;

            // Which screen the fish is currently seen through (-1: none). Touches only scare fish on the touched screen.
            public int Screen = -1;
            // Escape response: a scheduled startle (reaction delay), then a burst that eases back to cruising.
            public float PendingTime = -1f;
            public Vector3 PendingDir;
            public float PendingStrength;
            public int PendingScreen = -1;
            public Vector3 FleeDir;
            public float FleeTimer;
            public float FleeDuration = 1f;
            public float FleeStrength;
            public float FleeEnvelope; // 0..1, how hard the fish is escaping right now
            public int FleeScreen = -1;
            public float ReactionJitter;

            // Curiosity: approach a calm finger, follow it, feed.
            public float Engaged;          // 0..1, how absorbed the fish is in the finger right now
            public float CuriousSince = -1f;
            public float NibbleTimer;
            public float EatAnimTimer;     // keeps an imported fish's eat clip playing after a bite
        }

        static readonly int s_PhaseId = Shader.PropertyToID("_Phase");
        static readonly int s_BendId = Shader.PropertyToID("_Bend");
        static readonly int s_AmpScaleId = Shader.PropertyToID("_AmpScale");
        static readonly int s_SwimState = Animator.StringToHash("swim");
        static readonly int s_IdleState = Animator.StringToHash("idle");
        static readonly int s_EatState = Animator.StringToHash("eat");
        readonly int[] m_VisibleCounts = new int[3];

        /// <summary>Number of fish currently visible on a screen.</summary>
        public int VisibleCount(ScreenId screen) => m_VisibleCounts[(int)screen];

        /// <summary>Number of fish on a screen that are currently gathered around a finger (curious).</summary>
        public int CuriousCount(ScreenId screen)
        {
            int n = 0;
            foreach (var f in m_Fish)
                if (f.Engaged > 0.3f && f.Screen == (int)screen) n++;
            return n;
        }

        /// <summary>Mean distance (m) from the given point of the fish that are curious.</summary>
        public float MeanCuriousDistance(Vector3 point)
        {
            float sum = 0f; int n = 0;
            foreach (var f in m_Fish)
                if (f.Engaged > 0.3f) { sum += Vector3.Distance(f.Position, point); n++; }
            return n > 0 ? sum / n : -1f;
        }

        /// <summary>Number of fish currently escaping from a touch on the given screen.</summary>
        public int EscapingCount(ScreenId touchedScreen)
        {
            int n = 0;
            foreach (var f in m_Fish)
                if ((f.FleeTimer > 0f && f.FleeScreen == (int)touchedScreen) || (f.PendingTime >= 0f && f.PendingScreen == (int)touchedScreen)) n++;
            return n;
        }

        AquariumDirector m_Director;
        readonly List<SpeciesRuntime> m_Species = new List<SpeciesRuntime>();
        readonly List<Fish> m_Fish = new List<Fish>();
        readonly List<int> m_Predators = new List<int>();
        MaterialPropertyBlock m_Block;

        // Uniform spatial hash
        const float CellSize = 2f;
        readonly Dictionary<long, List<int>> m_Grid = new Dictionary<long, List<int>>();
        readonly Stack<List<int>> m_ListPool = new Stack<List<int>>();

        public int FishCount => m_Fish.Count;

        public void Build(AquariumDirector director)
        {
            m_Director = director;
            m_Block = new MaterialPropertyBlock();
            var rng = new System.Random(director.seed + 17);
            int schoolId = 0;

            foreach (var source in director.species)
            {
                if (source == null || !source.enabled || source.count <= 0) continue;
                var def = ScaledCopy(source, director.fishSizeMultiplier);
                var built = FishMeshBuilder.Build(def);
                var rt = new SpeciesRuntime { Def = def, Mesh = built.Mesh, Material = CreateMaterial(def, built, director.Resources.Fish, false), FinMaterial = CreateMaterial(def, built, director.Resources.Fish, true) };
                if (def.customModel != null || (def.modelVariants != null && def.modelVariants.Count > 0))
                {
                    rt.Models = CustomFishModel.PrepareAll(def, rt.Material);
                    if (rt.Models.Count > 0)
                    {
                        var m0 = rt.Models[0];
                        Debug.Log($"[Aquarium] '{def.name}' uses {rt.Models.Count} imported model(s) ({(m0.Animated ? "own animation" : m0.BakedMesh != null ? "baked with swim shader" : "static")}).");
                    }
                }
                m_Species.Add(rt);
                int speciesIndex = m_Species.Count - 1;

                var parent = new GameObject(def.name).transform;
                parent.SetParent(transform, false);

                int remaining = def.count;
                int schoolInSpecies = 0;
                while (remaining > 0)
                {
                    int n = Mathf.Min(remaining, Mathf.Max(1, def.schoolSize));
                    remaining -= n;
                    Vector3 center = RandomHabitatPoint(def, rng, 1.5f);
                    float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
                    int variant = rt.Models.Count > 0 ? schoolInSpecies % rt.Models.Count : -1;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 offset = RandomInsideSphere(rng) * Mathf.Max(1f, def.neighbourRadius * 0.8f);
                        Vector3 p = center + offset;
                        if (!IsValidPosition(p, def, 0.5f)) p = center;
                        SpawnFish(speciesIndex, schoolId, p, sign, rng, parent, variant);
                    }
                    schoolId++;
                    schoolInSpecies++;
                }
            }

            for (int i = 0; i < m_Fish.Count; i++)
                if (m_Species[m_Fish[i].Species].Def.isPredator) m_Predators.Add(i);
        }

        /// <summary>Working copy of a species with the global size multiplier applied (the Inspector data is untouched).</summary>
        static FishSpecies ScaledCopy(FishSpecies src, float multiplier)
        {
            var d = JsonUtility.FromJson<FishSpecies>(JsonUtility.ToJson(src));
            d.customModel = src.customModel;
            d.modelVariants = src.modelVariants;
            float m = Mathf.Max(0.1f, multiplier);
            float sm = Mathf.Sqrt(m);
            d.length *= m;
            d.neighbourRadius *= m;
            d.separationDistance *= m;
            d.cruiseSpeed *= sm;
            d.burstSpeed *= sm;
            d.maxAcceleration *= sm;
            d.swimFrequency /= sm;          // bigger fish beat their tails more slowly
            d.fleeRadiusMultiplier *= Mathf.Lerp(1f, sm, 0.5f);
            d.preferredGlassDistance *= sm;
            return d;
        }

        static Material CreateMaterial(FishSpecies s, FishMeshBuilder.Result built, Shader shader, bool fins)
        {
            var m = new Material(shader) { name = s.name, enableInstancing = true };
            m.SetColor("_ColorTop", s.topColor);
            m.SetColor("_ColorBelly", s.bellyColor);
            m.SetColor("_StripeColor", s.stripeColor);
            m.SetVector("_StripeParams", new Vector4(s.stripeCount, s.stripeWidth, s.stripeOffset, s.stripeStrength));
            m.SetColor("_FinColor", s.finColor);
            m.SetColor("_TailColor", s.tailColor);
            m.SetFloat("_TailStart", s.tailColorStart);
            var eye = built.EyePosition;
            m.SetVector("_EyePos", new Vector4(eye.x, eye.y, eye.z, built.HalfSpan));
            m.SetFloat("_EyeRadius", built.EyeRadius);
            m.SetFloat("_Metallic", s.metallic);
            m.SetFloat("_Smoothness", s.smoothness);
            m.SetFloat("_Sheen", s.sheen);
            m.SetFloat("_BodyLength", 1f);
            m.SetFloat("_SwimAmp", s.swimAmplitude);
            m.SetFloat("_WaveLength", s.bodyWaves);
            m.SetFloat("_AnimMode", s.bodyType == FishBodyType.Ray ? 1f : 0f);
            m.SetFloat("_FinOpacity", s.finOpacity);
            m.SetVector("_Pattern", new Vector4(s.spots, 38f, s.bodyType == FishBodyType.Ray ? 0f : 0.35f, 0.5f - 0.2f * (1f - s.tailLength)));
            if (fins)
            {
                // Translucent fins: alpha-blended, drawn after opaques but before the water layer.
                m.name = s.name + " Fins";
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 2600;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            return m;
        }

        void SpawnFish(int speciesIndex, int school, Vector3 position, float sign, System.Random rng, Transform parent, int variant)
        {
            var rt = m_Species[speciesIndex];
            var def = rt.Def;
            var go = new GameObject(def.name);
            go.transform.SetParent(parent, false);
            Renderer[] renderers;
            bool swimShader = true;
            Animator animator = null;
            Animation legacy = null;
            if (variant >= 0 && variant < rt.Models.Count)
            {
                var model = CustomFishModel.Create(rt.Models[variant], go.transform, go.layer);
                renderers = model.Renderers;
                swimShader = model.UsesSwimShader;
                animator = model.Animator;
                legacy = model.Legacy;
                if (animator != null && !string.IsNullOrEmpty(def.modelAnimationState))
                    animator.Play(def.modelAnimationState, 0, (float)rng.NextDouble());
                if (legacy != null && legacy.clip != null)
                {
                    legacy.wrapMode = WrapMode.Loop;
                    legacy.Play();
                    legacy[legacy.clip.name].normalizedTime = (float)rng.NextDouble();
                }
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = rt.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = rt.Mesh.subMeshCount > 1 ? new[] { rt.Material, rt.FinMaterial } : new[] { rt.Material };
                renderers = new Renderer[] { mr };
            }
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            float size = def.length * (1f + ((float)rng.NextDouble() * 2f - 1f) * def.lengthVariance);
            go.transform.localScale = Vector3.one * size;

            Vector3 dir = def.migrationWeight > 0f
                ? new Vector3(((float)rng.NextDouble() - 0.5f) * 0.4f, 0f, sign).normalized
                : new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;

            var f = new Fish
            {
                Species = speciesIndex,
                School = school,
                Transform = go.transform,
                Renderers = renderers,
                UsesSwimShader = swimShader,
                Animator = animator,
                Legacy = legacy,
                Position = position,
                Velocity = dir * def.cruiseSpeed,
                Forward = dir,
                Size = size,
                Phase = (float)rng.NextDouble() * 100f,
                WanderSeed = (float)rng.NextDouble() * 1000f,
                MigrateSign = sign,
                ReactionJitter = (float)rng.NextDouble(),
            };
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                f.HasIdle = animator.HasState(0, s_IdleState);
                f.HasEat = animator.HasState(0, s_EatState);
                f.AnimState = s_SwimState;
            }
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir));
            m_Fish.Add(f);
        }

        // ------------------------------------------------------------------------------------------
        // Simulation
        // ------------------------------------------------------------------------------------------
        void Update()
        {
            if (m_Director == null) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            if (dt <= 0f) return;

            BuildGrid();

            // Which display shows each fish this frame.
            var rig = m_Director.Rig;
            m_VisibleCounts[0] = m_VisibleCounts[1] = m_VisibleCounts[2] = 0;
            for (int i = 0; i < m_Fish.Count; i++)
            {
                var f = m_Fish[i];
                f.Screen = rig != null ? rig.ScreenIndexForPoint(f.Position) : -1;
                if (f.Screen >= 0) m_VisibleCounts[f.Screen]++;
            }

            var disturbances = m_Director.Input != null ? m_Director.Input.Disturbances : null;
            for (int i = 0; i < m_Fish.Count; i++)
            {
                var f = m_Fish[i];
                Vector3 raw = ComputeSteering(i, disturbances, dt);
                // Low-pass the steering so fish glide between decisions instead of twitching; escapes react faster.
                f.SmoothedAccel = Vector3.Lerp(f.SmoothedAccel, raw, 1f - Mathf.Exp(-dt * Mathf.Lerp(4f, 16f, f.FleeEnvelope)));
                f.Accel = f.SmoothedAccel;
            }
            for (int i = 0; i < m_Fish.Count; i++)
                Integrate(m_Fish[i], dt);
        }

        static long CellKey(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        void BuildGrid()
        {
            foreach (var list in m_Grid.Values)
            {
                list.Clear();
                m_ListPool.Push(list);
            }
            m_Grid.Clear();
            for (int i = 0; i < m_Fish.Count; i++)
            {
                Vector3 p = m_Fish[i].Position;
                long key = CellKey(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize), Mathf.FloorToInt(p.z / CellSize));
                if (!m_Grid.TryGetValue(key, out var list))
                {
                    list = m_ListPool.Count > 0 ? m_ListPool.Pop() : new List<int>(16);
                    m_Grid[key] = list;
                }
                list.Add(i);
            }
        }

        Vector3 ComputeSteering(int index, List<Disturbance> disturbances, float dt)
        {
            var f = m_Fish[index];
            var def = m_Species[f.Species].Def;
            Vector3 p = f.Position;
            Vector3 v = f.Velocity;

            // --- Neighbours -----------------------------------------------------------------------
            Vector3 separation = Vector3.zero, alignment = Vector3.zero, cohesion = Vector3.zero;
            int schoolCount = 0;
            Vector3 neighbourFlee = Vector3.zero;
            float neighbourFleeWeight = 0f;
            int fleeingNeighbours = 0;
            float nr = def.neighbourRadius;
            float nr2 = nr * nr;
            int cx = Mathf.FloorToInt(p.x / CellSize), cy = Mathf.FloorToInt(p.y / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            int reach = Mathf.CeilToInt(nr / CellSize);
            for (int x = cx - reach; x <= cx + reach; x++)
            for (int y = cy - reach; y <= cy + reach; y++)
            for (int z = cz - reach; z <= cz + reach; z++)
            {
                if (!m_Grid.TryGetValue(CellKey(x, y, z), out var list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    int j = list[k];
                    if (j == index) continue;
                    var o = m_Fish[j];
                    Vector3 d = p - o.Position;
                    float d2 = d.sqrMagnitude;
                    if (d2 > nr2 || d2 < 1e-8f) continue;
                    float sepDist = def.separationDistance + (f.Size + o.Size) * 0.35f;
                    if (d2 < sepDist * sepDist)
                        separation += d / d2 * sepDist;
                    if (o.School == f.School)
                    {
                        alignment += o.Velocity;
                        cohesion += o.Position;
                        schoolCount++;
                        // Escapes spread through a school, but only among fish on the same display as the touch.
                        if (o.FleeEnvelope > 0.3f && o.FleeScreen >= 0 && o.FleeScreen == f.Screen)
                        {
                            neighbourFlee += o.FleeDir * o.FleeEnvelope;
                            neighbourFleeWeight += o.FleeEnvelope * o.FleeStrength;
                            fleeingNeighbours++;
                        }
                    }
                }
            }

            Vector3 steer = Vector3.zero;
            if (schoolCount > 0)
            {
                alignment /= schoolCount;
                cohesion /= schoolCount;
                // Schooling is relaxed while escaping so the school "flash-expands" instead of clumping.
                // ...and while gathered around a finger, so individuals can come close instead of being pulled back.
                float schooling = (1f - f.FleeEnvelope * 0.8f) * (1f - f.Engaged * 0.75f);
                steer += (alignment - v) * (def.alignmentWeight * schooling);
                steer += (cohesion - p) * (def.cohesionWeight * schooling);
                if (fleeingNeighbours > 0 && f.FleeTimer <= 0f && f.PendingTime < 0f && neighbourFlee.sqrMagnitude > 1e-4f)
                {
                    float strength = Mathf.Clamp01(neighbourFleeWeight / fleeingNeighbours) * 0.75f;
                    if (strength > 0.15f)
                        Schedule(f, neighbourFlee.normalized, strength, f.Screen, 0.04f + f.ReactionJitter * 0.1f);
                }
            }
            steer += separation * def.separationWeight;

            // --- Curiosity: come to a calm finger, follow it, feed -------------------------------------
            Vector3 curious = CuriositySteer(f, def, p, v, dt);
            float free = 1f - f.Engaged; // how much of the normal roaming behaviour remains

            // --- Cruise, migration and wander ------------------------------------------------------
            float speed = v.magnitude;
            Vector3 heading = speed > 1e-4f ? v / speed : f.Forward;
            float targetSpeed = Mathf.Lerp(def.cruiseSpeed, def.burstSpeed, f.Panic);
            // Burst-and-coast: reef fish kick, then glide (speed follows the thrust with a lag).
            float bc = def.burstCoast * (1f - f.FleeEnvelope);
            targetSpeed *= Mathf.Lerp(1f, 0.75f + 0.5f * f.Thrust, bc);
            steer += (heading * targetSpeed - v) * 1.2f * free;

            if (def.migrationWeight > 0f)
                steer += (new Vector3(0f, 0f, f.MigrateSign * def.cruiseSpeed) - new Vector3(0f, 0f, v.z)) * (def.migrationWeight * free);

            float t = Time.time * 0.12f;
            Vector3 wander = new Vector3(
                Mathf.PerlinNoise(f.WanderSeed, t) - 0.5f,
                (Mathf.PerlinNoise(f.WanderSeed + 31.7f, t) - 0.5f) * 0.5f,
                Mathf.PerlinNoise(f.WanderSeed + 71.3f, t) - 0.5f) * 2f;
            steer += wander * (def.wanderWeight * def.cruiseSpeed * 2f * free);
            steer += curious;

            // --- Environment ----------------------------------------------------------------------
            steer += HabitatForce(f, def) * Mathf.Max(1f, def.maxAcceleration * 0.5f);
            steer += TunnelForce(p, f.Size) * Mathf.Max(2f, def.maxAcceleration);
            steer += ObstacleForce(p, f.Size) * Mathf.Max(2f, def.maxAcceleration);

            // --- Predators ------------------------------------------------------------------------
            if (!def.isPredator)
            {
                for (int k = 0; k < m_Predators.Count; k++)
                {
                    var pr = m_Fish[m_Predators[k]];
                    Vector3 d = p - pr.Position;
                    float avoid = 2.5f + pr.Size;
                    float dist = d.magnitude;
                    if (dist < avoid && dist > 1e-4f)
                    {
                        float w = 1f - dist / avoid;
                        steer += d / dist * (w * def.fleeStrength * 0.5f);
                        f.Panic = Mathf.Max(f.Panic, w * 0.6f);
                    }
                }
            }

            // --- Touch disturbances (only from the display this fish is visible on) -----------------
            var interaction = m_Director.interaction;
            if (disturbances != null)
            {
                for (int k = 0; k < disturbances.Count; k++)
                {
                    var d = disturbances[k];
                    if ((int)d.Screen != f.Screen) continue;
                    Vector3 op = p - d.Origin;
                    float along = Mathf.Clamp(Vector3.Dot(op, d.Direction), 0f, d.Reach);
                    Vector3 closest = d.Origin + d.Direction * along;
                    Vector3 away = p - closest;
                    float dist = away.magnitude;
                    // The scare zone is a cone that widens slightly with distance from the glass
                    float radius = d.Radius * def.fleeRadiusMultiplier + along * 0.12f;
                    if (dist >= radius) continue;
                    if (dist < 1e-3f)
                    {
                        away = Vector3.Cross(d.Direction, (index & 1) == 0 ? Vector3.up : Vector3.right);
                        dist = 1e-3f;
                    }
                    float w = 1f - dist / radius;
                    w = Mathf.Clamp01(w * (2f - w) * d.Strength); // smooth falloff, full strength near the ray
                    Vector3 fleeDir = (away / Mathf.Max(dist, 1e-3f) + d.Direction * 0.5f).normalized;

                    if (f.FleeTimer > 0f && f.FleeScreen == (int)d.Screen)
                    {
                        // Finger still down / dragging: keep escaping, turning smoothly toward the new way out.
                        f.FleeDir = Vector3.Slerp(f.FleeDir, fleeDir, 1f - Mathf.Exp(-dt * 5f)).normalized;
                        f.FleeStrength = Mathf.Max(f.FleeStrength, w);
                        f.FleeTimer = Mathf.Max(f.FleeTimer, f.FleeDuration * 0.55f);
                    }
                    else if (w > 0.05f && (f.PendingTime < 0f || w > f.PendingStrength + 0.1f))
                    {
                        // The startle travels outward from the touch: nearby fish react first.
                        float delay = Vector3.Distance(p, d.Origin) / Mathf.Max(interaction.reactionWaveSpeed, 0.1f)
                                      + f.ReactionJitter * interaction.reactionJitter;
                        Schedule(f, fleeDir, w, (int)d.Screen, delay);
                    }
                }
            }

            // Scheduled startle fires
            if (f.PendingTime >= 0f && Time.time >= f.PendingTime)
            {
                StartEscape(f, index);
                f.PendingTime = -1f;
            }

            // --- Escape burst -------------------------------------------------------------------
            f.FleeEnvelope = 0f;
            if (f.FleeTimer > 0f)
            {
                float u = 1f - f.FleeTimer / f.FleeDuration;              // 0 -> 1 over the escape
                float attack = Mathf.Clamp01(u / 0.1f);                    // fast C-start
                float release = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, u)); // long glide
                f.FleeEnvelope = attack * release;
                float burst = Mathf.Lerp(def.cruiseSpeed, def.burstSpeed, Mathf.Clamp01(0.35f + f.FleeStrength * 0.65f));
                Vector3 desired = f.FleeDir * Mathf.Lerp(def.cruiseSpeed, burst, f.FleeEnvelope);
                steer += (desired - v) * (1f + 5f * f.FleeEnvelope);
                f.Panic = Mathf.Max(f.Panic, f.FleeEnvelope * f.FleeStrength);
                f.FleeTimer -= dt;
                if (f.FleeTimer <= 0f) f.FleeScreen = -1;
            }

            // Big animals push water around
            if (def.stirsWater && m_Director.Fluid != null && (Time.frameCount + index) % 4 == 0)
                m_Director.Fluid.AddSplat(p, new Vector2(v.x, v.z) * 1.5f, f.Size * 0.8f, 0f);

            float maxAccel = def.maxAcceleration * (1f + f.Panic * 1.5f + f.FleeEnvelope * 3f);
            return Vector3.ClampMagnitude(steer, maxAccel);
        }

        /// <summary>
        /// Curious fish on the same display notice a calm finger after a moment, circle it loosely (each at its
        /// own angle), follow it as it moves, and chase and eat food flakes. Returns the steering force.
        /// </summary>
        Vector3 CuriositySteer(Fish f, FishSpecies def, Vector3 p, Vector3 v, float dt)
        {
            var input = m_Director.Input;
            var settings = m_Director.interaction;
            float targetEngaged = 0f;
            Vector3 desired = Vector3.zero;
            bool chasingFood = false;

            bool calm = f.FleeTimer <= 0f && f.PendingTime < 0f && f.Panic < 0.35f;
            if (input != null && def.curiosity > 0.01f && calm && f.Screen >= 0 && input.Attractors.Count > 0)
            {
                int best = -1;
                float range = settings.attractRadius * (0.5f + 0.5f * def.curiosity);
                float bestD2 = range * range;
                for (int k = 0; k < input.Attractors.Count; k++)
                {
                    var a = input.Attractors[k];
                    if ((int)a.Screen != f.Screen) continue;
                    float d2 = (a.Position - p).sqrMagnitude;
                    if (d2 < bestD2) { bestD2 = d2; best = k; }
                }

                if (best >= 0)
                {
                    var a = input.Attractors[best];
                    if (f.CuriousSince < 0f) f.CuriousSince = Time.time;
                    // It takes a moment to notice; the more curious the species, the sooner.
                    float notice = Mathf.Lerp(1.6f, 0.2f, def.curiosity) * (0.6f + 0.8f * f.ReactionJitter);
                    if (Time.time - f.CuriousSince > notice)
                    {
                        targetEngaged = def.curiosity * a.Strength;
                        Vector3 target;
                        float arriveSpeed;
                        var food = m_Director.Food;
                        if (food != null && food.TryGetNearest(p, f.Screen, 3f + def.curiosity * 3f, out var flake))
                        {
                            target = flake;
                            arriveSpeed = def.cruiseSpeed * 1.5f;
                            chasingFood = true;
                        }
                        else
                        {
                            // Loose orbit around the finger; bigger fish keep a larger, slower circle.
                            float angle = f.WanderSeed + Time.time * (0.35f / Mathf.Max(0.3f, f.Size * 2f));
                            float radius = 0.4f + f.Size * 1.5f + f.ReactionJitter * 0.8f;
                            target = a.Position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle * 0.7f) * 0.35f, Mathf.Sin(angle)) * radius;
                            arriveSpeed = def.cruiseSpeed * 0.8f;
                        }
                        Vector3 to = target - p;
                        float dist = to.magnitude;
                        float want = Mathf.Min(arriveSpeed, dist * 1.2f + def.cruiseSpeed * 0.1f);
                        desired = dist > 1e-3f ? to / dist * want : Vector3.zero;
                    }
                }
                else f.CuriousSince = -1f;
            }
            else f.CuriousSince = -1f;

            // Interest builds up gradually and fades slowly when the finger leaves.
            float rate = targetEngaged > f.Engaged ? 1.5f : 0.8f;
            f.Engaged = Mathf.Lerp(f.Engaged, targetEngaged, 1f - Mathf.Exp(-dt * rate));
            if (!calm) f.Engaged = Mathf.Min(f.Engaged, 0.2f);

            if (chasingFood && m_Director.Food.TryEat(p + f.Forward * (f.Size * 0.5f), 0.12f + f.Size * 0.35f))
            {
                f.NibbleTimer = 0.3f;
                f.EatAnimTimer = 1.6f;
            }

            return desired == Vector3.zero ? Vector3.zero : (desired - v) * (4f * f.Engaged);
        }

        void Schedule(Fish f, Vector3 dir, float strength, int screen, float delay)
        {
            f.PendingTime = Time.time + delay;
            f.PendingDir = dir;
            f.PendingStrength = strength;
            f.PendingScreen = screen;
        }

        void StartEscape(Fish f, int index)
        {
            // A little randomness in the escape angle makes a school scatter instead of moving as a block.
            float a = (Mathf.PerlinNoise(f.WanderSeed * 3.1f, Time.time) - 0.5f) * 50f;
            float b = (Mathf.PerlinNoise(f.WanderSeed * 1.7f + 5f, Time.time) - 0.5f) * 25f;
            Vector3 dir = Quaternion.Euler(b, a, 0f) * f.PendingDir;
            dir.y *= 0.6f;
            f.FleeDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : f.PendingDir;
            f.FleeStrength = Mathf.Clamp01(f.PendingStrength);
            f.FleeDuration = m_Director.interaction.escapeDuration * Mathf.Lerp(0.6f, 1.1f, f.FleeStrength) * (0.9f + 0.2f * f.ReactionJitter);
            f.FleeTimer = f.FleeDuration;
            f.FleeScreen = f.PendingScreen;
        }

        Vector3 HabitatForce(Fish f, FishSpecies def)
        {
            var tank = m_Director.tank;
            Vector3 p = f.Position;
            Vector3 force = Vector3.zero;
            const float soft = 2.5f;

            // Side walls of the tank
            float xMax = tank.halfWidth - 1f;
            if (def.migrationWeight <= 0f && def.homeMaxX > 0f) xMax = Mathf.Min(xMax, def.homeMaxX);
            float ax = Mathf.Abs(p.x);
            if (ax > xMax - soft) force.x -= Mathf.Sign(p.x) * Sq((ax - (xMax - soft)) / soft);

            // Vertical band: species preference clamped between seabed and surface
            float bed = Seabed.Height(p.x, p.z) + 0.4f + f.Size * 0.6f;
            float top = tank.surfaceHeight - 0.8f;
            float minY = Mathf.Max(bed, def.depthRange.x);
            float maxY = Mathf.Min(top, def.depthRange.y);
            if (maxY < minY + 0.5f) maxY = minY + 0.5f;
            const float band = 1.2f;
            if (p.y < minY + band) force.y += Sq((minY + band - p.y) / band);
            if (p.y > maxY - band) force.y -= Sq((p.y - (maxY - band)) / band);
            if (p.y < bed + 0.5f) force.y += 4f * Sq((bed + 0.5f - p.y) / 0.5f);

            // Stay close to the wall glass where visitors see the fish up close (not while curious).
            if (def.glassAffinity > 0f && f.Engaged < 0.5f)
            {
                float wall = m_Director.tunnel.width * 0.5f;
                if (ax > wall + 0.3f)
                {
                    float target = wall + tank.glassMargin + def.preferredGlassDistance;
                    force.x += Mathf.Sign(p.x) * Mathf.Clamp((target - ax) * 0.35f, -1f, 1f) * def.glassAffinity;
                }
            }

            // Home range along the tunnel for residents
            if (def.migrationWeight <= 0f)
            {
                float zMax = Mathf.Min(def.homeHalfLength, tank.halfLength - 2f);
                float az = Mathf.Abs(p.z);
                if (az > zMax - soft) force.z -= Mathf.Sign(p.z) * Sq((az - (zMax - soft)) / soft);
            }
            return force;
        }

        /// <summary>Soft push away from the glass tunnel (the volume the viewers stand in).</summary>
        Vector3 TunnelForce(Vector3 p, float size)
        {
            GetTunnelBox(size, out float bx, out float y0, out float y1);
            const float influence = 1.8f;
            float cxp = Mathf.Clamp(p.x, -bx, bx);
            float cyp = Mathf.Clamp(p.y, y0, y1);
            Vector2 d = new Vector2(p.x - cxp, p.y - cyp);
            float dist = d.magnitude;
            if (dist < 1e-4f)
            {
                // Inside: push out through the closest face
                float dx = bx - Mathf.Abs(p.x), dDown = p.y - y0, dUp = y1 - p.y;
                if (dx <= dDown && dx <= dUp) return new Vector3(Mathf.Sign(p.x == 0f ? 1f : p.x) * 6f, 0f, 0f);
                return dDown <= dUp ? new Vector3(0f, -6f, 0f) : new Vector3(0f, 6f, 0f);
            }
            if (dist > influence) return Vector3.zero;
            float w = Sq(1f - dist / influence) * 3f;
            return new Vector3(d.x / dist, d.y / dist, 0f) * w;
        }

        void GetTunnelBox(float size, out float halfX, out float minY, out float maxY)
        {
            var tunnel = m_Director.tunnel;
            float m = m_Director.tank.glassMargin + size * 0.5f;
            halfX = tunnel.width * 0.5f + m;
            minY = -m;
            maxY = tunnel.wallHeight + m;
        }

        Vector3 ObstacleForce(Vector3 p, float size)
        {
            var obstacles = m_Director.Environment != null ? m_Director.Environment.Obstacles : null;
            if (obstacles == null) return Vector3.zero;
            Vector3 force = Vector3.zero;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                Vector3 d = p - o.Center;
                float range = o.Radius + size + 1.2f;
                float d2 = d.sqrMagnitude;
                if (d2 > range * range) continue;
                float dist = Mathf.Sqrt(d2);
                if (dist < 1e-4f) continue;
                float w = Sq(1f - Mathf.Clamp01((dist - o.Radius) / (range - o.Radius)));
                force += d / dist * (w * 3f);
            }
            return force;
        }

        void Integrate(Fish f, float dt)
        {
            var def = m_Species[f.Species].Def;
            var tank = m_Director.tank;

            Vector3 v = f.Velocity + f.Accel * dt;

            // Turn-rate limit: fish cannot pivot instantly; escapes turn much faster.
            float esc0 = f.FleeEnvelope;
            float oldSpeed = f.Velocity.magnitude, newSpeed = v.magnitude;
            if (oldSpeed > 1e-4f && newSpeed > 1e-4f)
            {
                float maxTurn = def.turnRate * Mathf.Lerp(1f, 4f, esc0) * Mathf.Deg2Rad * dt;
                Vector3 dir = Vector3.RotateTowards(f.Velocity / oldSpeed, v / newSpeed, maxTurn, 0f);
                v = dir * newSpeed;
            }

            // Fish rarely swim steeply up or down
            float horiz = new Vector2(v.x, v.z).magnitude;
            float pitchLimit = def.bodyType == FishBodyType.Ray ? 0.3f : 0.6f;
            v.y = Mathf.Clamp(v.y, -horiz * pitchLimit - 0.15f, horiz * pitchLimit + 0.15f);

            float maxSpeed = Mathf.Lerp(def.cruiseSpeed * 1.3f, def.burstSpeed, Mathf.Max(f.Panic, f.FleeEnvelope));
            float minSpeed = def.cruiseSpeed * 0.35f;
            float speed = v.magnitude;
            if (speed > maxSpeed) v *= maxSpeed / speed;
            else if (speed < minSpeed) v = (speed > 1e-4f ? v / speed : f.Forward) * minSpeed;
            f.Velocity = v;

            // Drift with the fluid current
            Vector2 flow = m_Director.Fluid != null ? m_Director.Fluid.SampleVelocity(f.Position) : Vector2.zero;
            Vector3 p = f.Position + (v + new Vector3(flow.x, 0f, flow.y) * def.currentInfluence) * dt;

            // Hard constraints (never enter the tunnel, the seabed, rocks or leave the water)
            GetTunnelBox(f.Size * 0.5f, out float bx, out float y0, out float y1);
            if (Mathf.Abs(p.x) < bx && p.y > y0 && p.y < y1)
            {
                float dx = bx - Mathf.Abs(p.x), dDown = p.y - y0, dUp = y1 - p.y;
                if (dx <= dDown && dx <= dUp) p.x = Mathf.Sign(p.x == 0f ? 1f : p.x) * bx;
                else if (dDown <= dUp) p.y = y0;
                else p.y = y1;
            }
            float bed = Seabed.Height(p.x, p.z) + 0.15f + f.Size * 0.3f;
            p.y = Mathf.Clamp(p.y, bed, tank.surfaceHeight - 0.3f);
            p.x = Mathf.Clamp(p.x, -tank.halfWidth + 0.3f, tank.halfWidth - 0.3f);
            var obstacles = m_Director.Environment != null ? m_Director.Environment.Obstacles : null;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Count; i++)
                {
                    var o = obstacles[i];
                    Vector3 d = p - o.Center;
                    float minDist = o.Radius * 0.9f + f.Size * 0.3f;
                    if (d.sqrMagnitude < minDist * minDist && d.sqrMagnitude > 1e-6f)
                        p = o.Center + d.normalized * minDist;
                }
            }

            // Migrating species loop around the far ends of the tank (well outside every camera view)
            if (def.migrationWeight > 0f)
            {
                if (p.z > tank.halfLength) p.z -= tank.halfLength * 2f;
                else if (p.z < -tank.halfLength) p.z += tank.halfLength * 2f;
            }
            else
            {
                p.z = Mathf.Clamp(p.z, -tank.halfLength, tank.halfLength);
            }
            f.Position = p;

            // --- Orientation, banking and body bend -----------------------------------------------
            Vector3 fwd = v.sqrMagnitude > 1e-6f ? v.normalized : f.Forward;
            Vector3 prevFlat = new Vector3(f.Forward.x, 0f, f.Forward.z);
            Vector3 newFlat = new Vector3(fwd.x, 0f, fwd.z);
            float yawRate = 0f;
            if (prevFlat.sqrMagnitude > 1e-6f && newFlat.sqrMagnitude > 1e-6f)
                yawRate = Vector3.SignedAngle(prevFlat, newFlat, Vector3.up) * Mathf.Deg2Rad / dt;
            // Escaping fish turn much faster (still smoothed, so there is never a snap).
            float esc = f.FleeEnvelope;
            f.Forward = Vector3.Slerp(f.Forward, fwd, 1f - Mathf.Exp(-dt * Mathf.Lerp(10f, 22f, esc))).normalized;

            f.Bank = Mathf.Lerp(f.Bank, Mathf.Clamp(-yawRate * def.banking, -30f, 30f), 1f - Mathf.Exp(-dt * 4f));
            float bank = f.Bank;
            // Bodies pitch less than their path (fish level out quickly), which looks calmer.
            Vector3 look = new Vector3(f.Forward.x, f.Forward.y * 0.7f, f.Forward.z);
            Quaternion target = Quaternion.LookRotation(look.sqrMagnitude > 1e-6f ? look : f.Forward, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
            f.Transform.SetPositionAndRotation(p, Quaternion.Slerp(f.Transform.rotation, target, 1f - Mathf.Exp(-dt * Mathf.Lerp(6f, 16f, esc))));

            // Body curls into the turn (C-start) during an escape, then relaxes.
            float bendLimit = Mathf.Lerp(0.22f, 0.4f, esc);
            f.Bend = Mathf.Lerp(f.Bend, Mathf.Clamp(-yawRate * Mathf.Lerp(0.06f, 0.09f, esc), -bendLimit, bendLimit), 1f - Mathf.Exp(-dt * Mathf.Lerp(5f, 12f, esc)));

            // Tail beat frequency follows speed; escaping fish beat harder and wider
            float speedRatio = v.magnitude / Mathf.Max(def.cruiseSpeed, 0.01f);
            float freq = def.swimFrequency * Mathf.Clamp(0.45f + 0.55f * speedRatio, 0.4f, 3.2f);
            f.Phase += freq * Mathf.PI * 2f * dt;
            if (f.Phase > 10000f) f.Phase -= Mathf.PI * 2000f;

            f.Panic = Mathf.Max(0f, f.Panic - dt * 0.45f);

            m_Block.SetFloat(s_PhaseId, f.Phase);
            m_Block.SetFloat(s_BendId, f.Bend);
            // Kick-and-glide rhythm: tail beats hard during the kick and almost stops while gliding.
            float bcRate = def.burstCoastRate * (0.85f + 0.3f * f.ReactionJitter);
            f.KickPhase += dt * bcRate;
            float kick = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(f.KickPhase * Mathf.PI * 2f), 2f);
            f.Thrust = Mathf.Lerp(f.Thrust, kick, 1f - Mathf.Exp(-dt * 3f));
            float bcAmp = def.burstCoast * (1f - esc);
            float ampScale = Mathf.Lerp(1f, 0.2f + 1.3f * kick, bcAmp) * (1f + esc * 0.6f);
            // A quick flick of the tail when snapping up a food flake.
            f.NibbleTimer = Mathf.Max(0f, f.NibbleTimer - dt);
            ampScale *= 1f + f.NibbleTimer * 3f;
            m_Block.SetFloat(s_AmpScaleId, ampScale);
            if (f.UsesSwimShader)
            {
                foreach (var r in f.Renderers) r.SetPropertyBlock(m_Block);
            }
            else
            {
                // Imported animated model: its own swim cycle, played faster when the fish swims faster.
                float animSpeed = def.modelAnimationSpeed * Mathf.Clamp(0.45f + 0.55f * speedRatio, 0.3f, 3f) * ampScale;
                if (f.Animator != null && f.Animator.runtimeAnimatorController != null)
                {
                    // Pick the clip: eat when snapping up food, idle while hovering at a finger, swim otherwise.
                    int want = s_SwimState;
                    f.EatAnimTimer = Mathf.Max(0f, f.EatAnimTimer - dt);
                    if (f.EatAnimTimer > 0f && f.HasEat) want = s_EatState;
                    else if (f.Engaged > 0.5f && v.magnitude < def.cruiseSpeed * 0.6f && f.HasIdle) want = s_IdleState;
                    if (want != f.AnimState)
                    {
                        f.Animator.CrossFadeInFixedTime(want, want == s_EatState ? 0.1f : 0.3f);
                        f.AnimState = want;
                    }
                    f.Animator.speed = want == s_SwimState ? Mathf.Max(0.05f, animSpeed) : 1f;
                }
                else if (f.Animator != null) f.Animator.speed = Mathf.Max(0.05f, animSpeed);
                if (f.Legacy != null && f.Legacy.clip != null) f.Legacy[f.Legacy.clip.name].speed = Mathf.Max(0.05f, animSpeed);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Spawning helpers
        // ------------------------------------------------------------------------------------------
        Vector3 RandomHabitatPoint(FishSpecies def, System.Random rng, float margin)
        {
            var tank = m_Director.tank;
            float xMax = tank.halfWidth - 1.5f;
            if (def.migrationWeight <= 0f && def.homeMaxX > 0f) xMax = Mathf.Min(xMax, def.homeMaxX);
            float zMax = def.migrationWeight > 0f ? tank.halfLength * 0.8f : Mathf.Min(def.homeHalfLength, tank.halfLength) - 1f;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var p = new Vector3(
                    Mathf.Lerp(-xMax, xMax, (float)rng.NextDouble()),
                    Mathf.Lerp(def.depthRange.x, def.depthRange.y, (float)rng.NextDouble()),
                    Mathf.Lerp(-zMax, zMax, (float)rng.NextDouble()));
                if (IsValidPosition(p, def, margin)) return p;
            }
            return new Vector3(Mathf.Sign((float)rng.NextDouble() - 0.5f) * (m_Director.tunnel.width * 0.5f + 3f), -2f, 0f);
        }

        bool IsValidPosition(Vector3 p, FishSpecies def, float margin)
        {
            GetTunnelBox(def.length, out float bx, out float y0, out float y1);
            if (Mathf.Abs(p.x) < bx + margin && p.y > y0 - margin && p.y < y1 + margin) return false;
            if (p.y < Seabed.Height(p.x, p.z) + 0.5f + def.length) return false;
            if (p.y > m_Director.tank.surfaceHeight - 0.8f) return false;
            var obstacles = m_Director.Environment != null ? m_Director.Environment.Obstacles : null;
            if (obstacles != null)
                foreach (var o in obstacles)
                    if ((p - o.Center).sqrMagnitude < (o.Radius + 0.5f) * (o.Radius + 0.5f)) return false;
            return true;
        }

        static Vector3 RandomInsideSphere(System.Random rng)
        {
            for (int i = 0; i < 20; i++)
            {
                var v = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                if (v.sqrMagnitude <= 1f) return v;
            }
            return Vector3.zero;
        }

        static float Sq(float x) => x * x;

        void OnDestroy()
        {
            foreach (var s in m_Species)
            {
                if (s.Mesh != null) Destroy(s.Mesh);
                foreach (var m in s.Models) foreach (var o in m.Owned) if (o != null) Destroy(o);
                if (s.Material != null) Destroy(s.Material);
                if (s.FinMaterial != null) Destroy(s.FinMaterial);
            }
        }
    }
}
