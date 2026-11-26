using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

namespace GravityDrift
{
    public class GravityDriftBootstrap : MonoBehaviour
    {
        [Header("VFX Prefabs")]
        public GameObject smallExplosionPrefab;
        public GameObject energyExplosionPrefab;

        [Header("Editable UI Canvas References")]
        [SerializeField] private Canvas fadeCanvas;
        [SerializeField] private Image fadeOverlayImg;

        private World world;
        private EntityManager em;
        private Entity playerEntity;
        private List<(Entity entity, Transform tf, Rigidbody rb)> syncList = new List<(Entity, Transform, Rigidbody)>();
        private bool isInitialized = false;
        private MaterialPropertyBlock playerPropBlock;
        private bool wasPlayerOverloaded = false;

        private readonly List<GameObject> spawnedVfxList = new List<GameObject>();
        private Coroutine deathRespawnCoroutine;
        private bool isDeathSequenceRunning = false;
        private bool runOverOpenedForRun = false;

        private bool prevSwitchFlipped = false;
        private bool prevAirlockUnlocked = false;
        private int prevCoresCollected = 0;
        private bool prevWasBoosting = false;
        private bool prevWasOverloaded = false;

        private void Awake()
        {
            Application.runInBackground = true;
            playerPropBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            InitializeECS();
        }

        public void InitializeECS()
        {
            if (isInitialized) return;

            world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                world = DefaultWorldInitialization.Initialize("GravityDriftWorld", false);
                World.DefaultGameObjectInjectionWorld = world;
            }

            em = world.EntityManager;

            // 1. PhysicsStep
            var stepQuery = em.CreateEntityQuery(typeof(PhysicsStep));
            if (stepQuery.CalculateEntityCount() == 0)
            {
                var stepEntity = em.CreateEntity(typeof(PhysicsStep));
                em.SetComponentData(stepEntity, new PhysicsStep
                {
                    Gravity = new float3(0, -25f, 0)
                });
            }
            stepQuery.Dispose();

            // 2. GravityState
            var gravQuery = em.CreateEntityQuery(typeof(GravityState));
            if (gravQuery.CalculateEntityCount() == 0)
            {
                var gravEntity = em.CreateEntity(typeof(GravityState));
                em.SetComponentData(gravEntity, new GravityState
                {
                    CurrentDirection = GravityDirection.Down,
                    PreviousDirection = GravityDirection.Down,
                    GravityMagnitude = 25f,
                    GravityVector = new float3(0, -25f, 0),
                    TimeSinceLastFlip = 0f,
                    TotalFlips = 0,
                    DirectionChangedThisFrame = false,
                    CurrentCharges = 3,
                    MaxCharges = 3,
                    RechargeTimer = 0f,
                    RechargeCooldown = 1.5f
                });
            }
            gravQuery.Dispose();

            // 3. RoomBoundsState
            var boundsQuery = em.CreateEntityQuery(typeof(RoomBoundsState));
            if (boundsQuery.CalculateEntityCount() == 0)
            {
                var bEntity = em.CreateEntity(typeof(RoomBoundsState));
                em.SetComponentData(bEntity, new RoomBoundsState
                {
                    RoomWidth = 20,
                    RoomHeight = 12,
                    RoomOrigin = new float2(-10f, -6f),
                    InnerMinX = -9f,
                    InnerMaxX = 9f,
                    InnerMinY = -5f,
                    InnerMaxY = 5f
                });
            }
            boundsQuery.Dispose();

            // 4. CameraFeedbackState
            var fbQuery = em.CreateEntityQuery(typeof(CameraFeedbackState));
            if (fbQuery.CalculateEntityCount() == 0)
            {
                em.CreateEntity(typeof(CameraFeedbackState));
            }
            fbQuery.Dispose();

            // 4b. AudioEventState
            var aeQuery = em.CreateEntityQuery(typeof(AudioEventState));
            if (aeQuery.CalculateEntityCount() == 0)
            {
                em.CreateEntity(typeof(AudioEventState));
            }
            aeQuery.Dispose();

            // Ensure CameraFeedbackController, SoundManager, HapticFeedbackSystem, and SessionReportRecorder exist
            var mainCam = Camera.main;
            if (mainCam != null && mainCam.GetComponent<CameraFeedbackController>() == null)
            {
                mainCam.gameObject.AddComponent<CameraFeedbackController>();
            }
            if (FindAnyObjectByType<SoundManager>() == null)
            {
                gameObject.AddComponent<SoundManager>();
            }
            if (FindAnyObjectByType<HapticFeedbackSystem>() == null)
            {
                gameObject.AddComponent<HapticFeedbackSystem>();
            }
            if (FindAnyObjectByType<SessionReportRecorder>() == null)
            {
                gameObject.AddComponent<SessionReportRecorder>();
            }

            // 5. LevelObjectiveState
            var objQuery = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQuery.CalculateEntityCount() == 0)
            {
                var objEntity = em.CreateEntity(typeof(LevelObjectiveState));
                em.SetComponentData(objEntity, new LevelObjectiveState
                {
                    LevelIndex = 1,
                    TotalCores = 0,
                    CoresCollected = 0,
                    TotalSwitches = 1,
                    SwitchesActive = 0,
                    SwitchFlipped = false,
                    AirlockUnlocked = false,
                    DoorSlideProgress = 0f,
                    AirlockFullyOpen = false,
                    RoomCleared = false,
                    RoomTimer = 0f,
                    CurrentSeed = 101,
                    HazardHits = 0,
                    HazardWarningTimer = 0f,
                    CurrentPlayerAction = PlayerActionType.Idle,
                    MaxLives = 5,
                    LivesRemaining = 5,
                    LevelStartGraceTimer = 0f,
                    RespawnGraceTimer = 0f,
                    IsRespawning = false,
                    PendingDeathSequence = false,
                    IsRunOver = false,
                    IsRunCompleted = false
                });
            }
            objQuery.Dispose();

            // 6. DroneLearningState
            var learnQuery = em.CreateEntityQuery(typeof(DroneLearningState));
            if (learnQuery.CalculateEntityCount() == 0)
            {
                var learnEntity = em.CreateEntity(typeof(DroneLearningState));
                em.SetComponentData(learnEntity, new DroneLearningState
                {
                    TotalEscapesCompleted = 0,
                    TotalCrateDodgesAttempted = 0,
                    TotalCrateDodgesSucceeded = 0,
                    AverageEscapeTimeSec = 1.45f,
                    EscapeImpulseMultiplier = 1.05f,
                    TrappedDetectionThreshold = 0.80f,
                    CrateDodgeSkill = 0.22f,
                    SmartPlayerAttacksObserved = 0,
                    DroneImprovementPct = 12f,
                    CurrentLevelIndex = 1,
                    ActiveDroneCount = 1,
                    ConsecutiveEntropyRises = 0,
                    AdaptationStepStreak = 0,
                    OutplayedEvents = 0,
                    RegressionMomentum = 0f
                });
            }
            learnQuery.Dispose();

            // 7. PlayerCounterplayStats
            var statsQuery = em.CreateEntityQuery(typeof(PlayerCounterplayStats));
            if (statsQuery.CalculateEntityCount() == 0)
            {
                em.CreateEntity(typeof(PlayerCounterplayStats));
            }
            statsQuery.Dispose();

            // 8. Register Player Entity
            RegisterPlayerEntity();

