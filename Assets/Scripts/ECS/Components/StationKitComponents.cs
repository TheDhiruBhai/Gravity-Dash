using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public struct StationObstacleTag : IComponentData
    {
    }

    public struct CargoCrateTag : IComponentData
    {
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
