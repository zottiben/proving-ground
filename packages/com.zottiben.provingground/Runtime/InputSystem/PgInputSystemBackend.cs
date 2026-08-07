#if PG_INPUTSYSTEM
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Maps a logical action name to the physical controls that usually carry it.
    ///
    /// Both a key and a gamepad button are driven for every action, because the game
    /// under test may be reading either and the harness has no way to know which. A
    /// project with unusual bindings overrides this rather than rewriting the probe.
    /// </summary>
    public sealed class PgInputBindings
    {
        public readonly Dictionary<string, Key> Keys = new Dictionary<string, Key>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["jump"] = Key.Space,
            ["sprint"] = Key.LeftShift,
            ["crouch"] = Key.LeftCtrl,
            ["interact"] = Key.E,
            ["reload"] = Key.R,
            ["melee"] = Key.V,
            ["cancel"] = Key.Escape,
            ["submit"] = Key.Enter,
            ["inventory"] = Key.Tab,
            ["map"] = Key.M
        };

        public readonly Dictionary<string, GamepadButton> Buttons =
            new Dictionary<string, GamepadButton>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["jump"] = GamepadButton.South,
                ["sprint"] = GamepadButton.LeftStick,
                ["crouch"] = GamepadButton.East,
                ["interact"] = GamepadButton.West,
                ["reload"] = GamepadButton.North,
                ["fire"] = GamepadButton.RightTrigger,
                ["aim"] = GamepadButton.LeftTrigger,
                ["cancel"] = GamepadButton.East,
                ["submit"] = GamepadButton.South
            };

        public readonly Dictionary<string, MouseButton> MouseButtons =
            new Dictionary<string, MouseButton>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["fire"] = MouseButton.Left,
                ["aim"] = MouseButton.Right,
                ["attack"] = MouseButton.Left
            };

        /// <summary>Keys driven by the move stick, in W/A/S/D order.</summary>
        public Key[] MoveKeys = { Key.W, Key.A, Key.S, Key.D };
    }

    /// <summary>
    /// Drives the Input System's own device backend. Synthetic events queued here are
    /// indistinguishable to the game from events produced by real hardware, which is what
    /// makes a probe run evidence about the shipping controller rather than about a mock.
    /// </summary>
    public sealed class PgInputSystemBackend : IPgInputBackend
    {
        public string Name => "InputSystem";
        public bool IsAvailable => true;

        public PgInputBindings Bindings { get; set; } = new PgInputBindings();

        /// <summary>Pixels of mouse delta per unit of look stick per second.</summary>
        public float LookSensitivity { get; set; } = 400f;

        Gamepad _gamepad;
        Keyboard _keyboard;
        Mouse _mouse;

        readonly HashSet<Key> _heldKeys = new HashSet<Key>();
        readonly HashSet<GamepadButton> _heldButtons = new HashSet<GamepadButton>();
        readonly HashSet<MouseButton> _heldMouseButtons = new HashSet<MouseButton>();

        Vector2 _moveStick;
        Vector2 _lookStick;
        Vector2 _mousePosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 _pendingMouseDelta;
        Vector2 _pendingScroll;

        bool _ownsGamepad;
        bool _ownsKeyboard;
        bool _ownsMouse;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PgInput.Backend ??= new PgInputSystemBackend();
        }

        public void Begin()
        {
            // Reuse a device the project already has so its bindings and control schemes
            // keep resolving; only add one when nothing suitable exists.
            if (Gamepad.current == null)
            {
                _gamepad = InputSystem.AddDevice<Gamepad>("ProvingGroundGamepad");
                _ownsGamepad = true;
            }
            else _gamepad = Gamepad.current;

            if (Keyboard.current == null)
            {
                _keyboard = InputSystem.AddDevice<Keyboard>("ProvingGroundKeyboard");
                _ownsKeyboard = true;
            }
            else _keyboard = Keyboard.current;

            if (Mouse.current == null)
            {
                _mouse = InputSystem.AddDevice<Mouse>("ProvingGroundMouse");
                _ownsMouse = true;
            }
            else _mouse = Mouse.current;

            _heldKeys.Clear();
            _heldButtons.Clear();
            _heldMouseButtons.Clear();
            _moveStick = Vector2.zero;
            _lookStick = Vector2.zero;
            _pendingMouseDelta = Vector2.zero;
            _pendingScroll = Vector2.zero;
        }

        public void End()
        {
            _moveStick = Vector2.zero;
            _lookStick = Vector2.zero;
            _heldKeys.Clear();
            _heldButtons.Clear();
            _heldMouseButtons.Clear();
            Flush();

            if (_ownsGamepad && _gamepad != null) InputSystem.RemoveDevice(_gamepad);
            if (_ownsKeyboard && _keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_ownsMouse && _mouse != null) InputSystem.RemoveDevice(_mouse);

            _gamepad = null;
            _keyboard = null;
            _mouse = null;
            _ownsGamepad = _ownsKeyboard = _ownsMouse = false;
        }

        public void SetStick(string stick, Vector2 value)
        {
            if (stick == PgInput.StickMove) _moveStick = value;
            else if (stick == PgInput.StickLook) _lookStick = value;
        }

        public void SetButton(string button, bool pressed)
        {
            if (Bindings.Keys.TryGetValue(button, out var key))
            {
                if (pressed) _heldKeys.Add(key);
                else _heldKeys.Remove(key);
            }

            if (Bindings.Buttons.TryGetValue(button, out var pad))
            {
                if (pressed) _heldButtons.Add(pad);
                else _heldButtons.Remove(pad);
            }

            if (Bindings.MouseButtons.TryGetValue(button, out var mouseButton))
            {
                if (pressed) _heldMouseButtons.Add(mouseButton);
                else _heldMouseButtons.Remove(mouseButton);
            }
        }

        public void MoveMouse(Vector2 delta)
        {
            _pendingMouseDelta += delta;
            _mousePosition += new Vector2(delta.x, delta.y);
        }

        public void SetMousePosition(Vector2 position)
        {
            _pendingMouseDelta += position - _mousePosition;
            _mousePosition = position;
        }

        public void Scroll(Vector2 delta) => _pendingScroll += delta;

        public void Flush()
        {
            // The look stick is also expressed as mouse delta so that mouse-look
            // controllers respond to a probe written against sticks.
            if (_lookStick.sqrMagnitude > 0.0001f)
                _pendingMouseDelta += _lookStick * (LookSensitivity * Time.deltaTime);

            if (_gamepad != null)
            {
                var state = new GamepadState
                {
                    leftStick = _moveStick,
                    rightStick = _lookStick
                };

                foreach (var button in _heldButtons)
                {
                    // Triggers are axes; the button enum sets them to fully pressed.
                    if (button == GamepadButton.LeftTrigger) state.leftTrigger = 1f;
                    else if (button == GamepadButton.RightTrigger) state.rightTrigger = 1f;
                    state = state.WithButton(button);
                }

                InputSystem.QueueStateEvent(_gamepad, state);
            }

            if (_keyboard != null)
            {
                var keys = _heldKeys.ToList();
                keys.AddRange(MoveKeysFor(_moveStick));
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys.Distinct().ToArray()));
            }

            if (_mouse != null)
            {
                var state = new MouseState
                {
                    position = _mousePosition,
                    delta = _pendingMouseDelta,
                    scroll = _pendingScroll
                };

                foreach (var button in _heldMouseButtons)
                    state = state.WithButton(button);

                InputSystem.QueueStateEvent(_mouse, state);
            }

            _pendingMouseDelta = Vector2.zero;
            _pendingScroll = Vector2.zero;
        }

        IEnumerable<Key> MoveKeysFor(Vector2 move)
        {
            const float threshold = 0.3f;
            if (move.y > threshold) yield return Bindings.MoveKeys[0];
            if (move.x < -threshold) yield return Bindings.MoveKeys[1];
            if (move.y < -threshold) yield return Bindings.MoveKeys[2];
            if (move.x > threshold) yield return Bindings.MoveKeys[3];
        }
    }
}
#endif
