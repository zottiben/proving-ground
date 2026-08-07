using UnityEditor;
using UnityEngine;
using ProvingGround.Verification;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Menu entries. Each one calls the same method an agent would, so what a person sees
    /// in the Editor and what an agent gets back are never different implementations.
    /// </summary>
    public static class PgMenu
    {
        const string Root = "Tools/Proving Ground/";

        [MenuItem(Root + "Open Window", priority = 0)]
        public static void OpenWindow() => PgWindow.Open();

        [MenuItem(Root + "Initialise Project", priority = 20)]
        public static void Initialise() => Log(PgSetup.Initialise());

        [MenuItem(Root + "Initialise Design Docs", priority = 21)]
        public static void InitialiseProcess()
        {
            PgApi.InitProcess();
            Debug.Log($"[ProvingGround] Design templates written to {PgPaths.Relative(PgPaths.Design)}");
        }

        [MenuItem(Root + "Survey Project", priority = 22)]
        public static void Survey() => Log(PgBaseline.Survey());

        [MenuItem(Root + "Check/Everything (edit mode)", priority = 40)]
        public static void CheckAll()
        {
            PgApi.CheckAll();
            Debug.Log("[ProvingGround] Reports written to " + PgPaths.Relative(PgPaths.Artifacts));
        }

        [MenuItem(Root + "Check/Project Settings", priority = 41)]
        public static void CheckProject() => Log(PgProjectAudit.Run());

        [MenuItem(Root + "Check/Content", priority = 42)]
        public static void CheckContent() => Log(PgContentAudit.Run());

        [MenuItem(Root + "Check/Audio Assets", priority = 43)]
        public static void CheckAudioAssets() => Log(PgAudioAssetCheck.Run());

        [MenuItem(Root + "Check/Scene Truth", priority = 44)]
        public static void CheckScene() => Log(PgSceneTruth.Analyze());

        [MenuItem(Root + "Check/UI Conformance", priority = 45)]
        public static void CheckUi() => Log(PgUiConformance.Check());

        [MenuItem(Root + "Check/Quality Gate", priority = 46)]
        public static void Gate()
        {
            PgApi.Gate();
            var report = PgJson.Read<PgReport>(PgPaths.Report("gate"));
            if (report != null) Log(report);
        }

        [MenuItem(Root + "Perceive/Scene Digest", priority = 60)]
        public static void Digest() => Debug.Log(PgApi.Digest());

        [MenuItem(Root + "Perceive/Camera View", priority = 61)]
        public static void View() => Debug.Log(PgApi.View());

        [MenuItem(Root + "Perceive/Annotated Capture", priority = 62)]
        public static void Capture() => Debug.Log(PgApi.Capture());

        [MenuItem(Root + "Perceive/Event Log", priority = 63)]
        public static void Events() => Debug.Log(PgApi.Events());

        [MenuItem(Root + "Capture Baseline (play mode)", priority = 80)]
        public static void CaptureBaseline() => Log(PgBaseline.Capture());

        [MenuItem(Root + "Capture Baseline (play mode)", validate = true)]
        public static bool CaptureBaselineValidate() => Application.isPlaying;

        [MenuItem(Root + "Open Proving Ground Folder", priority = 200)]
        public static void OpenFolder()
        {
            var path = System.IO.Path.Combine(PgPaths.ProjectRoot, "ProvingGround");
            if (!System.IO.Directory.Exists(path))
            {
                Debug.LogWarning("[ProvingGround] Not initialised yet. Run Initialise Project first.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        static void Log(PgReport report)
        {
            var text = "[ProvingGround] " + report.ToConsole();
            if (!report.Ok || report.CountAtLeast(PgSeverity.Fail) > 0) Debug.LogError(text);
            else if (report.CountAtLeast(PgSeverity.Warn) > 0) Debug.LogWarning(text);
            else Debug.Log(text);

            PgApi.Emit(report);
        }
    }
}
