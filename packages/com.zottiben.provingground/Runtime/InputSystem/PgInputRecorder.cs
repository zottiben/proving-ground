#if PG_INPUTSYSTEM
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Watches a person play and writes what they did as a scenario.
    ///
    /// This is the shortest path from "it broke when I did this" to something anyone can
    /// re-run. A designer plays until the bug happens, stops the recording, and the result
    /// is a deterministic scenario file that reproduces the sequence, which an agent can
    /// then iterate against without needing the bug described in prose.
    ///
    /// It records intent (move this way, press this) rather than raw device samples, so
    /// the output stays readable and editable instead of being a wall of numbers.
    /// </summary>
    public sealed class PgInputRecorder
    {
        /// <summary>Stick movement below this is treated as neutral.</summary>
        public float Deadzone = 0.2f;

        /// <summary>Direction changes smaller than this do not start a new step.</summary>
        public float DirectionEpsilon = 0.25f;

        /// <summary>Actions to watch, mapped to the keys and buttons that carry them.</summary>
        public PgInputBindings Bindings = new PgInputBindings();

        readonly List<PgStep> _steps = new List<PgStep>();
        readonly HashSet<string> _held = new HashSet<string>();

        Vector2 _move;
        float _stateStartTime;
        float _startTime;

        public bool IsRecording { get; private set; }
        public int StepCount => _steps.Count;
        public float Duration => IsRecording ? Time.time - _startTime : 0f;

        public void Begin()
        {
            _steps.Clear();
            _held.Clear();
            _move = Vector2.zero;
            _startTime = Time.time;
            _stateStartTime = Time.time;
            IsRecording = true;
        }

        /// <summary>Call once per frame while recording.</summary>
        public void Tick()
        {
            if (!IsRecording) return;

            var move = ReadMove();
            if ((move - _move).sqrMagnitude > DirectionEpsilon * DirectionEpsilon)
            {
                FlushMove();
                _move = move;
            }

            foreach (var action in AllActions())
            {
                var pressed = IsPressed(action);
                if (pressed == _held.Contains(action)) continue;

                FlushMove();

                if (pressed)
                {
                    _held.Add(action);
                    _steps.Add(new PgStep { Do = "press", Action = action });
                }
                else
                {
                    _held.Remove(action);
                    _steps.Add(new PgStep { Do = "release", Action = action });
                }
            }
        }

        /// <summary>Ends the recording and returns the scenario. Save it to play it back.</summary>
        public PgScenario Stop(string name = "recorded")
        {
            if (!IsRecording) return null;

            FlushMove();
            IsRecording = false;

            // Leaving buttons held at the end of a scenario would leak into whatever runs
            // next, so release everything explicitly.
            foreach (var action in _held)
                _steps.Add(new PgStep { Do = "release", Action = action });
            _held.Clear();

            return new PgScenario
            {
                Name = name,
                Note = $"Recorded from live play on {System.DateTime.Now:yyyy-MM-dd HH:mm}. " +
                       "Edit it freely: the steps are intent, not raw samples. Add assert steps " +
                       "to turn the reproduction into a test.",
                Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                Seed = PgSession.Current?.Seed ?? 12345,
                TimeoutSeconds = Mathf.Max(Mathf.Ceil(Time.time - _startTime) + 15f, 30f),
                Steps = new List<PgStep>(_steps)
            };
        }

        /// <summary>Emits the pending movement as a step covering the time it was held.</summary>
        void FlushMove()
        {
            var elapsed = Time.time - _stateStartTime;
            _stateStartTime = Time.time;

            // Sub-frame slivers are noise; they make the scenario unreadable and change nothing.
            if (elapsed < 0.05f) return;

            _steps.Add(_move.sqrMagnitude < Deadzone * Deadzone
                ? new PgStep { Do = "wait", Seconds = Round(elapsed) }
                : new PgStep
                {
                    Do = "move",
                    X = Round(_move.x),
                    Y = Round(_move.y),
                    Seconds = Round(elapsed)
                });
        }

        Vector2 ReadMove()
        {
            var move = Vector2.zero;

            var pad = Gamepad.current;
            if (pad != null) move += pad.leftStick.ReadValue();

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[Bindings.MoveKeys[0]].isPressed) move.y += 1f;
                if (keyboard[Bindings.MoveKeys[1]].isPressed) move.x -= 1f;
                if (keyboard[Bindings.MoveKeys[2]].isPressed) move.y -= 1f;
                if (keyboard[Bindings.MoveKeys[3]].isPressed) move.x += 1f;
            }

            return Vector2.ClampMagnitude(move, 1f);
        }

        IEnumerable<string> AllActions()
        {
            var seen = new HashSet<string>();
            foreach (var action in Bindings.Keys.Keys) if (seen.Add(action)) yield return action;
            foreach (var action in Bindings.Buttons.Keys) if (seen.Add(action)) yield return action;
            foreach (var action in Bindings.MouseButtons.Keys) if (seen.Add(action)) yield return action;
        }

        bool IsPressed(string action)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && Bindings.Keys.TryGetValue(action, out var key) && keyboard[key].isPressed)
                return true;

            var pad = Gamepad.current;
            if (pad != null && Bindings.Buttons.TryGetValue(action, out var button) && pad[button].isPressed)
                return true;

            var mouse = Mouse.current;
            if (mouse == null || !Bindings.MouseButtons.TryGetValue(action, out var mouseButton)) return false;

            switch (mouseButton)
            {
                case MouseButton.Left: return mouse.leftButton.isPressed;
                case MouseButton.Right: return mouse.rightButton.isPressed;
                case MouseButton.Middle: return mouse.middleButton.isPressed;
                default: return false;
            }
        }

        static float Round(float value) => Mathf.Round(value * 100f) / 100f;
    }
}
#endif
