using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using GravityDrift.DualSenseInput;

namespace GravityDrift
{
    public enum ControlScheme
    {
        PC_KeyboardMouse = 0,
        PlayStation_DualSense = 1
    }

    public static class InputHelper
    {
        public static ControlScheme activeScheme = ControlScheme.PC_KeyboardMouse;
        public static float stickDeadzone = 0.16f;
        public static bool enableRightStickGravity = true;
        public static string lastGravityInputSource = "PC_ArrowsOrKeys";
        public static string lastThrustInputSource = "None";
        public static string lastBoostInputSource = "None";
        public static string lastActiveInputSource = "None";
        public static bool IsInputFrozen = false;

        private static float lastFlickTime = -10f;
        private static int inputBlockedUntilFrame = -1;
        private const float FlickCooldown = 0.25f;

        public static void FlushBufferedGravityInputs()
        {
            lastFlickTime = Time.unscaledTime;
            inputBlockedUntilFrame = Time.frameCount;
            DualSenseTouchpadReader.ClearPendingSwipe();
        }

        public static Gamepad GetActiveGamepad()
        {
            if (DualSenseGamepadHID.current != null) return DualSenseGamepadHID.current;
            if (DualShockGamepad.current != null) return DualShockGamepad.current;
            return Gamepad.current;
        }

        public static bool IsDualSenseConnected()
        {
            return DualSenseGamepadHID.current != null || DualShockGamepad.current != null;
        }

        public static Vector2 GetJetpackThrustVector()
        {
            if (IsInputFrozen) return Vector2.zero;

            Vector2 thrust = Vector2.zero;
            bool kbPressed = false;
            bool padPressed = false;

            // 1. PC Keyboard WASD
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[Key.A].isPressed) { thrust.x -= 1f; kbPressed = true; }
                if (kb[Key.D].isPressed) { thrust.x += 1f; kbPressed = true; }
                if (kb[Key.W].isPressed) { thrust.y += 1f; kbPressed = true; }
                if (kb[Key.S].isPressed) { thrust.y -= 1f; kbPressed = true; }
            }

