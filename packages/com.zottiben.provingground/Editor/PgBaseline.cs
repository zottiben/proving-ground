using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using ProvingGround.Contracts;
using ProvingGround.Perception;
using ProvingGround.Verification;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Turns a game that already exists into a game that can be verified.
    ///
    /// A project with no spec cannot be diffed against one, which is why iterating on an
    /// existing game with an agent goes wrong so reliably. The way out is the one legacy
    /// code has used for years: characterise the current behaviour first, and treat that
    /// as the thing to preserve. What the game does today becomes the baseline, and every
    /// later change is measured as a deviation from it.
    ///
    /// The contracts written here are a starting point that describes the game as it is,
    /// not as it should be. They are meant to be edited afterwards, and the tolerances are
    /// deliberately generous so the first run does not drown the reader in noise.
    /// </summary>
    public static class PgBaseline
    {
        /// <summary>Fractional tolerance applied around each measured feel value.</summary>
        public const double FeelTolerance = 0.20;

        /// <summary>
        /// Captures contracts from the running game. Must be called during play mode, after
        /// a scenario or probe has exercised the systems being captured.
        /// </summary>
        /// <param name="overwrite">Replace contracts that already exist.</param>
        public static PgReport Capture(IReadOnlyDictionary<string, double> feelMetrics = null,
            string genre = null, bool overwrite = false)
        {
            var report = new PgReport("baseline");

            if (!Application.isPlaying)
            {
                report.Failed("Baseline capture reads the running game, so it must be run during play mode.");
                return report;
            }

            CaptureFeel(report, feelMetrics, genre, overwrite);
            CaptureUi(report, overwrite);
            CaptureAudio(report, overwrite);

            report.Summary = report.Passed
                ? "Baseline captured. Review the contracts before committing them: they describe the game as it is, not as it should be."
                : "Baseline capture had problems.";

            return report;
        }

        static void CaptureFeel(PgReport report, IReadOnlyDictionary<string, double> metrics,
            string genre, bool overwrite)
        {
            if (metrics == null || metrics.Count == 0)
            {
                report.Add(PgFinding
                    .Warn("baseline.noFeel", "No feel metrics were supplied, so feel.json was not written")
                    .Fix("Run a scenario or the probe bot first, then capture the baseline from its results."));
                return;
            }

            var path = PgFeelSpec.DefaultPath;
            if (File.Exists(path) && !overwrite)
            {
                report.Add(PgFinding.Info("baseline.feelKept", "feel.json already exists and was not overwritten")
                    .At(PgPaths.Relative(path)));
                return;
            }

            var spec = new PgFeelSpec
            {
                Genre = genre,
                Note = "Captured from the running game. These are the values the game had when the baseline " +
                       "was taken, not values anyone chose. Edit them into intentions."
            };

            foreach (var pair in metrics)
            {
                // Counts are evidence that a metric was exercised, not something to hold constant.
                if (pair.Key.EndsWith("count", System.StringComparison.OrdinalIgnoreCase)) continue;

                var tolerance = System.Math.Max(System.Math.Abs(pair.Value) * FeelTolerance, 0.001);
                spec.Metrics[pair.Key] = new PgMetricSpec
                {
                    Target = System.Math.Round(pair.Value, 4),
                    Tolerance = System.Math.Round(tolerance, 4),
                    Unit = UnitFor(pair.Key),
                    Severity = PgSeverity.Warn,
                    Note = "Captured, not chosen."
                };
            }

            spec.Save(path);
            report.Add(PgFinding
                .Info("baseline.feel", $"Wrote {spec.Metrics.Count} feel metrics captured from the running game")
                .At(PgPaths.Relative(path)));
        }

        static void CaptureUi(PgReport report, bool overwrite)
        {
            var facts = PgUi.Collect();
            if (facts.Count == 0)
            {
                report.Add(PgFinding.Info("baseline.noUi", "No UI was on screen, so ui.json was not written"));
                return;
            }

            var path = PgUiManifest.DefaultPath;
            if (File.Exists(path) && !overwrite)
            {
                report.Add(PgFinding.Info("baseline.uiKept", "ui.json already exists and was not overwritten")
                    .At(PgPaths.Relative(path)));
                return;
            }

            var manifest = new PgUiManifest
            {
                Note = "Captured from the running game. Promote the values that are deliberate into tokens, " +
                       "and delete the elements you do not intend to hold to a design."
            };

            // Colours that appear on several elements are almost certainly design tokens.
            var colorCounts = new Dictionary<string, int>();
            foreach (var color in facts.Select(f => f.Color).Concat(facts.Select(f => f.BackgroundColor)))
            {
                if (string.IsNullOrEmpty(color)) continue;
                colorCounts.TryGetValue(color, out var count);
                colorCounts[color] = count + 1;
            }

            var tokenIndex = 1;
            var tokenByColor = new Dictionary<string, string>();
            foreach (var pair in colorCounts.Where(p => p.Value > 1).OrderByDescending(p => p.Value).Take(12))
            {
                var name = $"color.captured{tokenIndex++}";
                manifest.Tokens[name] = pair.Key;
                tokenByColor[pair.Key] = "$" + name;
            }

            foreach (var element in facts.Where(f => f.Active && !string.IsNullOrEmpty(f.Name)).Take(80))
            {
                var expect = new Dictionary<string, string>();

                if (!string.IsNullOrEmpty(element.Color))
                    expect["color"] = tokenByColor.TryGetValue(element.Color, out var token) ? token : element.Color;

                if (element.FontSize.HasValue)
                    expect["fontSize"] = element.FontSize.Value.ToString("0.##");

                if (expect.Count == 0) continue;

                var id = element.Name;
                if (manifest.Elements.ContainsKey(id)) continue;

                manifest.Elements[id] = new PgUiElementSpec
                {
                    Match = element.Name,
                    Required = false,
                    Expect = expect,
                    Note = "Captured, not chosen."
                };
            }

            manifest.Save(path);
            report.Add(PgFinding
                .Info("baseline.ui",
                    $"Wrote {manifest.Elements.Count} UI elements and {manifest.Tokens.Count} candidate tokens")
                .At(PgPaths.Relative(path)));
        }

        static void CaptureAudio(PgReport report, bool overwrite)
        {
            var fired = PgEventLog.Histogram(PgEventLog.ChannelAudio);
            if (fired.Count == 0)
            {
                report.Add(PgFinding
                    .Info("baseline.noAudio", "No audio events were observed, so audio.json was not written")
                    .Fix("Call PgAudio.Watch() before the run to infer events from AudioSource activity."));
                return;
            }

            var path = PgAudioContract.DefaultPath;
            if (File.Exists(path) && !overwrite)
            {
                report.Add(PgFinding.Info("baseline.audioKept", "audio.json already exists and was not overwritten")
                    .At(PgPaths.Relative(path)));
                return;
            }

            var contract = new PgAudioContract
            {
                Note = "Captured from the running game. Rate ceilings are set well above what was observed; " +
                       "tighten them once you know what is intentional.",
                ForbidUndeclaredEvents = false,
                ForbidDeadEvents = false
            };

            foreach (var pair in fired)
            {
                var peak = PgEventLog.PeakPerSecond(PgEventLog.ChannelAudio, pair.Key);
                contract.Events[pair.Key] = new PgAudioEventSpec
                {
                    Required = false,
                    MaxPerSecond = System.Math.Max(peak * 2, 4),
                    Note = $"Observed {pair.Value} time(s), peaking at {peak}/s."
                };
            }

            contract.Save(path);
            report.Add(PgFinding
                .Info("baseline.audio", $"Wrote {contract.Events.Count} audio events observed during the run")
                .At(PgPaths.Relative(path)));
        }

        /// <summary>
        /// Reads an existing project and describes what it is, for an agent meeting the
        /// codebase for the first time. Runs entirely in edit mode.
        /// </summary>
        public static PgReport Survey()
        {
            var report = new PgReport("survey");

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            report.Datum("buildScenes", scenes);
            report.Datum("unityVersion", Application.unityVersion);
            report.Datum("renderPipeline",
                GraphicsSettings.defaultRenderPipeline != null
                    ? GraphicsSettings.defaultRenderPipeline.GetType().Name
                    : "Built-in");

            var scripts = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }).Length;
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Length;
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }).Length;
            var textures = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" }).Length;
            var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Length;

            report.Datum("scripts", scripts);
            report.Datum("prefabs", prefabs);
            report.Datum("audioClips", clips);
            report.Datum("textures", textures);
            report.Datum("materials", materials);

            report.Datum("hasInputSystem", HasPackage("com.unity.inputsystem"));
            report.Datum("hasUgui", HasPackage("com.unity.ugui"));
            report.Datum("hasNavMesh", HasPackage("com.unity.modules.ai"));
            report.Datum("contractsPresent", PgSetup.IsInitialised);

            report.Add(PgFinding.Info("survey.summary",
                $"{scripts} scripts, {prefabs} prefabs, {clips} audio clips, {textures} textures " +
                $"across {scenes.Count} build scene(s), Unity {Application.unityVersion}"));

            if (!PgSetup.IsInitialised)
                report.Add(PgFinding
                    .Info("survey.notInitialised", "Proving Ground has not been initialised in this project")
                    .Fix("Run Tools > Proving Ground > Initialise Project, then capture a baseline while playing."));

            return report;
        }

        static bool HasPackage(string id)
        {
            var manifest = Path.Combine(PgPaths.ProjectRoot, "Packages", "manifest.json");
            return File.Exists(manifest) && File.ReadAllText(manifest).Contains(id);
        }

        static string UnitFor(string metricId)
        {
            if (metricId.StartsWith("perf.frameTime")) return "ms";
            if (metricId.Contains("Speed")) return "m/s";
            if (metricId.Contains("Height")) return "m";
            if (metricId.Contains("Latency")) return "frames";
            if (metricId.Contains("Time") || metricId.Contains("airtime")) return "s";
            return null;
        }
    }
}
