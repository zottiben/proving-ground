using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ProvingGround.Contracts
{
    /// <summary>
    /// The numeric definition of how the game is supposed to feel. Measured by
    /// <c>PgFeelProbe</c> during a real play session and diffed as a whole set, so that
    /// "snappier" becomes a target rather than a matter of opinion.
    /// </summary>
    [Serializable]
    public sealed class PgFeelSpec
    {
        public const string FileName = "feel.json";

        [JsonProperty("schema")] public string Schema = "provingground/feel@1";

        /// <summary>Used to look up comparison values in the shipped genre norm library.</summary>
        [JsonProperty("genre", NullValueHandling = NullValueHandling.Ignore)]
        public string Genre;

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>Metric id (e.g. <c>jump.apexHeight</c>) to its expectation.</summary>
        [JsonProperty("metrics")]
        public Dictionary<string, PgMetricSpec> Metrics = new Dictionary<string, PgMetricSpec>();

        public static string DefaultPath => Path.Combine(PgPaths.Contracts, FileName);

        public static PgFeelSpec Load(string path = null) =>
            PgJson.Read(path ?? DefaultPath, (PgFeelSpec)null);

        public void Save(string path = null) =>
            PgJson.Write(path ?? DefaultPath, this);

        public PgMetricSpec Get(string id) =>
            Metrics != null && Metrics.TryGetValue(id, out var spec) ? spec : null;

        /// <summary>
        /// Diffs an entire measurement set in one pass. Metrics present in the spec but
        /// absent from the measurements are reported: a metric that silently stopped being
        /// measured is the failure mode this exists to catch.
        /// </summary>
        public List<PgFinding> Diff(IReadOnlyDictionary<string, double> measured, string subject = null)
        {
            var findings = new List<PgFinding>();
            if (Metrics == null) return findings;

            foreach (var pair in Metrics)
            {
                var spec = pair.Value;
                if (spec == null || spec.IsEmpty) continue;

                if (measured == null || !measured.TryGetValue(pair.Key, out var value))
                {
                    findings.Add(PgFinding
                        .Warn($"feel.missing.{pair.Key}", $"{pair.Key} is specified but was not measured")
                        .At(subject)
                        .Fix("Check the probe exercised the behaviour this metric depends on."));
                    continue;
                }

                var finding = spec.Evaluate(pair.Key, value, subject);
                if (finding != null) findings.Add(finding);
            }

            return findings;
        }

        /// <summary>A spec seeded from the genre norms, as the starting point for a new game.</summary>
        public static PgFeelSpec Starter(string genre)
        {
            var spec = new PgFeelSpec
            {
                Genre = genre,
                Note = "Seeded from genre norms. Tune the targets, do not delete the metrics."
            };

            foreach (var pair in PgGenreNorms.For(genre))
                spec.Metrics[pair.Key] = pair.Value;

            return spec;
        }
    }
}
