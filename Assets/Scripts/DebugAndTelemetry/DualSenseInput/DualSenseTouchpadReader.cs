using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;

namespace GravityDrift.DualSenseInput
{
    public enum TouchpadSwipeDir : byte
    {
        None = 0,
        Down = 1,
        Up = 2,
        Left = 3,
        Right = 4
    }

    /// <summary>
    /// Reads DualSense Touchpad finger-0 swipes cleanly on USB (64B) / Bluetooth (78B) HID reports,
    /// PlayStation / custom touchpad controls, or Touchpad/Touchscreen pointer deltas, emitting
    /// crisp 4-way swipes with swipe distance deadzone & 0.25s anti-jitter cooldown.
    /// </summary>
    public static class DualSenseTouchpadReader
    {
        public static float SwipeThresholdNormalized = 0.16f;
        public static float AntiJitterCooldown = 0.25f;

        private static bool initialized = false;
        private static bool isTouching = false;
        private static Vector2 touchStartNorm = Vector2.zero;
        private static Vector2 currentTouchNorm = Vector2.zero;
        private static float lastSwipeTime = -10f;
        private static TouchpadSwipeDir pendingSwipe = TouchpadSwipeDir.None;

        private static Vector2 pointerSwipeAccum = Vector2.zero;
        private static bool pointerWasDown = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            isTouching = false;
            pendingSwipe = TouchpadSwipeDir.None;
            lastSwipeTime = -10f;
            pointerSwipeAccum = Vector2.zero;
            pointerWasDown = false;
        }

        public static void EnsureInitialized()
        {
            if (initialized) return;
            InputSystem.onEvent += OnInputEvent;
            initialized = true;
        }

        public static void Shutdown()
        {
            if (!initialized) return;
            InputSystem.onEvent -= OnInputEvent;
            initialized = false;
        }

        public static void ClearPendingSwipe()
        {
            pendingSwipe = TouchpadSwipeDir.None;
            isTouching = false;
            pointerWasDown = false;
            pointerSwipeAccum = Vector2.zero;
            lastSwipeTime = Time.unscaledTime;
        }

