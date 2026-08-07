using System;
using System.Collections.Generic;
using UnityEngine;
using ProvingGround.Perception;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// A backend that can push synthetic input into whatever the game actually reads.
    /// Implementations exist for the Input System; a project on the legacy Input Manager
    /// or a custom input stack can supply its own and everything above this line keeps
    /// working unchanged.
    /// </summary>
    public interface IPgInputBackend
    {
        string Name { get; }
        bool IsAvailable { get; }

        void Begin();
        void End();

        void SetStick(string stick, Vector2 value);
        void SetButton(string button, bool pressed);
        void MoveMouse(Vector2 delta);
        void SetMousePosition(Vector2 position);
        void Scroll(Vector2 delta);

        /// <summary>Pushes queued state so the game sees it on the next update.</summary>
        void Flush();
    }

    /// <summary>
    /// The one place synthetic input enters the game.
    ///
    /// Input generated here is delivered through the same path as input from a real
    /// device, so it drives the game's actual controller rather than a test double. That
    /// is the entire point: a mock proves the mock works.
    /// </summary>
    public static class PgInput
    {
        public const string StickMove = "move";
        public const string StickLook = "look";

        static IPgInputBackend _backend;
        static readonly Dictionary<string, bool> Buttons = new Dictionary<string, bool>();
        static readonly Dictionary<string, Vector2> Sticks = new Dictionary<string, Vector2>();

        /// <summary>Registered by the Input System backend on load, or set manually.</summary>
        public static IPgInputBackend Backend
        {
            get => _backend;
            set => _backend = value;
        }

        public static bool IsAvailable => _backend != null && _backend.IsAvailable;

        public static string BackendName => _backend?.Name ?? "none";

        /// <summary>Snapshot of the synthetic state, for reporting what the probe was doing.</summary>
        public static IReadOnlyDictionary<string, Vector2> CurrentSticks => Sticks;

        public static IReadOnlyDictionary<string, bool> CurrentButtons => Buttons;

        public static void Begin()
        {
            if (_backend == null)
                throw new InvalidOperationException(
                    "No Proving Ground input backend is registered. Install com.unity.inputsystem, " +
                    "or assign PgInput.Backend with an IPgInputBackend for your input stack.");

            Buttons.Clear();
            Sticks.Clear();
            _backend.Begin();
            PgEventLog.Record(PgEventLog.ChannelInput, "input.begin", _backend.Name);
        }

        public static void End()
        {
            if (_backend == null) return;
            ReleaseAll();
            _backend.End();
            PgEventLog.Record(PgEventLog.ChannelInput, "input.end", _backend.Name);
        }

        public static void Move(Vector2 direction) => Stick(StickMove, direction);

        public static void Look(Vector2 direction) => Stick(StickLook, direction);

        public static void Stick(string stick, Vector2 value)
        {
            value = Vector2.ClampMagnitude(value, 1f);
            if (Sticks.TryGetValue(stick, out var current) && current == value) return;

            Sticks[stick] = value;
            _backend?.SetStick(stick, value);
            PgEventLog.Record(PgEventLog.ChannelInput, "stick." + stick, $"({value.x:0.##}, {value.y:0.##})");
        }

        public static void Press(string button) => SetButton(button, true);

        public static void Release(string button) => SetButton(button, false);

        public static void SetButton(string button, bool pressed)
        {
            if (Buttons.TryGetValue(button, out var current) && current == pressed) return;

            Buttons[button] = pressed;
            _backend?.SetButton(button, pressed);
            PgEventLog.Record(PgEventLog.ChannelInput, (pressed ? "press." : "release.") + button);
        }

        public static bool IsPressed(string button) => Buttons.TryGetValue(button, out var v) && v;

        /// <summary>Relative pointer movement, in pixels. This is how mouse look is driven.</summary>
        public static void MouseDelta(Vector2 delta)
        {
            if (delta == Vector2.zero) return;
            _backend?.MoveMouse(delta);
            PgEventLog.Record(PgEventLog.ChannelInput, "mouse.delta", $"({delta.x:0.#}, {delta.y:0.#})");
        }

        public static void MousePosition(Vector2 position)
        {
            _backend?.SetMousePosition(position);
            PgEventLog.Record(PgEventLog.ChannelInput, "mouse.position", $"({position.x:0}, {position.y:0})");
        }

        public static void Scroll(Vector2 delta)
        {
            _backend?.Scroll(delta);
            PgEventLog.Record(PgEventLog.ChannelInput, "mouse.scroll", $"({delta.x:0.#}, {delta.y:0.#})");
        }

        public static void ReleaseAll()
        {
            foreach (var button in new List<string>(Buttons.Keys))
                if (Buttons[button])
                    SetButton(button, false);

            foreach (var stick in new List<string>(Sticks.Keys))
                Stick(stick, Vector2.zero);
        }

        public static void Flush() => _backend?.Flush();
    }
}
