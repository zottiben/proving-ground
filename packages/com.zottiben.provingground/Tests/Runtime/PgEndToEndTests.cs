#if PG_INPUTSYSTEM
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using ProvingGround.Actuation;
using ProvingGround.Perception;
using ProvingGround.Verification;

namespace ProvingGround.Tests
{
    /// <summary>
    /// A deliberately ordinary character controller, reading input the way a real game
    /// does. Nothing here knows Proving Ground exists, which is the point: the test proves
    /// that injected input drives an unmodified controller, not that a mock returns what it
    /// was told to.
    /// </summary>
    public sealed class PgTestController : MonoBehaviour
    {
        public float Speed = 6f;
        public float JumpVelocity = 5f;
        public float Gravity = -20f;

        CharacterController _controller;
        float _verticalVelocity;

        void Awake() => _controller = GetComponent<CharacterController>();

        void Update()
        {
            var pad = Gamepad.current;
            var move = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
            var jumpHeld = pad != null && pad.buttonSouth.isPressed;

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (jumpHeld) _verticalVelocity = JumpVelocity;
            }
            else _verticalVelocity += Gravity * Time.deltaTime;

            var motion = new Vector3(move.x, 0f, move.y) * Speed;
            motion.y = _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);
        }
    }

    /// <summary>
    /// End-to-end proof of the core claim: an agent can drive a real game through synthetic
    /// input and get back measurements it could not have obtained from a screenshot.
    /// </summary>
    public class PgEndToEndTests
    {
        GameObject _ground;
        GameObject _player;

        [SetUp]
        public void SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.transform.localScale = new Vector3(10, 1, 10);
            _ground.isStatic = true;

            _player = new GameObject("Player");
            _player.transform.position = new Vector3(0, 1.2f, 0);

            var controller = _player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            _player.AddComponent<PgTestController>();

            PgLocate.PlayerOverride = _player.transform;
        }

        [TearDown]
        public void TearDown()
        {
            PgLocate.PlayerOverride = null;
            if (_player != null) Object.DestroyImmediate(_player);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator InjectedInputMovesTheRealController()
        {
            var start = _player.transform.position;

            using var session = new PgSession(seed: 1);
            PgInput.Begin();
            PgInput.Move(new Vector2(0, 1));

            for (var frame = 0; frame < 60; frame++)
            {
                PgInput.Flush();
                yield return null;
            }

            PgInput.End();

            var travelled = Vector3.Distance(start, _player.transform.position);
            Assert.Greater(travelled, 1f,
                "the character should have moved under injected input, but it did not move at all");
        }

        [UnityTest]
        public IEnumerator TheFeelProbeMeasuresSpeedAndJumpFromObservedMotion()
        {
            var probe = new PgFeelProbeHarness();
            yield return probe.Run(this);

            var results = probe.Results;

            Assert.IsTrue(results.ContainsKey("locomotion.moveSpeed"), "move speed was never measured");
            Assert.AreEqual(6.0, results["locomotion.moveSpeed"], 1.0,
                "measured speed should be close to the controller's configured 6 m/s");

            Assert.IsTrue(results.ContainsKey("jump.apexHeight"), "no jump was detected");
            Assert.Greater(results["jump.apexHeight"], 0.2, "the measured jump arc is implausibly flat");
            Assert.Greater(results["jump.airtime"], 0.1, "the measured airtime is implausibly short");
        }

        [UnityTest]
        public IEnumerator AScenarioRunsEndToEndAndProducesAReport()
        {
            var scenario = new PgScenario
            {
                Name = "test-smoke",
                TimeoutSeconds = 20,
                MeasureFeel = true,
                Steps = new List<PgStep>
                {
                    new PgStep { Do = "wait", Seconds = 0.2f },
                    new PgStep { Do = "move", X = 0, Y = 1, Seconds = 1.0f },
                    new PgStep { Do = "tap", Action = "jump", Frames = 4 },
                    new PgStep { Do = "wait", Seconds = 1.0f },
                    new PgStep { Do = "capture", Name = "end" }
                }
            };

            var runner = new PgScenarioRunner(scenario);
            yield return runner.Run();

            Assert.IsNotNull(runner.Report);
            Assert.IsTrue(runner.Report.Ok, "the scenario could not run: " + runner.Report.Error);
            Assert.IsTrue(runner.Captures.ContainsKey("end"), "the capture step produced no digest");

            var digest = runner.Captures["end"];
            Assert.Greater(digest.Roots.Count, 0, "the scene digest saw nothing at all");
        }

        [UnityTest]
        public IEnumerator TheSceneDigestReportsObjectsThatActuallyExist()
        {
            yield return null;

            var digest = PgSceneDigest.Capture(new PgDigestOptions { IncludeTransforms = true });
            var text = digest.ToText();

            StringAssert.Contains("Player", text);
            Assert.Greater(digest.NodeCount, 0);
        }

        [UnityTest]
        public IEnumerator TheProbeBotReportsCleanlyOnAWorkingLevel()
        {
            var bot = new PgProbeBot();
            yield return bot.Run(3f);

            Assert.IsNotNull(bot.Report);
            Assert.IsTrue(bot.Report.Ok, bot.Report.Error);
            Assert.IsTrue(bot.Report.Passed,
                "a flat plane with a working controller should produce no failures:\n" + bot.Report.ToConsole());
        }

        [UnityTest]
        public IEnumerator RecordingALiveSessionProducesAReplayableScenario()
        {
            Assert.IsTrue(PgRecording.IsAvailable, "no recorder registered");

            using (new PgSession(seed: 7))
            {
                PgInput.Begin();
                PgRecording.Start();

                // Walk forward, then jump: two distinct intents the recorder should separate.
                PgInput.Move(new Vector2(0, 1));
                for (var frame = 0; frame < 40; frame++)
                {
                    PgInput.Flush();
                    yield return null;
                }

                PgInput.Press("jump");
                for (var frame = 0; frame < 10; frame++)
                {
                    PgInput.Flush();
                    yield return null;
                }

                PgInput.Release("jump");
                PgInput.Move(Vector2.zero);
                for (var frame = 0; frame < 20; frame++)
                {
                    PgInput.Flush();
                    yield return null;
                }

                var recorded = PgRecording.Stop("test-recording");
                PgInput.End();

                Assert.IsNotNull(recorded, "the recorder returned no scenario");
                Assert.Greater(recorded.Steps.Count, 0, "the recording captured nothing");

                var verbs = recorded.Steps.Select(s => s.Do).ToList();
                CollectionAssert.Contains(verbs, "move");
                CollectionAssert.Contains(verbs, "press");

                var moved = recorded.Steps.First(s => s.Do == "move");
                Assert.Greater(moved.Y ?? 0f, 0.5f, "forward movement was not captured as forward");

                // The recorded scenario has to be runnable, or it is a log rather than a repro.
                var replay = new PgScenarioRunner(recorded);
                yield return replay.Run();

                Assert.IsTrue(replay.Report.Ok, "the recorded scenario failed to replay: " + replay.Report.Error);
            }
        }

        /// <summary>Drives a movement-then-jump sequence and exposes what the probe measured.</summary>
        sealed class PgFeelProbeHarness
        {
            public Dictionary<string, double> Results = new Dictionary<string, double>();

            public IEnumerator Run(PgEndToEndTests test)
            {
                var probe = new PgFeelProbe();
                using var session = new PgSession(seed: 1);
                PgInput.Begin();
                probe.Begin(test._player.transform);

                // Settle on the ground so the first grounded reading is truthful.
                for (var frame = 0; frame < 10; frame++)
                {
                    PgInput.Flush();
                    probe.Tick();
                    yield return null;
                }

                PgInput.Move(new Vector2(0, 1));
                for (var frame = 0; frame < 60; frame++)
                {
                    PgInput.Flush();
                    probe.Tick();
                    yield return null;
                }

                PgInput.Press("jump");
                for (var frame = 0; frame < 4; frame++)
                {
                    PgInput.Flush();
                    probe.Tick();
                    yield return null;
                }

                PgInput.Release("jump");
                for (var frame = 0; frame < 120; frame++)
                {
                    PgInput.Flush();
                    probe.Tick();
                    yield return null;
                }

                probe.Stop();
                PgInput.End();
                Results = probe.Results();
            }
        }
    }
}
#endif
