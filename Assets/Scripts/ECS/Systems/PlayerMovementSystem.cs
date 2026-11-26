using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace GravityDrift
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GravitySystem))]
    public partial struct PlayerMovementSystem : ISystem
    {
        private float hazardCooldownTimer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GravityState>();
            state.RequireForUpdate<RoomBoundsState>();
            hazardCooldownTimer = 0f;
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = math.min(SystemAPI.Time.DeltaTime, 0.05f);
            if (dt <= 0.00001f) return;
            if (GameUIManager.Instance != null && GameUIManager.Instance.currentState != UIState.Gameplay) return;

            if (SystemAPI.HasSingleton<LevelObjectiveState>())
            {
                var objState = SystemAPI.GetSingleton<LevelObjectiveState>();
                if (objState.IsRespawning || objState.IsRunOver || objState.IsRunCompleted || InputHelper.IsInputFrozen)
                {
                    foreach (var (velocity, jetpack) in
                        SystemAPI.Query<RefRW<PhysicsVelocity>, RefRW<JetpackComponent>>().WithAll<PlayerTag>())
                    {
                        velocity.ValueRW.Linear = float3.zero;
                        velocity.ValueRW.Angular = float3.zero;
                        jetpack.ValueRW.IsThrusting = false;
                        jetpack.ValueRW.IsBoosting = false;
                    }
                    return;
                }
            }

            var gravity = SystemAPI.GetSingleton<GravityState>();
            var bounds = SystemAPI.GetSingleton<RoomBoundsState>();
            hazardCooldownTimer = math.max(0f, hazardCooldownTimer - dt);

            Vector2 thrustInput = InputHelper.GetJetpackThrustVector();
            bool boostInput = InputHelper.GetJetpackBoost();

            float3 thrustDir = new float3(thrustInput.x, thrustInput.y, 0f);
            float inputMagnitude = math.length(thrustDir);
            bool hasThrustInput = inputMagnitude > 0.05f;
            if (hasThrustInput)
            {
                thrustDir = math.normalize(thrustDir);
            }

            var hazardList = new NativeList<HazardPadTag>(Allocator.Temp);
            foreach (var hz in SystemAPI.Query<RefRO<HazardPadTag>>())
            {
                hazardList.Add(hz.ValueRO);
            }

            var wallPositions = new NativeList<float3>(Allocator.Temp);
            foreach (var wTf in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<StationObstacleTag>())
            {
                wallPositions.Add(wTf.ValueRO.Position);
            }

            var cratePositions = new NativeList<float3>(Allocator.Temp);
            foreach (var cTf in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<CargoCrateTag>())
            {
                cratePositions.Add(cTf.ValueRO.Position);
            }

            bool hasNearDroneOrCrate = false;
            PlayerActionType activeAction = PlayerActionType.Idle;

            foreach (var (transform, velocity, jetpack, movement) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRW<JetpackComponent>, RefRW<PlayerMovement>>().WithAll<PlayerTag>())
            {
                // 1. Jetpack Cooldown
                if (jetpack.ValueRO.IsInCooldown)
                {
                    jetpack.ValueRW.CooldownTimer -= dt;
                    if (jetpack.ValueRO.CooldownTimer <= 0f)
                    {
                        jetpack.ValueRW.IsInCooldown = false;
                        jetpack.ValueRW.CooldownTimer = 0f;
                    }
                }

                bool canThrust = hasThrustInput && !jetpack.ValueRO.IsInCooldown && jetpack.ValueRO.Fuel > 0f;
                bool isBoosting = canThrust && boostInput;

                jetpack.ValueRW.IsThrusting = canThrust;
                jetpack.ValueRW.IsBoosting = isBoosting;

                if (canThrust)
                {
                    jetpack.ValueRW.ThrustDirection = thrustDir;

                    float burn = isBoosting ? jetpack.ValueRO.BoostBurnRate : jetpack.ValueRO.BurnRate;
                    jetpack.ValueRW.Fuel = math.max(0f, jetpack.ValueRO.Fuel - burn * dt);

                    if (isBoosting)
                    {
                        jetpack.ValueRW.TotalBoostTime += dt;
                    }

                    if (jetpack.ValueRO.Fuel <= 0f)
                    {
                        if (!jetpack.ValueRO.IsInCooldown)
                        {
                            jetpack.ValueRW.OverloadCount++;
                        }
                        jetpack.ValueRW.IsInCooldown = true;
                        jetpack.ValueRW.CooldownTimer = jetpack.ValueRO.CooldownDuration;
                        jetpack.ValueRW.IsThrusting = false;
                        jetpack.ValueRW.IsBoosting = false;
                    }
                    else
                    {
                        float maxSpd = jetpack.ValueRO.MaxSpeed * (isBoosting ? jetpack.ValueRO.BoostMultiplier : 1.0f);
                        float effectiveForce = jetpack.ValueRO.ThrustForce * (isBoosting ? jetpack.ValueRO.BoostMultiplier : 1.0f);

                        // Apply directional jetpack thrust and responsive velocity steering
                        float3 desiredVel = thrustDir * maxSpd;
                        velocity.ValueRW.Linear = math.lerp(velocity.ValueRO.Linear, desiredVel, math.clamp(dt * (effectiveForce * 0.45f), 0f, 1f));
                        velocity.ValueRW.Linear += thrustDir * (effectiveForce * 0.35f * dt);

                        float currentSpd = math.length(velocity.ValueRO.Linear);
                        if (currentSpd > maxSpd)
                        {
                            velocity.ValueRW.Linear = math.normalize(velocity.ValueRO.Linear) * maxSpd;
                        }

                        quaternion targetRot = quaternion.LookRotationSafe(new float3(0, 0, 1), thrustDir);
                        transform.ValueRW.Rotation = math.slerp(transform.ValueRO.Rotation, targetRot, dt * movement.ValueRO.RotationSpeed);
                    }
                }
                else
                {
                    // Hover Idle / Cooldown: regenerate fuel when not overloaded, smooth inertial damping
                    if (!jetpack.ValueRO.IsInCooldown)
                    {
                        jetpack.ValueRW.Fuel = math.min(jetpack.ValueRO.MaxFuel, jetpack.ValueRO.Fuel + jetpack.ValueRO.RechargeRate * dt);
                    }

                    velocity.ValueRW.Linear *= math.clamp(1.0f - 4.2f * dt, 0.0f, 1.0f);

                    quaternion upright = quaternion.identity;
                    transform.ValueRW.Rotation = math.slerp(transform.ValueRO.Rotation, upright, dt * 6.0f);
                }

                // 2. EXPLICIT POSITION INTEGRATION (Player moves cleanly via ECS LocalTransform + Wall/Crate Collision!)
                float3 pos = transform.ValueRO.Position + velocity.ValueRO.Linear * dt;
                float3 vel = velocity.ValueRO.Linear;
                pos.z = 0f;
                vel.z = 0f;

                // Resolve collision against internal StationObstacleTag walls/platforms (1.0x1.0 AABB vs player radius 0.36)
                const float playerRadius = 0.36f;
                const float halfWall = 0.50f;
                for (int w = 0; w < wallPositions.Length; w++)
                {
                    float3 wp = wallPositions[w];
                    float cx = math.clamp(pos.x, wp.x - halfWall, wp.x + halfWall);
                    float cy = math.clamp(pos.y, wp.y - halfWall, wp.y + halfWall);
                    float dx = pos.x - cx;
                    float dy = pos.y - cy;
                    float dSq = dx * dx + dy * dy;
                    if (dSq < playerRadius * playerRadius)
                    {
                        float d = math.sqrt(math.max(0.00001f, dSq));
                        float nx = (d > 0.001f) ? (dx / d) : 0f;
                        float ny = (d > 0.001f) ? (dy / d) : 1f;
                        float push = playerRadius - d;
                        pos.x += nx * push;
                        pos.y += ny * push;
                        float vn = vel.x * nx + vel.y * ny;
                        if (vn < 0f)
                        {
                            vel.x -= vn * nx;
                            vel.y -= vn * ny;
                        }
                    }
                }

                // Resolve collision against heavy Cargo Crates (0.8x0.8 AABB vs player radius 0.36)
                const float halfCrate = 0.40f;
                for (int c = 0; c < cratePositions.Length; c++)
                {
                    float3 cp = cratePositions[c];
                    float cx = math.clamp(pos.x, cp.x - halfCrate, cp.x + halfCrate);
                    float cy = math.clamp(pos.y, cp.y - halfCrate, cp.y + halfCrate);
                    float dx = pos.x - cx;
                    float dy = pos.y - cy;
                    float dSq = dx * dx + dy * dy;
                    if (dSq < playerRadius * playerRadius)
                    {
                        float d = math.sqrt(math.max(0.00001f, dSq));
                        float nx = (d > 0.001f) ? (dx / d) : 0f;
                        float ny = (d > 0.001f) ? (dy / d) : 1f;
                        float push = playerRadius - d;
                        pos.x += nx * push;
                        pos.y += ny * push;
                        float vn = vel.x * nx + vel.y * ny;
                        if (vn < 0f)
                        {
                            vel.x -= vn * nx;
                            vel.y -= vn * ny;
                        }
                    }
                }

                // 3. Hazard Pad Penalty: Fuel Drain + Knockback + 1.0 Camera Shake!
                if (hazardCooldownTimer <= 0f)
                {
                    for (int h = 0; h < hazardList.Length; h++)
                    {
                        var hz = hazardList[h];
                        if (math.abs(pos.x - hz.Position.x) < 0.62f && math.abs(pos.y - hz.Position.y) < 0.52f)
                        {
                            hazardCooldownTimer = 0.70f;
                            float drain = (hz.PenaltyFuelDrain > 1f) ? hz.PenaltyFuelDrain : 25f;
                            float knock = (hz.KnockbackImpulse > 1f) ? hz.KnockbackImpulse : 7.5f;

                            jetpack.ValueRW.Fuel = math.max(0f, jetpack.ValueRO.Fuel - drain);
                            if (jetpack.ValueRO.Fuel <= 0f)
                            {
                                if (!jetpack.ValueRO.IsInCooldown)
                                {
                                    jetpack.ValueRW.OverloadCount++;
                                }
                                jetpack.ValueRW.IsInCooldown = true;
                                jetpack.ValueRW.CooldownTimer = jetpack.ValueRO.CooldownDuration;
                            }

                            float3 knockDir = math.normalizesafe(new float3(pos.x - hz.Position.x, 1.2f, 0f));
                            vel += knockDir * knock;

                            if (SystemAPI.HasSingleton<LevelObjectiveState>())
                            {
                                var objEntity = SystemAPI.GetSingletonEntity<LevelObjectiveState>();
                                var lvl = SystemAPI.GetComponent<LevelObjectiveState>(objEntity);
                                lvl.HazardHits++;
                                lvl.HazardWarningTimer = 1.6f;
                                SystemAPI.SetComponent(objEntity, lvl);
                            }

                            // 1.0 intensity screen shake on player damage!
                            if (SystemAPI.HasSingleton<CameraFeedbackState>())
                            {
                                var fbEntity = SystemAPI.GetSingletonEntity<CameraFeedbackState>();
                                var fb = SystemAPI.GetComponent<CameraFeedbackState>(fbEntity);
                                fb.ShakeIntensity = math.max(fb.ShakeIntensity, 1.0f);
                                SystemAPI.SetComponent(fbEntity, fb);
                            }

                            if (SystemAPI.HasSingleton<AudioEventState>())
                            {
                                var aeEntity = SystemAPI.GetSingletonEntity<AudioEventState>();
                                var ae = SystemAPI.GetComponent<AudioEventState>(aeEntity);
                                ae.PendingCues |= AudioCueFlags.HazardZap | AudioCueFlags.PlayerDamage | AudioCueFlags.SuitSpark;
                                SystemAPI.SetComponent(aeEntity, ae);
                            }
                            break;
                        }
                    }
                }

                // 4. Classify Player Action (a^player_t)
                foreach (var droneTf in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<DroneAITag>())
                {
                    if (math.distance(pos, droneTf.ValueRO.Position) < 4.5f)
                    {
                        hasNearDroneOrCrate = true;
                        break;
                    }
                }

                if (gravity.DirectionChangedThisFrame)
                {
                    activeAction = hasNearDroneOrCrate ? PlayerActionType.CrateAttack : PlayerActionType.GravityFlip;
                }
                else if (isBoosting && hasNearDroneOrCrate)
                {
                    activeAction = PlayerActionType.CrateAttack;
                }
                else if (isBoosting)
                {
                    activeAction = PlayerActionType.JetpackBoost;
                }
                else if (canThrust)
                {
                    activeAction = PlayerActionType.JetpackThrust;
                }
                else
                {
                    activeAction = PlayerActionType.Idle;
                }

                // 5. Strict Inner-Room Boundary Clamping
                const float margin = 0.45f;
                float minX = bounds.InnerMinX + margin;
                float maxX = bounds.InnerMaxX - margin;
                float minY = bounds.InnerMinY + margin;
                float maxY = bounds.InnerMaxY - margin;

                if (pos.x < minX) { pos.x = minX; if (vel.x < 0f) vel.x = 0f; }
                else if (pos.x > maxX) { pos.x = maxX; if (vel.x > 0f) vel.x = 0f; }

                if (pos.y < minY) { pos.y = minY; if (vel.y < 0f) vel.y = 0f; }
                else if (pos.y > maxY) { pos.y = maxY; if (vel.y > 0f) vel.y = 0f; }

                transform.ValueRW.Position = pos;
                velocity.ValueRW.Linear = vel;
                movement.ValueRW.CurrentVelocity = vel;
            }

            if (SystemAPI.HasSingleton<LevelObjectiveState>())
            {
                var objEntity = SystemAPI.GetSingletonEntity<LevelObjectiveState>();
                var lvl = SystemAPI.GetComponent<LevelObjectiveState>(objEntity);
                if (activeAction != PlayerActionType.GravityFlip && activeAction != PlayerActionType.CrateAttack)
                {
                    if (lvl.CurrentPlayerAction == PlayerActionType.ObjectivePush && activeAction == PlayerActionType.JetpackThrust)
                    {
                        activeAction = PlayerActionType.ObjectivePush;
                    }
                }
                lvl.CurrentPlayerAction = activeAction;
                SystemAPI.SetComponent(objEntity, lvl);
            }

            if (activeAction == PlayerActionType.CrateAttack)
            {
                if (SystemAPI.HasSingleton<PlayerCounterplayStats>())
                {
                    var statsEntity = SystemAPI.GetSingletonEntity<PlayerCounterplayStats>();
                    var stats = SystemAPI.GetComponent<PlayerCounterplayStats>(statsEntity);
                    stats.SmartPlayerAttacks++;
                    SystemAPI.SetComponent(statsEntity, stats);
                }
                if (SystemAPI.HasSingleton<DroneLearningState>())
                {
                    var learnEntity = SystemAPI.GetSingletonEntity<DroneLearningState>();
                    var lState = SystemAPI.GetComponent<DroneLearningState>(learnEntity);
                    lState.SmartPlayerAttacksObserved++;
                    SystemAPI.SetComponent(learnEntity, lState);
                }
            }

            hazardList.Dispose();
            wallPositions.Dispose();
            cratePositions.Dispose();
        }
    }
}
