using System;
using System.Collections;
using UnityEngine;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Hosts Proving Ground coroutines during play mode, so a scenario can be started from
    /// the Editor, from a test, or from game code without each caller needing its own
    /// MonoBehaviour.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class PgRunner : MonoBehaviour
    {
        static PgRunner _instance;

        public static PgRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;

                var host = new GameObject("[ProvingGround] Runner") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<PgRunner>();
                return _instance;
            }
        }

        /// <summary>True while a scenario or probe is running.</summary>
        public static bool IsBusy { get; private set; }

        /// <summary>The report from the most recent run, once it has finished.</summary>
        public static PgReport LastReport { get; private set; }

        /// <summary>Runs a scenario and invokes <paramref name="onComplete"/> with its report.</summary>
        public static void Play(PgScenario scenario, Action<PgReport> onComplete = null)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("Scenarios need play mode. Enter play mode first.");

            if (IsBusy)
                throw new InvalidOperationException("A run is already in progress.");

            var runner = new PgScenarioRunner(scenario);
            Instance.StartCoroutine(Wrap(runner.Run(), () => runner.Report, onComplete));
        }

        /// <summary>Runs the probe bot for a duration and invokes <paramref name="onComplete"/>.</summary>
        public static void Probe(float seconds = 60f, int seed = 12345, Action<PgReport> onComplete = null)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("The probe needs play mode. Enter play mode first.");

            if (IsBusy)
                throw new InvalidOperationException("A run is already in progress.");

            var bot = new PgProbeBot();
            Instance.StartCoroutine(Wrap(bot.Run(seconds, seed), () => bot.Report, onComplete));
        }

        static IEnumerator Wrap(IEnumerator routine, Func<PgReport> reportSource, Action<PgReport> onComplete)
        {
            IsBusy = true;
            try
            {
                yield return routine;
            }
            finally
            {
                IsBusy = false;
                LastReport = reportSource();
            }

            onComplete?.Invoke(LastReport);
        }
    }
}
