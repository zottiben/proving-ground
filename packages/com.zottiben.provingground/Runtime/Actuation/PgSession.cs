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

        readonly Random.State _priorRandomState;
        readonly float _priorFixedDelta;
        readonly float _priorMaximumDelta;
        readonly float _priorTimeScale;
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
        public PgSession(int seed = 12345, float fixedDeltaTime = 1f / 60f, bool deterministic = true,
            bool recordEvents = true)
        {
            Seed = seed;
            FixedDeltaTime = fixedDeltaTime;
            Deterministic = deterministic;

            _priorRandomState = Random.state;
            _priorFixedDelta = Time.fixedDeltaTime;
            _priorMaximumDelta = Time.maximumDeltaTime;
            _priorTimeScale = Time.timeScale;
            _priorVSync = QualitySettings.vSyncCount;
            _priorTargetFrameRate = Application.targetFrameRate;
            _priorRunInBackground = Application.runInBackground;

            Random.InitState(seed);
            Time.fixedDeltaTime = fixedDeltaTime;
            if (deterministic) Time.maximumDeltaTime = fixedDeltaTime;

            // Batch mode has no vsync to disable, and forcing a frame rate there only
            // slows the run down.
            if (!Application.isBatchMode)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = Mathf.RoundToInt(1f / fixedDeltaTime);
            }

            Application.runInBackground = true;

            if (recordEvents) PgEventLog.Start();
            PgEventLog.Record(PgEventLog.ChannelScenario, "session.begin",
                $"seed={seed} step={fixedDeltaTime:0.####} deterministic={deterministic}");

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
            QualitySettings.vSyncCount = _priorVSync;
            Application.targetFrameRate = _priorTargetFrameRate;
            Application.runInBackground = _priorRunInBackground;
            Random.state = _priorRandomState;

            if (Current == this) Current = null;
        }
    }
}
