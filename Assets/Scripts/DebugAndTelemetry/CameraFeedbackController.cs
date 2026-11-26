using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace GravityDrift
{
    /// <summary>
    /// Drives smooth 2D X/Y screen shake (decaying random offset) and
    /// snappy spring-damped directional bounce bumps on every 4-way gravity shift.
    /// </summary>
    public class CameraFeedbackController : MonoBehaviour
    {
        public static CameraFeedbackController Instance { get; private set; }

        [Header("Screen Shake Settings")]
        public float maxShakeAmplitude = 0.60f;
        public float shakeDecayPerSecond = 2.35f;

        [Header("Gravity Shift Bounce Bump")]
        public float bounceImpulseStrength = 12.5f;
        public float springStiffness = 145f;
        public float springDamping = 16.5f;

        private Camera targetCamera;
        private Vector3 baseCameraPos = new Vector3(0f, 0f, -22f);
        private float currentShake = 0f;
        public float CurrentShakeIntensity => currentShake;
        private Vector2 bounceOffset = Vector2.zero;
        private Vector2 bounceVelocity = Vector2.zero;
        private float noiseSeed = 17.3f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(this);

            targetCamera = GetComponent<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera != null) baseCameraPos = targetCamera.transform.position;
        }

        public void SetBasePosition(Vector3 basePos)
        {
            baseCameraPos = basePos;
            if (targetCamera == null) targetCamera = Camera.main;
        }

        public void TriggerShake(float intensity)
        {
            currentShake = Mathf.Clamp(Mathf.Max(currentShake, intensity), 0f, 1.5f);
        }

        public void TriggerGravityBounce(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.001f)
            {
                bounceVelocity += direction.normalized * bounceImpulseStrength;
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            // Pause camera motion cleanly when menu is open
            if (Time.timeScale <= 0.001f)
            {
                targetCamera.transform.position = baseCameraPos;
                return;
            }

            // Pull ECS CameraFeedbackState events
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var em = world.EntityManager;
                var fbQuery = em.CreateEntityQuery(typeof(CameraFeedbackState));
                if (fbQuery.CalculateEntityCount() > 0)
                {
                    using var entities = fbQuery.ToEntityArray(Allocator.Temp);
                    var fb = em.GetComponentData<CameraFeedbackState>(entities[0]);
                    if (fb.ShakeIntensity > 0.001f)
                    {
                        TriggerShake(fb.ShakeIntensity);
                        fb.ShakeIntensity = 0f;
                    }
                    if (math.lengthsq(fb.PendingBounceImpulse) > 0.001f)
                    {
                        TriggerGravityBounce(new Vector2(fb.PendingBounceImpulse.x, fb.PendingBounceImpulse.y));
                        fb.PendingBounceImpulse = float2.zero;
                    }
                    em.SetComponentData(entities[0], fb);
                }
                fbQuery.Dispose();
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            // 1. 2D X/Y Noise Screen Shake with smooth quadratic decay
            Vector2 shakeOffset = Vector2.zero;
            if (currentShake > 0.001f)
            {
                noiseSeed += dt * 42f;
                float sq = currentShake * currentShake;
                float nx = (Mathf.PerlinNoise(noiseSeed, 1.1f) * 2f - 1f) + UnityEngine.Random.Range(-0.35f, 0.35f);
                float ny = (Mathf.PerlinNoise(2.7f, noiseSeed) * 2f - 1f) + UnityEngine.Random.Range(-0.35f, 0.35f);
                shakeOffset = new Vector2(nx, ny) * (maxShakeAmplitude * sq);

                currentShake = Mathf.Max(0f, currentShake - shakeDecayPerSecond * dt);
            }

            // 2. Spring-Damper Gravity-Shift Directional Bounce Bump
            Vector2 springForce = -springStiffness * bounceOffset - springDamping * bounceVelocity;
            bounceVelocity += springForce * dt;
            bounceOffset += bounceVelocity * dt;
            if (bounceOffset.sqrMagnitude < 0.00001f && bounceVelocity.sqrMagnitude < 0.0001f)
            {
                bounceOffset = Vector2.zero;
                bounceVelocity = Vector2.zero;
            }

            targetCamera.transform.position = baseCameraPos + new Vector3(shakeOffset.x + bounceOffset.x, shakeOffset.y + bounceOffset.y, 0f);
        }
    }
}
