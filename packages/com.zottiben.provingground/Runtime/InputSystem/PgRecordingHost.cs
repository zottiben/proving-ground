#if PG_INPUTSYSTEM
using UnityEngine;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Ticks a <see cref="PgInputRecorder"/> for the life of a recording and registers it
    /// as the recording implementation for the package.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class PgRecordingHost : MonoBehaviour
    {
        static PgRecordingHost _instance;
        static readonly PgInputRecorder Recorder = new PgInputRecorder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PgRecording.StartHandler = Begin;
            PgRecording.StopHandler = End;
            PgRecording.IsRecordingHandler = () => Recorder.IsRecording;
        }

        static void Begin()
        {
            if (!Application.isPlaying)
                throw new System.InvalidOperationException("Recording captures live play, so it needs play mode.");

            if (_instance == null)
            {
                var host = new GameObject("[ProvingGround] Recorder") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<PgRecordingHost>();
            }

            Recorder.Begin();
        }

        static PgScenario End(string name)
        {
            if (!Recorder.IsRecording) return null;

            var scenario = Recorder.Stop(name);
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }

            return scenario;
        }

        void Update()
        {
            if (Recorder.IsRecording) Recorder.Tick();
        }
    }
}
#endif
