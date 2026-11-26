using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace GravityDrift
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    public partial struct DroneAISystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GravityState>();
            state.RequireForUpdate<RoomBoundsState>();

            if (!SystemAPI.HasSingleton<DroneLearningState>())
            {
                var le = state.EntityManager.CreateEntity(typeof(DroneLearningState));
                state.EntityManager.SetComponentData(le, new DroneLearningState
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
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = math.min(SystemAPI.Time.DeltaTime, 0.05f);
            if (dt <= 0.00001f) return;
            if (GameUIManager.Instance != null && GameUIManager.Instance.currentState != UIState.Gameplay) return;

            var gravity = SystemAPI.GetSingleton<GravityState>();
            var bounds = SystemAPI.GetSingleton<RoomBoundsState>();

            Entity learningEntity = Entity.Null;
            DroneLearningState learningState = default;
            if (SystemAPI.HasSingleton<DroneLearningState>())
            {
                learningEntity = SystemAPI.GetSingletonEntity<DroneLearningState>();
                learningState = SystemAPI.GetComponent<DroneLearningState>(learningEntity);
            }

            PlayerActionType playerAction = PlayerActionType.Idle;
            float respawnGraceTimer = 0f;
            bool isRespawning = false;
            float3 switchWorldPos = float3.zero;
            bool hasUnflippedSwitch = false;
            if (SystemAPI.HasSingleton<LevelObjectiveState>())
            {
                var objState = SystemAPI.GetComponent<LevelObjectiveState>(SystemAPI.GetSingletonEntity<LevelObjectiveState>());
                if (objState.IsRunOver || objState.IsRunCompleted) return;
                if (objState.IsRespawning || InputHelper.IsInputFrozen)
                {
                    foreach (var velocity in SystemAPI.Query<RefRW<PhysicsVelocity>>().WithAll<DroneAITag>())
                    {
                        velocity.ValueRW.Linear = float3.zero;
                        velocity.ValueRW.Angular = float3.zero;
                    }
                    return;
                }
                playerAction = objState.CurrentPlayerAction;
                respawnGraceTimer = objState.RespawnGraceTimer;
                isRespawning = objState.IsRespawning;
            }
            foreach (var pSwitch in SystemAPI.Query<RefRO<PressureSwitchTag>>())
            {
                if (!pSwitch.ValueRO.IsPressed)
                {
                    switchWorldPos = pSwitch.ValueRO.SwitchPosition;
                    hasUnflippedSwitch = true;
                    break;
                }
            }

            // Player position, velocity & boost habit
            float3 playerPos = float3.zero;
            float3 playerVel = float3.zero;
            bool playerBoosting = false;
            bool playerFound = false;
            foreach (var (playerTransform, pVel, pJet) in
                SystemAPI.Query<RefRO<LocalTransform>, RefRO<PhysicsVelocity>, RefRO<JetpackComponent>>().WithAll<PlayerTag>())
            {
                playerPos = playerTransform.ValueRO.Position;
                playerVel = pVel.ValueRO.Linear;
                playerBoosting = pJet.ValueRO.IsBoosting;
                playerFound = true;
                break;
            }

            if (learningEntity != Entity.Null)
            {
                UpdatePlayerPatternAdaptation(
                    ref learningState,
                    gravity.DirectionChangedThisFrame,
                    (byte)gravity.CurrentDirection,
                    new float2(playerVel.x, playerVel.y),
                    playerBoosting,
                    dt);
            }

            // Obstacles, Crates (with velocities!), and Hazards
            var blockedCells = new NativeParallelHashSet<int2>(256, Allocator.Temp);
            var hazardCells = new NativeParallelHashSet<int2>(64, Allocator.Temp);
            var cratePositions = new NativeList<float3>(Allocator.Temp);
            var crateVelocities = new NativeList<float3>(Allocator.Temp);
            var wallPositions = new NativeList<float3>(Allocator.Temp);
            var hazardPositions = new NativeList<float3>(Allocator.Temp);

            foreach (var (crateTransform, crateVel) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PhysicsVelocity>>().WithAll<CargoCrateTag>())
            {
                float3 cpos = crateTransform.ValueRO.Position;
                cratePositions.Add(cpos);
                crateVelocities.Add(crateVel.ValueRO.Linear);
                blockedCells.Add(GridAStar.WorldToGrid(cpos, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin));
            }

            foreach (var wallTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<StationObstacleTag>())
            {
                float3 wpos = wallTransform.ValueRO.Position;
                wallPositions.Add(wpos);
                blockedCells.Add(GridAStar.WorldToGrid(wpos, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin));
            }

            foreach (var hazardTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<HazardPadTag>())
            {
                float3 hpos = hazardTransform.ValueRO.Position;
                hazardPositions.Add(hpos);
                hazardCells.Add(GridAStar.WorldToGrid(hpos, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin));
            }

            // Active Drones
            foreach (var (transform, velocity, droneState, telemetry, belief, pathBuffer, beliefBuffer) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRW<DroneState>, RefRW<DroneTelemetry>,
                RefRW<BayesianBeliefComponent>, DynamicBuffer<GridCell>, DynamicBuffer<BeliefCell>>())
            {
                if (droneState.ValueRO.IsDestroyed)
                {
                    velocity.ValueRW.Linear = float3.zero;
                    continue;
                }

                float3 dronePos = transform.ValueRO.Position;
                droneState.ValueRW.StateTimer += dt;
                droneState.ValueRW.PathReplanCooldown -= dt;
                droneState.ValueRW.CrateDodgeCooldown = math.max(0f, droneState.ValueRO.CrateDodgeCooldown - dt);

                // 1. GRAVITY SHIFT REACTION & PATTERN-ADAPTIVE RECOVERY
                if (gravity.DirectionChangedThisFrame)
                {
                    if (!droneState.ValueRO.BaselineMode && learningEntity != Entity.Null &&
                        (droneState.ValueRO.CurrentState == DroneFSMState.Pursue || belief.ValueRO.HasVisualContact) &&
                        learningState.GravityPredictability < 0.45f)
                    {
                        ApplyOutplayRegression(ref learningState, 0.035f, 0.05f, 0.03f, 0.12f);
                    }
                    droneState.ValueRW.PreviousState = droneState.ValueRO.CurrentState;
                    droneState.ValueRW.CurrentState = DroneFSMState.GravityShift;
                    droneState.ValueRW.ActiveAction = DroneActionType.GravityShift;
                    droneState.ValueRW.StateTimer = 0f;
                    droneState.ValueRW.IsUnlatched = true;
                    float adapt = (learningEntity != Entity.Null && !droneState.ValueRO.BaselineMode) ? learningState.PatternAdaptationScore : 0f;
                    droneState.ValueRW.UnlatchedDuration = math.lerp(0.90f, 0.36f, adapt);
                }

                if (droneState.ValueRO.IsUnlatched)
                {
                    droneState.ValueRW.UnlatchedDuration -= dt;
                    if (droneState.ValueRO.UnlatchedDuration <= 0f)
                    {
                        droneState.ValueRW.IsUnlatched = false;
                    }
                }

                // 2. FAST-MOVING CRATE CRUSH CHECK & DYNAMIC CRATE-DODGE EVALUATION (a^drone_t = EvadeCrateDodge)
                float nearestFastCrateDist = 999f;
                for (int c = 0; c < cratePositions.Length; c++)
                {
                    float3 cpos = cratePositions[c];
                    float3 cvel = crateVelocities[c];
                    float crateSpd = math.length(cvel);
                    float distToCrate = math.distance(dronePos, cpos);
                    if (crateSpd > 1.5f && distToCrate < nearestFastCrateDist) nearestFastCrateDist = distToCrate;

                    // Crushed if fast-moving crate impacts drone directly -> 0.5 intensity shake + failed-dodge regression!
                    if (distToCrate < 0.88f && crateSpd > 2.2f)
                    {
                        droneState.ValueRW.IsDestroyed = true;
                        droneState.ValueRW.PendingDodgeOutcome = false;
                        if (!droneState.ValueRO.BaselineMode && learningEntity != Entity.Null)
                        {
                            ApplyOutplayRegression(ref learningState, 0.14f, 0.14f, 0.08f, 0.28f);
                        }
                        IncrementDronesDestroyed(ref state);
                        EmitCameraShake(ref state, 0.5f);
                        break;
                    }

                    // Threat anticipation: incoming falling crate within 2.65m for smart drones!
                    if (!droneState.ValueRO.BaselineMode && crateSpd > 1.8f && distToCrate < 2.65f && droneState.ValueRO.CrateDodgeCooldown <= 0f)
                    {
                        float3 toDrone = dronePos - cpos;
                        toDrone.z = 0f;
                        float approachDot = math.dot(math.normalizesafe(cvel), math.normalizesafe(toDrone));
                        if (approachDot > 0.35f)
                        {
                            // Trigger smart crate-dodge perpendicular + away from crate trajectory!
                            droneState.ValueRW.CrateDodgesAttempted++;
                            droneState.ValueRW.CrateDodgeCooldown = 1.1f;
                            droneState.ValueRW.PendingDodgeOutcome = true;
                            droneState.ValueRW.PendingDodgeTimer = 0.42f;
                            droneState.ValueRW.ActiveAction = DroneActionType.EvadeCrateDodge;

                            float skill = (learningEntity != Entity.Null) ? learningState.CrateDodgeSkill : 0.25f;
                            float impMult = (learningEntity != Entity.Null) ? learningState.EscapeImpulseMultiplier : 1.0f;

                            float3 cDir = math.normalizesafe(cvel);
                            float3 perp = new float3(-cDir.y, cDir.x, 0f);
                            if (math.dot(perp, toDrone) < 0f) perp = -perp;
                            float3 dodgeDir = math.normalizesafe(perp * 0.8f + math.normalizesafe(toDrone) * 0.45f);

                            velocity.ValueRW.Linear = dodgeDir * (droneState.ValueRO.MoveSpeed * (1.25f + skill * 0.9f) * math.min(1.6f, impMult));

                            if (learningEntity != Entity.Null)
                            {
                                learningState.TotalCrateDodgesAttempted++;
                            }
                        }
                    }
                }

                // Resolve delayed dodge outcome (whiff regression vs real clear spike)
                if (!droneState.ValueRO.IsDestroyed && droneState.ValueRO.PendingDodgeOutcome)
                {
                    droneState.ValueRW.PendingDodgeTimer -= dt;
                    if (droneState.ValueRO.PendingDodgeTimer <= 0f)
                    {
                        droneState.ValueRW.PendingDodgeOutcome = false;
                        if (nearestFastCrateDist < 1.35f)
                        {
                            // Whiffed / still trapped inside crate blast zone
                            if (learningEntity != Entity.Null)
                            {
                                ApplyOutplayRegression(ref learningState, 0.09f, 0.08f, 0.05f, 0.18f);
                            }
                        }
                        else
                        {
                            droneState.ValueRW.CrateDodgesSucceeded++;
                            if (learningEntity != Entity.Null)
                            {
                                learningState.TotalCrateDodgesSucceeded++;
                                ApplyAdaptationBoost(ref learningState, 0.075f, 0.08f, 0.03f, 0.20f);
                            }
                        }
                    }
                }

                // 3. HAZARD PAD INTERACTION:
                // Baseline drones ignore hazards in A* and get vaporized; Smart drones avoid them unless shoved into one!
                for (int h = 0; h < hazardPositions.Length; h++)
                {
                    float3 hpos = hazardPositions[h];
                    if (math.abs(dronePos.x - hpos.x) < 0.56f && math.abs(dronePos.y - hpos.y) < 0.48f)
                    {
                        droneState.ValueRW.IsDestroyed = true;
                        IncrementDronesDestroyed(ref state);
                        break;
                    }
                }

                if (droneState.ValueRO.IsDestroyed)
                {
                    velocity.ValueRW.Linear = float3.zero;
                    continue;
                }

                // 4. CROSS-LEVEL CRATE-ESCAPE LEARNING (escaping crate traps/corridors)
                bool nearCrate = false;
                float3 nearestCratePos = float3.zero;
                float minCrateDist = 999f;
                for (int c = 0; c < cratePositions.Length; c++)
                {
                    float dist = math.distance(dronePos, cratePositions[c]);
                    if (dist < minCrateDist)
                    {
                        minCrateDist = dist;
                        nearestCratePos = cratePositions[c];
                    }
                }
                if (minCrateDist < 1.18f) nearCrate = true;

                float currentSpeed = math.length(velocity.ValueRO.Linear);
                if (nearCrate && currentSpeed < 1.0f)
                {
                    droneState.ValueRW.TrappedByCrateTimer += dt;
                    droneState.ValueRW.CrateEscapeTime += dt;

                    float threshold = (learningEntity != Entity.Null) ? learningState.TrappedDetectionThreshold : 0.8f;
                    if (droneState.ValueRO.TrappedByCrateTimer >= threshold)
                    {
                        droneState.ValueRW.IsTrappedByCrate = true;
                        droneState.ValueRW.ActiveAction = DroneActionType.EvadeCrateDodge;

                        float3 awayFromCrate = math.normalizesafe(dronePos - nearestCratePos);
                        awayFromCrate.z = 0f;
                        float3 perpTangent = new float3(-awayFromCrate.y, awayFromCrate.x, 0f);
                        float3 escapeDir = math.normalizesafe(awayFromCrate + perpTangent * 0.55f);

                        float impulseMult = (learningEntity != Entity.Null) ? learningState.EscapeImpulseMultiplier : 1.0f;
                        velocity.ValueRW.Linear = escapeDir * (5.2f * impulseMult);
                    }
                }
                else
                {
                    if (droneState.ValueRO.IsTrappedByCrate && minCrateDist > 1.55f)
                    {
                        droneState.ValueRW.IsTrappedByCrate = false;
                        if (learningEntity != Entity.Null)
                        {
                            learningState.TotalEscapesCompleted++;
                            learningState.AverageEscapeTimeSec = math.max(0.28f, math.lerp(learningState.AverageEscapeTimeSec, math.max(0.25f, droneState.ValueRO.CrateEscapeTime), 0.35f));
                            ApplyAdaptationBoost(ref learningState, 0.035f, 0.14f, 0.05f, 0.15f);
                        }
                        UpdateCounterplayStats(ref state, 0, 1);
                        droneState.ValueRW.TrappedByCrateTimer = 0f;
                        droneState.ValueRW.CrateEscapeTime = 0f;
                    }
                    else if (!nearCrate)
                    {
                        droneState.ValueRW.TrappedByCrateTimer = math.max(0f, droneState.ValueRO.TrappedByCrateTimer - dt);
                    }
                }

                // Stuck metric
                if (math.distance(dronePos, droneState.ValueRO.LastPosition) < 0.05f * dt * droneState.ValueRO.MoveSpeed)
                {
                    droneState.ValueRW.StuckTimer += dt;
                }
                else
                {
                    droneState.ValueRW.StuckTimer = math.max(0f, droneState.ValueRO.StuckTimer - dt);
                }
                droneState.ValueRW.LastPosition = dronePos;
                telemetry.ValueRW.StuckOnObstacle = (droneState.ValueRO.StuckTimer > 1.2f);

                // 5. EXPLICIT BAYESIAN POSTERIOR PI UPDATE:
                // pi_t(s_i) = [ P(o_t | s_i, a_player, a_drone) * sum_j T(s_i | s_j, a_drone, gravity) * pi_{t-1}(s_j) ] / Z
                UpdateBayesianPosteriorPi(
                    dronePos,
                    playerFound,
                    playerPos,
                    playerAction,
                    droneState.ValueRO.ActiveAction,
                    gravity,
                    bounds,
                    hasUnflippedSwitch,
                    switchWorldPos,
                    blockedCells,
                    ref belief.ValueRW,
                    beliefBuffer,
                    dt);

                telemetry.ValueRW.CurrentBeliefEntropy = belief.ValueRO.Entropy;

                if (!droneState.ValueRO.BaselineMode && learningEntity != Entity.Null)
                {
                    if (belief.ValueRO.Entropy > belief.ValueRO.PriorEntropy + 0.015f)
                    {
                        learningState.ConsecutiveEntropyRises++;
                        if (learningState.ConsecutiveEntropyRises >= 2)
                        {
                            ApplyOutplayRegression(ref learningState, 0.015f, 0.02f, 0.018f, 0.06f);
                        }
                    }
                    else if (belief.ValueRO.Entropy < belief.ValueRO.PriorEntropy - 0.02f)
                    {
                        learningState.ConsecutiveEntropyRises = 0;
                        ApplyAdaptationBoost(ref learningState, 0.018f, 0.025f, 0.01f, 0.07f);
                    }

                    if (telemetry.ValueRO.StuckOnObstacle && droneState.ValueRO.StateTimer > 1.2f)
                    {
                        ApplyOutplayRegression(ref learningState, 0.01f, 0.03f, 0.02f, 0.05f);
                    }
                }

                // 6. EXECUTE 4-STATE FSM (Gravity-Neutral Hover + Hazard-Aware Grid A*)
                switch (droneState.ValueRO.CurrentState)
                {
                    case DroneFSMState.Patrol:
                    {
                        if (droneState.ValueRO.ActiveAction != DroneActionType.EvadeCrateDodge)
                        {
                            droneState.ValueRW.ActiveAction = DroneActionType.Patrol;
                        }

                        float3 targetWP = (droneState.ValueRO.CurrentPatrolIndex == 0) ?
                            droneState.ValueRO.PatrolPointA : droneState.ValueRO.PatrolPointB;

                        droneState.ValueRW.TargetPosition = targetWP;

                        float3 toTarget = targetWP - dronePos;
                        toTarget.z = 0f;
                        float dist = math.length(toTarget);

                        if (dist < 0.55f)
                        {
                            droneState.ValueRW.CurrentPatrolIndex = (byte)((droneState.ValueRO.CurrentPatrolIndex == 0) ? 1 : 0);
                        }
                        else if (!droneState.ValueRO.IsTrappedByCrate && droneState.ValueRO.CrateDodgeCooldown < 0.55f)
                        {
                            float3 desiredVel = math.normalizesafe(toTarget) * droneState.ValueRO.MoveSpeed;
                            velocity.ValueRW.Linear = math.lerp(velocity.ValueRO.Linear, desiredVel, dt * 8f);
                        }

                        bool shouldPursue = false;
                        if (playerFound && math.distance(dronePos, playerPos) < droneState.ValueRO.DetectionRadius)
                        {
                            shouldPursue = true;
                        }
                        else if (!droneState.ValueRO.BaselineMode && belief.ValueRO.TopClusterConfidence > 0.42f && belief.ValueRO.Entropy < 3.6f)
                        {
                            shouldPursue = true;
                        }

                        if (shouldPursue)
                        {
                            droneState.ValueRW.PreviousState = DroneFSMState.Patrol;
                            droneState.ValueRW.CurrentState = DroneFSMState.Pursue;
                            droneState.ValueRW.ActiveAction = DroneActionType.Pursue;
                            droneState.ValueRW.StateTimer = 0f;
                            droneState.ValueRW.PathReplanCooldown = 0f;
                            EmitAudioCue(ref state, AudioCueFlags.DroneAlertChirp);
                        }
                        break;
                    }

                    case DroneFSMState.GravityShift:
                    {
                        droneState.ValueRW.ActiveAction = DroneActionType.GravityShift;
                        // Gravity-neutral stabilization dampens drift while crates fall
                        velocity.ValueRW.Linear = math.lerp(velocity.ValueRO.Linear, float3.zero, dt * 6f);

                        float wobble = math.sin(droneState.ValueRO.StateTimer * 22f) * 0.12f;
                        transform.ValueRW.Rotation = math.mul(transform.ValueRO.Rotation, quaternion.Euler(0, 0, wobble));

                        float gravityShiftFinish = (learningEntity != Entity.Null && !droneState.ValueRO.BaselineMode)
                            ? math.lerp(0.48f, 0.22f, learningState.PatternAdaptationScore)
                            : 0.45f;
                        if (droneState.ValueRO.StateTimer >= gravityShiftFinish)
                        {
                            droneState.ValueRW.PreviousState = DroneFSMState.GravityShift;
                            droneState.ValueRW.CurrentState = DroneFSMState.Reorient;
                            droneState.ValueRW.StateTimer = 0f;
                        }
                        break;
                    }

                    case DroneFSMState.Reorient:
                    {
                        droneState.ValueRW.ActiveAction = DroneActionType.Reorient;
                        transform.ValueRW.Rotation = math.slerp(transform.ValueRO.Rotation, quaternion.identity, dt * 10f);

                        int2 startGrid = GridAStar.WorldToGrid(dronePos, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin);
                        int2 goalGrid = droneState.ValueRO.BaselineMode ?
                            GridAStar.WorldToGrid(playerFound ? playerPos : droneState.ValueRO.PatrolPointA, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin) :
                            belief.ValueRO.EstimatedTargetCell;

                        bool avoidHazards = !droneState.ValueRO.BaselineMode;
                        var res = GridAStar.FindPath(startGrid, goalGrid, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin, blockedCells, hazardCells, avoidHazards, pathBuffer);
                        telemetry.ValueRW.NodesExpanded = res.NodesExpanded;
                        telemetry.ValueRW.ReplanLatencyMs = res.LatencyMs;
                        telemetry.ValueRW.HazardCellsAvoided += res.HazardCellsAvoided;
                        droneState.ValueRW.CurrentPathIndex = 0;
                        droneState.ValueRW.PathReplanCooldown = 0.35f;

                        if (droneState.ValueRO.StateTimer >= 0.32f)
                        {
                            droneState.ValueRW.ReorientCountRecent++;
                            if (!droneState.ValueRO.BaselineMode && learningEntity != Entity.Null && droneState.ValueRO.ReorientCountRecent >= 2)
                            {
                                ApplyOutplayRegression(ref learningState, 0.02f, 0.04f, 0.025f, 0.08f);
                            }
                            var prevBefore = droneState.ValueRO.CurrentState;
                            var nextAfter = (playerFound && math.distance(dronePos, playerPos) < droneState.ValueRO.DetectionRadius)
                                ? DroneFSMState.Pursue
                                : DroneFSMState.Patrol;
                            droneState.ValueRW.CurrentState = nextAfter;
                            droneState.ValueRW.StateTimer = 0f;
                            if (nextAfter == DroneFSMState.Pursue && prevBefore != DroneFSMState.Pursue)
                            {
                                EmitAudioCue(ref state, AudioCueFlags.DroneAlertChirp);
                            }
                        }
                        break;
                    }

                    case DroneFSMState.Pursue:
                    {
                        if (droneState.ValueRO.ActiveAction != DroneActionType.EvadeCrateDodge)
                        {
                            droneState.ValueRW.ActiveAction = DroneActionType.Pursue;
                        }

                        float3 targetPoint = (droneState.ValueRO.BaselineMode || (playerFound && math.distance(dronePos, playerPos) < 5.5f))
                            ? (playerFound ? playerPos : belief.ValueRO.EstimatedWorldPos)
                            : belief.ValueRO.EstimatedWorldPos;

                        droneState.ValueRW.TargetPosition = targetPoint;
                        float distToPlayer = math.distance(dronePos, targetPoint);

                        if (playerFound && !isRespawning && respawnGraceTimer <= 0f && math.distance(dronePos, playerPos) < 0.85f)
                        {
                            telemetry.ValueRW.TargetReached = true;
                            TriggerPlayerTaggedReset(ref state);
                            break;
                        }

                        if (distToPlayer > droneState.ValueRO.LoseTargetRadius)
                        {
                            if (!droneState.ValueRO.BaselineMode && learningEntity != Entity.Null)
                            {
                                // Player out-maneuvered drone out of pursuit lock -> regression penalty
                                ApplyOutplayRegression(ref learningState, 0.028f, 0.045f, 0.025f, 0.11f);
                            }
                            droneState.ValueRW.CurrentState = DroneFSMState.Patrol;
                            droneState.ValueRW.StateTimer = 0f;
                            break;
                        }

                        if (!droneState.ValueRO.IsTrappedByCrate && droneState.ValueRO.CrateDodgeCooldown < 0.55f)
                        {
                            if (droneState.ValueRO.BaselineMode)
                            {
                                float3 directDir = math.normalizesafe(targetPoint - dronePos);
                                directDir.z = 0f;
                                velocity.ValueRW.Linear = math.lerp(velocity.ValueRO.Linear, directDir * droneState.ValueRO.MoveSpeed, dt * 6f);
                                telemetry.ValueRW.NodesExpanded = 0;
                                telemetry.ValueRW.ReplanLatencyMs = 0f;
                            }
                            else
                            {
                                if (droneState.ValueRO.PathReplanCooldown <= 0f)
                                {
                                    int2 startGrid = GridAStar.WorldToGrid(dronePos, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin);
                                    int2 goalGrid = belief.ValueRO.EstimatedTargetCell;
                                    var res = GridAStar.FindPath(startGrid, goalGrid, bounds.RoomWidth, bounds.RoomHeight, bounds.RoomOrigin, blockedCells, hazardCells, true, pathBuffer);
                                    telemetry.ValueRW.NodesExpanded = res.NodesExpanded;
                                    telemetry.ValueRW.ReplanLatencyMs = res.LatencyMs;
                                    telemetry.ValueRW.HazardCellsAvoided += res.HazardCellsAvoided;
                                    droneState.ValueRW.CurrentPathIndex = 0;
                                    droneState.ValueRW.PathReplanCooldown = 0.35f;
                                }

                                if (pathBuffer.Length > 0 && droneState.ValueRO.CurrentPathIndex < pathBuffer.Length)
                                {
                                    float3 targetWp = pathBuffer[droneState.ValueRO.CurrentPathIndex].WorldPos;
                                    targetWp.z = 0f;
                                    float3 toWp = targetWp - dronePos;
                                    toWp.z = 0f;
                                    float wpDist = math.length(toWp);

                                    if (wpDist < 0.6f && droneState.ValueRO.CurrentPathIndex < pathBuffer.Length - 1)
                                    {
                                        droneState.ValueRW.CurrentPathIndex++;
                                    }

                                    float3 steerDir = math.normalizesafe(toWp);
                                    float adaptBoost = (learningEntity != Entity.Null) ? (1f + 0.24f * learningState.PatternAdaptationScore) : 1f;
                                    float3 desiredVel = steerDir * (droneState.ValueRO.MoveSpeed * adaptBoost);
                                    velocity.ValueRW.Linear = math.lerp(velocity.ValueRO.Linear, desiredVel, dt * 8f);

                                    if (math.lengthsq(steerDir) > 0.01f)
                                    {
                                        quaternion lookRot = quaternion.LookRotationSafe(new float3(0, 0, 1), steerDir);
                                        transform.ValueRW.Rotation = math.slerp(transform.ValueRO.Rotation, lookRot, dt * 10f);
                                    }
                                }
                                else
                                {
                                    float3 directDir = math.normalizesafe(targetPoint - dronePos);
                                    directDir.z = 0f;
                                    velocity.ValueRW.Linear = math.lerp(velocity.ValueRO.Linear, directDir * droneState.ValueRO.MoveSpeed, dt * 6f);
                                }
                            }
                        }
                        break;
                    }
                }

                // 7. INTEGRATE + INTERIOR WALL PUSHOUT + STRICT INNER-ROOM BOUNDARY CLAMPING
                float3 newPos = transform.ValueRO.Position + velocity.ValueRO.Linear * dt;
                float3 linVel = velocity.ValueRO.Linear;
                newPos.z = 0f;
                linVel.z = 0f;

                // Resolve collision vs interior wall tiles (tile size 1.0x1.0, drone radius 0.38)
                const float droneRadius = 0.38f;
                const float halfTile = 0.50f;
                for (int w = 0; w < wallPositions.Length; w++)
                {
                    float3 wp = wallPositions[w];
                    float closestX = math.clamp(newPos.x, wp.x - halfTile, wp.x + halfTile);
                    float closestY = math.clamp(newPos.y, wp.y - halfTile, wp.y + halfTile);
                    float dx = newPos.x - closestX;
                    float dy = newPos.y - closestY;
                    float dSq = dx * dx + dy * dy;
                    if (dSq < droneRadius * droneRadius)
                    {
                        float d = math.sqrt(math.max(0.00001f, dSq));
                        float nx = (d > 0.001f) ? (dx / d) : 0f;
                        float ny = (d > 0.001f) ? (dy / d) : 1f;
                        float push = droneRadius - d;
                        newPos.x += nx * push;
                        newPos.y += ny * push;
                        float vn = linVel.x * nx + linVel.y * ny;
                        if (vn < 0f)
                        {
                            linVel.x -= vn * nx;
                            linVel.y -= vn * ny;
                        }
                    }
                }

                // Strict Inner-Room Boundary Clamping
                const float boundMargin = 0.42f;
                float minX = bounds.InnerMinX + boundMargin;
                float maxX = bounds.InnerMaxX - boundMargin;
                float minY = bounds.InnerMinY + boundMargin;
                float maxY = bounds.InnerMaxY - boundMargin;

                if (newPos.x < minX) { newPos.x = minX; if (linVel.x < 0f) linVel.x = 0f; }
                else if (newPos.x > maxX) { newPos.x = maxX; if (linVel.x > 0f) linVel.x = 0f; }

                if (newPos.y < minY) { newPos.y = minY; if (linVel.y < 0f) linVel.y = 0f; }
                else if (newPos.y > maxY) { newPos.y = maxY; if (linVel.y > 0f) linVel.y = 0f; }

                transform.ValueRW.Position = newPos;
                velocity.ValueRW.Linear = linVel;
            }

            // Dynamic Regressive AI Adaptation Score (2..94%, swings smoothly with successes & outplay dips)
            if (learningEntity != Entity.Null)
            {
                learningState.RegressionMomentum = math.lerp(learningState.RegressionMomentum, 0f, dt * 0.22f);
                float impulseScore = math.clamp((learningState.EscapeImpulseMultiplier - 0.85f) / 1.95f, 0f, 1f);
                float reactScore = math.clamp((0.95f - learningState.TrappedDetectionThreshold) / 0.75f, 0f, 1f);
                float dodgeRatio = (learningState.TotalCrateDodgesAttempted > 0)
                    ? ((float)learningState.TotalCrateDodgesSucceeded / learningState.TotalCrateDodgesAttempted)
                    : learningState.CrateDodgeSkill;
                float dynamicSkill = learningState.CrateDodgeSkill * 0.55f + dodgeRatio * 0.45f;
                float momentumBias = learningState.RegressionMomentum * 14f;
                float compositePct = math.clamp((impulseScore * 26f + reactScore * 22f + dynamicSkill * 42f) + momentumBias, 2f, 94f);
                learningState.DroneImprovementPct = compositePct;
                SystemAPI.SetComponent(learningEntity, learningState);
            }

            blockedCells.Dispose();
            hazardCells.Dispose();
            cratePositions.Dispose();
            crateVelocities.Dispose();
            wallPositions.Dispose();
            hazardPositions.Dispose();
        }

        public static void UpdatePlayerPatternAdaptation(
            ref DroneLearningState learn,
            bool flipOccurred,
            byte flipDir,
            float2 playerMoveVel,
            bool isBoosting,
            float dt)
        {
            if (flipOccurred)
            {
                switch (flipDir)
                {
                    case 0: learn.GravityDirCounts.x++; break;
                    case 1: learn.GravityDirCounts.y++; break;
                    case 2: learn.GravityDirCounts.z++; break;
                    case 3: learn.GravityDirCounts.w++; break;
                }

                int totalFlips = learn.GravityDirCounts.x + learn.GravityDirCounts.y + learn.GravityDirCounts.z + learn.GravityDirCounts.w;
                int maxDir = math.max(math.max(learn.GravityDirCounts.x, learn.GravityDirCounts.y), math.max(learn.GravityDirCounts.z, learn.GravityDirCounts.w));
                float favRatio = (totalFlips > 0) ? ((float)maxDir / totalFlips) : 0.25f;

                bool repeatedPattern = (totalFlips >= 2) &&
                    (flipDir == learn.PrevFlipDir || flipDir == learn.LastFlipDir || favRatio > 0.44f);

                if (repeatedPattern)
                {
                    learn.GravityPredictability = math.min(1.0f, learn.GravityPredictability + 0.14f);
                    learn.PatternAdaptationScore = math.min(1.0f, learn.PatternAdaptationScore + 0.10f);
                    ApplyAdaptationBoost(ref learn, 0.045f, 0.09f, 0.03f, 0.14f);
                }
                else
                {
                    learn.GravityPredictability = math.max(0.05f, learn.GravityPredictability - 0.16f);
                    learn.PatternAdaptationScore = math.max(0.04f, learn.PatternAdaptationScore - 0.12f);
                    ApplyOutplayRegression(ref learn, 0.04f, 0.06f, 0.035f, 0.14f);
                }

                learn.PrevFlipDir = learn.LastFlipDir;
                learn.LastFlipDir = flipDir;
            }

            float spdSq = math.lengthsq(playerMoveVel);
            if (spdSq > 0.09f)
            {
                float2 dir = math.normalize(playerMoveVel);
                if (math.lengthsq(learn.DominantMoveLaneBias) < 0.01f)
                {
                    learn.DominantMoveLaneBias = dir;
                }
                else
                {
                    float align = math.dot(dir, math.normalizesafe(learn.DominantMoveLaneBias));
                    learn.DominantMoveLaneBias = math.normalizesafe(math.lerp(learn.DominantMoveLaneBias, dir, math.clamp(dt * 1.4f, 0f, 0.5f)));
                    learn.BoostHabitRatio = math.lerp(learn.BoostHabitRatio, isBoosting ? 1f : 0f, math.clamp(dt * 1.5f, 0f, 0.5f));

                    if (align > 0.62f || learn.BoostHabitRatio > 0.65f)
                    {
                        learn.MovementPredictability = math.min(1.0f, learn.MovementPredictability + dt * 0.16f);
                    }
                    else if (align < 0.20f)
                    {
                        learn.MovementPredictability = math.max(0.05f, learn.MovementPredictability - dt * 0.30f);
                    }
                }
            }
            else
            {
                learn.MovementPredictability = math.max(0.05f, learn.MovementPredictability * math.max(0f, 1f - dt * 0.08f));
            }

            float targetPat = learn.GravityPredictability * 0.55f + learn.MovementPredictability * 0.45f;
            learn.PatternAdaptationScore = math.clamp(math.lerp(learn.PatternAdaptationScore, targetPat, math.clamp(dt * 1.8f, 0f, 0.5f)), 0.04f, 1.0f);
        }

        public static void ApplyOutplayRegression(ref DroneLearningState learn, float skillLoss, float impulseLoss, float threshRise, float momLoss)
        {
            learn.CrateDodgeSkill = math.max(0.08f, learn.CrateDodgeSkill - skillLoss);
            learn.EscapeImpulseMultiplier = math.max(0.85f, learn.EscapeImpulseMultiplier - impulseLoss);
            learn.TrappedDetectionThreshold = math.min(0.95f, learn.TrappedDetectionThreshold + threshRise);
            learn.RegressionMomentum = math.clamp(learn.RegressionMomentum - momLoss, -1.0f, 1.0f);
            learn.OutplayedEvents++;
            learn.AdaptationStepStreak = math.min(0, learn.AdaptationStepStreak - 1);
        }

        public static void ApplyAdaptationBoost(ref DroneLearningState learn, float skillGain, float impulseGain, float threshDrop, float momGain)
        {
            learn.CrateDodgeSkill = math.min(0.92f, learn.CrateDodgeSkill + skillGain);
            learn.EscapeImpulseMultiplier = math.min(2.80f, learn.EscapeImpulseMultiplier + impulseGain);
            learn.TrappedDetectionThreshold = math.max(0.20f, learn.TrappedDetectionThreshold - threshDrop);
            learn.RegressionMomentum = math.clamp(learn.RegressionMomentum + momGain, -1.0f, 1.0f);
            learn.AdaptationStepStreak = math.max(1, learn.AdaptationStepStreak + 1);
        }

        private static void EmitCameraShake(ref SystemState state, float intensity)
        {
            var fbQuery = state.EntityManager.CreateEntityQuery(typeof(CameraFeedbackState));
            if (fbQuery.CalculateEntityCount() > 0)
            {
                using var arr = fbQuery.ToEntityArray(Allocator.Temp);
                var fb = state.EntityManager.GetComponentData<CameraFeedbackState>(arr[0]);
                fb.ShakeIntensity = math.max(fb.ShakeIntensity, intensity);
                state.EntityManager.SetComponentData(arr[0], fb);
            }
            fbQuery.Dispose();
        }

        private static void EmitAudioCue(ref SystemState state, AudioCueFlags cue)
        {
            var aeQ = state.EntityManager.CreateEntityQuery(typeof(AudioEventState));
            if (aeQ.CalculateEntityCount() > 0)
            {
                using var arr = aeQ.ToEntityArray(Allocator.Temp);
                var ae = state.EntityManager.GetComponentData<AudioEventState>(arr[0]);
                ae.PendingCues |= cue;
                state.EntityManager.SetComponentData(arr[0], ae);
            }
            aeQ.Dispose();
        }

        private static void IncrementDronesDestroyed(ref SystemState state)
        {
            UpdateCounterplayStats(ref state, 1, 0);
            EmitAudioCue(ref state, AudioCueFlags.DronePlasmaBlast);
        }

        private static void UpdateCounterplayStats(ref SystemState state, int dronesDestroyedDelta, int escapesObservedDelta)
        {
            var q = state.EntityManager.CreateEntityQuery(typeof(PlayerCounterplayStats));
            if (q.CalculateEntityCount() > 0)
            {
                using var arr = q.ToEntityArray(Allocator.Temp);
                var s = state.EntityManager.GetComponentData<PlayerCounterplayStats>(arr[0]);
                s.DronesDestroyed += dronesDestroyedDelta;
                s.EscapesObserved += escapesObservedDelta;
                state.EntityManager.SetComponentData(arr[0], s);
            }
            q.Dispose();
        }

        private static void TriggerPlayerTaggedReset(ref SystemState state)
        {
            EmitCameraShake(ref state, 1.0f); // 1.0 intensity screen shake on player caught/death!
            EmitAudioCue(ref state, AudioCueFlags.DronePlasmaBlast | AudioCueFlags.PlayerDamage | AudioCueFlags.SuitSpark | AudioCueFlags.PlayerDeath);

            // Zero velocities immediately; actual entity repositioning waits until GravityDriftBootstrap's
            // 1.0s EnergyExplosion wait + ease-in dark midpoint so AI & player stay visibly frozen until ease-out ends.
            var query = state.EntityManager.CreateEntityQuery(typeof(PhysicsVelocity), typeof(ResetableEntity));
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var vel = state.EntityManager.GetComponentData<PhysicsVelocity>(e);
                vel.Linear = float3.zero;
                vel.Angular = float3.zero;
                state.EntityManager.SetComponentData(e, vel);
            }
            query.Dispose();

            var sQuery = state.EntityManager.CreateEntityQuery(typeof(PlayerCounterplayStats));
            if (sQuery.CalculateEntityCount() > 0)
            {
                using var arr = sQuery.ToEntityArray(Allocator.Temp);
                var stats = state.EntityManager.GetComponentData<PlayerCounterplayStats>(arr[0]);
                stats.TimesTaggedByDrone++;
                state.EntityManager.SetComponentData(arr[0], stats);
            }
            sQuery.Dispose();

            var oQuery = state.EntityManager.CreateEntityQuery(typeof(LevelObjectiveState));
            if (oQuery.CalculateEntityCount() > 0)
            {
                using var oArr = oQuery.ToEntityArray(Allocator.Temp);
                var lvl = state.EntityManager.GetComponentData<LevelObjectiveState>(oArr[0]);
                if (lvl.MaxLives < 3) lvl.MaxLives = 5;
                lvl.LivesRemaining = math.max(0, lvl.LivesRemaining - 1);
                lvl.IsRespawning = true;
                lvl.PendingDeathSequence = true;
                state.EntityManager.SetComponentData(oArr[0], lvl);
            }
            oQuery.Dispose();
        }

        /// <summary>
        /// Explicit Two-Step Bayesian Posterior pi_t(s_i) Filter:
        /// Step 1 (Predict):   bar_pi_t(s_i) = sum_j T(s_i | s_j, a_drone, gravity) * pi_{t-1}(s_j)
        /// Step 2 (Update):    pi_t(s_i)     = P(o_t | s_i, a_player, a_drone) * bar_pi_t(s_i) / Z
        /// </summary>
        public static void UpdateBayesianPosteriorPi(
            float3 dronePos,
            bool playerFound,
            float3 playerPos,
            PlayerActionType playerAction,
            DroneActionType droneAction,
            GravityState gravity,
            RoomBoundsState bounds,
            bool hasSwitchTarget,
            float3 switchWorldPos,
            NativeParallelHashSet<int2> blockedCells,
            ref BayesianBeliefComponent belief,
            DynamicBuffer<BeliefCell> beliefBuffer,
            float dt)
        {
            int w = math.clamp(bounds.RoomWidth, 10, GridAStar.MaxGridWidth);
            int h = math.clamp(bounds.RoomHeight, 8, GridAStar.MaxGridHeight);
            int totalCells = w * h;

            if (beliefBuffer.Length != totalCells)
            {
                beliefBuffer.Clear();
                float initP = 1.0f / totalCells;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        beliefBuffer.Add(new BeliefCell
                        {
                            Coordinate = new int2(x, y),
                            Probability = initP,
                            PredictedProbability = initP
                        });
                    }
                }
            }

            // Modulate Transition Kernel T(s_i | s_j, a_drone, gravity_t)
            float2 gravDir = math.normalizesafe(new float2(gravity.GravityVector.x, gravity.GravityVector.y));
            float gravDriftWeight = (gravity.DirectionChangedThisFrame ? 0.28f : 0.06f);
            float selfStayWeight = droneAction switch
            {
                DroneActionType.Patrol => 0.72f,
                DroneActionType.Pursue => 0.62f,
                DroneActionType.EvadeCrateDodge => 0.48f,
                DroneActionType.GravityShift => 0.38f,
                DroneActionType.Reorient => 0.50f,
                _ => 0.65f
            };
            float neighborDiffusionWeight = (1.0f - selfStayWeight) * math.clamp(dt * 12f, 0.05f, 1.0f);

            // STEP 1: Prediction Step -> bar_pi_t(s_i) = sum_j T(s_i | s_j, a_drone, g_t) * pi_{t-1}(s_j)
            float priorEntropy = 0f;
            float uniform = 1.0f / totalCells;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    var cell = beliefBuffer[idx];
                    float piPrev = cell.Probability;
                    if (piPrev > 0.00001f) priorEntropy -= piPrev * math.log2(piPrev);

                    if (blockedCells.Contains(cell.Coordinate))
                    {
                        cell.PredictedProbability = 0.00005f;
                        beliefBuffer[idx] = cell;
                        continue;
                    }

                    // Aggregate inflow from self + 4-connected neighbors j
                    float sumTransition = piPrev * selfStayWeight;
                    float weightNorm = selfStayWeight;

                    int2 coord = new int2(x, y);
                    int2[] nbrs = { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) };
                    for (int n = 0; n < 4; n++)
                    {
                        int nx = coord.x + nbrs[n].x;
                        int ny = coord.y + nbrs[n].y;
                        if (nx >= 1 && nx < w - 1 && ny >= 1 && ny < h - 1)
                        {
                            int nIdx = ny * w + nx;
                            float inflowDirAlign = math.max(0f, math.dot((float2)(coord - new int2(nx, ny)), gravDir));
                            float t_ij = (neighborDiffusionWeight * 0.25f) + (gravDriftWeight * 0.25f * inflowDirAlign);
                            sumTransition += t_ij * beliefBuffer[nIdx].Probability;
                            weightNorm += t_ij;
                        }
                    }

                    // Small uniform teleport floor prevents zero-locking on sudden flips
                    float predicted = (sumTransition / math.max(0.01f, weightNorm)) * 0.985f + uniform * 0.015f;
                    cell.PredictedProbability = predicted;
                    beliefBuffer[idx] = cell;
                }
            }

            // Modulate Observation Likelihood P(o_t | s_i, a_player, a_drone)
            float likelihoodSigmaSq = playerAction switch
            {
                PlayerActionType.CrateAttack => 4.2f,    // Boost/Flip near drone creates sharp localized signature
                PlayerActionType.JetpackBoost => 5.0f,   // Loud thruster spike
                PlayerActionType.GravityFlip => 6.0f,
                PlayerActionType.ObjectivePush => 7.5f,
                PlayerActionType.JetpackThrust => 8.5f,
                _ => 11.0f
            };
            float sharpness = (droneAction == DroneActionType.EvadeCrateDodge) ? 1.25f : 1.0f;

            int2 playerGrid = playerFound ? GridAStar.WorldToGrid(playerPos, w, h, bounds.RoomOrigin) : new int2(-1, -1);
            int2 droneGrid = GridAStar.WorldToGrid(dronePos, w, h, bounds.RoomOrigin);
            int2 switchGrid = hasSwitchTarget ? GridAStar.WorldToGrid(switchWorldPos, w, h, bounds.RoomOrigin) : new int2(-1, -1);

            bool visualContact = playerFound && (math.distance(dronePos, playerPos) < 9.5f);

            // STEP 2: Observation Likelihood & Unnormalized Posterior
            float partitionZ = 0f;
            for (int i = 0; i < beliefBuffer.Length; i++)
            {
                var cell = beliefBuffer[i];
                float likelihood = 0.20f;

                if (blockedCells.Contains(cell.Coordinate))
                {
                    likelihood = 0.0001f;
                }
                else
                {
                    if (playerFound)
                    {
                        float dPlayer = math.distance((float2)cell.Coordinate, (float2)playerGrid);
                        float sensorGain = visualContact ? 1.0f : 0.45f;
                        likelihood += sensorGain * sharpness * math.exp(-(dPlayer * dPlayer) / likelihoodSigmaSq);

                        // Negative observation exclusion in drone's close scanned neighborhood
                        float dDrone = math.distance((float2)cell.Coordinate, (float2)droneGrid);
                        if (dDrone < 3.5f && dPlayer > 1.8f)
                        {
                            likelihood *= 0.10f;
                        }
                    }

                    // Objective intent boost: when player is pushing switch/airlock
                    if (hasSwitchTarget && (playerAction == PlayerActionType.ObjectivePush || playerAction == PlayerActionType.JetpackThrust))
                    {
                        float dSwitch = math.distance((float2)cell.Coordinate, (float2)switchGrid);
                        likelihood += 0.18f * math.exp(-(dSwitch * dSwitch) / 10.0f);
                    }
                }

                float unnormPi = likelihood * cell.PredictedProbability;
                cell.Probability = unnormPi;
                beliefBuffer[i] = cell;
                partitionZ += unnormPi;
            }

            // STEP 3: Normalization by Partition Z & Posterior Summary Statistics
            float postEntropy = 0f;
            float maxP = 0f;
            int2 bestCoord = new int2(w / 2, h / 2);

            if (partitionZ > 0.000001f)
            {
                for (int i = 0; i < beliefBuffer.Length; i++)
                {
                    var cell = beliefBuffer[i];
                    cell.Probability /= partitionZ;
                    beliefBuffer[i] = cell;

                    if (cell.Probability > maxP)
                    {
                        maxP = cell.Probability;
                        bestCoord = cell.Coordinate;
                    }

                    if (cell.Probability > 0.00001f)
                    {
                        postEntropy -= cell.Probability * math.log2(cell.Probability);
                    }
                }
            }

            // Compute 3x3 cluster posterior confidence mass around MAP target s*
            float clusterMass = 0f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = bestCoord.x + dx;
                    int cy = bestCoord.y + dy;
                    if (cx >= 0 && cx < w && cy >= 0 && cy < h)
                    {
                        clusterMass += beliefBuffer[cy * w + cx].Probability;
                    }
                }
            }

            belief.PriorEntropy = priorEntropy;
            belief.Entropy = postEntropy;
            belief.Confidence = maxP;
            belief.TopClusterConfidence = math.clamp(clusterMass, 0f, 1f);
            belief.EstimatedTargetCell = bestCoord;
            belief.EstimatedWorldPos = GridAStar.GridToWorld(bestCoord, bounds.RoomOrigin, 0f);
            belief.HasVisualContact = visualContact;
            belief.TimeSinceLastSighting = visualContact ? 0f : (belief.TimeSinceLastSighting + dt);
            belief.LastPlayerAction = playerAction;
            belief.LastDroneAction = droneAction;
            belief.LikelihoodSharpness = sharpness;
            belief.TransitionDriftWeight = gravDriftWeight;
        }
    }
}
