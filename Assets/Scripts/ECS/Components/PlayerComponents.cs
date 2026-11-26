using Unity.Entities;
using Unity.Mathematics;

namespace GravityDrift
{
    public struct PlayerTag : IComponentData
    {
    }

    public struct PlayerMovement : IComponentData
    {
        public float MoveSpeed;
        public float JumpImpulse;
        public float AirControlFactor;
        public float RotationSpeed;
        public bool IsGrounded;
        public float GroundCheckRayDistance;
        public float3 SpawnPosition;
        public quaternion SpawnRotation;
        public float3 CurrentVelocity;
    }

    public struct JetpackComponent : IComponentData
    {
        public float Fuel;             // 0.0 to 100.0
        public float MaxFuel;          // 100.0
        public float BurnRate;         // Fuel consumed per second during normal thrust
        public float BoostBurnRate;    // Fuel consumed per second during boost
        public float RechargeRate;     // Fuel regenerated per second when idle
        public float ThrustForce;      // Base acceleration
        public float BoostMultiplier;  // Multiplier during boost (e.g. 1.85)
        public float MaxSpeed;         // Maximum speed cap
        public float CooldownTimer;    // Cooldown countdown when fuel is depleted
        public float CooldownDuration; // Duration of recharge lock (e.g. 1.5s)
        public bool IsInCooldown;      // True when overheated / empty
        public bool IsThrusting;       // True when thrust vector > 0.05
        public bool IsBoosting;        // True when boost key / trigger held
        public float3 ThrustDirection; // Direction vector of current thrust
        public int OverloadCount;      // Cumulative fuel overloads triggered
        public float TotalBoostTime;   // Cumulative boost duration (sec)
    }
}
