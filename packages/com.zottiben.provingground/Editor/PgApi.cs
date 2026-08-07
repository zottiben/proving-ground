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
    public static class PgApi
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
