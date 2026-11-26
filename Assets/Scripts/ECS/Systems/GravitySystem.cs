using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace GravityDrift
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct GravitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<PhysicsStep>())
            {
                var stepEntity = state.EntityManager.CreateEntity(typeof(PhysicsStep));
                state.EntityManager.SetComponentData(stepEntity, new PhysicsStep
                {
                    Gravity = new float3(0, -25f, 0)
                });
            }

            if (!SystemAPI.HasSingleton<GravityState>())
            {
                var gravEntity = state.EntityManager.CreateEntity(typeof(GravityState));
                state.EntityManager.SetComponentData(gravEntity, new GravityState
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

            if (!SystemAPI.HasSingleton<RoomBoundsState>())
            {
                var boundsEntity = state.EntityManager.CreateEntity(typeof(RoomBoundsState));
                state.EntityManager.SetComponentData(boundsEntity, new RoomBoundsState
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

            if (!SystemAPI.HasSingleton<CameraFeedbackState>())
            {
                state.EntityManager.CreateEntity(typeof(CameraFeedbackState));
            }
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
                    var frozenGravEntity = SystemAPI.GetSingletonEntity<GravityState>();
                    var frozenGrav = SystemAPI.GetComponent<GravityState>(frozenGravEntity);
                    frozenGrav.DirectionChangedThisFrame = false;
                    SystemAPI.SetComponent(frozenGravEntity, frozenGrav);
                    return;
                }
            }

            var gravityEntity = SystemAPI.GetSingletonEntity<GravityState>();
            var gravity = SystemAPI.GetComponent<GravityState>(gravityEntity);
            gravity.DirectionChangedThisFrame = false;
            gravity.TimeSinceLastFlip += dt;

            if (gravity.CurrentCharges < gravity.MaxCharges)
            {
                gravity.RechargeTimer += dt;
                if (gravity.RechargeTimer >= gravity.RechargeCooldown)
                {
                    gravity.CurrentCharges = math.min(gravity.MaxCharges, gravity.CurrentCharges + 1);
                    gravity.RechargeTimer = 0f;
                }
            }

            // 4-way gravity input: PC Arrow keys / 1-4 or PlayStation DualSense D-Pad / Face Buttons
            GravityDirection? requestedDir = InputHelper.GetGravityFlipInput();
            bool flipTriggered = requestedDir.HasValue;
            GravityDirection targetDir = requestedDir ?? gravity.CurrentDirection;

            if (flipTriggered && targetDir != gravity.CurrentDirection && gravity.CurrentCharges > 0)
            {
                gravity.PreviousDirection = gravity.CurrentDirection;
                gravity.CurrentDirection = targetDir;
                gravity.TotalFlips++;
                gravity.TimeSinceLastFlip = 0f;
                gravity.DirectionChangedThisFrame = true;
                gravity.CurrentCharges--;

                float2 bounceDir = float2.zero;
                switch (targetDir)
                {
                    case GravityDirection.Down:
                        gravity.GravityVector = new float3(0, -gravity.GravityMagnitude, 0);
                        bounceDir = new float2(0f, -1f);
                        break;
                    case GravityDirection.Up:
                        gravity.GravityVector = new float3(0, gravity.GravityMagnitude, 0);
                        bounceDir = new float2(0f, 1f);
                        break;
                    case GravityDirection.Left:
                        gravity.GravityVector = new float3(-gravity.GravityMagnitude, 0, 0);
                        bounceDir = new float2(-1f, 0f);
                        break;
                    case GravityDirection.Right:
                        gravity.GravityVector = new float3(gravity.GravityMagnitude, 0, 0);
                        bounceDir = new float2(1f, 0f);
                        break;
                }

                if (SystemAPI.HasSingleton<PhysicsStep>())
                {
                    var stepEntity = SystemAPI.GetSingletonEntity<PhysicsStep>();
                    var step = SystemAPI.GetComponent<PhysicsStep>(stepEntity);
                    step.Gravity = gravity.GravityVector;
                    SystemAPI.SetComponent(stepEntity, step);
                }

                if (SystemAPI.HasSingleton<PlayerCounterplayStats>())
                {
                    var statsEntity = SystemAPI.GetSingletonEntity<PlayerCounterplayStats>();
                    var stats = SystemAPI.GetComponent<PlayerCounterplayStats>(statsEntity);
                    stats.GravityFlipsUsed++;
                    SystemAPI.SetComponent(statsEntity, stats);
                }

                // Emit snappy Gravity-Shift Directional Bounce Bump!
                if (SystemAPI.HasSingleton<CameraFeedbackState>())
                {
                    var fbEntity = SystemAPI.GetSingletonEntity<CameraFeedbackState>();
                    var fb = SystemAPI.GetComponent<CameraFeedbackState>(fbEntity);
                    fb.PendingBounceImpulse = bounceDir;
                    SystemAPI.SetComponent(fbEntity, fb);
                }

                if (SystemAPI.HasSingleton<AudioEventState>())
                {
                    var aeEntity = SystemAPI.GetSingletonEntity<AudioEventState>();
                    var ae = SystemAPI.GetComponent<AudioEventState>(aeEntity);
                    ae.PendingCues |= AudioCueFlags.GravityShift;
                    SystemAPI.SetComponent(aeEntity, ae);
                }
            }

            SystemAPI.SetComponent(gravityEntity, gravity);

            // Strict Planar 2.5D Lock + Inner-Room Boundary Clamping
            var bounds = SystemAPI.GetSingleton<RoomBoundsState>();
            const float wallMargin = 0.44f;
            float minX = bounds.InnerMinX + wallMargin;
            float maxX = bounds.InnerMaxX - wallMargin;
            float minY = bounds.InnerMinY + wallMargin;
            float maxY = bounds.InnerMaxY - wallMargin;

            foreach (var (transform, velocity, planar) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRO<PlanarPhysics2DTag>>())
            {
                float3 pos = transform.ValueRO.Position;
                float3 lin = velocity.ValueRO.Linear;

                pos.z = planar.ValueRO.LockZPosition;
                lin.z = 0f;

                if (pos.x < minX) { pos.x = minX; if (lin.x < 0f) lin.x = 0f; }
                else if (pos.x > maxX) { pos.x = maxX; if (lin.x > 0f) lin.x = 0f; }

                if (pos.y < minY) { pos.y = minY; if (lin.y < 0f) lin.y = 0f; }
                else if (pos.y > maxY) { pos.y = maxY; if (lin.y > 0f) lin.y = 0f; }

                transform.ValueRW.Position = pos;
                velocity.ValueRW.Linear = lin;

                float3 ang = velocity.ValueRO.Angular;
                ang.x = 0f;
                ang.y = 0f;
                velocity.ValueRW.Angular = ang;
            }
        }
    }
}
