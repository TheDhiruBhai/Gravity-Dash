using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GravityDrift
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ObjectiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<LevelObjectiveState>())
            {
                var e = state.EntityManager.CreateEntity(typeof(LevelObjectiveState));
                state.EntityManager.SetComponentData(e, new LevelObjectiveState
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
                    RespawnGraceTimer = 0f,
                    IsRunOver = false,
                    IsRunCompleted = false
                });
            }

            if (!SystemAPI.HasSingleton<PlayerCounterplayStats>())
            {
                var se = state.EntityManager.CreateEntity(typeof(PlayerCounterplayStats));
                state.EntityManager.SetComponentData(se, new PlayerCounterplayStats());
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = math.min(SystemAPI.Time.DeltaTime, 0.05f);
            if (dt <= 0.00001f) return;
            if (GameUIManager.Instance != null && GameUIManager.Instance.currentState != UIState.Gameplay) return;

            var objEntity = SystemAPI.GetSingletonEntity<LevelObjectiveState>();
            var levelState = SystemAPI.GetComponent<LevelObjectiveState>(objEntity);

            if (levelState.IsRunOver || levelState.IsRunCompleted || levelState.IsRespawning) return;

            if (levelState.RespawnGraceTimer > 0f)
            {
                levelState.RespawnGraceTimer = math.max(0f, levelState.RespawnGraceTimer - dt);
            }

            if (!levelState.RoomCleared)
            {
                levelState.RoomTimer += dt;
            }
            if (levelState.HazardWarningTimer > 0f)
            {
                levelState.HazardWarningTimer = math.max(0f, levelState.HazardWarningTimer - dt);
            }

            float3 playerPos = float3.zero;
            bool playerFound = false;
            foreach (var pTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                playerPos = pTransform.ValueRO.Position;
                playerFound = true;
                break;
            }

            bool pushingObjective = false;

            // 1. Energy Cores
            int totalCores = 0;
            int coresCollected = 0;
            foreach (var (transform, core) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<EnergyCoreTag>>())
            {
                totalCores++;
                if (!core.ValueRO.IsCollected)
                {
                    transform.ValueRW.Rotation = math.mul(transform.ValueRO.Rotation, quaternion.Euler(0, dt * 3.5f, dt * 1.5f));
                    float d = playerFound ? math.distance(playerPos, transform.ValueRO.Position) : 99f;
                    if (d < 3.0f) pushingObjective = true;
                    if (playerFound && d < 0.95f)
                    {
                        core.ValueRW.IsCollected = true;
                        transform.ValueRW.Position = new float3(999f, 999f, 0f);
                    }
                }
                else
                {
                    coresCollected++;
                }
            }
            levelState.TotalCores = totalCores;
            levelState.CoresCollected = coresCollected;

            // 2. Proximity Switch
            int totalSwitches = 0;
            int switchesActive = 0;
            foreach (var (transform, pSwitch) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PressureSwitchTag>>())
            {
                totalSwitches++;
                float3 switchPos = pSwitch.ValueRO.SwitchPosition;
                float proxRadius = (pSwitch.ValueRO.ProximityRadius > 0.1f) ? pSwitch.ValueRO.ProximityRadius : 1.35f;

                if (playerFound)
                {
                    float d = math.distance(playerPos, switchPos);
                    if (!pSwitch.ValueRO.IsPressed && d < 3.2f) pushingObjective = true;
                    if (d < proxRadius)
                    {
                        pSwitch.ValueRW.IsPressed = true;
                    }
                }

                if (pSwitch.ValueRO.IsPressed)
                {
                    switchesActive++;
                }
            }
            levelState.TotalSwitches = totalSwitches;
            levelState.SwitchesActive = switchesActive;
            levelState.SwitchFlipped = (totalSwitches == 0 || switchesActive >= totalSwitches);

            // 3. Stationary Exit Airlock with Outward Room-Facing Trigger Zone
            bool allCoresCollected = (totalCores == 0 || coresCollected >= totalCores);
            bool canUnlock = levelState.SwitchFlipped && allCoresCollected;
            levelState.AirlockUnlocked = canUnlock;

            foreach (var (transform, airlock) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ExitAirlockTag>>())
            {
                airlock.ValueRW.IsUnlocked = canUnlock;
                airlock.ValueRW.SlideProgress = canUnlock ? 1.0f : 0.0f;
                airlock.ValueRW.DoorPosition = airlock.ValueRO.ClosedPosition;
                transform.ValueRW.Position = airlock.ValueRO.ClosedPosition;

                levelState.DoorSlideProgress = airlock.ValueRO.SlideProgress;
                levelState.AirlockFullyOpen = canUnlock;

                // Trigger zone extends outward from right wall (-X into room) so player clears cleanly
                float3 triggerCenter = airlock.ValueRO.ClosedPosition + new float3(-0.25f, 0f, 0f);
                if (canUnlock && playerFound)
                {
                    if (math.distance(playerPos, triggerCenter) < 4.5f)
                    {
                        pushingObjective = true;
                    }
                    if (math.abs(playerPos.x - triggerCenter.x) < 1.15f &&
                        math.abs(playerPos.y - triggerCenter.y) < 1.25f)
                    {
                        levelState.RoomCleared = true;
                    }
                }
            }

            if (pushingObjective && levelState.CurrentPlayerAction == PlayerActionType.JetpackThrust)
            {
                levelState.CurrentPlayerAction = PlayerActionType.ObjectivePush;
            }

            SystemAPI.SetComponent(objEntity, levelState);
        }
    }
}
