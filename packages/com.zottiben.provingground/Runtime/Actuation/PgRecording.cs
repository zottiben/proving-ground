using System;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// Recording a live play session into a scenario, without the rest of the package
    /// needing to know which input stack is installed.
    ///
    /// The implementation lives in the Input System assembly, which is only compiled when
    /// that package is present, and registers itself here on load. Same arrangement as
    /// <see cref="PgInput.Backend"/>, for the same reason.
    /// </summary>
    public static class PgRecording
    {
        /// <summary>Set by whichever input stack provides recording.</summary>
        public static Action StartHandler;

        /// <summary>Takes a scenario name, returns the recorded scenario.</summary>
        public static Func<string, PgScenario> StopHandler;

        public static Func<bool> IsRecordingHandler;

        public static bool IsAvailable => StartHandler != null && StopHandler != null;

        public static bool IsRecording => IsRecordingHandler != null && IsRecordingHandler();

        public static void Start()
        {
            if (!IsAvailable)
                throw new InvalidOperationException(
                    "No recorder is registered. Recording needs com.unity.inputsystem.");

            StartHandler();
        }

        /// <summary>Ends the recording and returns the scenario, or null if none was running.</summary>
        public static PgScenario Stop(string name = "recorded")
        {
            if (!IsAvailable)
                throw new InvalidOperationException(
                    "No recorder is registered. Recording needs com.unity.inputsystem.");

            return StopHandler(name);
        }
    }
}
