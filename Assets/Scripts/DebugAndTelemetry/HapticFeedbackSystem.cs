using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace GravityDrift
{
    /// <summary>
    /// Unified DualSense & Gamepad Haptic Rumble + LightBar Controller Bridge.
    /// Synchronizes low/high motor speeds and LED color with:
    /// - Jetpack Thrust (0.12 / 0.25) & Boost (0.30 / 0.55)
    /// - Jetpack Overload Cooldown (0.85 / 0.75 rhythmic pulse locked to player red blink cycle)
    /// - 2D Camera Shake Trauma (0.5 crate hit drone, 1.0 player damage/death)
    /// Automatically zeros all motors on Pause, Menu, Disable, and Quit.
    /// </summary>
    public class HapticFeedbackSystem : MonoBehaviour
    {
        public static HapticFeedbackSystem Instance { get; private set; }

        public static bool HapticsEnabled = true;
        public static float HapticsIntensity = 0.025f;

        private float lastSentLow = -1f;
        private float lastSentHigh = -1f;
        private Color lastSentLight = Color.black;
        private float lastSendTime = -10f;
        private const float SendIntervalSec = 0.032f; // ~31 Hz safe HID IOCTL rate

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(this);
        }

        public static void StopAllHaptics()
        {
            var ds = InputHelper.GetActiveGamepad() as DualSenseGamepadHID;
            if (ds != null)
            {
                ds.SetMotorSpeedsAndLightBarColor(0f, 0f, new Color(0.1f, 0.85f, 1.0f));
            }
            else
            {
                var pad = InputHelper.GetActiveGamepad();
                if (pad != null)
                {
                    pad.SetMotorSpeeds(0f, 0f);
                }
            }

            if (Instance != null)
            {
                Instance.lastSentLow = 0f;
                Instance.lastSentHigh = 0f;
            }
        }

        private void OnDisable()
        {
            StopAllHaptics();
        }

        private void OnApplicationQuit()
        {
            StopAllHaptics();
        }

        private void LateUpdate()
        {
            var pad = InputHelper.GetActiveGamepad();
            if (pad == null) return;

            // Stop all vibration immediately when paused, in menus, or disabled
            if (!HapticsEnabled || Time.timeScale <= 0.001f ||
                (GameUIManager.Instance != null && GameUIManager.Instance.currentState != UIState.Gameplay))
            {
                if (lastSentLow > 0.001f || lastSentHigh > 0.001f)
                {
                    StopAllHaptics();
                }
                return;
            }

            float targetLow = 0f;
            float targetHigh = 0f;
            Color targetLight = new Color(0.1f, 0.85f, 1.0f); // Default Cyan

            // 1. Read ECS Jetpack state (Thrust, Boost, Overload Red-Blink Lockstep)
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var em = world.EntityManager;
                var jetQuery = em.CreateEntityQuery(typeof(PlayerTag), typeof(JetpackComponent));
                if (jetQuery.CalculateEntityCount() > 0)
                {
                    using var jets = jetQuery.ToComponentDataArray<JetpackComponent>(Allocator.Temp);
                    var jet = jets[0];

                    if (jet.IsInCooldown)
                    {
                        // Lockstep with GravityDriftBootstrap red blinking cycle
                        bool blinkHigh = Mathf.PingPong(Time.unscaledTime * 11f, 1f) > 0.42f;
                        targetLow = blinkHigh ? 0.85f : 0.08f;
                        targetHigh = blinkHigh ? 0.75f : 0.06f;
                        targetLight = blinkHigh ? Color.red : new Color(0.25f, 0.02f, 0.02f);
                    }
                    else if (jet.IsBoosting)
                    {
                        targetLow = 0.30f;
                        targetHigh = 0.55f;
                        targetLight = new Color(1.0f, 0.55f, 0.1f);
                    }
                    else if (jet.IsThrusting)
                    {
                        targetLow = 0.12f;
                        targetHigh = 0.25f;
                        targetLight = new Color(0.15f, 0.95f, 1.0f);
                    }
                }
                jetQuery.Dispose();
            }

            // 2. Sync with Camera Shake Trauma (0.5 crate hit -> medium-heavy burst; 1.0 damage -> max rumble)
            float shake = (CameraFeedbackController.Instance != null) ? CameraFeedbackController.Instance.CurrentShakeIntensity : 0f;
            if (shake > 0.02f)
            {
                float normShake = Mathf.Clamp01(shake);
                float shakeLow = Mathf.Lerp(0.35f, 1.0f, normShake);
                float shakeHigh = Mathf.Lerp(0.45f, 1.0f, normShake);
                if (shake < 0.25f)
                {
                    shakeLow *= (shake / 0.25f);
                    shakeHigh *= (shake / 0.25f);
                }

                targetLow = Mathf.Max(targetLow, shakeLow);
                targetHigh = Mathf.Max(targetHigh, shakeHigh);

                if (shake > 0.45f)
                {
                    targetLight = new Color(1.0f, 0.22f, 0.05f);
                }
            }

            // Apply Intensity Multiplier
            float scale = Mathf.Clamp(HapticsIntensity, 0f, 1.5f);
            float finalLow = Mathf.Clamp01(targetLow * scale);
            float finalHigh = Mathf.Clamp01(targetHigh * scale);

            // Push to hardware immediately on zero-stop, or at smooth rate when active/changing
            bool wantsZero = (finalLow < 0.005f && finalHigh < 0.005f);
            bool isZeroAlready = (lastSentLow < 0.005f && lastSentHigh < 0.005f);
            if (wantsZero && !isZeroAlready)
            {
                SendToHardware(pad, 0f, 0f, targetLight);
                return;
            }

            bool changed = Mathf.Abs(finalLow - lastSentLow) > 0.04f ||
                           Mathf.Abs(finalHigh - lastSentHigh) > 0.04f ||
                           targetLight != lastSentLight;

            if (changed && (Time.unscaledTime - lastSendTime) >= SendIntervalSec)
            {
                SendToHardware(pad, finalLow, finalHigh, targetLight);
            }
        }

        private void SendToHardware(Gamepad pad, float low, float high, Color lightColor)
        {
            if (pad is DualSenseGamepadHID ds)
            {
                // DualSense requires atomic motor + lightbar HID report so setting color does not cancel rumble
                ds.SetMotorSpeedsAndLightBarColor(low, high, lightColor);
            }
            else
            {
                pad.SetMotorSpeeds(low, high);
                if (pad is DualShockGamepad d4)
                {
                    d4.SetLightBarColor(lightColor);
                }
            }

            lastSentLow = low;
            lastSentHigh = high;
            lastSentLight = lightColor;
            lastSendTime = Time.unscaledTime;
        }
    }
}
