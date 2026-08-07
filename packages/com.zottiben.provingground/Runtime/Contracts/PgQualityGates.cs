using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ProvingGround.Contracts
{
    /// <summary>
    /// Performance budget for a named target device class. Gating on the tail rather than
    /// the mean is deliberate: players feel spikes, not averages.
    /// </summary>
    [Serializable]
    public sealed class PgPerformanceBudget
    {
        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName;

        [JsonProperty("frameTimeMeanMs", NullValueHandling = NullValueHandling.Ignore)]
        public double? FrameTimeMeanMs = 16.6;

        [JsonProperty("frameTimeP95Ms", NullValueHandling = NullValueHandling.Ignore)]
        public double? FrameTimeP95Ms = 20.0;

        [JsonProperty("frameTimeMaxMs", NullValueHandling = NullValueHandling.Ignore)]
        public double? FrameTimeMaxMs = 50.0;

        /// <summary>Managed allocations per frame. Steady-state gameplay should be at or near zero.</summary>
        [JsonProperty("gcAllocPerFrameBytes", NullValueHandling = NullValueHandling.Ignore)]
        public double? GcAllocPerFrameBytes = 0;

        [JsonProperty("maxDrawCalls", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxDrawCalls;

        [JsonProperty("maxTriangles", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxTriangles;

        [JsonProperty("maxTextureMemoryMb", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxTextureMemoryMb;

        /// <summary>Ceiling on scene load time, measured from load call to first rendered frame.</summary>
        [JsonProperty("maxSceneLoadSeconds", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxSceneLoadSeconds;
    }

    /// <summary>
    /// What "good enough to ship" means, expressed so a machine can decide it. Gates are
    /// evaluated against the findings from every other layer.
    /// </summary>
    [Serializable]
    public sealed class PgQualityGates
    {
        public const string FileName = "gates.json";

        [JsonProperty("schema")] public string Schema = "provingground/gates@1";

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>Findings at or above this severity fail the run.</summary>
        [JsonProperty("failAt")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public PgSeverity FailAt = PgSeverity.Fail;

        /// <summary>Budgets keyed by target name, e.g. <c>desktop</c>, <c>steamdeck</c>, <c>mobile-mid</c>.</summary>
        [JsonProperty("performance")]
        public Dictionary<string, PgPerformanceBudget> Performance = new Dictionary<string, PgPerformanceBudget>();

        /// <summary>Finding ids to suppress, with a reason. An unexplained suppression is a smell.</summary>
        [JsonProperty("suppress")] public Dictionary<string, string> Suppress = new Dictionary<string, string>();

        /// <summary>Checks that must have run for a gate evaluation to be considered valid.</summary>
        [JsonProperty("require")] public List<string> Require = new List<string>
        {
            "feel", "ui", "audio", "scene", "content", "accessibility"
        };

        /// <summary>Soak duration for the long-running stability probe.</summary>
        [JsonProperty("soakMinutes")] public double SoakMinutes = 5;

        /// <summary>Maximum pixel difference ratio before a visual regression is a failure.</summary>
        [JsonProperty("visualRegressionThreshold")] public double VisualRegressionThreshold = 0.002;

        public static string DefaultPath => Path.Combine(PgPaths.Contracts, FileName);

        public static PgQualityGates Load(string path = null) =>
            PgJson.Read(path ?? DefaultPath, (PgQualityGates)null);

        public void Save(string path = null) => PgJson.Write(path ?? DefaultPath, this);

        public bool IsSuppressed(string findingId) =>
            Suppress != null && findingId != null && Suppress.ContainsKey(findingId);

        /// <summary>
        /// Applies suppressions and decides pass or fail. Suppressed findings are kept and
        /// downgraded to Info rather than removed, so they stay visible in the report.
        /// </summary>
        public bool Evaluate(PgReport report)
        {
            if (report == null) return false;
            foreach (var finding in report.Findings)
            {
                if (!IsSuppressed(finding.Id)) continue;
                finding.Severity = PgSeverity.Info;
                finding.Message += $" (suppressed: {Suppress[finding.Id]})";
            }

            return report.CountAtLeast(FailAt) == 0;
        }

        public static PgQualityGates Starter()
        {
            var gates = new PgQualityGates
            {
                Note = "Budgets are per target device class. Add one entry per platform you actually ship."
            };
            gates.Performance["desktop"] = new PgPerformanceBudget
            {
                DisplayName = "Desktop 60fps",
                FrameTimeMeanMs = 16.6,
                FrameTimeP95Ms = 20.0,
                FrameTimeMaxMs = 50.0,
                GcAllocPerFrameBytes = 0
            };
            return gates;
        }
    }
}
