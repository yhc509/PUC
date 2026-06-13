#nullable enable
#if ENABLE_INPUT_SYSTEM
using System;
using UnityCli.Protocol;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace UnityCliBridge.Bridge.Editor
{
    /// <summary>
    /// Simulates QA input through Unity Input System devices.
    /// </summary>
    internal sealed class QaInputSimulator
    {
        private const float PressedValue = 1f;
        private const float ReleasedValue = 0f;
        private const int DefaultFrameDurationMs = 16;

        private QaInputSimulator()
        {
        }

        /// <summary>
        /// Queues a key press and release in the current frame.
        /// </summary>
        public static void SimulateKey(string keyName)
        {
            Keyboard keyboard = RequireKeyboard();
            if (!Enum.TryParse(keyName, true, out Key key))
            {
                throw new CommandFailureException("QA_INVALID_KEY", $"Unknown key name: {keyName}. Use Input System Key enum values.", false, null);
            }

            QueueKeyboardState(keyboard, key, true);
            QueueKeyboardState(keyboard, key, false);
        }

        /// <summary>
        /// Creates a swipe operation that advances one screen-space input step per editor update.
        /// </summary>
        public static SwipeOperation BeginSwipe(Vector2 fromScreenPosition, Vector2 toScreenPosition, int durationMs)
        {
            return BeginSwipe(fromScreenPosition, toScreenPosition, durationMs, string.Empty);
        }

        public static SwipeOperation BeginSwipe(Vector2 fromScreenPosition, Vector2 toScreenPosition, int durationMs, string? button)
        {
            int normalizedDurationMs = durationMs > 0 ? durationMs : ProtocolConstants.DefaultQaSwipeDurationMs;
            int totalSteps = Mathf.Max(1, Mathf.CeilToInt(normalizedDurationMs / (float)DefaultFrameDurationMs));
            bool isRightButton = IsRightButton(button);

            Touchscreen? touchscreen = Touchscreen.current;
            if (!isRightButton && touchscreen != null)
            {
                return new SwipeOperation(fromScreenPosition, toScreenPosition, totalSteps, touchscreen);
            }

            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                return new SwipeOperation(fromScreenPosition, toScreenPosition, totalSteps, mouse, isRightButton);
            }

            throw new CommandFailureException("QA_NO_POINTER_DEVICE", "No pointer device found in Input System.", false, null);
        }

        /// <summary>
        /// Queues a pointer press and release at the given screen position in the current frame.
        /// Mirrors SimulateKey's same-frame press/release; reaches Input System polling that reads
        /// Mouse/Touchscreen state. Prefers Touchscreen, then Mouse.
        /// </summary>
        public static void SimulateTap(Vector2 screenPosition)
        {
            SimulateTap(screenPosition, string.Empty);
        }

        public static void SimulateTap(Vector2 screenPosition, string? button)
        {
            bool isRightButton = IsRightButton(button);
            Touchscreen? touchscreen = Touchscreen.current;
            if (!isRightButton && touchscreen != null)
            {
                QueueTouchState(touchscreen, screenPosition, UnityEngine.InputSystem.TouchPhase.Began);
                QueueTouchState(touchscreen, screenPosition, UnityEngine.InputSystem.TouchPhase.Ended);
                return;
            }

            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                QueueMouseState(mouse, screenPosition, true, isRightButton);
                QueueMouseState(mouse, screenPosition, false, isRightButton);
                return;
            }

            throw new CommandFailureException("QA_NO_POINTER_DEVICE", "No pointer device found in Input System.", false, null);
        }

        private static bool IsRightButton(string? button)
        {
            return string.Equals(button, "right", StringComparison.OrdinalIgnoreCase);
        }

        private static Keyboard RequireKeyboard()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                throw new CommandFailureException("QA_NO_KEYBOARD", "No keyboard device found in Input System.", false, null);
            }

            return keyboard;
        }

        private static void QueueKeyboardState(Keyboard keyboard, Key key, bool isPressed)
        {
            InputEventPtr eventPtr;
            using (StateEvent.From(keyboard, out eventPtr))
            {
                keyboard[key].WriteValueIntoEvent(isPressed ? PressedValue : ReleasedValue, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static void QueueMouseState(Mouse mouse, Vector2 screenPosition, bool isPressed, bool isRightButton)
        {
            InputEventPtr eventPtr;
            using (StateEvent.From(mouse, out eventPtr))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, eventPtr);
                if (isRightButton)
                {
                    mouse.rightButton.WriteValueIntoEvent(isPressed ? PressedValue : ReleasedValue, eventPtr);
                }
                else
                {
                    mouse.leftButton.WriteValueIntoEvent(isPressed ? PressedValue : ReleasedValue, eventPtr);
                }

                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static void QueueTouchState(Touchscreen touchscreen, Vector2 position, UnityEngine.InputSystem.TouchPhase phase)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState
            {
                touchId = 1,
                position = position,
                phase = phase,
            });
        }

        internal sealed class SwipeOperation
        {
            private readonly Vector2 _from;
            private readonly Vector2 _to;
            private readonly int _totalSteps;
            private readonly Touchscreen? _touchscreen;
            private readonly Mouse? _mouse;
            private readonly bool _isRightButton;
            private Vector2 _lastPosition;
            private int _currentStep;
            private bool _isAborted;

            public SwipeOperation(Vector2 from, Vector2 to, int totalSteps, Touchscreen touchscreen)
            {
                _from = from;
                _to = to;
                _totalSteps = totalSteps;
                _touchscreen = touchscreen;
                _lastPosition = from;
            }

            public SwipeOperation(Vector2 from, Vector2 to, int totalSteps, Mouse mouse, bool isRightButton)
            {
                _from = from;
                _to = to;
                _totalSteps = totalSteps;
                _mouse = mouse;
                _isRightButton = isRightButton;
                _lastPosition = from;
            }

            public bool Advance()
            {
                if (_isAborted)
                {
                    return true;
                }

                float t = _totalSteps > 0 ? (float)_currentStep / _totalSteps : 1f;
                Vector2 position = Vector2.Lerp(_from, _to, t);
                _lastPosition = position;

                if (_touchscreen != null)
                {
                    QueueTouchState(_touchscreen, position, GetTouchPhase());
                }
                else if (_mouse != null)
                {
                    QueueMouseState(_mouse, position, _currentStep < _totalSteps, _isRightButton);
                }
                else
                {
                    throw new InvalidOperationException("Swipe operation has no active pointer device.");
                }

                bool isCompleted = _currentStep >= _totalSteps;
                _currentStep++;
                return isCompleted;
            }

            public void Abort()
            {
                if (_isAborted)
                {
                    return;
                }

                _isAborted = true;

                if (_touchscreen != null)
                {
                    QueueTouchState(_touchscreen, _lastPosition, UnityEngine.InputSystem.TouchPhase.Canceled);
                    return;
                }

                if (_mouse != null)
                {
                    QueueMouseState(_mouse, _lastPosition, false, _isRightButton);
                }
            }

            private UnityEngine.InputSystem.TouchPhase GetTouchPhase()
            {
                if (_currentStep == 0)
                {
                    return UnityEngine.InputSystem.TouchPhase.Began;
                }

                if (_currentStep >= _totalSteps)
                {
                    return UnityEngine.InputSystem.TouchPhase.Ended;
                }

                return UnityEngine.InputSystem.TouchPhase.Moved;
            }
        }
    }
}
#endif