        private static unsafe void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!eventPtr.valid || device == null) return;
            if (!(device is DualSenseGamepadHID)) return;
            if (!eventPtr.IsA<StateEvent>()) return;

            var stateEvent = StateEvent.From(eventPtr);
            uint size = stateEvent->stateSizeInBytes;
            if (size < 40) return; // Minimal 10B reports carry no touchpad payload

            byte* payload = (byte*)stateEvent->state;

            // USB full report: 64 bytes -> touch0 starts at offset 33 (data shifted after PreProcessEvent size unchanged)
            // Bluetooth full report: 78 bytes -> touch0 starts at offset 34
            int touchOffset = (size >= 78) ? 34 : 33;
            if (touchOffset + 4 >= size) return;

            byte touchCounterAndActive = payload[touchOffset];
            bool active = (touchCounterAndActive & 0x80) == 0;

            int rawX = ((payload[touchOffset + 2] & 0x0F) << 8) | payload[touchOffset + 1];
            int rawY = (payload[touchOffset + 3] << 4) | ((payload[touchOffset + 2] & 0xF0) >> 4);

            // Guard against zero-filled Bluetooth minimal packets pretending to touch at (0,0)
            if (active && rawX == 0 && rawY == 0)
            {
                active = false;
            }

            if (active)
            {
                Vector2 norm = new Vector2(Mathf.Clamp01(rawX / 1919f), Mathf.Clamp01(rawY / 1079f));
                ProcessTouchSample(norm);
            }
            else
            {
                isTouching = false;
            }
        }

        private static void ProcessTouchSample(Vector2 norm)
        {
            currentTouchNorm = norm;
            if (!isTouching)
            {
                isTouching = true;
                touchStartNorm = norm;
                return;
            }

            Vector2 delta = currentTouchNorm - touchStartNorm;
            if (Mathf.Abs(delta.x) >= SwipeThresholdNormalized || Mathf.Abs(delta.y) >= SwipeThresholdNormalized)
            {
                float now = Time.unscaledTime;
                if ((now - lastSwipeTime) >= AntiJitterCooldown)
                {
                    if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                    {
                        pendingSwipe = (delta.x > 0f) ? TouchpadSwipeDir.Right : TouchpadSwipeDir.Left;
                    }
                    else
                    {
                        // DualSense touchpad Y=0 is top, Y=1 is bottom
                        pendingSwipe = (delta.y < 0f) ? TouchpadSwipeDir.Up : TouchpadSwipeDir.Down;
                    }
                    lastSwipeTime = now;
                    touchStartNorm = currentTouchNorm; // Re-anchor so long drags don't double-fire without fresh swipe
                }
            }
        }

        /// <summary>
        /// Polls and consumes any pending DualSense touchpad swipe (from raw HID, child Vector2 controls,
        /// Touchscreen, or touchpadButton + Pointer drag fallback under Steam Input / OS HID wrappers).
        /// </summary>
        public static TouchpadSwipeDir ConsumeSwipe(DualShockGamepad dsPad)
        {
            EnsureInitialized();

            // 1. Check raw HID touchpad capture
            if (pendingSwipe != TouchpadSwipeDir.None)
            {
                var res = pendingSwipe;
                pendingSwipe = TouchpadSwipeDir.None;
                return res;
            }

            float now = Time.unscaledTime;
            if ((now - lastSwipeTime) < AntiJitterCooldown)
            {
                return TouchpadSwipeDir.None;
            }

            // 2. Check PlayStation SDK / custom HID child touchpad Vector2Control if present
            if (dsPad != null)
            {
                var tpVec = dsPad.TryGetChildControl<Vector2Control>("touchpad")
                         ?? dsPad.TryGetChildControl<Vector2Control>("touchpadPrimary")
                         ?? dsPad.TryGetChildControl<Vector2Control>("touch0/position");
                if (tpVec != null)
                {
                    Vector2 val = tpVec.ReadValue();
                    if (val.sqrMagnitude > 0.001f)
                    {
                        ProcessTouchSample(val);
                        if (pendingSwipe != TouchpadSwipeDir.None)
                        {
                            var res = pendingSwipe;
                            pendingSwipe = TouchpadSwipeDir.None;
                            return res;
                        }
                    }
                    else
                    {
                        isTouching = false;
                    }
                }

                // 3. Fallback if Touchpad Click + Pointer / Mouse swipe is active (e.g. Steam Input / DS4Windows wrapper)
                if (dsPad.touchpadButton != null && dsPad.touchpadButton.isPressed && Pointer.current != null)
                {
                    Vector2 delta = Pointer.current.delta.ReadValue();
                    if (!pointerWasDown)
                    {
                        pointerWasDown = true;
                        pointerSwipeAccum = Vector2.zero;
                    }
                    pointerSwipeAccum += delta;

                    const float pixelThreshold = 42f;
                    if (Mathf.Abs(pointerSwipeAccum.x) >= pixelThreshold || Mathf.Abs(pointerSwipeAccum.y) >= pixelThreshold)
                    {
                        Vector2 swipe = pointerSwipeAccum;
                        lastSwipeTime = now;
                        pointerSwipeAccum = Vector2.zero;
                        if (Mathf.Abs(swipe.x) > Mathf.Abs(swipe.y))
                        {
                            return (swipe.x > 0f) ? TouchpadSwipeDir.Right : TouchpadSwipeDir.Left;
                        }
                        else
                        {
                            return (swipe.y > 0f) ? TouchpadSwipeDir.Up : TouchpadSwipeDir.Down;
                        }
                    }
                }
                else
                {
                    pointerWasDown = false;
                    pointerSwipeAccum = Vector2.zero;
                }
            }

            return TouchpadSwipeDir.None;
        }
    }
}
