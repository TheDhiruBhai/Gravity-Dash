using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace GravityDrift
{
    public class DroneAuthoring : MonoBehaviour
    {
        public float moveSpeed = 4.2f;
        public float detectionRadius = 9.0f;
        public float loseTargetRadius = 13.0f;
        public Vector3 patrolPointA = new Vector3(-6f, 3.5f, 0f);
        public Vector3 patrolPointB = new Vector3(6f, 3.5f, 0f);
        public bool baselineMode = false;

        public class Baker : Baker<DroneAuthoring>
        {
            public override void Bake(DroneAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new DroneAITag());
                AddComponent(entity, new PhysicsGravityFactor { Value = 0f }); // Gravity-neutral Drone!
                AddComponent(entity, new DroneState
                {
                    CurrentState = DroneFSMState.Patrol,
                    PreviousState = DroneFSMState.Patrol,
                    ActiveAction = DroneActionType.Patrol,
                    StateTimer = 0f,
                    MoveSpeed = authoring.moveSpeed,
                    DetectionRadius = authoring.detectionRadius,
                    LoseTargetRadius = authoring.loseTargetRadius,
                    TargetPosition = authoring.transform.position,
                    PatrolPointA = authoring.patrolPointA,
                    PatrolPointB = authoring.patrolPointB,
                    CurrentPatrolIndex = 0,
                    CurrentPathIndex = 0,
                    PathReplanCooldown = 0f,
                    StuckTimer = 0f,
                    LastPosition = authoring.transform.position,
                    HasMagBoots = true,
                    IsUnlatched = false,
                    UnlatchedDuration = 0f,
                    IsDestroyed = false,
                    BaselineMode = authoring.baselineMode
                });

                AddComponent(entity, new DroneTelemetry
                {
                    NodesExpanded = 0,
                    ReplanLatencyMs = 0f,
                    TargetReached = false,
                    StuckOnObstacle = false,
                    CurrentBeliefEntropy = 4.5f,
                    HazardCellsAvoided = 0
                });

                AddComponent(entity, new BayesianBeliefComponent
                {
                    Entropy = 4.5f,
                    PriorEntropy = 4.5f,
                    EstimatedTargetCell = GridAStar.WorldToGrid(authoring.transform.position),
                    EstimatedWorldPos = authoring.transform.position,
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

                AddComponent(entity, new PlanarPhysics2DTag
                {
                    LockZPosition = 0f
                });

                AddComponent(entity, new ResetableEntity
                {
                    SpawnPosition = authoring.transform.position,
                    SpawnRotation = authoring.transform.rotation,
                    PendingReset = false
                });

                AddBuffer<GridCell>(entity);
                AddBuffer<BeliefCell>(entity);
            }
        }
    }
}
