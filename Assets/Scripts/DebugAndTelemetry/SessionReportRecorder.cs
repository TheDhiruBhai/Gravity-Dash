using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace GravityDrift
{
    [Serializable]
    public struct SessionLevelRecord
    {
        public string session_id;
        public int attempt_index;
        public int level_index;
        public int seed;
        public string outcome;
        public int room_width;
        public int room_height;
        public int total_cores;
        public int cores_collected;
        public bool switch_flipped;
        public bool airlock_unlocked;
        public bool room_cleared;
        public float room_timer_sec;
        public int hazard_hits;
        public int deaths_in_attempt;
        public int cumulative_deaths;
        public float cumulative_death_rate_pct;
        public int drones_destroyed_delta;
        public int gravity_flips_delta;
        public int smart_player_attacks_delta;
        public int escapes_observed_delta;
        public int jetpack_overloads_delta;
        public float jetpack_boost_sec_delta;
        public int gravity_charges_remaining;
        public int total_escapes_completed;
        public int crate_dodges_attempted;
        public int crate_dodges_succeeded;
        public float avg_crate_escape_sec;
        public float escape_impulse_mult;
        public float trapped_threshold_sec;
        public float crate_dodge_skill;
        public int smart_attacks_observed;
        public float drone_improvement_pct;
        public float mean_posterior_confidence;
        public float mean_belief_entropy;
        public float final_belief_entropy;
        public int drone_count;
        public int obstacle_count;
        public int hazard_count;
        public int crate_count;
        public float complexity_score;
        public int lives_remaining;
        public int max_lives;
        public string level_archetype;
        public float gravity_predictability;
        public float movement_predictability;
        public float pattern_adaptation_pct;
    }

    [Serializable]
    public struct PerInputAIEvent
    {
        public int input_index;
        public float elapsed_sec;
        public int level_index;
        public string input_source;
        public string player_action;
        public string drone_action;
        public float prior_entropy;
        public float posterior_entropy;
        public float entropy_reduction;
        public float confidence;
        public float top_cluster_confidence;
        public int astar_nodes;
        public float replan_latency_ms;
        public int hazards_avoided;
        public float drone_improvement_pct;
        public int gravity_flips_total;
        public int jetpack_overloads_total;
        public float gravity_predictability;
        public float movement_predictability;
        public float pattern_adaptation_pct;
    }

    /// <summary>
    /// Live per-game session & per-level telemetry recorder.
    /// Captures real played runs (outcomes, deltas across ECS resets, per-input Bayesian AI updates,
    /// DualSense vs. PC input sources) and exports CSV + JSON reports automatically and on demand.
    /// </summary>
    public class SessionReportRecorder : MonoBehaviour
    {
        public static SessionReportRecorder Instance { get; private set; }

        public string CurrentSessionId { get; private set; } = "";
        public string LastSavedStatus { get; private set; } = "No session saved yet.";
        public string LastSavedProjectDir { get; private set; } = "";
        public string LastSavedPersistentDir { get; private set; } = "";

        public IReadOnlyList<SessionLevelRecord> LevelRecords => levelRecords;
        public IReadOnlyList<PerInputAIEvent> InputEvents => inputEvents;
        public IReadOnlyDictionary<string, int> InputSourceCounts => inputSourceCounts;

        private readonly List<SessionLevelRecord> levelRecords = new List<SessionLevelRecord>();
        private readonly List<PerInputAIEvent> inputEvents = new List<PerInputAIEvent>();
        private readonly Dictionary<string, int> inputSourceCounts = new Dictionary<string, int>();

        private bool hasActiveSession = false;
        private bool attemptActive = false;
        private bool dirtySinceLastSave = false;
        private DateTime sessionStartUtc;
        private float sessionElapsedSec = 0f;

        // Current attempt tracking & delta baselines
        private int attemptCounter = 0;
        private int cumulativeDeaths = 0;
        private int currentAttemptLevel = 1;
        private int currentAttemptSeed = 101;
        private int baseDronesDestroyed = 0;
        private int baseGravityFlips = 0;
        private int baseSmartAttacks = 0;
        private int baseEscapesObserved = 0;
        private int baseOverloads = 0;
        private float baseBoostTime = 0f;
        private int lastObservedTagged = 0;

        // Per-attempt posterior entropy & confidence accumulators
        private float accumConf = 0f;
        private float accumEntropy = 0f;
        private float lastEntropy = 4.5f;
        private int sampleCount = 0;

        // Per-input change detection
        private PlayerActionType lastLoggedPlayerAct = PlayerActionType.Idle;
        private DroneActionType lastLoggedDroneAct = DroneActionType.Patrol;
        private int lastLoggedFlips = 0;
        private int lastLoggedOverloads = 0;
        private int lastLoggedHazardHits = 0;
        private bool lastLoggedBoosting = false;
        private float timeSinceLastInputSample = 0f;

        private float initialImprovementPct = -1f;
        private float initialDodgeSkill = -1f;

        private static readonly string[] KnownSources =
        {
            "DualSense_Touchpad",
            "DualSense_Stick",
            "DualSense_R2",
            "DualSense_DPad",
            "DualSense_RStick",
            "PC_WASD",
            "PC_ArrowsOrKeys",
            "PC_Boost"
        };

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                ResetSourceCounts();
            }
            else if (Instance != this)
            {
                Destroy(this);
            }
        }

        private void OnDisable()
        {
            FlushUnsavedOnExit();
        }

        private void OnApplicationQuit()
        {
            FlushUnsavedOnExit();
        }

        private void FlushUnsavedOnExit()
        {
            if (hasActiveSession && dirtySinceLastSave)
            {
                ArchiveBeforeLevelChange("Quit", saveImmediately: false);
                SaveAllReports();
            }
        }

        private void ResetSourceCounts()
        {
            inputSourceCounts.Clear();
            for (int i = 0; i < KnownSources.Length; i++)
            {
                inputSourceCounts[KnownSources[i]] = 0;
            }
        }

        /// <summary>
        /// Starts a clean live session when the player clicks Start Mission.
        /// </summary>
        public void BeginNewSession(int startingLevel, int seed)
        {
            levelRecords.Clear();
            inputEvents.Clear();
            ResetSourceCounts();

            sessionStartUtc = DateTime.UtcNow;
            CurrentSessionId = $"GD_{sessionStartUtc:yyyyMMdd_HHmmss}_{seed}";
            hasActiveSession = true;
            attemptActive = false;
            dirtySinceLastSave = false;
            sessionElapsedSec = 0f;

            attemptCounter = 0;
            cumulativeDeaths = 0;
            currentAttemptLevel = startingLevel;
            currentAttemptSeed = seed;

            initialImprovementPct = -1f;
            initialDodgeSkill = -1f;

            SnapshotBaselinesFromECS();
            ResetAttemptAccumulators();
            LastSavedStatus = $"Active session: {CurrentSessionId}";
        }

        /// <summary>
        /// Marks start of a new level attempt after previous level was archived & rebuilt.
        /// </summary>
        public void NotifyLevelStarted(int levelIndex, int seed)
        {
            if (!hasActiveSession)
            {
                BeginNewSession(levelIndex, seed);
                return;
            }

            currentAttemptLevel = levelIndex;
            currentAttemptSeed = seed;
            attemptActive = true;
            SnapshotBaselinesFromECS();
            ResetAttemptAccumulators();
        }

        private void ResetAttemptAccumulators()
        {
            accumConf = 0f;
            accumEntropy = 0f;
            lastEntropy = 4.5f;
            sampleCount = 0;
            lastLoggedPlayerAct = PlayerActionType.Idle;
            lastLoggedDroneAct = DroneActionType.Patrol;
            timeSinceLastInputSample = 0f;
        }

        private void SnapshotBaselinesFromECS()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var sQ = em.CreateEntityQuery(typeof(PlayerCounterplayStats));
            if (sQ.CalculateEntityCount() > 0)
            {
                using var arr = sQ.ToComponentDataArray<PlayerCounterplayStats>(Allocator.Temp);
                baseDronesDestroyed = arr[0].DronesDestroyed;
                baseGravityFlips = arr[0].GravityFlipsUsed;
                baseSmartAttacks = arr[0].SmartPlayerAttacks;
                baseEscapesObserved = arr[0].EscapesObserved;
                lastObservedTagged = arr[0].TimesTaggedByDrone;
            }
            sQ.Dispose();

            var gQ = em.CreateEntityQuery(typeof(GravityState));
            if (gQ.CalculateEntityCount() > 0)
            {
                using var gArr = gQ.ToComponentDataArray<GravityState>(Allocator.Temp);
                lastLoggedFlips = gArr[0].TotalFlips;
            }
            gQ.Dispose();

            var jQ = em.CreateEntityQuery(typeof(PlayerTag), typeof(JetpackComponent));
            if (jQ.CalculateEntityCount() > 0)
            {
                using var jArr = jQ.ToComponentDataArray<JetpackComponent>(Allocator.Temp);
                baseOverloads = jArr[0].OverloadCount;
                baseBoostTime = jArr[0].TotalBoostTime;
                lastLoggedOverloads = jArr[0].OverloadCount;
                lastLoggedBoosting = jArr[0].IsBoosting;
            }
            jQ.Dispose();

            var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (oQ.CalculateEntityCount() > 0)
            {
                using var oArr = oQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                lastLoggedHazardHits = oArr[0].HazardHits;
            }
            oQ.Dispose();
        }

        /// <summary>
        /// Archives the current level/attempt BEFORE RebuildLevelEntities or ResetToMenuIdleState resets LevelObjectiveState.
        /// </summary>
        public void ArchiveBeforeLevelChange(string outcome, bool saveImmediately = true)
        {
            if (!hasActiveSession || !attemptActive) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var objQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQ.CalculateEntityCount() == 0)
            {
                objQ.Dispose();
                return;
            }

            using var objArr = objQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
            var lvl = objArr[0];
            objQ.Dispose();

            bool meaningfulActivity = (lvl.RoomTimer > 0.05f || sampleCount > 0 || lvl.RoomCleared || outcome == "Death_Tagged");
            if (!meaningfulActivity)
            {
                attemptActive = false;
                return;
            }

            RecordSnapshotInternal(em, lvl, outcome, deathsThisAttempt: outcome == "Death_Tagged" ? 1 : 0);
            attemptActive = false;

            if (saveImmediately)
            {
                SaveAllReports();
            }
        }

        private void RecordSnapshotInternal(EntityManager em, LevelObjectiveState lvl, string outcome, int deathsThisAttempt)
        {
            attemptCounter++;
            cumulativeDeaths += deathsThisAttempt;
            float deathRatePct = attemptCounter > 0 ? (100f * cumulativeDeaths / attemptCounter) : 0f;

            RoomBoundsState bounds = new RoomBoundsState { RoomWidth = 20, RoomHeight = 12 };
            var bQ = em.CreateEntityQuery(typeof(RoomBoundsState));
            if (bQ.CalculateEntityCount() > 0)
            {
                using var bArr = bQ.ToComponentDataArray<RoomBoundsState>(Allocator.Temp);
                bounds = bArr[0];
            }
            bQ.Dispose();

            PlayerCounterplayStats stats = default;
            var sQ = em.CreateEntityQuery(typeof(PlayerCounterplayStats));
            if (sQ.CalculateEntityCount() > 0)
            {
                using var sArr = sQ.ToComponentDataArray<PlayerCounterplayStats>(Allocator.Temp);
                stats = sArr[0];
            }
            sQ.Dispose();

            DroneLearningState learn = default;
            var lQ = em.CreateEntityQuery(typeof(DroneLearningState));
            if (lQ.CalculateEntityCount() > 0)
            {
                using var lArr = lQ.ToComponentDataArray<DroneLearningState>(Allocator.Temp);
                learn = lArr[0];
            }
            lQ.Dispose();

            JetpackComponent jet = default;
            var jQ = em.CreateEntityQuery(typeof(PlayerTag), typeof(JetpackComponent));
            if (jQ.CalculateEntityCount() > 0)
            {
                using var jArr = jQ.ToComponentDataArray<JetpackComponent>(Allocator.Temp);
                jet = jArr[0];
            }
            jQ.Dispose();

            GravityState grav = default;
            var gQ = em.CreateEntityQuery(typeof(GravityState));
            if (gQ.CalculateEntityCount() > 0)
            {
                using var gArr = gQ.ToComponentDataArray<GravityState>(Allocator.Temp);
                grav = gArr[0];
            }
            gQ.Dispose();

            float meanConf = sampleCount > 0 ? (accumConf / sampleCount) : 0.15f;
            float meanEnt = sampleCount > 0 ? (accumEntropy / sampleCount) : lastEntropy;
            QueryRoomComplexity(em, bounds, lvl.TotalCores, out int dronesCnt, out int obsCnt, out int hazCnt, out int crateCnt, out float compScore, out string archName);

            var rec = new SessionLevelRecord
            {
                session_id = CurrentSessionId,
                attempt_index = attemptCounter,
                level_index = lvl.LevelIndex > 0 ? lvl.LevelIndex : currentAttemptLevel,
                seed = lvl.CurrentSeed > 0 ? lvl.CurrentSeed : currentAttemptSeed,
                outcome = outcome,
                room_width = bounds.RoomWidth,
                room_height = bounds.RoomHeight,
                total_cores = lvl.TotalCores,
                cores_collected = lvl.CoresCollected,
                switch_flipped = lvl.SwitchFlipped,
                airlock_unlocked = lvl.AirlockUnlocked,
                room_cleared = lvl.RoomCleared || outcome == "Cleared",
                room_timer_sec = lvl.RoomTimer,
                hazard_hits = lvl.HazardHits,
                deaths_in_attempt = deathsThisAttempt,
                cumulative_deaths = cumulativeDeaths,
                cumulative_death_rate_pct = deathRatePct,
                drones_destroyed_delta = math.max(0, stats.DronesDestroyed - baseDronesDestroyed),
                gravity_flips_delta = math.max(0, stats.GravityFlipsUsed - baseGravityFlips),
                smart_player_attacks_delta = math.max(0, stats.SmartPlayerAttacks - baseSmartAttacks),
                escapes_observed_delta = math.max(0, stats.EscapesObserved - baseEscapesObserved),
                jetpack_overloads_delta = math.max(0, jet.OverloadCount - baseOverloads),
                jetpack_boost_sec_delta = math.max(0f, jet.TotalBoostTime - baseBoostTime),
                gravity_charges_remaining = grav.CurrentCharges,
                total_escapes_completed = learn.TotalEscapesCompleted,
                crate_dodges_attempted = learn.TotalCrateDodgesAttempted,
                crate_dodges_succeeded = learn.TotalCrateDodgesSucceeded,
                avg_crate_escape_sec = learn.AverageEscapeTimeSec,
                escape_impulse_mult = learn.EscapeImpulseMultiplier,
                trapped_threshold_sec = learn.TrappedDetectionThreshold,
                crate_dodge_skill = learn.CrateDodgeSkill,
                smart_attacks_observed = learn.SmartPlayerAttacksObserved,
                drone_improvement_pct = learn.DroneImprovementPct,
                mean_posterior_confidence = meanConf,
                mean_belief_entropy = meanEnt,
                final_belief_entropy = lastEntropy,
                drone_count = dronesCnt,
                obstacle_count = obsCnt,
                hazard_count = hazCnt,
                crate_count = crateCnt,
                complexity_score = compScore,
                lives_remaining = lvl.LivesRemaining,
                max_lives = math.max(3, lvl.MaxLives),
                level_archetype = archName,
                gravity_predictability = learn.GravityPredictability,
                movement_predictability = learn.MovementPredictability,
                pattern_adaptation_pct = learn.PatternAdaptationScore * 100f
            };

            levelRecords.Add(rec);
            dirtySinceLastSave = true;

            // Advance delta baselines so next attempt (death continuation or next level) tracks clean deltas
            baseDronesDestroyed = stats.DronesDestroyed;
            baseGravityFlips = stats.GravityFlipsUsed;
            baseSmartAttacks = stats.SmartPlayerAttacks;
            baseEscapesObserved = stats.EscapesObserved;
            baseOverloads = jet.OverloadCount;
            baseBoostTime = jet.TotalBoostTime;
            ResetAttemptAccumulators();
        }

        private void LateUpdate()
        {
            if (!hasActiveSession || !attemptActive) return;
            if (Time.timeScale <= 0.001f) return;
            if (GameUIManager.Instance != null && GameUIManager.Instance.currentState != UIState.Gameplay) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            float dt = Time.deltaTime;
            sessionElapsedSec += dt;
            timeSinceLastInputSample += dt;

            // 1. Poll Objective, Gravity, Jetpack, Counterplay & Learning
            LevelObjectiveState lvl = default;
            bool hasLvl = false;
            var objQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQ.CalculateEntityCount() > 0)
            {
                using var arr = objQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                lvl = arr[0];
                hasLvl = true;
            }
            objQ.Dispose();
            if (!hasLvl) return;

            GravityState grav = default;
            var gQ = em.CreateEntityQuery(typeof(GravityState));
            if (gQ.CalculateEntityCount() > 0)
            {
                using var gArr = gQ.ToComponentDataArray<GravityState>(Allocator.Temp);
                grav = gArr[0];
            }
            gQ.Dispose();

            JetpackComponent jet = default;
            var jQ = em.CreateEntityQuery(typeof(PlayerTag), typeof(JetpackComponent));
            if (jQ.CalculateEntityCount() > 0)
            {
                using var jArr = jQ.ToComponentDataArray<JetpackComponent>(Allocator.Temp);
                jet = jArr[0];
            }
            jQ.Dispose();

            DroneLearningState learn = default;
            var lQ = em.CreateEntityQuery(typeof(DroneLearningState));
            if (lQ.CalculateEntityCount() > 0)
            {
                using var lArr = lQ.ToComponentDataArray<DroneLearningState>(Allocator.Temp);
                learn = lArr[0];
                if (initialImprovementPct < 0f)
                {
                    initialImprovementPct = learn.DroneImprovementPct;
                    initialDodgeSkill = learn.CrateDodgeSkill;
                }
            }
            lQ.Dispose();

            // Primary Drone belief & A* telemetry (lowest DroneID or Drone 0)
            BayesianBeliefComponent primaryBelief = new BayesianBeliefComponent { Entropy = 4.5f, PriorEntropy = 4.5f };
            DroneTelemetry primaryTel = default;
            DroneActionType primaryDroneAction = DroneActionType.Patrol;
            var dQ = em.CreateEntityQuery(typeof(DroneState), typeof(BayesianBeliefComponent), typeof(DroneTelemetry));
            if (dQ.CalculateEntityCount() > 0)
            {
                using var dSt = dQ.ToComponentDataArray<DroneState>(Allocator.Temp);
                using var dBel = dQ.ToComponentDataArray<BayesianBeliefComponent>(Allocator.Temp);
                using var dTel = dQ.ToComponentDataArray<DroneTelemetry>(Allocator.Temp);

                int chosen = 0;
                for (int i = 0; i < dSt.Length; i++)
                {
                    if (!dSt[i].IsDestroyed)
                    {
                        chosen = i;
                        break;
                    }
                }
                primaryDroneAction = dSt[chosen].ActiveAction;
                primaryBelief = dBel[chosen];
                primaryTel = dTel[chosen];
            }
            dQ.Dispose();

            // Accumulate per-attempt belief summary
            accumConf += primaryBelief.TopClusterConfidence;
            accumEntropy += primaryBelief.Entropy;
            lastEntropy = primaryBelief.Entropy;
            sampleCount++;

            // 2. Detect Player Death / Tagged by Drone cleanly (TriggerPlayerTaggedReset keeps RoomTimer running unless archived)
            CheckAndRecordTagDeathsInternal(em, lvl);

            // 3. Detect meaningful player / AI input events
            bool flipOccurred = grav.TotalFlips > lastLoggedFlips;
            bool overloadOccurred = jet.OverloadCount > lastLoggedOverloads;
            bool hazardOccurred = lvl.HazardHits > lastLoggedHazardHits;
            bool boostStarted = jet.IsBoosting && !lastLoggedBoosting;
            bool playerActChanged = lvl.CurrentPlayerAction != lastLoggedPlayerAct && lvl.CurrentPlayerAction != PlayerActionType.Idle;
            bool droneActChanged = primaryDroneAction != lastLoggedDroneAct;
            bool periodicActiveThrust = (jet.IsThrusting || jet.IsBoosting) && timeSinceLastInputSample >= 0.35f;

            if (flipOccurred || overloadOccurred || hazardOccurred || boostStarted || playerActChanged || droneActChanged || periodicActiveThrust)
            {
                string source = ResolveEventInputSource(flipOccurred, boostStarted || jet.IsBoosting);
                if (!inputSourceCounts.ContainsKey(source)) inputSourceCounts[source] = 0;
                inputSourceCounts[source]++;

                float priorEnt = primaryBelief.PriorEntropy > 0.0001f ? primaryBelief.PriorEntropy : primaryBelief.Entropy;
                float postEnt = primaryBelief.Entropy;
                float entropyRed = priorEnt - postEnt;

                inputEvents.Add(new PerInputAIEvent
                {
                    input_index = inputEvents.Count + 1,
                    elapsed_sec = sessionElapsedSec,
                    level_index = lvl.LevelIndex > 0 ? lvl.LevelIndex : currentAttemptLevel,
                    input_source = source,
                    player_action = lvl.CurrentPlayerAction.ToString(),
                    drone_action = primaryDroneAction.ToString(),
                    prior_entropy = priorEnt,
                    posterior_entropy = postEnt,
                    entropy_reduction = entropyRed,
                    confidence = primaryBelief.Confidence,
                    top_cluster_confidence = primaryBelief.TopClusterConfidence,
                    astar_nodes = primaryTel.NodesExpanded,
                    replan_latency_ms = primaryTel.ReplanLatencyMs,
                    hazards_avoided = primaryTel.HazardCellsAvoided,
                    drone_improvement_pct = learn.DroneImprovementPct,
                    gravity_flips_total = grav.TotalFlips,
                    jetpack_overloads_total = jet.OverloadCount,
                    gravity_predictability = learn.GravityPredictability,
                    movement_predictability = learn.MovementPredictability,
                    pattern_adaptation_pct = learn.PatternAdaptationScore * 100f
                });

                dirtySinceLastSave = true;
                timeSinceLastInputSample = 0f;
            }

            lastLoggedFlips = grav.TotalFlips;
            lastLoggedOverloads = jet.OverloadCount;
            lastLoggedHazardHits = lvl.HazardHits;
            lastLoggedBoosting = jet.IsBoosting;
            lastLoggedPlayerAct = lvl.CurrentPlayerAction;
            lastLoggedDroneAct = primaryDroneAction;
        }

        public void CheckAndRecordTagDeaths()
        {
            if (!hasActiveSession) return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (oQ.CalculateEntityCount() > 0)
            {
                using var oArr = oQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                var lvl = oArr[0];
                oQ.Dispose();
                CheckAndRecordTagDeathsInternal(em, lvl);
            }
            else
            {
                oQ.Dispose();
            }
        }

        private void CheckAndRecordTagDeathsInternal(EntityManager em, LevelObjectiveState lvl)
        {
            var sQ = em.CreateEntityQuery(typeof(PlayerCounterplayStats));
            if (sQ.CalculateEntityCount() > 0)
            {
                using var sArr = sQ.ToComponentDataArray<PlayerCounterplayStats>(Allocator.Temp);
                var stats = sArr[0];
                if (stats.TimesTaggedByDrone > lastObservedTagged)
                {
                    int deltaTags = stats.TimesTaggedByDrone - lastObservedTagged;
                    lastObservedTagged = stats.TimesTaggedByDrone;
                    RecordSnapshotInternal(em, lvl, "Death_Tagged", deltaTags);
                    SaveAllReports();
                }
            }
            sQ.Dispose();
        }

        private static string ResolveEventInputSource(bool flipOccurred, bool boosting)
        {
            if (flipOccurred)
            {
                return string.IsNullOrEmpty(InputHelper.lastGravityInputSource) ? "PC_ArrowsOrKeys" : InputHelper.lastGravityInputSource;
            }
            if (boosting)
            {
                return string.IsNullOrEmpty(InputHelper.lastBoostInputSource) || InputHelper.lastBoostInputSource == "None"
                    ? "PC_Boost" : InputHelper.lastBoostInputSource;
            }
            if (!string.IsNullOrEmpty(InputHelper.lastThrustInputSource) && InputHelper.lastThrustInputSource != "None")
            {
                return InputHelper.lastThrustInputSource;
            }
            return string.IsNullOrEmpty(InputHelper.lastActiveInputSource) || InputHelper.lastActiveInputSource == "None"
                ? "PC_WASD" : InputHelper.lastActiveInputSource;
        }

        public List<SessionLevelRecord> GetLevelRecordsSnapshot(bool includeCurrentInProgress)
        {
            var exportLevels = new List<SessionLevelRecord>(levelRecords);
            if (includeCurrentInProgress && attemptActive)
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    var em = world.EntityManager;
                    var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
                    if (oQ.CalculateEntityCount() > 0)
                    {
                        using var oArr = oQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                        var lvl = oArr[0];
                        oQ.Dispose();
                        var bQ = em.CreateEntityQuery(typeof(RoomBoundsState));
                        var bounds = bQ.CalculateEntityCount() > 0 ? bQ.ToComponentDataArray<RoomBoundsState>(Allocator.Temp)[0] : new RoomBoundsState { RoomWidth = 20, RoomHeight = 12 };
                        bQ.Dispose();
                        var lQ = em.CreateEntityQuery(typeof(DroneLearningState));
                        var learn = lQ.CalculateEntityCount() > 0 ? lQ.ToComponentDataArray<DroneLearningState>(Allocator.Temp)[0] : default;
                        lQ.Dispose();
                        float deathPct = (attemptCounter + 1) > 0 ? (100f * cumulativeDeaths / (attemptCounter + 1)) : 0f;
                        QueryRoomComplexity(em, bounds, lvl.TotalCores, out int dronesCnt, out int obsCnt, out int hazCnt, out int crateCnt, out float compScore, out string archName);
                        exportLevels.Add(new SessionLevelRecord
                        {
                            session_id = CurrentSessionId,
                            attempt_index = attemptCounter + 1,
                            level_index = lvl.LevelIndex > 0 ? lvl.LevelIndex : currentAttemptLevel,
                            seed = lvl.CurrentSeed > 0 ? lvl.CurrentSeed : currentAttemptSeed,
                            outcome = "InProgress",
                            room_width = bounds.RoomWidth,
                            room_height = bounds.RoomHeight,
                            total_cores = lvl.TotalCores,
                            cores_collected = lvl.CoresCollected,
                            switch_flipped = lvl.SwitchFlipped,
                            airlock_unlocked = lvl.AirlockUnlocked,
                            room_cleared = lvl.RoomCleared,
                            room_timer_sec = lvl.RoomTimer,
                            hazard_hits = lvl.HazardHits,
                            deaths_in_attempt = 0,
                            cumulative_deaths = cumulativeDeaths,
                            cumulative_death_rate_pct = deathPct,
                            total_escapes_completed = learn.TotalEscapesCompleted,
                            crate_dodges_attempted = learn.TotalCrateDodgesAttempted,
                            crate_dodges_succeeded = learn.TotalCrateDodgesSucceeded,
                            drone_improvement_pct = learn.DroneImprovementPct,
                            crate_dodge_skill = learn.CrateDodgeSkill,
                            avg_crate_escape_sec = learn.AverageEscapeTimeSec,
                            escape_impulse_mult = learn.EscapeImpulseMultiplier,
                            trapped_threshold_sec = learn.TrappedDetectionThreshold,
                            smart_attacks_observed = learn.SmartPlayerAttacksObserved,
                            mean_posterior_confidence = sampleCount > 0 ? accumConf / sampleCount : 0.15f,
                            mean_belief_entropy = sampleCount > 0 ? accumEntropy / sampleCount : lastEntropy,
                            final_belief_entropy = lastEntropy,
                            drone_count = dronesCnt,
                            obstacle_count = obsCnt,
                            hazard_count = hazCnt,
                            crate_count = crateCnt,
                            complexity_score = compScore,
                            lives_remaining = lvl.LivesRemaining,
                            max_lives = math.max(3, lvl.MaxLives),
                            level_archetype = archName,
                            gravity_predictability = learn.GravityPredictability,
                            movement_predictability = learn.MovementPredictability,
                            pattern_adaptation_pct = learn.PatternAdaptationScore * 100f
                        });
                    }
                    else
                    {
                        oQ.Dispose();
                    }
                }
            }
            return exportLevels;
        }

        /// <summary>
        /// Exports current live session data (`*_levels.csv`, `*_per_input_ai.csv`, `*_summary.json`)
        /// to both project `Telemetry/Sessions/` and `Application.persistentDataPath/Reports/`.
        /// Optionally appends an in-progress snapshot of the currently running level without resetting deltas.
        /// </summary>
        public bool SaveAllReports(bool includeCurrentInProgress = false)
        {
            if (string.IsNullOrEmpty(CurrentSessionId))
            {
                CurrentSessionId = $"GD_{DateTime.UtcNow:yyyyMMdd_HHmmss}_101";
            }

            var exportLevels = GetLevelRecordsSnapshot(includeCurrentInProgress);

            string levelsCsv = BuildLevelsCsv(exportLevels);
            string inputsCsv = BuildInputsCsv(inputEvents);
            string summaryJson = BuildSummaryJson(exportLevels);

            string projDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Telemetry/Sessions"));
            string persDir = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Reports"));

            bool wroteAny = false;
            wroteAny |= TryWriteSessionBundle(projDir, CurrentSessionId, levelsCsv, inputsCsv, summaryJson);
            wroteAny |= TryWriteSessionBundle(persDir, CurrentSessionId, levelsCsv, inputsCsv, summaryJson);

            LastSavedProjectDir = projDir;
            LastSavedPersistentDir = persDir;
            dirtySinceLastSave = false;

            if (wroteAny)
            {
                LastSavedStatus = $"Saved {CurrentSessionId} ({exportLevels.Count} lvl rows, {inputEvents.Count} input events)";
                Debug.Log($"<color=#38BDF8><b>[SESSION REPORT]</b></color> {LastSavedStatus}\n  Project: {projDir}\n  Persistent: {persDir}");
            }
            return wroteAny;
        }

        private static bool TryWriteSessionBundle(string dir, string sessionId, string levelsCsv, string inputsCsv, string summaryJson)
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"{sessionId}_levels.csv"), levelsCsv);
                File.WriteAllText(Path.Combine(dir, $"{sessionId}_per_input_ai.csv"), inputsCsv);
                File.WriteAllText(Path.Combine(dir, $"{sessionId}_summary.json"), summaryJson);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SESSION REPORT] Failed writing to {dir}: {ex.Message}");
                return false;
            }
        }

        private static void QueryRoomComplexity(
            EntityManager em, RoomBoundsState bounds, int coresCount,
            out int drones, out int obstacles, out int hazards, out int crates, out float complexity, out string archetype)
        {
            var pcg = FindAnyObjectByType<PCGRoomGenerator>();
            if (pcg != null && pcg.LastComplexityScore > 0.1f)
            {
                drones = pcg.LastDroneCount;
                obstacles = pcg.LastObstacleCount;
                hazards = pcg.LastHazardCount;
                crates = pcg.LastCrateCount;
                complexity = pcg.LastComplexityScore;
                archetype = string.IsNullOrEmpty(pcg.LastArchetypeName) ? "SplitBaffles" : pcg.LastArchetypeName;
                return;
            }

            var dQ = em.CreateEntityQuery(typeof(DroneAITag)); drones = dQ.CalculateEntityCount(); dQ.Dispose();
            var oQ = em.CreateEntityQuery(typeof(StationObstacleTag)); obstacles = oQ.CalculateEntityCount(); oQ.Dispose();
            var hQ = em.CreateEntityQuery(typeof(HazardPadTag)); hazards = hQ.CalculateEntityCount(); hQ.Dispose();
            var cQ = em.CreateEntityQuery(typeof(CargoCrateTag)); crates = cQ.CalculateEntityCount(); cQ.Dispose();
            complexity = obstacles * 0.45f + hazards * 4.5f + crates * 3.0f + coresCount * 3.5f + drones * 8.0f + (bounds.RoomWidth * bounds.RoomHeight) * 0.06f;
            archetype = "Unknown";
        }

        private static string BuildLevelsCsv(List<SessionLevelRecord> rows)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("session_id,attempt_index,level_index,seed,outcome,room_width,room_height,total_cores,cores_collected,switch_flipped,airlock_unlocked,room_cleared,room_timer_sec,hazard_hits,deaths_in_attempt,cumulative_deaths,cumulative_death_rate_pct,drones_destroyed_delta,gravity_flips_delta,smart_player_attacks_delta,escapes_observed_delta,jetpack_overloads_delta,jetpack_boost_sec_delta,gravity_charges_remaining,total_escapes_completed,crate_dodges_attempted,crate_dodges_succeeded,avg_crate_escape_sec,escape_impulse_mult,trapped_threshold_sec,crate_dodge_skill,smart_attacks_observed,drone_improvement_pct,mean_posterior_confidence,mean_belief_entropy,final_belief_entropy,drone_count,obstacle_count,hazard_count,crate_count,complexity_score,lives_remaining,max_lives,level_archetype,gravity_predictability,movement_predictability,pattern_adaptation_pct");

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.AppendLine(string.Format(inv,
                    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12:F3},{13},{14},{15},{16:F2},{17},{18},{19},{20},{21},{22:F3},{23},{24},{25},{26},{27:F3},{28:F3},{29:F3},{30:F4},{31},{32:F2},{33:F4},{34:F4},{35:F4},{36},{37},{38},{39},{40:F2},{41},{42},{43},{44:F4},{45:F4},{46:F2}",
                    r.session_id, r.attempt_index, r.level_index, r.seed, r.outcome,
                    r.room_width, r.room_height, r.total_cores, r.cores_collected,
                    r.switch_flipped, r.airlock_unlocked, r.room_cleared, r.room_timer_sec,
                    r.hazard_hits, r.deaths_in_attempt, r.cumulative_deaths, r.cumulative_death_rate_pct,
                    r.drones_destroyed_delta, r.gravity_flips_delta, r.smart_player_attacks_delta, r.escapes_observed_delta,
                    r.jetpack_overloads_delta, r.jetpack_boost_sec_delta, r.gravity_charges_remaining,
                    r.total_escapes_completed, r.crate_dodges_attempted, r.crate_dodges_succeeded,
                    r.avg_crate_escape_sec, r.escape_impulse_mult, r.trapped_threshold_sec, r.crate_dodge_skill,
                    r.smart_attacks_observed, r.drone_improvement_pct,
                    r.mean_posterior_confidence, r.mean_belief_entropy, r.final_belief_entropy,
                    r.drone_count, r.obstacle_count, r.hazard_count, r.crate_count, r.complexity_score,
                    r.lives_remaining, r.max_lives, r.level_archetype ?? "Unknown",
                    r.gravity_predictability, r.movement_predictability, r.pattern_adaptation_pct));
            }
            return sb.ToString();
        }

        private static string BuildInputsCsv(List<PerInputAIEvent> rows)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("input_index,elapsed_sec,level_index,input_source,player_action,drone_action,prior_entropy,posterior_entropy,entropy_reduction,confidence,top_cluster_confidence,astar_nodes,replan_latency_ms,hazards_avoided,drone_improvement_pct,gravity_flips_total,jetpack_overloads_total,gravity_predictability,movement_predictability,pattern_adaptation_pct");

            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i];
                sb.AppendLine(string.Format(inv,
                    "{0},{1:F3},{2},{3},{4},{5},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11},{12:F4},{13},{14:F2},{15},{16},{17:F4},{18:F4},{19:F2}",
                    e.input_index, e.elapsed_sec, e.level_index, e.input_source,
                    e.player_action, e.drone_action, e.prior_entropy, e.posterior_entropy,
                    e.entropy_reduction, e.confidence, e.top_cluster_confidence,
                    e.astar_nodes, e.replan_latency_ms, e.hazards_avoided,
                    e.drone_improvement_pct, e.gravity_flips_total, e.jetpack_overloads_total,
                    e.gravity_predictability, e.movement_predictability, e.pattern_adaptation_pct));
            }
            return sb.ToString();
        }

        private string BuildSummaryJson(List<SessionLevelRecord> rows)
        {
            var inv = CultureInfo.InvariantCulture;
            var distinctLevels = new HashSet<int>();
            var clearedLevels = new HashSet<int>();
            var perLevelAttempts = new Dictionary<int, (int attempts, int deaths, int clears)>();

            int totalDeaths = 0;
            int totalFlips = 0;
            int totalOverloads = 0;
            float totalBoostSec = 0f;
            float startImp = initialImprovementPct >= 0f ? initialImprovementPct : 0f;
            float endImp = startImp;
            float startSkill = initialDodgeSkill >= 0f ? initialDodgeSkill : 0.15f;
            float endSkill = startSkill;

            int latestMaxLives = 5, latestLivesRem = 5;
            float latestPatAdapt = 0f;
            bool anyCompletedRun = false;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                distinctLevels.Add(r.level_index);
                if (r.room_cleared) clearedLevels.Add(r.level_index);
                if (r.level_index >= LevelObjectiveState.MaxRunLevels && r.room_cleared) anyCompletedRun = true;

                totalDeaths += r.deaths_in_attempt;
                totalFlips += r.gravity_flips_delta;
                totalOverloads += r.jetpack_overloads_delta;
                totalBoostSec += r.jetpack_boost_sec_delta;
                endImp = r.drone_improvement_pct;
                endSkill = r.crate_dodge_skill;
                latestMaxLives = math.max(3, r.max_lives);
                latestLivesRem = r.lives_remaining;
                latestPatAdapt = r.pattern_adaptation_pct;

                if (!perLevelAttempts.TryGetValue(r.level_index, out var entry)) entry = (0, 0, 0);
                entry.attempts++;
                entry.deaths += r.deaths_in_attempt;
                if (r.room_cleared) entry.clears++;
                perLevelAttempts[r.level_index] = entry;
            }

            float overallDeathRate = rows.Count > 0 ? (100f * totalDeaths / rows.Count) : 0f;

            float sumEntRed = 0f;
            for (int i = 0; i < inputEvents.Count; i++) sumEntRed += inputEvents[i].entropy_reduction;
            float meanEntRed = inputEvents.Count > 0 ? (sumEntRed / inputEvents.Count) : 0f;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"session_id\": \"{CurrentSessionId}\",");
            sb.AppendLine($"  \"started_utc\": \"{sessionStartUtc:O}\",");
            sb.AppendLine($"  \"ended_utc\": \"{DateTime.UtcNow:O}\",");
            sb.AppendLine(string.Format(inv, "  \"duration_sec\": {0:F2},", sessionElapsedSec));
            sb.AppendLine($"  \"levels_played_count\": {distinctLevels.Count},");
            sb.AppendLine($"  \"max_run_levels\": {LevelObjectiveState.MaxRunLevels},");
            sb.AppendLine($"  \"levels_cleared_count\": {clearedLevels.Count},");
            sb.AppendLine($"  \"max_lives\": {latestMaxLives},");
            sb.AppendLine($"  \"lives_remaining\": {latestLivesRem},");
            sb.AppendLine($"  \"run_completed\": {anyCompletedRun.ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"total_attempts\": {rows.Count},");
            sb.AppendLine($"  \"total_deaths\": {totalDeaths},");
            sb.AppendLine(string.Format(inv, "  \"overall_death_rate_pct\": {0:F2},", overallDeathRate));
            sb.AppendLine(string.Format(inv, "  \"initial_drone_improvement_pct\": {0:F2},", startImp));
            sb.AppendLine(string.Format(inv, "  \"final_drone_improvement_pct\": {0:F2},", endImp));
            sb.AppendLine(string.Format(inv, "  \"improvement_delta_pct\": {0:F2},", endImp - startImp));
            sb.AppendLine(string.Format(inv, "  \"final_pattern_adaptation_pct\": {0:F2},", latestPatAdapt));
            sb.AppendLine(string.Format(inv, "  \"initial_crate_dodge_skill\": {0:F4},", startSkill));
            sb.AppendLine(string.Format(inv, "  \"final_crate_dodge_skill\": {0:F4},", endSkill));
            sb.AppendLine(string.Format(inv, "  \"mean_entropy_reduction_bits\": {0:F4},", meanEntRed));
            sb.AppendLine($"  \"total_gravity_flips\": {totalFlips},");
            sb.AppendLine($"  \"total_jetpack_overloads\": {totalOverloads},");
            sb.AppendLine(string.Format(inv, "  \"total_boost_sec\": {0:F2},", totalBoostSec));
            sb.AppendLine($"  \"total_inputs_logged\": {inputEvents.Count},");

            sb.AppendLine("  \"per_level_death_rates\": [");
            var sortedLvls = new List<int>(perLevelAttempts.Keys);
            sortedLvls.Sort();
            for (int i = 0; i < sortedLvls.Count; i++)
            {
                int l = sortedLvls[i];
                var e = perLevelAttempts[l];
                float rate = e.attempts > 0 ? (100f * e.deaths / e.attempts) : 0f;
                string comma = (i < sortedLvls.Count - 1) ? "," : "";
                sb.AppendLine(string.Format(inv,
                    "    {{ \"level_index\": {0}, \"attempts\": {1}, \"deaths\": {2}, \"clears\": {3}, \"death_rate_pct\": {4:F2} }}{5}",
                    l, e.attempts, e.deaths, e.clears, rate, comma));
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"input_source_counts\": {");
            var keys = new List<string>(inputSourceCounts.Keys);
            keys.Sort();
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                string comma = (i < keys.Count - 1) ? "," : "";
                sb.AppendLine($"    \"{k}\": {inputSourceCounts[k]}{comma}");
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
