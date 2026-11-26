using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace GravityDrift
{
    public class PlayerAuthoring : MonoBehaviour
    {
        [Header("Jetpack Physics")]
        public float moveSpeed = 10.0f;
        public float maxSpeed = 10.0f;
        public float thrustForce = 22.0f;
        public float jumpImpulse = 11.0f;
        public float boostMultiplier = 1.85f;
        public float linearDamping = 2.5f;
        public float rotationSpeed = 14.0f;
        public float groundCheckRayDistance = 0.6f;

        [Header("Fuel & Cooldown")]
        public float maxFuel = 100.0f;
        public float burnRate = 26.0f;
        public float boostBurnRate = 54.0f;
        public float rechargeRate = 38.0f;
        public float cooldownDuration = 1.4f;

        [Header("Thruster VFX Reference")]
        public ParticleSystem thrusterParticles;

        public class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new PlayerTag());
                AddComponent(entity, new PhysicsGravityFactor { Value = 0f }); // Gravity-neutral Player!
                AddComponent(entity, new PlayerMovement
                {
                    MoveSpeed = authoring.maxSpeed,
                    JumpImpulse = 0f,
                    AirControlFactor = 1.0f,
                    RotationSpeed = authoring.rotationSpeed,
                    IsGrounded = false,
                    GroundCheckRayDistance = 0.5f,
                    SpawnPosition = authoring.transform.position,
                    SpawnRotation = authoring.transform.rotation,
                    CurrentVelocity = float3.zero
                });

                AddComponent(entity, new JetpackComponent
                {
                    Fuel = authoring.maxFuel,
                    MaxFuel = authoring.maxFuel,
                    BurnRate = authoring.burnRate,
                    BoostBurnRate = authoring.boostBurnRate,
                    RechargeRate = authoring.rechargeRate,
                    ThrustForce = authoring.thrustForce,
                    BoostMultiplier = authoring.boostMultiplier,
                    MaxSpeed = authoring.maxSpeed,
                    CooldownTimer = 0f,
                    CooldownDuration = authoring.cooldownDuration,
                    IsInCooldown = false,
                    IsThrusting = false,
                    IsBoosting = false,
                    ThrustDirection = new float3(0, 1, 0)
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
            }
        }
    }
}
