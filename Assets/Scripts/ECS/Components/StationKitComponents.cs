using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public struct StationObstacleTag : IComponentData
    {
    }

    public struct CargoCrateTag : IComponentData
    {
        public float3 PrePhysicsVelocity;
        public float3 PendingImpactVelocity;
        public float3 PendingImpactTargetPos;
        public float PendingImpactTimer;
        public bool HasPendingImpact;
    }

    public struct HazardPadTag : IComponentData
    {
        public float3 Position;
        public float PenaltyFuelDrain;
        public float KnockbackImpulse;
    }

    public struct ResetableEntity : IComponentData
    {
        public float3 SpawnPosition;
        public quaternion SpawnRotation;
        public bool PendingReset;
    }

    public struct ProceduralLevelTag : IComponentData
    {
        public int LevelSeed;
        public int LevelIndex;
    }
}
