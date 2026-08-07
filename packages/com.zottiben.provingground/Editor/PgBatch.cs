using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Entry points for <c>-executeMethod</c>, so CI and an agent's shell can run the same
    /// checks the Editor runs.
    ///
    /// Each one exits with a non-zero code when the check fails, because a CI step that
    /// reports success regardless of the result is worse than no CI step.
    /// </summary>
    public static class PgBatch
    {
        /// <summary>Reads <c>-pgArg name value</c> pairs from the command line.</summary>
        public static string Arg(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-" + name)
                    return args[i + 1];
            return fallback;
        }

        public static void Init()
        {
            var report = PgSetup.Initialise(Arg("pgGenre", "fps"));
            Finish(report);
        }

        public static void Survey() => Finish(PgBaseline.Survey());

        public static void CheckProject() => Finish(PgProjectAudit.Run());

        public static void CheckContent() => Finish(PgContentAudit.Run());

        public static void CheckAudioAssets() => Finish(PgAudioAssetCheck.Run());

        public static void CheckScene() => Finish(Verification.PgSceneTruth.Analyze());

        public static void CheckAll()
        {
            PgApi.CheckAll();
            Finish(PgJson.Read<PgReport>(PgPaths.Report("all")));
        }

        public static void Gate()
        {
            PgApi.Gate();
            Finish(PgJson.Read<PgReport>(PgPaths.Report("gate")));
        }

        public static void Milestone()
        {
            var id = Arg("pgMilestone");
            if (string.IsNullOrEmpty(id))
            {
                Console.Error.WriteLine("Pass -pgMilestone <id>. Known: " +
                                        string.Join(", ", PgProcess.Standard().Select(m => m.Id)));
                EditorApplication.Exit(2);
                return;
            }

            Finish(PgProcess.Evaluate(id));
        }

        /// <summary>
        /// Starts the agent bridge and keeps the Editor alive serving it.
        ///
        /// Run with <c>-batchmode</c> and without <c>-quit</c>. Useful for CI, and for
        /// driving a project on a machine with no display. Play-mode operations still work,
        /// because Unity's main loop runs in batch mode.
        /// </summary>
        public static void Serve()
        {
            if (int.TryParse(Arg("pgPort", PgBridge.DefaultPort.ToString()), out var port))
                PgBridge.Port = port;

            PgBridge.AllowShutdownRoute = true;
            PgBridge.Start();

            if (!PgBridge.IsRunning)
            {
                Console.Error.WriteLine("[ProvingGround] The bridge did not start.");
                EditorApplication.Exit(2);
                return;
            }

            Console.WriteLine($"[ProvingGround] Serving on http://127.0.0.1:{PgBridge.Port} " +
                              "(POST /shutdown to stop)");
        }

        static void Finish(PgReport report)
        {
            if (report == null)
            {
                Console.Error.WriteLine("[ProvingGround] No report was produced.");
                EditorApplication.Exit(2);
                return;
            }

            report.Summarise();
            PgApi.Emit(report);

            // Written to stdout rather than the Unity log so it survives -logFile piping.
            Console.WriteLine(report.ToConsole());
            Debug.Log("[ProvingGround] " + report.Summary);

            EditorApplication.Exit(report.Passed ? 0 : 1);
        }
    }
}