            // 2. DualSense / Gamepad Left Stick
            var pad = GetActiveGamepad();
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.magnitude > stickDeadzone)
                {
                    thrust += stick;
                    padPressed = true;
                }
            }

            if (padPressed)
            {
                lastThrustInputSource = "DualSense_Stick";
                lastActiveInputSource = "DualSense_Stick";
            }
            else if (kbPressed)
            {
                lastThrustInputSource = "PC_WASD";
                lastActiveInputSource = "PC_WASD";
            }

            if (thrust.sqrMagnitude > 1f)
            {
                thrust.Normalize();
            }

            return thrust;
        }

        public static bool GetJetpackBoost()
        {
            if (IsInputFrozen) return false;

            var kb = Keyboard.current;
            if (kb != null && (kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed || kb[Key.Space].isPressed))
            {
                lastBoostInputSource = "PC_Boost";
                lastActiveInputSource = "PC_Boost";
                return true;
            }

            var pad = GetActiveGamepad();
            if (pad != null)
            {
                if (pad.rightTrigger.ReadValue() > 0.15f || pad.rightShoulder.isPressed)
                {
                    lastBoostInputSource = "DualSense_R2";
                    lastActiveInputSource = "DualSense_R2";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 4-Way Gravity Direction:
        /// - PC: Arrow Keys or 1-4
        /// - PlayStation DualSense: Touchpad Swipe (primary) with 0.25s anti-jitter cooldown,
        ///   plus D-Pad, Face Buttons (Cross/Circle/Triangle/Square), and Right Stick Flick fallbacks!
        /// </summary>
        public static GravityDirection? GetGravityFlipInput()
        {
            if (IsInputFrozen || Time.frameCount <= inputBlockedUntilFrame)
            {
                DualSenseTouchpadReader.ClearPendingSwipe();
                return null;
            }

            // 1. PC Keyboard Testing Controls
            if (GetKeyDown(Key.DownArrow) || GetKeyDown(Key.Digit1) || GetKeyDown(Key.Numpad1))
            {
                lastGravityInputSource = "PC_ArrowsOrKeys";
                lastActiveInputSource = "PC_ArrowsOrKeys";
                return GravityDirection.Down;
            }
            if (GetKeyDown(Key.RightArrow) || GetKeyDown(Key.Digit2) || GetKeyDown(Key.Numpad2))
            {
                lastGravityInputSource = "PC_ArrowsOrKeys";
                lastActiveInputSource = "PC_ArrowsOrKeys";
                return GravityDirection.Right;
            }
            if (GetKeyDown(Key.UpArrow) || GetKeyDown(Key.Digit3) || GetKeyDown(Key.Numpad3))
            {
                lastGravityInputSource = "PC_ArrowsOrKeys";
                lastActiveInputSource = "PC_ArrowsOrKeys";
                return GravityDirection.Up;
            }
            if (GetKeyDown(Key.LeftArrow) || GetKeyDown(Key.Digit4) || GetKeyDown(Key.Numpad4))
            {
                lastGravityInputSource = "PC_ArrowsOrKeys";
                lastActiveInputSource = "PC_ArrowsOrKeys";
                return GravityDirection.Left;
            }

            // 2. DualSense Touchpad Swipe (USB/BT HID + Child Controls + Pointer Fallback)
            var dsPad = DualSenseGamepadHID.current ?? DualShockGamepad.current;
            var swipe = DualSenseTouchpadReader.ConsumeSwipe(dsPad);
            if (swipe != TouchpadSwipeDir.None)
            {
                lastGravityInputSource = "DualSense_Touchpad";
                lastActiveInputSource = "DualSense_Touchpad";
                return swipe switch
                {
                    TouchpadSwipeDir.Down => GravityDirection.Down,
                    TouchpadSwipeDir.Up => GravityDirection.Up,
                    TouchpadSwipeDir.Left => GravityDirection.Left,
                    TouchpadSwipeDir.Right => GravityDirection.Right,
                    _ => null
                };
            }

            // 3. Gamepad D-Pad, Face Buttons & Right-Stick Flick Fallbacks
            var pad = GetActiveGamepad();
            if (pad != null)
            {
                if (pad.dpad.down.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame)
                {
                    lastGravityInputSource = "DualSense_DPad";
                    lastActiveInputSource = "DualSense_DPad";
                    return GravityDirection.Down;
                }
                if (pad.dpad.right.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame)
                {
                    lastGravityInputSource = "DualSense_DPad";
                    lastActiveInputSource = "DualSense_DPad";
                    return GravityDirection.Right;
                }
                if (pad.dpad.up.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame)
                {
                    lastGravityInputSource = "DualSense_DPad";
                    lastActiveInputSource = "DualSense_DPad";
                    return GravityDirection.Up;
                }
                if (pad.dpad.left.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame)
                {
                    lastGravityInputSource = "DualSense_DPad";
                    lastActiveInputSource = "DualSense_DPad";
                    return GravityDirection.Left;
                }

                if (enableRightStickGravity && (Time.unscaledTime - lastFlickTime) >= FlickCooldown)
                {
                    Vector2 rs = pad.rightStick.ReadValue();
                    if (rs.magnitude > 0.68f)
                    {
                        lastFlickTime = Time.unscaledTime;
                        lastGravityInputSource = "DualSense_RStick";
                        lastActiveInputSource = "DualSense_RStick";
                        if (Mathf.Abs(rs.x) > Mathf.Abs(rs.y))
                            return (rs.x > 0f) ? GravityDirection.Right : GravityDirection.Left;
                        else
                            return (rs.y > 0f) ? GravityDirection.Up : GravityDirection.Down;
                    }
                }
            }

            return null;
        }

        public static bool GetReportToggleDown()
        {
            return GetKeyDown(Key.G);
        }

        public static bool GetPauseDown()
        {
            if (GetKeyDown(Key.Escape)) return true;

            var pad = GetActiveGamepad();
            if (pad != null && pad.startButton.wasPressedThisFrame)
            {
                return true;
            }

            return false;
        }

        public static bool GetDebugToggleDown()
        {
            if (GetKeyDown(Key.Tab)) return true;

            var pad = GetActiveGamepad();
            if (pad != null && pad.selectButton.wasPressedThisFrame)
            {
                return true;
            }

            return false;
        }

        public static bool GetKey(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        public static bool GetKeyDown(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }
    }
}
