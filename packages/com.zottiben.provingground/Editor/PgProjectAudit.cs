using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ProvingGround;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Project-level settings that are easy to leave wrong and expensive to discover late:
    /// a build with no scenes, a game still shipping under DefaultCompany, gamma lighting
    /// on a 3D project, development builds heading for release.
    /// </summary>
    public static class PgProjectAudit
    {
        public static PgReport Run()
        {
            var report = new PgReport("project");

            CheckBuildScenes(report);
            CheckIdentity(report);
            CheckRendering(report);
            CheckBuildFlags(report);
            CheckPhysics(report);
            CheckInputHandling(report);

            report.Datum("unityVersion", Application.unityVersion);
            report.Datum("buildTarget", EditorUserBuildSettings.activeBuildTarget.ToString());

            if (report.Findings.Count == 0)
                report.Add(PgFinding.Info("project.clean", "Project settings look sound"));

            return report;
        }

        static void CheckBuildScenes(PgReport report)
        {
            var scenes = EditorBuildSettings.scenes;
            var enabled = scenes.Where(s => s.enabled).ToList();

            if (enabled.Count == 0)
            {
                report.Add(PgFinding
                    .Blocker("project.noScenes", "No scenes are enabled in the build settings")
                    .Fix("A build would launch into nothing. Add at least the entry scene."));
                return;
            }

            report.Datum("buildScenes", enabled.Count);

            foreach (var scene in scenes)
            {
                if (File.Exists(scene.path)) continue;
                report.Add(PgFinding
                    .Fail("project.missingScene", "A scene listed in the build settings does not exist")
                    .At(scene.path)
                    .Fix("Remove the entry, or restore the scene."));
            }
        }

        static void CheckIdentity(PgReport report)
        {
            if (PlayerSettings.companyName == "DefaultCompany")
                report.Add(PgFinding
                    .Warn("project.defaultCompany", "Company name is still DefaultCompany")
                    .Fix("This ends up in the save path, the registry keys and the executable metadata."));

            if (string.IsNullOrWhiteSpace(PlayerSettings.productName) ||
                PlayerSettings.productName == "New Unity Project")
                report.Add(PgFinding
                    .Warn("project.defaultProductName", "Product name has not been set")
                    .Fix("This is the window title and the folder players will find their saves in."));

            var bundleId = PlayerSettings.applicationIdentifier;
            if (string.IsNullOrEmpty(bundleId) || bundleId.Contains("DefaultCompany") ||
                bundleId.EndsWith(".com.unity3d"))
                report.Add(PgFinding
                    .Warn("project.defaultBundleId", $"Application identifier is still a default ('{bundleId}')")
                    .Fix("Stores reject duplicates, and changing it later orphans existing saves."));
        }

        static void CheckRendering(PgReport report)
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                report.Add(PgFinding
                    .Warn("project.colorSpace", "The project renders in gamma colour space")
                    .With("Linear", PlayerSettings.colorSpace.ToString())
                    .Fix("Linear is correct for anything with real lighting. Switching late changes how every asset looks."));

            var quality = QualitySettings.names;
            report.Datum("qualityLevels", quality.Length);

            if (QualitySettings.vSyncCount == 0 && Application.targetFrameRate <= 0)
                report.Add(PgFinding
                    .Info("project.uncappedFrameRate", "VSync is off and no target frame rate is set")
                    .Fix("The game will render as fast as it can, which heats devices and wastes battery."));
        }

        static void CheckBuildFlags(PgReport report)
        {
            if (EditorUserBuildSettings.development)
                report.Add(PgFinding
                    .Warn("project.developmentBuild", "Development Build is enabled")
                    .Fix("Fine while iterating. It must be off for anything you hand to a player."));

            if (EditorUserBuildSettings.allowDebugging)
                report.Add(PgFinding
                    .Warn("project.scriptDebugging", "Script debugging is enabled")
                    .Fix("This costs performance and should not ship."));

            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
#pragma warning disable CS0618 // The NamedBuildTarget replacement is not available on every version this package supports.
            var backend = PlayerSettings.GetScriptingBackend(group);
#pragma warning restore CS0618

            report.Datum("scriptingBackend", backend.ToString());
        }

        /// <summary>
        /// Catches the Input System being installed while the project is still set to the
        /// old Input Manager.
        ///
        /// This one is worth a dedicated check because of how it fails. Code guarded by
        /// ENABLE_INPUT_SYSTEM silently compiles to nothing, so a controller builds without
        /// a single error and simply never responds. Proving Ground's own scenarios and
        /// probe bot drive the game through the Input System, so with the old handler
        /// selected they hold input that nothing is listening for, and report a game that
        /// does not move.
        /// </summary>
        static void CheckInputHandling(PgReport report)
        {
            var manifest = Path.Combine(PgPaths.ProjectRoot, "Packages", "manifest.json");
            var hasPackage = File.Exists(manifest) &&
                             File.ReadAllText(manifest).Contains("com.unity.inputsystem");

            if (!hasPackage) return;

            // activeInputHandler has no public API, so it is read from the settings asset:
            // 0 = old, 1 = new, 2 = both.
            var settings = Resources.FindObjectsOfTypeAll<PlayerSettings>().FirstOrDefault();
            if (settings == null) return;

            using var serialized = new SerializedObject(settings);
            var handler = serialized.FindProperty("activeInputHandler");
            if (handler == null) return;

            report.Datum("activeInputHandler", handler.intValue);

            if (handler.intValue != 0) return;

            report.Add(PgFinding
                .Blocker("project.inputHandlerMismatch",
                    "The Input System package is installed but the project still uses the old Input Manager")
                .With("Input System Package, or Both", "Input Manager (Old)")
                .Fix("Set Project Settings > Player > Active Input Handling to 'Both' or 'Input System Package'. " +
                     "Until then, code inside ENABLE_INPUT_SYSTEM compiles to nothing, so controllers build " +
                     "cleanly and never respond, and Proving Ground scenarios cannot drive the game."));
        }

        static void CheckPhysics(PgReport report)
        {
            var step = Time.fixedDeltaTime;
            report.Datum("fixedTimestep", step);

            if (step > 0.03f)
                report.Add(PgFinding
                    .Warn("project.slowPhysics", $"The fixed timestep is {step * 1000:0}ms")
                    .With("≤ 20ms", $"{step * 1000:0}ms")
                    .Fix("Below about 50Hz, fast-moving collisions start being missed."));

            if (step < 0.005f)
                report.Add(PgFinding
                    .Warn("project.expensivePhysics", $"The fixed timestep is {step * 1000:0.#}ms")
                    .Fix("Physics will run more than 200 times a second, which is rarely worth the cost."));

            if (Time.maximumDeltaTime < Time.fixedDeltaTime)
                report.Add(PgFinding
                    .Warn("project.maximumDeltaTime",
                        "Maximum allowed timestep is below the fixed timestep, so physics will always fall behind")
                    .Fix("Set maximum allowed timestep to at least the fixed timestep."));
        }
    }
}
