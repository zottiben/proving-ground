using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProvingGround.Contracts;
using ProvingGround.Perception;
using ProvingGround.Verification;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Executes a <see cref="PgScenario"/> against the running game and produces a report.
    ///
    /// Run it from a PlayMode test with <c>yield return runner.Run()</c>, from a
    /// MonoBehaviour coroutine, or from the batch entry point. The scenario drives real
    /// input, so what it proves is what a player would experience.
    /// </summary>
    public sealed class PgScenarioRunner
    {
        public delegate bool AssertionHandler(PgStep step, out string detail);

        public delegate IEnumerator StepHandler(PgScenarioRunner runner, PgStep step);

        static readonly Dictionary<string, StepHandler> CustomSteps =
            new Dictionary<string, StepHandler>(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<string, AssertionHandler> CustomAssertions =
            new Dictionary<string, AssertionHandler>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Adds a project-specific verb. Overrides a built-in of the same name.</summary>
        public static void RegisterStep(string name, StepHandler handler) => CustomSteps[name] = handler;

        /// <summary>Adds a project-specific assertion for <c>assert that=&lt;name&gt;</c>.</summary>
        public static void RegisterAssertion(string name, AssertionHandler handler) => CustomAssertions[name] = handler;

        public PgScenario Scenario { get; }
        public PgReport Report { get; private set; }
        public PgFeelProbe Feel { get; } = new PgFeelProbe();

        /// <summary>Digests captured by <c>capture</c> steps, keyed by label.</summary>
        public Dictionary<string, PgSceneDigest> Captures { get; } = new Dictionary<string, PgSceneDigest>();

        readonly List<string> _errors = new List<string>();
        float _startTime;

        public PgScenarioRunner(PgScenario scenario)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        }

        public IEnumerator Run()
        {
            Report = new PgReport("scenario:" + Scenario.Name);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            if (!string.IsNullOrEmpty(Scenario.Scene) &&
                SceneManager.GetActiveScene().name != Scenario.Scene)
            {
                var load = SceneManager.LoadSceneAsync(Scenario.Scene, LoadSceneMode.Single);
                if (load == null)
                {
                    Report.Failed($"Scene '{Scenario.Scene}' is not in the build settings.");
                    yield break;
                }

                while (!load.isDone) yield return null;
            }

            Application.logMessageReceived += OnLog;
            using var session = new PgSession(Scenario.Seed, Scenario.FixedDeltaTime);

            var inputStarted = false;
            if (PgInput.IsAvailable)
            {
                PgInput.Begin();
                inputStarted = true;
            }
            else
            {
                Report.Add(PgFinding
                    .Warn("scenario.noInput", "No input backend is available; steps that drive input will be skipped")
                    .Fix("Install com.unity.inputsystem, or assign PgInput.Backend for your input stack."));
            }

            if (Scenario.MeasureFeel) Feel.Begin();

            _startTime = Time.time;
            var stepIndex = 0;

            foreach (var step in Scenario.Steps)
            {
                if (TimedOut())
                {
                    Report.Add(PgFinding
                        .Fail("scenario.timeout",
                            $"Scenario exceeded {Scenario.TimeoutSeconds}s at step {stepIndex} ({step})")
                        .Fix("Raise timeoutSeconds, or find why the step never completed."));
                    break;
                }

                PgEventLog.Record(PgEventLog.ChannelScenario, "step", step.ToString());

                IEnumerator routine = null;
                try
                {
                    routine = Execute(step);
                }
                catch (Exception e)
                {
                    Report.Add(PgFinding.Fail($"scenario.step.{stepIndex}", $"Step {step} threw: {e.Message}"));
                }

                if (routine != null)
                {
                    // The enumerator is advanced outside the try so that a yield inside a
                    // step is legal; exceptions from within surface through OnLog.
                    while (true)
                    {
                        object current;
                        try
                        {
                            if (!routine.MoveNext()) break;
                            current = routine.Current;
                        }
                        catch (Exception e)
                        {
                            Report.Add(PgFinding.Fail($"scenario.step.{stepIndex}",
                                $"Step {step} threw: {e.Message}"));
                            break;
                        }

                        yield return current;
                        if (Scenario.MeasureFeel) Feel.Tick();
                    }
                }

                stepIndex++;
            }

            if (Scenario.MeasureFeel) Feel.Stop();
            if (inputStarted) PgInput.End();
            Application.logMessageReceived -= OnLog;

            Finish(stopwatch.Elapsed.TotalMilliseconds);
        }

        bool TimedOut() => Time.time - _startTime > Scenario.TimeoutSeconds;

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _errors.Add(condition);
            PgEventLog.Record(PgEventLog.ChannelError, type.ToString(), condition);
        }

        IEnumerator Execute(PgStep step)
        {
            var verb = (step.Do ?? "").Trim().ToLowerInvariant();

            if (CustomSteps.TryGetValue(verb, out var custom))
                return custom(this, step);

            switch (verb)
            {
                case "wait": return Wait(step);
                case "move": return Move(step);
                case "look": return Look(step);
                case "press": return Simple(() => PgInput.Press(step.Action));
                case "release": return Simple(() => PgInput.Release(step.Action));
                case "tap": return Tap(step);
                case "mouse": return Simple(() => PgInput.MouseDelta(new Vector2(step.X ?? 0f, step.Y ?? 0f)));
                case "teleport": return Teleport(step);
                case "capture": return Capture(step);
                case "assert": return Assert(step);
                case "measure": return Measure(step);
                case "log": return Simple(() => PgEventLog.Gameplay("scenario.log", step.Note ?? step.Name));
                default:
                    Report.Add(PgFinding
                        .Warn("scenario.unknownStep", $"Unknown step verb '{step.Do}'")
                        .Fix("Use one of: wait, move, look, press, release, tap, mouse, teleport, capture, assert, measure, log."));
                    return null;
            }
        }

        static IEnumerator Simple(Action action)
        {
            action();
            PgInput.Flush();
            yield return null;
        }

        IEnumerator Wait(PgStep step)
        {
            if (step.Frames.HasValue)
            {
                for (var i = 0; i < step.Frames.Value; i++) yield return null;
                yield break;
            }

            var duration = step.Seconds ?? 0.25f;
            var until = Time.time + duration;
            while (Time.time < until && !TimedOut())
            {
                PgInput.Flush();
                yield return null;
            }
        }

        IEnumerator Move(PgStep step)
        {
            PgInput.Move(new Vector2(step.X ?? 0f, step.Y ?? 0f));
            if (step.Seconds.HasValue || step.Frames.HasValue)
            {
                yield return Wait(step);
                PgInput.Move(Vector2.zero);
                PgInput.Flush();
            }
            else
            {
                PgInput.Flush();
                yield return null;
            }
        }

        IEnumerator Look(PgStep step)
        {
            PgInput.Look(new Vector2(step.X ?? 0f, step.Y ?? 0f));
            if (step.Seconds.HasValue || step.Frames.HasValue)
            {
                yield return Wait(step);
                PgInput.Look(Vector2.zero);
                PgInput.Flush();
            }
            else
            {
                PgInput.Flush();
                yield return null;
            }
        }

        IEnumerator Tap(PgStep step)
        {
            var action = step.Action ?? "jump";
            PgInput.Press(action);
            PgInput.Flush();

            var frames = step.Frames ?? 2;
            for (var i = 0; i < frames; i++) yield return null;

            PgInput.Release(action);
            PgInput.Flush();
            yield return null;
        }

        IEnumerator Teleport(PgStep step)
        {
            var player = PgLocate.Player();
            if (player == null)
            {
                Report.Add(PgFinding
                    .Fail("scenario.noPlayer", "teleport step could not find the player")
                    .Fix("Tag the player 'Player', or set PgLocate.PlayerOverride."));
                yield break;
            }

            var destination = step.Target != null
                ? PgLocate.Find(step.Target)?.position
                : new Vector3(step.X ?? 0f, step.Y ?? 0f, step.Z ?? 0f);

            if (!destination.HasValue)
            {
                Report.Add(PgFinding.Fail("scenario.noTarget", $"teleport target '{step.Target}' not found"));
                yield break;
            }

            // A CharacterController overwrites direct transform writes, so disable it
            // across the move.
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.position = destination.Value;
            if (controller != null) controller.enabled = true;

            yield return null;
        }

        IEnumerator Capture(PgStep step)
        {
            var label = step.Name ?? $"capture{Captures.Count}";
            Captures[label] = PgSceneDigest.Capture(PgDigestOptions.Compact);
            PgEventLog.Record(PgEventLog.ChannelScenario, "capture", label);
            yield return null;
        }

        IEnumerator Measure(PgStep step)
        {
            var mode = (step.Name ?? "start").ToLowerInvariant();
            if (mode == "stop" || mode == "end") Feel.Stop();
            else Feel.Begin();
            yield return null;
        }

        IEnumerator Assert(PgStep step)
        {
            var kind = (step.That ?? "").Trim().ToLowerInvariant();
            string detail;
            bool passed;

            if (CustomAssertions.TryGetValue(kind, out var custom))
            {
                passed = custom(step, out detail);
            }
            else
            {
                switch (kind)
                {
                    case "reached":
                        passed = Reached(step, out detail);
                        break;
                    case "exists":
                        passed = PgLocate.Find(step.Target) != null;
                        detail = passed ? "found" : "not found";
                        break;
                    case "absent":
                        passed = PgLocate.Find(step.Target) == null;
                        detail = passed ? "absent" : "still present";
                        break;
                    case "visible":
                        passed = Visible(step, out detail);
                        break;
                    default:
                        Report.Add(PgFinding
                            .Warn("scenario.unknownAssertion", $"Unknown assertion '{step.That}'")
                            .Fix("Use reached, exists, absent or visible, or register your own with PgScenarioRunner.RegisterAssertion."));
                        yield break;
                }
            }

            var id = $"assert.{kind}.{step.Target ?? "player"}";
            Report.Add(passed
                ? PgFinding.Info(id, $"{kind} {step.Target} held", detail)
                : PgFinding.Fail(id, $"{kind} {step.Target} did not hold").With(kind, detail));

            yield return null;
        }

        bool Reached(PgStep step, out string detail)
        {
            var player = PgLocate.Player();
            var target = PgLocate.Find(step.Target);

            if (player == null || target == null)
            {
                detail = player == null ? "player not found" : $"target '{step.Target}' not found";
                return false;
            }

            var distance = Vector3.Distance(player.position, target.position);
            detail = $"{distance:0.##}m away";
            return distance <= (step.Within ?? 2f);
        }

        bool Visible(PgStep step, out string detail)
        {
            var view = PgViewDigest.Capture(PgLocate.Eye());
            foreach (var visible in view.Visible)
            {
                if (visible.Path == null || step.Target == null) continue;
                if (!visible.Path.EndsWith(step.Target, StringComparison.Ordinal) &&
                    visible.Name != step.Target) continue;

                detail = visible.Occluded
                    ? $"on screen at ({visible.Rect[0]}, {visible.Rect[1]}) but occluded"
                    : $"on screen at ({visible.Rect[0]}, {visible.Rect[1]})";
                return !visible.Occluded;
            }

            detail = "not in view";
            return false;
        }

        void Finish(double elapsedMs)
        {
            Report.DurationMs = elapsedMs;

            foreach (var error in _errors)
            {
                Report.Add(new PgFinding
                {
                    Id = "runtime.error",
                    Severity = Scenario.FailOnError ? PgSeverity.Fail : PgSeverity.Warn,
                    Message = "Error logged during the run",
                    Actual = error.Length > 400 ? error.Substring(0, 400) + "..." : error
                });
            }

            if (!Scenario.MeasureFeel) return;

            var measured = Feel.Results();
            foreach (var pair in measured) Report.Datum("feel." + pair.Key, pair.Value);

            var spec = PgFeelSpec.Load();
            if (spec != null)
            {
                Report.AddRange(spec.Diff(measured, Scenario.Name));
                Report.AddRange(PgGenreNorms.Compare(spec.Genre, measured));
            }
            else if (measured.Count > 0)
            {
                Report.Add(PgFinding
                    .Info("feel.noSpec", "Feel was measured but no feel spec exists to compare it against")
                    .Fix($"Write {PgFeelSpec.DefaultPath}, or run the baseline capture to generate one from this run."));
            }
        }
    }
}
