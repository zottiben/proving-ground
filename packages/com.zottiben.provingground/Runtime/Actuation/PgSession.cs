using System;
using UnityEngine;
using ProvingGround.Perception;
using Random = UnityEngine.Random;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Puts the game into a state where a run means something: fixed seed, fixed
    /// timestep, no vsync jitter, event log recording.
    ///
    /// Nothing else in this package is trustworthy without it. A feel measurement taken
    /// under a variable timestep is measuring the machine, not the game.
    /// </summary>
    public sealed class PgSession : IDisposable
    {
        public int Seed { get; }
        public float FixedDeltaTime { get; }
        public bool Deterministic { get; }

        /// <summary>True when the clock is being driven by the frame count rather than by real time.</summary>
        public bool CapturingTime { get; }

        readonly Random.State _priorRandomState;
        readonly float _priorFixedDelta;
        readonly float _priorMaximumDelta;
        readonly float _priorTimeScale;
        readonly float _priorCaptureDeltaTime;
        readonly int _priorVSync;
        readonly int _priorTargetFrameRate;
        readonly bool _priorRunInBackground;
        bool _disposed;

        /// <summary>The session currently in scope, if any.</summary>
        public static PgSession Current { get; private set; }

        /// <param name="seed">Seed for UnityEngine.Random. Games with their own RNG must seed it themselves.</param>
        /// <param name="fixedDeltaTime">Physics step. 1/60 by default.</param>
        /// <param name="deterministic">
        /// When true, pins Time.maximumDeltaTime to the fixed step so a slow frame cannot
        /// change how much simulation happens. Trades real-time fidelity for repeatability.
        /// </param>
        /// <param name="recordEvents">Start the event log with the session.</param>
        /// <param name="captureTime">
        /// Drives the clock from the frame count instead of from real elapsed time, so every
        /// frame advances by exactly <paramref name="fixedDeltaTime"/>.
        ///
        /// This is not optional in batch mode, and finding that out is worth the paragraph.
        /// Unity runs headless frames as fast as it can, so the real interval between them
        /// rounds to zero, and any controller that multiplies by Time.deltaTime simply does
        /// not move. A headless run without a controlled clock measures nothing at all.
        /// Defaults to on in batch mode and off in the Editor, where real-time play is
        /// usually what you want to watch.
        /// </param>
        public PgSession(int seed = 12345, float fixedDeltaTime = 1f / 60f, bool deterministic = true,
            bool recordEvents = true, bool? captureTime = null)
        {
            Seed = seed;
            FixedDeltaTime = fixedDeltaTime;
            Deterministic = deterministic;
            CapturingTime = captureTime ?? Application.isBatchMode;

            _priorRandomState = Random.state;
            _priorFixedDelta = Time.fixedDeltaTime;
            _priorMaximumDelta = Time.maximumDeltaTime;
            _priorTimeScale = Time.timeScale;
            _priorCaptureDeltaTime = Time.captureDeltaTime;
            _priorVSync = QualitySettings.vSyncCount;
            _priorTargetFrameRate = Application.targetFrameRate;
            _priorRunInBackground = Application.runInBackground;

            Random.InitState(seed);
            Time.fixedDeltaTime = fixedDeltaTime;
            if (deterministic) Time.maximumDeltaTime = fixedDeltaTime;
            if (CapturingTime) Time.captureDeltaTime = fixedDeltaTime;

            // Batch mode has no vsync to disable, and capping the frame rate there only
            // makes the run take longer in wall-clock time for no benefit.
            if (!Application.isBatchMode)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = Mathf.RoundToInt(1f / fixedDeltaTime);
            }

            Application.runInBackground = true;

            if (recordEvents) PgEventLog.Start();
            PgEventLog.Record(PgEventLog.ChannelScenario, "session.begin",
                $"seed={seed} step={fixedDeltaTime:0.####} deterministic={deterministic} captureTime={CapturingTime}");

            Current = this;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            PgEventLog.Record(PgEventLog.ChannelScenario, "session.end");

            Time.fixedDeltaTime = _priorFixedDelta;
            Time.maximumDeltaTime = _priorMaximumDelta;
            Time.timeScale = _priorTimeScale;
            Time.captureDeltaTime = _priorCaptureDeltaTime;
            QualitySettings.vSyncCount = _priorVSync;
            Application.targetFrameRate = _priorTargetFrameRate;
            Application.runInBackground = _priorRunInBackground;
            Random.state = _priorRandomState;

            if (Current == this) Current = null;
        }
    }
}