            isInitialized = true;
        }

        public void SetRoomBounds(int width, int height, Vector2 origin)
        {
            InitializeECS();
            GridAStar.SetActiveBounds(width, height, new float2(origin.x, origin.y));

            var boundsQuery = em.CreateEntityQuery(typeof(RoomBoundsState));
            if (boundsQuery.CalculateEntityCount() > 0)
            {
                using var bArr = boundsQuery.ToEntityArray(Allocator.Temp);
                em.SetComponentData(bArr[0], new RoomBoundsState
                {
                    RoomWidth = width,
                    RoomHeight = height,
                    RoomOrigin = new float2(origin.x, origin.y),
                    InnerMinX = origin.x + 1.0f,
                    InnerMaxX = origin.x + width - 1.0f,
                    InnerMinY = origin.y + 1.0f,
                    InnerMaxY = origin.y + height - 1.0f
                });
            }
            boundsQuery.Dispose();
        }

        private void RegisterPlayerEntity()
        {
            if (playerEntity != Entity.Null && em.Exists(playerEntity))
            {
                return;
            }

            var existingPlayerQuery = em.CreateEntityQuery(typeof(PlayerTag));
            if (existingPlayerQuery.CalculateEntityCount() > 0)
            {
                using var pArr = existingPlayerQuery.ToEntityArray(Allocator.Temp);
                playerEntity = pArr[0];
                existingPlayerQuery.Dispose();
                var pAuth = FindAnyObjectByType<PlayerAuthoring>();
                if (pAuth != null)
                {
                    var pRb = pAuth.GetComponent<Rigidbody>();
                    if (pRb != null)
                    {
                        pRb.useGravity = false;
                        pRb.isKinematic = true; // Driven cleanly by PlayerMovementSystem ECS integration!
                    }
                    syncList.Add((playerEntity, pAuth.transform, pRb));
                }
                return;
            }
            existingPlayerQuery.Dispose();

            var playerAuth = FindAnyObjectByType<PlayerAuthoring>();
            if (playerAuth != null)
            {
                var pTf = playerAuth.transform;
                var pRb = playerAuth.GetComponent<Rigidbody>();
                if (pRb != null)
                {
                    pRb.useGravity = false;
                    pRb.isKinematic = true;
                    pRb.linearDamping = 2.0f;
                    pRb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
                }

                playerEntity = em.CreateEntity(
                    typeof(PlayerTag),
                    typeof(PlayerMovement),
                    typeof(JetpackComponent),
                    typeof(PhysicsGravityFactor),
                    typeof(PlanarPhysics2DTag),
                    typeof(ResetableEntity),
                    typeof(LocalTransform),
                    typeof(PhysicsVelocity)
                );

                em.SetComponentData(playerEntity, new PhysicsGravityFactor { Value = 0f });
                em.SetComponentData(playerEntity, new PlayerMovement
                {
                    MoveSpeed = playerAuth.maxSpeed,
                    JumpImpulse = 0f,
                    AirControlFactor = 1.0f,
                    RotationSpeed = playerAuth.rotationSpeed,
                    IsGrounded = false,
                    GroundCheckRayDistance = 0.6f,
                    SpawnPosition = pTf.position,
                    SpawnRotation = pTf.rotation,
                    CurrentVelocity = float3.zero
                });

                em.SetComponentData(playerEntity, new JetpackComponent
                {
                    Fuel = playerAuth.maxFuel,
                    MaxFuel = playerAuth.maxFuel,
                    BurnRate = playerAuth.burnRate,
                    BoostBurnRate = playerAuth.boostBurnRate,
                    RechargeRate = playerAuth.rechargeRate,
                    ThrustForce = playerAuth.thrustForce,
                    BoostMultiplier = playerAuth.boostMultiplier,
                    MaxSpeed = playerAuth.maxSpeed,
                    CooldownTimer = 0f,
                    CooldownDuration = playerAuth.cooldownDuration,
                    IsInCooldown = false,
                    IsThrusting = false,
                    IsBoosting = false,
                    ThrustDirection = new float3(0, 1, 0)
                });

                em.SetComponentData(playerEntity, new PlanarPhysics2DTag { LockZPosition = 0f });
                em.SetComponentData(playerEntity, new ResetableEntity
                {
                    SpawnPosition = pTf.position,
                    SpawnRotation = pTf.rotation,
                    PendingReset = false
                });

                em.SetComponentData(playerEntity, LocalTransform.FromPositionRotation(pTf.position, pTf.rotation));
                em.SetComponentData(playerEntity, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero });

                syncList.Add((playerEntity, pTf, pRb));
            }
        }

        public void ClearProceduralEntities()
        {
            CleanupTempVfx();
            if (world == null || !world.IsCreated) return;

            var procQuery = em.CreateEntityQuery(typeof(ProceduralLevelTag));
            em.DestroyEntity(procQuery);
            procQuery.Dispose();

            syncList.RemoveAll(item => !em.Exists(item.entity) || !em.HasComponent<PlayerTag>(item.entity));
        }

        public void ResetRunLives(int maxLives)
        {
            InitializeECS();
            AbortDeathRespawnSequence();
            runOverOpenedForRun = false;
            int clamped = math.clamp(maxLives, 3, 5);
            var objQuery = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQuery.CalculateEntityCount() > 0)
            {
                using var objArr = objQuery.ToEntityArray(Allocator.Temp);
                var lvl = em.GetComponentData<LevelObjectiveState>(objArr[0]);
                lvl.MaxLives = clamped;
                lvl.LivesRemaining = clamped;
                lvl.LevelStartGraceTimer = 0f;
                lvl.RespawnGraceTimer = 0f;
                lvl.IsRespawning = false;
                lvl.PendingDeathSequence = false;
                lvl.IsRunOver = false;
                lvl.IsRunCompleted = false;
                em.SetComponentData(objArr[0], lvl);
            }
            objQuery.Dispose();
        }

        private void ResetAudioEdgeTrackers()
        {
            prevSwitchFlipped = false;
            prevAirlockUnlocked = false;
            prevCoresCollected = 0;
            prevWasBoosting = false;
            prevWasOverloaded = false;
            SoundManager.Instance?.SetJetpackThrustLoop(false, false);
            SoundManager.Instance?.SetDeathDuck(false);
        }

        public void ResetToMenuIdleState()
        {
            InitializeECS();
            AbortDeathRespawnSequence();
            runOverOpenedForRun = false;
            ResetAudioEdgeTrackers();
            ClearProceduralEntities();

            var objQuery = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQuery.CalculateEntityCount() > 0)
            {
                using var objArr = objQuery.ToEntityArray(Allocator.Temp);
                var lvl = em.GetComponentData<LevelObjectiveState>(objArr[0]);
                lvl.RoomTimer = 0f;
                lvl.RoomCleared = false;
                lvl.SwitchFlipped = false;
                lvl.AirlockUnlocked = false;
                lvl.AirlockFullyOpen = false;
                lvl.DoorSlideProgress = 0f;
                lvl.HazardHits = 0;
                lvl.HazardWarningTimer = 0f;
                lvl.CurrentPlayerAction = PlayerActionType.Idle;
                lvl.LivesRemaining = math.max(3, lvl.MaxLives);
                lvl.LevelStartGraceTimer = 0f;
                lvl.RespawnGraceTimer = 0f;
                lvl.IsRespawning = false;
                lvl.PendingDeathSequence = false;
                lvl.IsRunOver = false;
                lvl.IsRunCompleted = false;
                em.SetComponentData(objArr[0], lvl);
            }
            objQuery.Dispose();

            var gravQuery = em.CreateEntityQuery(typeof(GravityState));
            if (gravQuery.CalculateEntityCount() > 0)
            {
                using var gArr = gravQuery.ToEntityArray(Allocator.Temp);
                var g = em.GetComponentData<GravityState>(gArr[0]);
                g.CurrentDirection = GravityDirection.Down;
                g.PreviousDirection = GravityDirection.Down;
                g.GravityVector = new float3(0f, -g.GravityMagnitude, 0f);
                g.DirectionChangedThisFrame = false;
                g.TotalFlips = 0;
                g.CurrentCharges = g.MaxCharges;
                em.SetComponentData(gArr[0], g);
            }
            gravQuery.Dispose();

            var playerAuth = FindAnyObjectByType<PlayerAuthoring>();
            if (playerAuth != null && em.Exists(playerEntity))
            {
                em.SetComponentData(playerEntity, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero });
                var jet = em.GetComponentData<JetpackComponent>(playerEntity);
                jet.Fuel = jet.MaxFuel;
                jet.IsInCooldown = false;
                jet.CooldownTimer = 0f;
                jet.IsThrusting = false;
                jet.IsBoosting = false;
                jet.OverloadCount = 0;
                jet.TotalBoostTime = 0f;
                em.SetComponentData(playerEntity, jet);

                ApplyPlayerOverloadVisual(playerAuth.transform, false);
                var ps = playerAuth.GetComponentInChildren<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.rateOverTime = 0f;
                }
            }

            HapticFeedbackSystem.StopAllHaptics();
        }

        public void RebuildLevelEntities(int levelIndex, int seed, int droneCount, int coreCount)
        {
            InitializeECS();
            AbortDeathRespawnSequence();
            ResetAudioEdgeTrackers();
            ClearProceduralEntities();

            var objQuery = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQuery.CalculateEntityCount() > 0)
            {
                using var objEntities = objQuery.ToEntityArray(Allocator.Temp);
                var objEntity = objEntities[0];
                var state = em.GetComponentData<LevelObjectiveState>(objEntity);
                state.LevelIndex = levelIndex;
                state.CurrentSeed = seed;
                state.TotalCores = coreCount;
                state.CoresCollected = 0;
                state.TotalSwitches = 1;
                state.SwitchesActive = 0;
                state.SwitchFlipped = false;
                state.AirlockUnlocked = false;
                state.DoorSlideProgress = 0f;
                state.AirlockFullyOpen = false;
                state.RoomCleared = false;
                state.RoomTimer = 0f;
                state.HazardHits = 0;
                state.HazardWarningTimer = 0f;
                state.CurrentPlayerAction = PlayerActionType.Idle;
                if (state.MaxLives < 3) state.MaxLives = 5;
                if (state.LivesRemaining <= 0 || state.IsRunOver || state.IsRunCompleted)
                {
                    state.LivesRemaining = state.MaxLives;
                }
                state.LevelStartGraceTimer = 1.0f;
                state.RespawnGraceTimer = 0f;
                state.IsRespawning = false;
                state.PendingDeathSequence = false;
                state.IsRunOver = false;
                state.IsRunCompleted = false;
                runOverOpenedForRun = false;
                em.SetComponentData(objEntity, state);
            }
            objQuery.Dispose();

            // Reset physics & player gravity direction/charges between levels
            var stepQuery = em.CreateEntityQuery(typeof(PhysicsStep));
            if (stepQuery.CalculateEntityCount() > 0)
            {
                using var stepEntities = stepQuery.ToEntityArray(Allocator.Temp);
                em.SetComponentData(stepEntities[0], new PhysicsStep
                {
                    Gravity = new float3(0f, -25f, 0f)
                });
            }
            stepQuery.Dispose();

            var gravQuery = em.CreateEntityQuery(typeof(GravityState));
            if (gravQuery.CalculateEntityCount() > 0)
            {
                using var gravEntities = gravQuery.ToEntityArray(Allocator.Temp);
                var gState = em.GetComponentData<GravityState>(gravEntities[0]);
                float mag = gState.GravityMagnitude > 0.1f ? gState.GravityMagnitude : 25f;
                int maxCharges = gState.MaxCharges > 0 ? gState.MaxCharges : 3;
                gState.CurrentDirection = GravityDirection.Down;
                gState.PreviousDirection = GravityDirection.Down;
                gState.GravityMagnitude = mag;
                gState.GravityVector = new float3(0f, -mag, 0f);
                gState.TimeSinceLastFlip = 0f;
                gState.DirectionChangedThisFrame = false;
                gState.CurrentCharges = maxCharges;
                gState.MaxCharges = maxCharges;
                gState.RechargeTimer = 0f;
                em.SetComponentData(gravEntities[0], gState);
            }
            gravQuery.Dispose();

            var learnQuery = em.CreateEntityQuery(typeof(DroneLearningState));
            if (learnQuery.CalculateEntityCount() > 0)
            {
                using var learnEntities = learnQuery.ToEntityArray(Allocator.Temp);
                var learnEntity = learnEntities[0];
                var lState = em.GetComponentData<DroneLearningState>(learnEntity);
                lState.CurrentLevelIndex = levelIndex;
                lState.ActiveDroneCount = droneCount;
                if (levelIndex > 1)
                {
                    // New procedural room layout introduces genuine uncertainty/regression rather than forcing upward plateaus
                    lState.CrateDodgeSkill = math.max(0.10f, lState.CrateDodgeSkill - 0.04f);
                    lState.EscapeImpulseMultiplier = math.max(0.90f, lState.EscapeImpulseMultiplier - 0.05f);
                    lState.TrappedDetectionThreshold = math.min(0.90f, lState.TrappedDetectionThreshold + 0.03f);
                    lState.RegressionMomentum = math.clamp(lState.RegressionMomentum * 0.5f - 0.08f, -1f, 1f);
                    lState.ConsecutiveEntropyRises = 0;
                }
                em.SetComponentData(learnEntity, lState);
            }
            learnQuery.Dispose();

            var playerAuth = FindAnyObjectByType<PlayerAuthoring>();
            if (playerAuth != null && em.Exists(playerEntity))
            {
                var pTf = playerAuth.transform;
                var pRb = playerAuth.GetComponent<Rigidbody>();
                if (pRb != null)
                {
                    pRb.useGravity = false;
                    pRb.isKinematic = true;
                }
                em.SetComponentData(playerEntity, LocalTransform.FromPositionRotation(pTf.position, pTf.rotation));
                em.SetComponentData(playerEntity, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero });
                em.SetComponentData(playerEntity, new ResetableEntity
                {
                    SpawnPosition = pTf.position,
                    SpawnRotation = pTf.rotation,
                    PendingReset = false
                });
                var jet = em.GetComponentData<JetpackComponent>(playerEntity);
                jet.Fuel = jet.MaxFuel;
                jet.IsInCooldown = false;
                jet.CooldownTimer = 0f;
                jet.OverloadCount = 0;
                jet.TotalBoostTime = 0f;
                em.SetComponentData(playerEntity, jet);

                ApplyPlayerOverloadVisual(pTf, false);
            }

            var stationKits = FindObjectsByType<StationKitAuthoring>(FindObjectsInactive.Exclude);
            foreach (var kit in stationKits)
            {
                var tf = kit.transform;
                var rb = kit.GetComponent<Rigidbody>();

                if (kit.pieceType == StationPieceType.CargoCrate)
                {
                    if (rb != null)
                    {
                        rb.useGravity = false;
                        rb.isKinematic = false;
                    }
                    var crateEntity = em.CreateEntity(
                        typeof(ProceduralLevelTag),
                        typeof(CargoCrateTag),
                        typeof(PhysicsGravityFactor),
                        typeof(PlanarPhysics2DTag),
                        typeof(ResetableEntity),
                        typeof(LocalTransform),
                        typeof(PhysicsVelocity)
                    );

                    em.SetComponentData(crateEntity, new ProceduralLevelTag { LevelIndex = levelIndex, LevelSeed = seed });
                    em.SetComponentData(crateEntity, new PhysicsGravityFactor { Value = 1.0f });
                    em.SetComponentData(crateEntity, new PlanarPhysics2DTag { LockZPosition = 0f });
                    em.SetComponentData(crateEntity, new ResetableEntity { SpawnPosition = tf.position, SpawnRotation = tf.rotation, PendingReset = false });
                    em.SetComponentData(crateEntity, LocalTransform.FromPositionRotation(tf.position, tf.rotation));
                    em.SetComponentData(crateEntity, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero });

                    var impactRelay = kit.GetComponent<CargoCrateImpactRelay>();
                    if (impactRelay == null) impactRelay = kit.gameObject.AddComponent<CargoCrateImpactRelay>();
                    impactRelay.CrateEntity = crateEntity;
                    impactRelay.PrePhysicsVelocity = Vector3.zero;
                    impactRelay.ImpactFrozen = false;

                    syncList.Add((crateEntity, tf, rb));
                }
                else if (kit.pieceType == StationPieceType.WallModule || kit.pieceType == StationPieceType.FloorPanel)
                {
                    var wallEntity = em.CreateEntity(
                        typeof(ProceduralLevelTag),
                        typeof(StationObstacleTag),
                        typeof(LocalTransform)
                    );
                    em.SetComponentData(wallEntity, new ProceduralLevelTag { LevelIndex = levelIndex, LevelSeed = seed });
                    em.SetComponentData(wallEntity, LocalTransform.FromPositionRotation(tf.position, tf.rotation));
                }
                else if (kit.pieceType == StationPieceType.HazardPad)
                {
                    var hazardEntity = em.CreateEntity(
                        typeof(ProceduralLevelTag),
                        typeof(HazardPadTag),
                        typeof(LocalTransform)
                    );
                    em.SetComponentData(hazardEntity, new ProceduralLevelTag { LevelIndex = levelIndex, LevelSeed = seed });
                    em.SetComponentData(hazardEntity, new HazardPadTag
                    {
                        Position = tf.position,
                        PenaltyFuelDrain = 25f + math.min(20f, (levelIndex - 1) * 2.5f),
                        KnockbackImpulse = 7.5f + math.min(3.5f, (levelIndex - 1) * 0.4f)
                    });
                    em.SetComponentData(hazardEntity, LocalTransform.FromPositionRotation(tf.position, tf.rotation));
                }
            }

            var drones = FindObjectsByType<DroneAuthoring>(FindObjectsInactive.Exclude);
            int dIndex = 1;
            foreach (var dAuth in drones)
            {
                var dTf = dAuth.transform;
                var dRb = dAuth.GetComponent<Rigidbody>();
                if (dRb != null)
                {
                    dRb.useGravity = false;
                    dRb.isKinematic = true;
                }

                var dEntity = em.CreateEntity(
                    typeof(ProceduralLevelTag),
                    typeof(DroneAITag),
                    typeof(DroneState),
                    typeof(DroneTelemetry),
                    typeof(BayesianBeliefComponent),
                    typeof(PhysicsGravityFactor),
                    typeof(PlanarPhysics2DTag),
                    typeof(ResetableEntity),
                    typeof(LocalTransform),
                    typeof(PhysicsVelocity)
                );

                em.SetComponentData(dEntity, new ProceduralLevelTag { LevelIndex = levelIndex, LevelSeed = seed });
                em.SetComponentData(dEntity, new PhysicsGravityFactor { Value = 0f });
                em.SetComponentData(dEntity, new DroneState
                {
                    DroneID = dIndex++,
                    CurrentState = DroneFSMState.Patrol,
                    PreviousState = DroneFSMState.Patrol,
                    ActiveAction = DroneActionType.Patrol,
                    StateTimer = 0f,
                    MoveSpeed = dAuth.moveSpeed,
                    DetectionRadius = dAuth.detectionRadius,
                    LoseTargetRadius = dAuth.loseTargetRadius,
                    TargetPosition = dTf.position,
                    PatrolPointA = dAuth.patrolPointA,
                    PatrolPointB = dAuth.patrolPointB,
                    CurrentPatrolIndex = 0,
                    CurrentPathIndex = 0,
                    PathReplanCooldown = 0f,
                    StuckTimer = 0f,
                    LastPosition = dTf.position,
                    HasMagBoots = true,
                    IsUnlatched = false,
                    UnlatchedDuration = 0f,
                    IsDestroyed = false,
                    BaselineMode = dAuth.baselineMode,
                    TrappedByCrateTimer = 0f,
                    IsTrappedByCrate = false,
                    CrateEscapeTime = 0f,
                    CrateDodgeCooldown = 0f,
                    CrateDodgesAttempted = 0,
                    CrateDodgesSucceeded = 0,
                    PendingDodgeOutcome = false,
                    PendingDodgeTimer = 0f,
                    ReorientCountRecent = 0
                });

                em.SetComponentData(dEntity, new DroneTelemetry
                {
                    NodesExpanded = 0,
                    ReplanLatencyMs = 0f,
                    TargetReached = false,
                    StuckOnObstacle = false,
                    CurrentBeliefEntropy = 4.5f,
                    HazardCellsAvoided = 0
                });

                em.SetComponentData(dEntity, new BayesianBeliefComponent
                {
                    Entropy = 4.5f,
                    PriorEntropy = 4.5f,
                    EstimatedTargetCell = GridAStar.WorldToGrid(dTf.position),
                    EstimatedWorldPos = dTf.position,
                    Confidence = 0.05f,
                    TopClusterConfidence = 0.15f,
                    TimeSinceLastSighting = 99f,
                    DiffusionRate = 0.08f,
                    HasVisualContact = false,
                    LastPlayerAction = PlayerActionType.Idle,
                    LastDroneAction = DroneActionType.Patrol,
                    LikelihoodSharpness = 1.0f,
                    TransitionDriftWeight = 0.12f
                });

                em.SetComponentData(dEntity, new PlanarPhysics2DTag { LockZPosition = 0f });
                em.SetComponentData(dEntity, new ResetableEntity { SpawnPosition = dTf.position, SpawnRotation = dTf.rotation, PendingReset = false });
                em.SetComponentData(dEntity, LocalTransform.FromPositionRotation(dTf.position, dTf.rotation));
                em.SetComponentData(dEntity, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero });

                em.AddBuffer<GridCell>(dEntity);
                em.AddBuffer<BeliefCell>(dEntity);

                syncList.Add((dEntity, dTf, dRb));
            }

            int coreIdx = 1;
            foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude))
            {
                if (go.name.StartsWith("EnergyCore"))
                {
                    var coreEntity = em.CreateEntity(typeof(ProceduralLevelTag), typeof(EnergyCoreTag), typeof(LocalTransform));
                    em.SetComponentData(coreEntity, new ProceduralLevelTag { LevelIndex = levelIndex, LevelSeed = seed });
                    em.SetComponentData(coreEntity, new EnergyCoreTag
                    {
                        CoreID = coreIdx++,
                        IsCollected = false,
                        SpawnPosition = go.transform.position
                    });
                    em.SetComponentData(coreEntity, LocalTransform.FromPositionRotation(go.transform.position, go.transform.rotation));
                    syncList.Add((coreEntity, go.transform, null));
                }
                else if (go.name.StartsWith("ProximitySwitch") || go.name.StartsWith("PressureSwitch"))
                {
                    var switchEntity = em.CreateEntity(typeof(ProceduralLevelTag), typeof(PressureSwitchTag), typeof(LocalTransform));
                    em.SetComponentData(switchEntity, new ProceduralLevelTag { LevelIndex = levelIndex, LevelSeed = seed });
                    em.SetComponentData(switchEntity, new PressureSwitchTag
                    {
                        SwitchID = 1,
                        IsPressed = false,
                        SwitchPosition = go.transform.position,
                        ProximityRadius = 1.35f
                    });
                    em.SetComponentData(switchEntity, LocalTransform.FromPositionRotation(go.transform.position, go.transform.rotation));
                    syncList.Add((switchEntity, go.transform, null));
                }
                else if (go.name.StartsWith("ExitAirlock"))
                {
                    var airlockEntity = em.CreateEntity(typeof(ProceduralLevelTag), typeof(ExitAirlockTag), typeof(LocalTransform));
                    em.SetComponentData(airlockEntity, new ProceduralLevelTag { LevelIndex = levelIndex, LevelSeed = seed });
                    em.SetComponentData(airlockEntity, new ExitAirlockTag
                    {
                        IsUnlocked = false,
                        DoorPosition = go.transform.position,
                        ClosedPosition = go.transform.position,
                        OpenPosition = go.transform.position,
                        SlideProgress = 0f
                    });
                    em.SetComponentData(airlockEntity, LocalTransform.FromPositionRotation(go.transform.position, go.transform.rotation));
                    syncList.Add((airlockEntity, go.transform, null));
                }
            }

            Debug.Log($"<color=#00ff88><b>[BOOTSTRAP] Level {levelIndex} (Seed #{seed}) Re-built with {syncList.Count} synced entities!</b></color>");
            SessionReportRecorder.Instance?.NotifyLevelStarted(levelIndex, seed);
        }

        private RoomBoundsState GetActiveBounds()
        {
            var bQuery = em.CreateEntityQuery(typeof(RoomBoundsState));
            RoomBoundsState b = new RoomBoundsState
            {
                RoomWidth = GridAStar.ActiveWidth,
                RoomHeight = GridAStar.ActiveHeight,
                RoomOrigin = GridAStar.ActiveOrigin,
                InnerMinX = GridAStar.ActiveOrigin.x + 1.0f,
                InnerMaxX = GridAStar.ActiveOrigin.x + GridAStar.ActiveWidth - 1.0f,
                InnerMinY = GridAStar.ActiveOrigin.y + 1.0f,
                InnerMaxY = GridAStar.ActiveOrigin.y + GridAStar.ActiveHeight - 1.0f
            };
            if (bQuery.CalculateEntityCount() > 0)
            {
                using var arr = bQuery.ToComponentDataArray<RoomBoundsState>(Allocator.Temp);
                b = arr[0];
            }
            bQuery.Dispose();
            return b;
        }

        private static void ClampVectorInsideBounds(ref Vector3 pos, ref Vector3 vel, RoomBoundsState bounds, float margin)
        {
            float minX = bounds.InnerMinX + margin;
            float maxX = bounds.InnerMaxX - margin;
            float minY = bounds.InnerMinY + margin;
            float maxY = bounds.InnerMaxY - margin;

            pos.z = 0f;
            vel.z = 0f;

            if (pos.x < minX) { pos.x = minX; if (vel.x < 0f) vel.x = 0f; }
            else if (pos.x > maxX) { pos.x = maxX; if (vel.x > 0f) vel.x = 0f; }

            if (pos.y < minY) { pos.y = minY; if (vel.y < 0f) vel.y = 0f; }
            else if (pos.y > maxY) { pos.y = maxY; if (vel.y > 0f) vel.y = 0f; }
        }

        private void ApplyPlayerOverloadVisual(Transform playerTf, bool overloaded)
        {
            if (playerPropBlock == null) playerPropBlock = new MaterialPropertyBlock();

            var renderers = playerTf.GetComponentsInChildren<MeshRenderer>();
            if (overloaded)
            {
                float flash = Mathf.PingPong(Time.unscaledTime * 11f, 1f) > 0.42f ? 1f : 0.18f;
                Color baseTint = new Color(flash, 0.05f, 0.04f, 1f);
                Color emissive = new Color(flash * 4.2f, 0.14f, 0.06f, 1f);

                for (int r = 0; r < renderers.Length; r++)
                {
                    renderers[r].GetPropertyBlock(playerPropBlock);
                    playerPropBlock.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                    playerPropBlock.SetColor("_BaseColor", baseTint);
                    playerPropBlock.SetColor("_Color", baseTint);
                    playerPropBlock.SetColor("_EmissionColor", emissive);
                    renderers[r].SetPropertyBlock(playerPropBlock);
                }

                wasPlayerOverloaded = true;
            }
            else if (wasPlayerOverloaded)
            {
                for (int r = 0; r < renderers.Length; r++)
                {
                    renderers[r].SetPropertyBlock(null);
                }
                wasPlayerOverloaded = false;
            }
        }

        private void FixedUpdate()
        {
            if (!isInitialized || world == null || !world.IsCreated) return;

            var objQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            bool freezeCratePhysics = false;
            bool inStartupGrace = false;
            if (objQ.CalculateEntityCount() > 0)
            {
                using var oArr = objQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                freezeCratePhysics = oArr[0].IsRespawning || oArr[0].IsRunOver || oArr[0].IsRunCompleted;
                inStartupGrace = oArr[0].LevelStartGraceTimer > 0f;
            }
            objQ.Dispose();

            var gravQuery = em.CreateEntityQuery(typeof(GravityState));
            if (gravQuery.CalculateEntityCount() == 0)
            {
                gravQuery.Dispose();
                return;
            }
            using var gravArray = gravQuery.ToComponentDataArray<GravityState>(Allocator.Temp);
            var gravity = gravArray[0];
            gravQuery.Dispose();

            var bounds = GetActiveBounds();

            for (int i = 0; i < syncList.Count; i++)
            {
                var (e, tf, rb) = syncList[i];
                if (tf == null || !em.Exists(e)) continue;

                // Player & Drones are driven smoothly inside ECS (PlayerMovementSystem / DroneAISystem)!
                if (em.HasComponent<PlayerTag>(e) || em.HasComponent<DroneAITag>(e)) continue;
                if (em.HasComponent<EnergyCoreTag>(e) || em.HasComponent<PressureSwitchTag>(e) || em.HasComponent<ExitAirlockTag>(e)) continue;

                if (em.HasComponent<ResetableEntity>(e))
                {
                    var reset = em.GetComponentData<ResetableEntity>(e);
                    if (reset.PendingReset)
                    {
                        tf.position = reset.SpawnPosition;
                        tf.rotation = reset.SpawnRotation;
                        if (rb != null)
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                        reset.PendingReset = false;
                        em.SetComponentData(e, reset);
                        em.SetComponentData(e, LocalTransform.FromPositionRotation(tf.position, tf.rotation));
                        em.SetComponentData(e, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero });
                        continue;
                    }
                }

                // STRICT GRAVITY RULE: Only Cargo Crates accelerate with global gravity (frozen during death/respawn lock)
                if (rb != null)
                {
                    rb.useGravity = false;
                    if (freezeCratePhysics)
                    {
                        if (!rb.isKinematic)
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                    }
                    else if (em.HasComponent<CargoCrateTag>(e))
                    {
                        rb.AddForce(gravity.GravityVector, ForceMode.Acceleration);
                    }
                }

                Vector3 preClampVel = (rb != null) ? rb.linearVelocity : Vector3.zero;
                if (freezeCratePhysics) preClampVel = Vector3.zero;

                Vector3 pos = tf.position;
                Vector3 vel = preClampVel;
                ClampVectorInsideBounds(ref pos, ref vel, bounds, 0.44f);
                tf.position = pos;
                if (rb != null) rb.linearVelocity = vel;

                if (em.HasComponent<CargoCrateTag>(e))
                {
                    var impactRelay = tf.GetComponent<CargoCrateImpactRelay>();
                    if (impactRelay != null)
                    {
                        impactRelay.CrateEntity = e;
                        impactRelay.ImpactFrozen = freezeCratePhysics || inStartupGrace;
                        impactRelay.PrePhysicsVelocity = (freezeCratePhysics || inStartupGrace) ? Vector3.zero : preClampVel;
                    }

                    var cTag = em.GetComponentData<CargoCrateTag>(e);
                    cTag.PrePhysicsVelocity = (freezeCratePhysics || inStartupGrace) ? float3.zero : (float3)preClampVel;
                    em.SetComponentData(e, cTag);
                }

                em.SetComponentData(e, LocalTransform.FromPositionRotation(tf.position, tf.rotation));
                em.SetComponentData(e, new PhysicsVelocity { Linear = (float3)vel, Angular = float3.zero });
            }
        }

        private void FlushEcsAudioCues()
        {
            var sound = SoundManager.Instance;
            if (sound == null || world == null || !world.IsCreated) return;

            var aeQ = em.CreateEntityQuery(typeof(AudioEventState));
            if (aeQ.CalculateEntityCount() > 0)
            {
                using var arr = aeQ.ToEntityArray(Allocator.Temp);
                var ae = em.GetComponentData<AudioEventState>(arr[0]);
                var cues = ae.PendingCues;
                if (cues != AudioCueFlags.None)
                {
                    if ((cues & AudioCueFlags.GravityShift) != 0) sound.PlayGravityShift();
                    if ((cues & AudioCueFlags.HazardZap) != 0) sound.PlayHazardZap();
                    if ((cues & AudioCueFlags.PlayerDamage) != 0 || (cues & AudioCueFlags.SuitSpark) != 0) sound.PlayPlayerHitAndSpark();
                    if ((cues & AudioCueFlags.DroneAlertChirp) != 0) sound.PlayDroneAlertChirp();
                    if ((cues & AudioCueFlags.DronePlasmaBlast) != 0) sound.PlayDronePlasmaBlast();
                    if ((cues & AudioCueFlags.PlayerDeath) != 0) sound.PlayPlayerDeath();

                    ae.PendingCues = AudioCueFlags.None;
                    em.SetComponentData(arr[0], ae);
                }
            }
            aeQ.Dispose();
        }

        private void LateUpdate()
        {
            if (!isInitialized || world == null || !world.IsCreated) return;

            FlushEcsAudioCues();

            bool isCurrentlyRespawning = false;
            var objQuery = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQuery.CalculateEntityCount() > 0)
            {
                using var objArray = objQuery.ToEntityArray(Allocator.Temp);
                var objEntity = objArray[0];
                var lvl = em.GetComponentData<LevelObjectiveState>(objEntity);
                isCurrentlyRespawning = lvl.IsRespawning;
                SoundManager.Instance?.SetDeathDuck(isCurrentlyRespawning);

                if (lvl.CoresCollected > prevCoresCollected) SoundManager.Instance?.PlayCoreCollect();
                if (lvl.SwitchFlipped && !prevSwitchFlipped) SoundManager.Instance?.PlaySwitchActivate();
                if (lvl.AirlockUnlocked && !prevAirlockUnlocked) SoundManager.Instance?.PlayAirlockUnlock();
                prevCoresCollected = lvl.CoresCollected;
                prevSwitchFlipped = lvl.SwitchFlipped;
                prevAirlockUnlocked = lvl.AirlockUnlocked;

                if (lvl.PendingDeathSequence && !isDeathSequenceRunning)
                {
                    lvl.PendingDeathSequence = false;
                    em.SetComponentData(objEntity, lvl);
                    objQuery.Dispose();
                    StartDeathRespawnSequence();
                    return;
                }
                if (lvl.IsRunOver && !isDeathSequenceRunning)
                {
                    objQuery.Dispose();
                    if (!runOverOpenedForRun)
                    {
                        runOverOpenedForRun = true;
                        SessionReportRecorder.Instance?.CheckAndRecordTagDeaths();
                        SessionReportRecorder.Instance?.ArchiveRunEndIfPending("RunOver");
                        GameUIManager.Instance?.OpenRunOverScreen(isVictory: false);
                    }
                    return;
                }
                if (lvl.RoomCleared && !lvl.IsRunCompleted)
                {
                    SoundManager.Instance?.PlayLevelClearedFanfare();
                    SessionReportRecorder.Instance?.ArchiveBeforeLevelChange("Cleared", saveImmediately: true);
                    if (lvl.LevelIndex >= LevelObjectiveState.MaxRunLevels)
                    {
                        lvl.IsRunCompleted = true;
                        em.SetComponentData(objEntity, lvl);
                        objQuery.Dispose();
                        runOverOpenedForRun = true;
                        GameUIManager.Instance?.OpenRunOverScreen(isVictory: true);
                        return;
                    }
                    var pcg = FindAnyObjectByType<PCGRoomGenerator>();
                    if (pcg != null)
                    {
                        pcg.AdvanceToNextLevel();
                        objQuery.Dispose();
                        return;
                    }
                }
            }
            objQuery.Dispose();

            var bounds = GetActiveBounds();

            for (int i = 0; i < syncList.Count; i++)
            {
                var (e, tf, rb) = syncList[i];
                if (tf == null || !em.Exists(e)) continue;

                var ltf = em.GetComponentData<LocalTransform>(e);
                var pvel = em.HasComponent<PhysicsVelocity>(e) ? em.GetComponentData<PhysicsVelocity>(e) : default;

                if (em.HasComponent<PlayerTag>(e))
                {
                    // Handle ResetableEntity PendingReset cleanly on Player
                    if (em.HasComponent<ResetableEntity>(e))
                    {
                        var reset = em.GetComponentData<ResetableEntity>(e);
                        if (reset.PendingReset)
                        {
                            reset.PendingReset = false;
                            em.SetComponentData(e, reset);
                        }
                    }

                    Vector3 clampedPos = ltf.Position;
                    Vector3 clampedVel = pvel.Linear;
                    ClampVectorInsideBounds(ref clampedPos, ref clampedVel, bounds, 0.45f);

                    tf.position = clampedPos;
                    tf.rotation = ltf.Rotation;

                    var jet = em.GetComponentData<JetpackComponent>(e);

                    bool activeThrust = jet.IsThrusting && !jet.IsInCooldown && !isCurrentlyRespawning &&
                                        (GameUIManager.Instance == null || GameUIManager.Instance.currentState == UIState.Gameplay);
                    SoundManager.Instance?.SetJetpackThrustLoop(activeThrust, jet.IsBoosting);
                    if (jet.IsBoosting && !prevWasBoosting && !jet.IsInCooldown) SoundManager.Instance?.PlayJetpackBoostBurst();
                    if (jet.IsInCooldown && !prevWasOverloaded) SoundManager.Instance?.PlayJetpackOverload();
                    prevWasBoosting = jet.IsBoosting;
                    prevWasOverloaded = jet.IsInCooldown;

                    // Red Blinking Feedback on Jetpack Overload!
                    ApplyPlayerOverloadVisual(tf, jet.IsInCooldown);

                    // Sync Thruster Particle System
                    var ps = tf.GetComponentInChildren<ParticleSystem>();
                    if (ps != null)
                    {
                        var emission = ps.emission;
                        if (jet.IsThrusting && !jet.IsInCooldown)
                        {
                            emission.rateOverTime = jet.IsBoosting ? 85f : 35f;
                            if (math.lengthsq(jet.ThrustDirection) > 0.01f)
                            {
                                ps.transform.rotation = Quaternion.LookRotation(Vector3.forward, -new Vector3(jet.ThrustDirection.x, jet.ThrustDirection.y, 0f));
                            }
                        }
                        else
                        {
                            emission.rateOverTime = 0f;
                        }
                    }
                }
                else if (em.HasComponent<DroneAITag>(e))
                {
                    var dState = em.GetComponentData<DroneState>(e);
                    if (dState.IsDestroyed)
                    {
                        if (tf.gameObject.activeSelf)
                        {
                            Vector3 destroyPos = tf.position;
                            tf.gameObject.SetActive(false);
                            SpawnOneShotVFX(ResolveSmallExplosionPrefab(), destroyPos, 0.36f, 3.0f, "SmallExplosion_Drone");
                        }
                    }
                    else
                    {
                        if (!tf.gameObject.activeSelf) tf.gameObject.SetActive(true);
                        Vector3 pos = ltf.Position;
                        Vector3 vel = pvel.Linear;
                        ClampVectorInsideBounds(ref pos, ref vel, bounds, 0.42f);
                        tf.position = pos;
                        tf.rotation = ltf.Rotation;
                    }
                }
                else if (em.HasComponent<EnergyCoreTag>(e))
                {
                    var core = em.GetComponentData<EnergyCoreTag>(e);
                    if (core.IsCollected)
                    {
                        if (tf.gameObject.activeSelf) tf.gameObject.SetActive(false);
                    }
                    else
                    {
                        tf.rotation = ltf.Rotation;
                        float shinePulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f + core.CoreID * 1.3f);
                        float scale = 0.54f * (1f + 0.08f * shinePulse);
                        tf.localScale = new Vector3(scale, scale, scale);
                        tf.position = core.SpawnPosition + new float3(0f, 0.06f * Mathf.Sin(Time.unscaledTime * 3.2f + core.CoreID), 0f);

                        var rend = tf.GetComponent<MeshRenderer>();
                        if (rend != null)
                        {
                            if (playerPropBlock == null) playerPropBlock = new MaterialPropertyBlock();
                            Color coreCol = Color.Lerp(new Color(0.24f, 0.88f, 1.0f, 1f), new Color(0.85f, 0.98f, 1.0f, 1f), shinePulse);
                            rend.GetPropertyBlock(playerPropBlock);
                            playerPropBlock.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                            playerPropBlock.SetColor("_BaseColor", coreCol);
                            playerPropBlock.SetColor("_Color", coreCol);
                            playerPropBlock.SetColor("_HighlightColor", Color.white);
                            playerPropBlock.SetFloat("_RimPower", 2.0f);
                            playerPropBlock.SetFloat("_RimIntensity", 1.85f);
                            playerPropBlock.SetColor("_EmissionColor", coreCol * Mathf.Lerp(1.8f, 3.4f, shinePulse));
                            rend.SetPropertyBlock(playerPropBlock);
                        }
                    }
                }
                else if (em.HasComponent<PressureSwitchTag>(e))
                {
                    var pSwitch = em.GetComponentData<PressureSwitchTag>(e);
                    float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * (pSwitch.IsPressed ? 2.5f : 5.0f));
                    Color targetColor = pSwitch.IsPressed
                        ? new Color(0.14f, 0.95f, 0.52f, 1f)
                        : new Color(1.0f, 0.52f, 0.14f, 1f);

                    var rend = tf.GetComponent<MeshRenderer>();
                    if (rend != null)
                    {
                        if (playerPropBlock == null) playerPropBlock = new MaterialPropertyBlock();
                        rend.GetPropertyBlock(playerPropBlock);
                        playerPropBlock.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                        playerPropBlock.SetColor("_BaseColor", targetColor);
                        playerPropBlock.SetColor("_Color", targetColor);
                        playerPropBlock.SetColor("_EmissionColor", targetColor * (pSwitch.IsPressed ? 3.8f : 3.2f * pulse));
                        rend.SetPropertyBlock(playerPropBlock);
                    }

                    var swLight = tf.GetComponentInChildren<Light>();
                    if (swLight != null) swLight.enabled = false;

                    if (pSwitch.IsPressed)
                    {
                        var p = tf.position;
                        p.y = pSwitch.SwitchPosition.y - 0.04f;
                        tf.position = p;
                    }
                }
                else if (em.HasComponent<ExitAirlockTag>(e))
                {
                    var airlock = em.GetComponentData<ExitAirlockTag>(e);
                    // Keep airlock stationary on right wall with smooth 0.5..1.0 sine-wave HDR emission cue
                    tf.position = airlock.ClosedPosition;
                    float sine01 = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (airlock.IsUnlocked ? 4.8f : 2.6f));
                    float intensity05To1 = Mathf.Lerp(0.5f, 1.0f, sine01);

                    Color stateCol = airlock.IsUnlocked
                        ? new Color(0.13f, 0.92f, 0.44f, 1f)
                        : new Color(0.94f, 0.24f, 0.24f, 1f);

                    var rend = tf.GetComponent<MeshRenderer>();
                    if (rend != null)
                    {
                        if (playerPropBlock == null) playerPropBlock = new MaterialPropertyBlock();
                        rend.GetPropertyBlock(playerPropBlock);
                        playerPropBlock.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                        playerPropBlock.SetColor("_BaseColor", stateCol * intensity05To1);
                        playerPropBlock.SetColor("_Color", stateCol * intensity05To1);
                        playerPropBlock.SetColor("_EmissionColor", stateCol * (intensity05To1 * (airlock.IsUnlocked ? 2.8f : 1.8f)));
                        rend.SetPropertyBlock(playerPropBlock);
                    }

                    var airLight = tf.GetComponentInChildren<Light>();
                    if (airLight != null) airLight.enabled = false;
                }
            }
        }

        // =========================================================================
        // VFX BRIDGE & DEATH -> 1.0s WAIT -> EASE-IN / EASE-OUT -> RESPAWN LOCK
        // =========================================================================
        private GameObject ResolveSmallExplosionPrefab()
        {
            if (smallExplosionPrefab != null) return smallExplosionPrefab;
            var pcg = FindAnyObjectByType<PCGRoomGenerator>();
            if (pcg != null)
            {
                pcg.LoadResourcesIfNeeded();
                if (pcg.smallExplosionPrefab != null) smallExplosionPrefab = pcg.smallExplosionPrefab;
            }
#if UNITY_EDITOR
            if (smallExplosionPrefab == null)
            {
                smallExplosionPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/SmallExplosion.prefab");
            }
#endif
            return smallExplosionPrefab;
        }

        private GameObject ResolveEnergyExplosionPrefab()
        {
            if (energyExplosionPrefab != null) return energyExplosionPrefab;
            var pcg = FindAnyObjectByType<PCGRoomGenerator>();
            if (pcg != null)
            {
                pcg.LoadResourcesIfNeeded();
                if (pcg.energyExplosionPrefab != null) energyExplosionPrefab = pcg.energyExplosionPrefab;
            }
#if UNITY_EDITOR
            if (energyExplosionPrefab == null)
            {
                energyExplosionPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/EnergyExplosion.prefab");
            }
#endif
            return energyExplosionPrefab;
        }

        private void SpawnOneShotVFX(GameObject prefab, Vector3 worldPos, float scale, float destroyDelaySec, string instanceName)
        {
            if (prefab == null) return;

            var inst = Instantiate(prefab, new Vector3(worldPos.x, worldPos.y, -0.1f), Quaternion.identity);
            inst.name = instanceName;
            inst.transform.localScale = new Vector3(scale, scale, scale);

            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false;
                main.prewarm = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(false);
                ps.Play(false);
            }

            spawnedVfxList.Add(inst);
            Destroy(inst, destroyDelaySec);
        }

        private void CleanupTempVfx()
        {
            for (int i = 0; i < spawnedVfxList.Count; i++)
            {
                if (spawnedVfxList[i] != null)
                {
                    DestroyImmediate(spawnedVfxList[i]);
                }
            }
            spawnedVfxList.Clear();
        }

        private void EnsureFadeOverlay()
        {
            if (fadeOverlayImg != null)
            {
                if (fadeCanvas == null) fadeCanvas = fadeOverlayImg.GetComponentInParent<Canvas>(true);
                return;
            }

            var existingFadeGo = GameObject.Find("GravityDrift_FadeCanvas");
            if (existingFadeGo != null)
            {
                fadeCanvas = existingFadeGo.GetComponent<Canvas>();
                fadeOverlayImg = existingFadeGo.GetComponentInChildren<Image>(true);
                if (fadeOverlayImg != null) return;
            }

            var go = new GameObject("GravityDrift_FadeCanvas");
            fadeCanvas = go.AddComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fadeCanvas.sortingOrder = 50;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var imgGo = new GameObject("FadeOverlay", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(go.transform, false);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            fadeOverlayImg = imgGo.GetComponent<Image>();
            fadeOverlayImg.color = new Color(0f, 0f, 0f, 0f);
            fadeOverlayImg.raycastTarget = false;
            fadeCanvas.gameObject.SetActive(false);
        }

        private void SetFadeAlpha(float alpha)
        {
            EnsureFadeOverlay();
            float clamped = Mathf.Clamp01(alpha);
            Color c = fadeOverlayImg.color;
            c.a = clamped;
            fadeOverlayImg.color = c;
            bool active = clamped > 0.001f;
            if (fadeCanvas != null && fadeCanvas.gameObject.activeSelf != active)
            {
                fadeCanvas.gameObject.SetActive(active);
            }
        }

        private void SetPlayerVisualVisible(bool visible)
        {
            var playerAuth = FindAnyObjectByType<PlayerAuthoring>(FindObjectsInactive.Include);
            if (playerAuth == null) return;

            foreach (var mr in playerAuth.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.enabled = visible;
            }
            var ps = playerAuth.GetComponentInChildren<ParticleSystem>(true);
            if (ps != null)
            {
                var emMod = ps.emission;
                if (!visible) emMod.rateOverTime = 0f;
            }
        }

        private void AbortDeathRespawnSequence()
        {
            if (deathRespawnCoroutine != null)
            {
                StopCoroutine(deathRespawnCoroutine);
                deathRespawnCoroutine = null;
            }
            isDeathSequenceRunning = false;
            InputHelper.IsInputFrozen = false;
            SetFadeAlpha(0f);
            SetPlayerVisualVisible(true);
        }

        private void StartDeathRespawnSequence()
        {
            if (isDeathSequenceRunning) return;

            isDeathSequenceRunning = true;
            InputHelper.IsInputFrozen = true;
            InputHelper.FlushBufferedGravityInputs();

            Vector3 deathPos = Vector3.zero;
            var playerAuth = FindAnyObjectByType<PlayerAuthoring>();
            if (playerAuth != null)
            {
                deathPos = playerAuth.transform.position;
            }
            else if (world != null && world.IsCreated && playerEntity != Entity.Null && em.Exists(playerEntity))
            {
                deathPos = em.GetComponentData<LocalTransform>(playerEntity).Position;
            }

            SetPlayerVisualVisible(false);
            SpawnOneShotVFX(ResolveEnergyExplosionPrefab(), deathPos, 0.44f, 3.2f, "EnergyExplosion_Player");

            if (deathRespawnCoroutine != null) StopCoroutine(deathRespawnCoroutine);
            deathRespawnCoroutine = StartCoroutine(DeathRespawnCoroutine());
        }

        private IEnumerator DeathRespawnCoroutine()
        {
            // Phase 1: Wait 1.0s while EnergyExplosion plays and AI / Player / Gravity remain frozen
            float elapsed = 0f;
            while (elapsed < 1.0f)
            {
                if (GameUIManager.Instance == null || GameUIManager.Instance.currentState == UIState.Gameplay)
                {
                    elapsed += Time.unscaledDeltaTime;
                }
                yield return null;
            }

            // Phase 2: Ease-In dark transition (0 -> 1 over 0.36s)
            const float fadeDuration = 0.36f;
            float fadeT = 0f;
            while (fadeT < fadeDuration)
            {
                if (GameUIManager.Instance == null || GameUIManager.Instance.currentState == UIState.Gameplay)
                {
                    fadeT += Time.unscaledDeltaTime;
                    float u = Mathf.Clamp01(fadeT / fadeDuration);
                    float easeIn = u * u * (3f - 2f * u);
                    SetFadeAlpha(easeIn);
                }
                yield return null;
            }
            SetFadeAlpha(1f);

            // Phase 3: Dark midpoint — check Game Over vs Respawn
            bool isOutOfLives = false;
            if (world != null && world.IsCreated)
            {
                var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
                if (oQ.CalculateEntityCount() > 0)
                {
                    using var arr = oQ.ToEntityArray(Allocator.Temp);
                    var lvl = em.GetComponentData<LevelObjectiveState>(arr[0]);
                    isOutOfLives = (lvl.LivesRemaining <= 0);
                    if (isOutOfLives)
                    {
                        lvl.IsRunOver = true;
                        lvl.IsRespawning = false;
                        lvl.PendingDeathSequence = false;
                        em.SetComponentData(arr[0], lvl);
                    }
                }
                oQ.Dispose();
            }

            if (isOutOfLives)
            {
                SetFadeAlpha(0f);
                isDeathSequenceRunning = false;
                deathRespawnCoroutine = null;
                InputHelper.IsInputFrozen = false;
                runOverOpenedForRun = true;
                SessionReportRecorder.Instance?.CheckAndRecordTagDeaths();
                SessionReportRecorder.Instance?.ArchiveRunEndIfPending("RunOver");
                GameUIManager.Instance?.OpenRunOverScreen(isVictory: false);
                yield break;
            }

            PerformMidpointRespawnReset();
            yield return null; // Allow one frame with player safely positioned behind curtain before opening eyes

            // Phase 4: Ease-Out dark transition (1 -> 0 over 0.36s) while AI + inputs stay frozen
            fadeT = 0f;
            while (fadeT < fadeDuration)
            {
                if (GameUIManager.Instance == null || GameUIManager.Instance.currentState == UIState.Gameplay)
                {
                    fadeT += Time.unscaledDeltaTime;
                    float u = Mathf.Clamp01(fadeT / fadeDuration);
                    float easeOut = 1f - (u * u * (3f - 2f * u));
                    SetFadeAlpha(easeOut);
                }
                yield return null;
            }
            SetFadeAlpha(0f);

            // Phase 5: Flush any spammed/buffered inputs and release AI/Player/Gravity freeze ONLY after spawn + ease-out
            InputHelper.FlushBufferedGravityInputs();

            if (world != null && world.IsCreated)
            {
                var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
                if (oQ.CalculateEntityCount() > 0)
                {
                    using var arr = oQ.ToEntityArray(Allocator.Temp);
                    var lvl = em.GetComponentData<LevelObjectiveState>(arr[0]);
                    lvl.IsRespawning = false;
                    lvl.PendingDeathSequence = false;
                    lvl.RespawnGraceTimer = 1.25f;
                    em.SetComponentData(arr[0], lvl);
                }
                oQ.Dispose();
            }

            InputHelper.IsInputFrozen = false;
            isDeathSequenceRunning = false;
            deathRespawnCoroutine = null;
        }

        private void PerformMidpointRespawnReset()
        {
            if (world == null || !world.IsCreated) return;

            var query = em.CreateEntityQuery(typeof(LocalTransform), typeof(PhysicsVelocity), typeof(ResetableEntity));
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var r = em.GetComponentData<ResetableEntity>(e);
                em.SetComponentData(e, LocalTransform.FromPositionRotation(r.SpawnPosition, r.SpawnRotation));
                em.SetComponentData(e, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero });
                r.PendingReset = false;
                em.SetComponentData(e, r);

                if (em.HasComponent<DroneState>(e))
                {
                    var d = em.GetComponentData<DroneState>(e);
                    d.CurrentState = DroneFSMState.Patrol;
                    d.PreviousState = DroneFSMState.Patrol;
                    d.ActiveAction = DroneActionType.Patrol;
                    d.StateTimer = 0f;
                    d.PathReplanCooldown = 0f;
                    d.StuckTimer = 0f;
                    d.LastPosition = r.SpawnPosition;
                    d.IsUnlatched = false;
                    d.UnlatchedDuration = 0f;
                    d.IsTrappedByCrate = false;
                    d.TrappedByCrateTimer = 0f;
                    d.CrateEscapeTime = 0f;
                    d.PendingDodgeOutcome = false;
                    d.PendingDodgeTimer = 0f;
                    em.SetComponentData(e, d);

                    if (em.HasBuffer<GridCell>(e))
                    {
                        em.GetBuffer<GridCell>(e).Clear();
                    }
                }
            }
            query.Dispose();

            for (int i = 0; i < syncList.Count; i++)
            {
                var (e, tf, rb) = syncList[i];
                if (tf == null || !em.Exists(e)) continue;
                if (em.HasComponent<ResetableEntity>(e))
                {
                    var r = em.GetComponentData<ResetableEntity>(e);
                    tf.position = r.SpawnPosition;
                    tf.rotation = r.SpawnRotation;
                    if (rb != null && !rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
            }

            if (playerEntity != Entity.Null && em.Exists(playerEntity))
            {
                var jet = em.GetComponentData<JetpackComponent>(playerEntity);
                jet.Fuel = jet.MaxFuel;
                jet.IsInCooldown = false;
                jet.CooldownTimer = 0f;
                jet.IsThrusting = false;
                jet.IsBoosting = false;
                em.SetComponentData(playerEntity, jet);
            }

            var gravQ = em.CreateEntityQuery(typeof(GravityState));
            if (gravQ.CalculateEntityCount() > 0)
            {
                using var gArr = gravQ.ToEntityArray(Allocator.Temp);
                var g = em.GetComponentData<GravityState>(gArr[0]);
                g.DirectionChangedThisFrame = false;
                em.SetComponentData(gArr[0], g);
            }
            gravQ.Dispose();

            var playerAuth = FindAnyObjectByType<PlayerAuthoring>(FindObjectsInactive.Include);
            if (playerAuth != null)
            {
                ApplyPlayerOverloadVisual(playerAuth.transform, false);
            }
            SetPlayerVisualVisible(true);
            InputHelper.FlushBufferedGravityInputs();
        }
    }
}
