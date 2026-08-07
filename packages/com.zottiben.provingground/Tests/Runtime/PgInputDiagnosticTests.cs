#if PG_INPUTSYSTEM
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using ProvingGround.Actuation;

namespace ProvingGround.Tests
{
    /// <summary>
    /// Narrow tests for the two things everything else rests on: that synthetic input
    /// reaches a real device, and that the clock advances during a headless run.
    /// </summary>
    public class PgInputPathTests
    {
        [UnityTest]
        public IEnumerator QueuedStateEventsReachTheDevice()
        {
            var pad = InputSystem.AddDevice<Gamepad>("DiagnosticPad");
            yield return null;

            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
            yield return null;

            var read = pad.leftStick.ReadValue();
            InputSystem.RemoveDevice(pad);

            Assert.Greater(read.y, 0.5f, $"QueueStateEvent did not reach the device; read {read}");
        }

        [UnityTest]
        public IEnumerator TheBackendDrivesTheDeviceTheGameReads()
        {
            PgInput.Begin();
            PgInput.Move(new Vector2(0f, 1f));

            var read = Vector2.zero;
            for (var frame = 0; frame < 5; frame++)
            {
                PgInput.Flush();
                yield return null;
                if (Gamepad.current != null) read = Gamepad.current.leftStick.ReadValue();
            }

            PgInput.End();
            Assert.Greater(read.y, 0.5f, $"injected input did not reach Gamepad.current; read {read}");
        }

        /// <summary>
        /// Regression test for a failure that silently invalidates every headless
        /// measurement.
        ///
        /// Unity runs batch mode frames as fast as it can, so the real interval between
        /// them rounds to zero and Time.deltaTime comes back as 0. Any controller that
        /// multiplies its speed by delta time then does not move, the probe measures a
        /// stationary player, and the run reports nothing wrong. PgSession pins the clock
        /// to the frame count to prevent it.
        /// </summary>
        [UnityTest]
        public IEnumerator ASessionMakesTheClockAdvanceInHeadlessRuns()
        {
            using var session = new PgSession(seed: 1, fixedDeltaTime: 1f / 60f, captureTime: true);

            // Skip the first frame: the step is applied from the following one.
            yield return null;

            for (var frame = 0; frame < 5; frame++)
            {
                yield return null;
                Assert.AreEqual(1f / 60f, Time.deltaTime, 0.0001f,
                    "the clock must advance by the fixed step on every frame of a captured run");
            }
        }

        [UnityTest]
        public IEnumerator TheSessionRestoresTheClockWhenItEnds()
        {
            var before = Time.captureDeltaTime;

            using (new PgSession(seed: 1, captureTime: true))
            {
                yield return null;
                Assert.AreNotEqual(before, Time.captureDeltaTime);
            }

            Assert.AreEqual(before, Time.captureDeltaTime,
                "a session must leave the project's timing exactly as it found it");
        }
    }
}
#endif
