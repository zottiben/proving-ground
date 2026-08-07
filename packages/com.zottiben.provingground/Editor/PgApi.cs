using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ProvingGround.Contracts;
using ProvingGround.Perception;
using ProvingGround.Verification;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// The surface an agent drives.
    ///
    /// Every method returns the report as JSON and writes it to
    /// <c>ProvingGround/Artifacts/reports</c>. Returning the result rather than logging it
    /// matters: an agent invoking this through an editor bridge gets the answer back
    /// directly instead of having to scrape the console.
    ///
    /// These are also the methods the menu items and the window call, so there is exactly
    /// one implementation of each operation regardless of who asked for it.
    /// </summary>
    public static partial class PgApi
    {
        /// <summary>Creates the folder layout and starter contracts.</summary>
        public static string Init(string genre = "fps") => Emit(PgSetup.Initialise(genre));

        /// <summary>Describes an existing project, for an agent meeting it for the first time.</summary>
        public static string Survey() => Emit(PgBaseline.Survey());

        /// <summary>Project settings health.</summary>
        public static string CheckProject() => Emit(PgProjectAudit.Run());

        /// <summary>Asset hygiene: broken references, missing scripts, duplicates, import rules.</summary>
        public static string CheckContent() => Emit(PgContentAudit.Run());

        /// <summary>Audio asset measurement: level, peak, silence, loop seams.</summary>
        public static string CheckAudioAssets() => Emit(PgAudioAssetCheck.Run());

        /// <summary>Level truth: spawns, floor holes, navmesh islands, objective reachability.</summary>
        public static string CheckScene() => Emit(PgSceneTruth.Analyze());

        /// <summary>
        /// UI conformance and accessibility. Runs against whatever is on screen, so in edit
        /// mode it will only see UI that exists without play mode.
        /// </summary>
        public static string CheckUi() => Emit(PgUiConformance.Check());

        /// <summary>Everything that does not require play mode.</summary>
        public static string CheckAll()
        {
            var combined = new PgReport("all");
            foreach (var report in new[]
                     {
                         PgProjectAudit.Run(), PgContentAudit.Run(),
                         PgAudioAssetCheck.Run(), PgSceneTruth.Analyze()
                     })
            {
                combined.AddRange(report.Findings);
                if (report.Data == null) continue;
                foreach (var pair in report.Data) combined.Datum(report.Tool + "." + pair.Key, pair.Value);
            }

            return Emit(combined);
        }

        /// <summary>
        /// Applies the quality gates to every report written so far and returns one verdict.
        /// This is what CI should call.
        /// </summary>
        public static string Gate()
        {
            var report = new PgReport("gate");
            var gates = PgQualityGates.Load() ?? PgQualityGates.Starter();
            var directory = Path.Combine(PgPaths.Artifacts, "reports");

            if (!Directory.Exists(directory))
            {
                report.Failed("No reports have been written yet. Run the checks first.");
                return Emit(report);
            }

            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                var loaded = PgJson.Read<PgReport>(path);
                if (loaded == null || loaded.Tool == "gate") continue;

                seen.Add(loaded.Tool.Split(':')[0]);
                report.AddRange(loaded.Findings);
            }

            foreach (var required in gates.Require ?? new System.Collections.Generic.List<string>())
            {
                if (seen.Contains(required)) continue;
                report.Add(PgFinding
                    .Fail("gate.missingCheck", $"'{required}' is required by the gates but has not been run")
                    .Fix("Run it, or remove it from the require list in gates.json."));
            }

            var passed = gates.Evaluate(report);
            report.Summary = passed
                ? $"Gate passed. {report.Findings.Count} finding(s), none at or above {gates.FailAt}."
                : $"Gate failed. {report.CountAtLeast(gates.FailAt)} finding(s) at or above {gates.FailAt}.";

            return Emit(report);
        }

        /// <summary>Readiness for a production milestone, judged on evidence rather than assertion.</summary>
        public static string Milestone(string milestoneId) => Emit(PgProcess.Evaluate(milestoneId));

        /// <summary>Writes the standard milestone ladder and the design doc templates.</summary>
        public static string InitProcess()
        {
            var report = new PgReport("initProcess");
            PgPaths.EnsureDirectory(PgPaths.Design);
            PgProcess.SaveStandard();

            Write(report, "pillars.md", PgProcess.PillarsTemplate);
            Write(report, "one-pager.md", PgProcess.OnePagerTemplate);
            Write(report, "gdd.md", PgProcess.GddTemplate);

            report.Add(PgFinding.Info("process.milestones", "Wrote the standard milestone ladder")
                .At(PgPaths.Relative(PgProcess.DefaultPath)));

            AssetDatabase.Refresh();
            return Emit(report);
        }

        /// <summary>
        /// A symbolic digest of the open scene. This is the "what is actually here" call:
        /// prefer it over a screenshot when the question is about structure or position.
        /// </summary>
        public static string Digest(int maxNodes = 400, bool includeInactive = false, string nameFilter = null)
        {
            var options = new PgDigestOptions { MaxNodes = maxNodes, IncludeInactive = includeInactive };
            if (!string.IsNullOrEmpty(nameFilter)) options.NameFilter.Add(nameFilter);
            return PgSceneDigest.Capture(options).ToText();
        }

        /// <summary>What the camera can currently see, as symbols rather than pixels.</summary>
        public static string View(int maxObjects = 40) =>
            PgViewDigest.Capture(PgLocate.Eye(), maxObjects).ToText();

        /// <summary>
        /// A screenshot with labelled boxes, plus the legend naming them. Send both: the
        /// image alone forces the model to guess what it is looking at.
        /// </summary>
        public static string Capture(string name = "capture", int maxBoxes = 8)
        {
            var path = PgPaths.Capture(name + ".png");
            var annotations = PgCapture.Annotated(path, PgLocate.Eye(), maxBoxes);
            return $"wrote {PgPaths.Relative(path)}\n{PgCapture.LegendText(annotations)}";
        }

        /// <summary>Compares a capture to its stored baseline.</summary>
        public static string VisualCheck(string name) =>
            Emit(PgVisualRegression.Check(name, PgLocate.Eye()));

        /// <summary>The event timeline from the most recent run.</summary>
        public static string Events(int maxEvents = 200) => PgEventLog.ToText(maxEvents);

        /// <summary>Lists the scenarios defined in this project.</summary>
        public static string Scenarios()
        {
            var files = Actuation.PgScenario.All().ToList();
            if (files.Count == 0)
                return $"No scenarios found in {PgPaths.Relative(PgPaths.Scenarios)}. " +
                       "Run Init to create a starter, or write one as JSON.";

            return string.Join("\n", files.Select(f =>
            {
                var scenario = Actuation.PgScenario.Load(f);
                return scenario == null
                    ? $"  {Path.GetFileName(f)}  (could not parse)"
                    : $"  {scenario.Name}  {scenario.Steps.Count} steps, seed {scenario.Seed}, timeout {scenario.TimeoutSeconds}s";
            }));
        }

        /// <summary>Enters play mode. Returns immediately; poll <see cref="RunStatus"/> until playing.</summary>
        public static string EnterPlayMode()
        {
            if (Application.isPlaying) return RunStatus();
            EditorApplication.EnterPlaymode();
            return "{\"state\":\"entering\"}";
        }

        public static string ExitPlayMode()
        {
            if (!Application.isPlaying) return RunStatus();
            EditorApplication.ExitPlaymode();
            return "{\"state\":\"exiting\"}";
        }

        /// <summary>
        /// Starts a scenario and returns at once. A scenario spans many frames, so it cannot
        /// complete inside a single call; poll <see cref="RunStatus"/> for the report.
        /// </summary>
        public static string RunScenario(string name)
        {
            if (!Application.isPlaying)
                return Emit(new PgReport("scenario:" + name)
                    .Failed("Scenarios drive the running game. Call EnterPlayMode first."));

            if (Actuation.PgRunner.IsBusy)
                return "{\"state\":\"busy\"}";

            var scenario = Actuation.PgScenario.LoadByName(name);
            if (scenario == null)
                return Emit(new PgReport("scenario:" + name)
                    .Failed($"No scenario named '{name}' in {PgPaths.Relative(PgPaths.Scenarios)}."));

            Actuation.PgRunner.Play(scenario, report => Emit(report));
            return "{\"state\":\"running\"}";
        }

        /// <summary>Starts the probe bot. Poll <see cref="RunStatus"/> for the report.</summary>
        public static string RunProbe(float seconds = 60f, int seed = 12345)
        {
            if (!Application.isPlaying)
                return Emit(new PgReport("probe")
                    .Failed("The probe drives the running game. Call EnterPlayMode first."));

            if (Actuation.PgRunner.IsBusy) return "{\"state\":\"busy\"}";

            Actuation.PgRunner.Probe(seconds, seed, report => Emit(report));
            return "{\"state\":\"running\"}";
        }

        /// <summary>Whether a run is in progress, and the report from the last one.</summary>
        public static string RunStatus()
        {
            var report = Actuation.PgRunner.LastReport;
            return PgJson.Stringify(new
            {
                isPlaying = Application.isPlaying,
                isCompiling = EditorApplication.isCompiling,
                busy = Actuation.PgRunner.IsBusy,
                lastReport = report
            });
        }

        /// <summary>
        /// Writes contracts describing the game as it currently behaves. Play mode only, and
        /// only meaningful after a run has exercised the systems being captured.
        /// </summary>
        public static string CaptureBaseline(bool overwrite = false)
        {
            var metrics = new System.Collections.Generic.Dictionary<string, double>();
            var last = Actuation.PgRunner.LastReport;

            if (last?.Data != null)
                foreach (var pair in last.Data)
                {
                    if (!pair.Key.StartsWith("feel.")) continue;
                    if (double.TryParse(pair.Value?.ToString(), out var value))
                        metrics[pair.Key.Substring("feel.".Length)] = value;
                }

            return Emit(PgBaseline.Capture(metrics, PgFeelSpec.Load()?.Genre, overwrite));
        }

        /// <summary>
        /// Starts inferring audio events from AudioSource activity, for a game with no
        /// explicit instrumentation.
        /// </summary>
        public static string WatchAudio()
        {
            if (!Application.isPlaying)
                return "{\"ok\":false,\"error\":\"Play mode only.\"}";

            PgAudio.Watch();
            return "{\"ok\":true,\"watching\":true}";
        }

        /// <summary>Audio event wiring, diffed against the contract, from the last run.</summary>
        public static string CheckAudio() => Emit(PgAudio.Check());

        /// <summary>
        /// Starts recording live play. Play until the thing you care about happens, then call
        /// <see cref="StopRecording"/> to get a scenario that reproduces it.
        /// </summary>
        public static string StartRecording()
        {
            if (!Application.isPlaying)
                return "{\"ok\":false,\"error\":\"Recording captures live play, so it needs play mode.\"}";

            if (!Actuation.PgRecording.IsAvailable)
                return "{\"ok\":false,\"error\":\"Recording needs com.unity.inputsystem.\"}";

            Actuation.PgRecording.Start();
            return "{\"ok\":true,\"recording\":true}";
        }

        /// <summary>Ends the recording and saves it as a runnable scenario.</summary>
        public static string StopRecording(string name = "recorded")
        {
            var report = new PgReport("recording");

            if (!Actuation.PgRecording.IsAvailable || !Actuation.PgRecording.IsRecording)
                return Emit(report.Failed("Nothing is being recorded."));

            var scenario = Actuation.PgRecording.Stop(name);
            if (scenario == null) return Emit(report.Failed("The recording produced no scenario."));

            var path = Actuation.PgScenario.PathFor(name);
            scenario.Save(path);

            report.Add(PgFinding
                .Info("recording.saved", $"Recorded {scenario.Steps.Count} steps as '{name}'")
                .At(PgPaths.Relative(path))
                .Fix("Add assert steps to turn the reproduction into a test that stays green."));

            return Emit(report);
        }

        /// <summary>Genre norm values, for tuning against something other than a guess.</summary>
        public static string Norms(string genre)
        {
            var norms = PgGenreNorms.For(genre);
            if (norms.Count == 0)
                return $"No norms for '{genre}'. Known genres: {string.Join(", ", PgGenreNorms.Genres)}";

            return string.Join("\n", norms.Select(n =>
                $"  {n.Key,-28} {n.Value.Describe(),-22} {n.Value.Note}"));
        }

        static void Write(PgReport report, string fileName, string contents)
        {
            var path = Path.Combine(PgPaths.Design, fileName);
            if (File.Exists(path))
            {
                report.Add(PgFinding.Info("process.kept", $"Kept existing {fileName}").At(PgPaths.Relative(path)));
                return;
            }

            File.WriteAllText(path, contents);
            report.Add(PgFinding.Info("process.created", $"Created {fileName}").At(PgPaths.Relative(path)));
        }

        /// <summary>Writes the report to disk and returns it as JSON.</summary>
        internal static string Emit(PgReport report)
        {
            report.Summarise();
            var safeName = report.Tool.Replace(':', '-').Replace('/', '-');

            try
            {
                PgJson.Write(PgPaths.Report(safeName), report);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProvingGround] Could not write the report: {e.Message}");
            }

            return PgJson.Stringify(report);
        }
    }
}
