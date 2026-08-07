using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProvingGround.Perception;
using ProvingGround.Verification;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Drives the player around unsupervised, looking for the class of defect that only
    /// shows up when someone actually walks into things: getting wedged on geometry,
    /// falling out of the world, NaN transforms, and errors that only appear after
    /// minutes of play.
    ///
    /// It is deliberately heuristic rather than learned. Reinforcement-trained playtest
    /// agents are a research programme with a poor record of surviving contact with a
    /// shipping schedule; a bot that walks, turns and jumps finds most of the same bugs
    /// this week.
    /// </summary>
    public sealed class PgProbeBot
    {
        /// <summary>Below this speed while being told to move, the player counts as stuck.</summary>
        public float StuckSpeedThreshold = 0.15f;

        /// <summary>Seconds of being stuck before it is reported.</summary>
        public float StuckDuration = 2.5f;

        /// <summary>Falling below this Y counts as leaving the world.</summary>
        public float KillPlaneY = -100f;

        /// <summary>Distance from origin beyond which the player is considered escaped.</summary>
        public float MaxDistanceFromOrigin = 5000f;

        /// <summary>How often the bot picks a new heading.</summary>
        public float DecisionInterval = 1.2f;

        /// <summary>Chance of jumping at each decision point.</summary>
        public float JumpChance = 0.25f;

        public PgReport Report { get; private set; }
        public PgFeelProbe Feel { get; } = new PgFeelProbe();

        readonly HashSet<string> _reported = new HashSet<string>();
        readonly List<string> _errors = new List<string>();

        /// <summary>Runs the bot for <paramref name="seconds"/> of game time.</summary>
        public IEnumerator Run(float seconds = 60f, int seed = 12345)
        {
            Report = new PgReport("probe");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var player = PgLocate.Player();
            if (player == null)
            {
                Report.Failed("No player found. Tag the player 'Player' or set PgLocate.PlayerOverride.");
                yield break;
            }

            Application.logMessageReceived += OnLog;
            using var session = new PgSession(seed);

            var inputAvailable = PgInput.IsAvailable;
            if (inputAvailable) PgInput.Begin();
            else
                Report.Add(PgFinding.Warn("probe.noInput",
                    "No input backend; the bot can only observe, not drive"));

            Feel.Begin(player);

            var random = new System.Random(seed);
            var origin = player.position;
            var endTime = Time.time + seconds;
            var nextDecision = 0f;
            var stuckSince = -1f;
            var lastPosition = player.position;
            var visited = new List<Vector3>();

            while (Time.time < endTime)
            {
                if (player == null)
                {
                    Report.Add(PgFinding.Fail("probe.playerDestroyed",
                        "The player was destroyed part way through the run"));
                    break;
                }

                var position = player.position;

                if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
                    float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                {
                    Once("probe.nanTransform", PgFinding
                        .Blocker("probe.nanTransform", "Player transform became NaN or infinite")
                        .Fix("Usually a divide by zero in movement, or a physics force applied with an unnormalised vector."));
                    break;
                }

                if (position.y < KillPlaneY)
                {
                    Once("probe.fellOutOfWorld", PgFinding
                        .Fail("probe.fellOutOfWorld", $"Player fell below y={KillPlaneY}")
                        .At(Describe(position))
                        .Fix("Add a kill volume and a respawn, or close the hole in the collision."));
                    break;
                }

                if (Vector3.Distance(position, origin) > MaxDistanceFromOrigin)
                {
                    Once("probe.escapedBounds", PgFinding
                        .Fail("probe.escapedBounds", $"Player travelled more than {MaxDistanceFromOrigin}m from spawn")
                        .At(Describe(position)));
                    break;
                }

                // Stuck detection only counts while the bot is actually asking to move.
                var speed = (position - lastPosition).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
                var commanded = PgInput.CurrentSticks.TryGetValue(PgInput.StickMove, out var move) &&
                                move.sqrMagnitude > 0.01f;

                if (commanded && speed < StuckSpeedThreshold)
                {
                    if (stuckSince < 0f) stuckSince = Time.time;
                    else if (Time.time - stuckSince > StuckDuration)
                    {
                        Once("probe.stuck." + Describe(position), PgFinding
                            .Fail("probe.stuck", $"Player was unable to move for {StuckDuration}s while input was held")
                            .At(Describe(position))
                            .Fix("Geometry the character controller cannot climb or slide off. Check step offset, slope limit and collider seams."));
                        stuckSince = -1f;
                        // Break out by turning around rather than ending the run.
                        if (inputAvailable) PgInput.Look(new Vector2(1f, 0f));
                    }
                }
                else stuckSince = -1f;

                if (Time.time >= nextDecision && inputAvailable)
                {
                    nextDecision = Time.time + DecisionInterval;
                    var angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                    PgInput.Move(new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)).normalized);
                    PgInput.Look(new Vector2((float)(random.NextDouble() * 2 - 1) * 0.6f, 0f));

                    if (random.NextDouble() < JumpChance) PgInput.Press("jump");
                    else PgInput.Release("jump");

                    visited.Add(position);
                }

                PgInput.Flush();
                Feel.Tick();
                lastPosition = position;
                yield return null;
            }

            Feel.Stop();
            if (inputAvailable) PgInput.End();
            Application.logMessageReceived -= OnLog;

            Report.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
            Report.Datum("samples", visited.Count);
            Report.Datum("coverageRadius", CoverageRadius(visited));

            foreach (var pair in Feel.Results()) Report.Datum("feel." + pair.Key, pair.Value);

            foreach (var error in _errors)
                Report.Add(new PgFinding
                {
                    Id = "runtime.error",
                    Severity = PgSeverity.Fail,
                    Message = "Error logged during the probe run",
                    Actual = error.Length > 400 ? error.Substring(0, 400) + "..." : error
                });

            if (Report.Findings.Count == 0)
                Report.Add(PgFinding.Info("probe.clean",
                    $"{seconds:0}s of unsupervised play produced no stuck points, no falls and no errors"));
        }

        void Once(string key, PgFinding finding)
        {
            if (!_reported.Add(key)) return;
            Report.Add(finding);
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _errors.Add(condition);
            PgEventLog.Record(PgEventLog.ChannelError, type.ToString(), condition);
        }

        static string Describe(Vector3 position) =>
            $"({position.x:0.#}, {position.y:0.#}, {position.z:0.#})";

        static float CoverageRadius(IReadOnlyList<Vector3> visited)
        {
            if (visited.Count < 2) return 0f;
            var max = 0f;
            foreach (var a in visited)
            foreach (var b in visited)
                max = Mathf.Max(max, Vector3.Distance(a, b));
            return Mathf.Round(max * 10f) / 10f;
        }
    }
}
