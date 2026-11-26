using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public enum GravityDirection : byte
    {
        Down = 0,
        Up = 1,
        Left = 2,
        Right = 3
    }

    public struct GravityState : IComponentData
    {
        public GravityDirection CurrentDirection;
        public GravityDirection PreviousDirection;
        public float3 GravityVector;
        public float GravityMagnitude;
        public float TimeSinceLastFlip;
        public int TotalFlips;
        public bool DirectionChangedThisFrame;

        // Gravity Charges (recharging steadily over time)
        public int CurrentCharges;
        public int MaxCharges;
        public float RechargeTimer;
        public float RechargeCooldown;
        public bool CanFlip => CurrentCharges > 0;
    }

    public struct PlanarPhysics2DTag : IComponentData
    {
        public float LockZPosition;
    }

    /// <summary>
    /// Synchronized singleton holding exact inner-room playable bounds and dynamic grid dimensions.
    /// Updated whenever PCGRoomGenerator creates or scales a level.
    /// </summary>
    public struct RoomBoundsState : IComponentData
    {
        public int RoomWidth;
        public int RoomHeight;
        public float2 RoomOrigin;
        public float InnerMinX;
        public float InnerMaxX;
        public float InnerMinY;
        public float InnerMaxY;
    }

    /// <summary>
    /// Camera screen-shake and gravity-shift directional bounce feedback singleton.
    /// </summary>
    public struct CameraFeedbackState : IComponentData
    {
        public float ShakeIntensity;          // 0.5 crate hit drone, 1.0 player damage/death
        public float2 PendingBounceImpulse;   // X/Y directional bounce toward gravity flip direction
    }
}
