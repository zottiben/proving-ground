using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ProvingGround
{
    /// <summary>
    /// How much a finding matters. Anything at <see cref="Fail"/> or above breaks a gate.
    /// </summary>
    public enum PgSeverity
    {
        Info = 0,
        Warn = 1,
        Fail = 2,
        Blocker = 3
    }

    /// <summary>
    /// One thing that was observed to be true about the project or the running game.
    /// Findings are the only currency in Proving Ground: every check, probe, audit and
    /// measurement reduces to a list of these, so an agent only ever parses one shape.
    /// </summary>
    [Serializable]
    public sealed class PgFinding
    {
        /// <summary>Stable dotted identifier, e.g. <c>feel.jump.apex</c>. Used to suppress and to diff across runs.</summary>
        [JsonProperty("id")] public string Id;

        [JsonProperty("severity")] [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public PgSeverity Severity = PgSeverity.Info;

        /// <summary>One line, written for a reader who cannot see the screen.</summary>
        [JsonProperty("message")] public string Message;

        /// <summary>Where this came from: an asset path, scene path, GameObject path or file:line.</summary>
        [JsonProperty("subject", NullValueHandling = NullValueHandling.Ignore)]
        public string Subject;

        /// <summary>What the contract asked for, when the finding came from a contract diff.</summary>
        [JsonProperty("expected", NullValueHandling = NullValueHandling.Ignore)]
        public string Expected;

        /// <summary>What was actually measured or observed.</summary>
        [JsonProperty("actual", NullValueHandling = NullValueHandling.Ignore)]
        public string Actual;

        /// <summary>Concrete next step. Omit rather than write filler.</summary>
        [JsonProperty("remedy", NullValueHandling = NullValueHandling.Ignore)]
        public string Remedy;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Data;

        public static PgFinding Info(string id, string message, string subject = null) =>
            new PgFinding { Id = id, Severity = PgSeverity.Info, Message = message, Subject = subject };

        public static PgFinding Warn(string id, string message, string subject = null) =>
            new PgFinding { Id = id, Severity = PgSeverity.Warn, Message = message, Subject = subject };

        public static PgFinding Fail(string id, string message, string subject = null) =>
            new PgFinding { Id = id, Severity = PgSeverity.Fail, Message = message, Subject = subject };

        public static PgFinding Blocker(string id, string message, string subject = null) =>
            new PgFinding { Id = id, Severity = PgSeverity.Blocker, Message = message, Subject = subject };

        public PgFinding With(string expected, string actual)
        {
            Expected = expected;
            Actual = actual;
            return this;
        }

        public PgFinding Fix(string remedy)
        {
            Remedy = remedy;
            return this;
        }

        public PgFinding At(string subject)
        {
            Subject = subject;
            return this;
        }

        public PgFinding Datum(string key, object value)
        {
            Data ??= new Dictionary<string, object>();
            Data[key] = value;
            return this;
        }

        public override string ToString()
        {
            var head = $"[{Severity.ToString().ToUpperInvariant()}] {Id}: {Message}";
            if (!string.IsNullOrEmpty(Subject)) head += $"  ({Subject})";
            if (Expected != null || Actual != null) head += $"  expected {Expected}, got {Actual}";
            return head;
        }
    }

    /// <summary>
    /// The result of one Proving Ground operation. Serialises to JSON as the single
    /// artifact an agent reads back.
    /// </summary>
    [Serializable]
    public sealed class PgReport
    {
        [JsonProperty("tool")] public string Tool;
        [JsonProperty("schema")] public string Schema = "provingground/report@1";
        [JsonProperty("startedUtc")] public string StartedUtc = DateTime.UtcNow.ToString("o");
        [JsonProperty("durationMs")] public double DurationMs;

        /// <summary>False when the operation itself could not run (as distinct from running and finding failures).</summary>
        [JsonProperty("ok")] public bool Ok = true;

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error;

        [JsonProperty("summary", NullValueHandling = NullValueHandling.Ignore)]
        public string Summary;

        [JsonProperty("findings")] public List<PgFinding> Findings = new List<PgFinding>();

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Data;

        public PgReport() { }

        public PgReport(string tool)
        {
            Tool = tool;
        }

        public int CountAtLeast(PgSeverity severity) => Findings.Count(f => f.Severity >= severity);

        /// <summary>True when nothing at <see cref="PgSeverity.Fail"/> or above was found.</summary>
        [JsonProperty("passed")]
        public bool Passed => Ok && CountAtLeast(PgSeverity.Fail) == 0;

        public PgReport Add(PgFinding finding)
        {
            if (finding != null) Findings.Add(finding);
            return this;
        }

        public PgReport AddRange(IEnumerable<PgFinding> findings)
        {
            if (findings != null) Findings.AddRange(findings.Where(f => f != null));
            return this;
        }

        public PgReport Datum(string key, object value)
        {
            Data ??= new Dictionary<string, object>();
            Data[key] = value;
            return this;
        }

        public PgReport Failed(string error)
        {
            Ok = false;
            Error = error;
            return this;
        }

        /// <summary>
        /// Fills <see cref="Summary"/> with a counts line. Called automatically by
        /// <see cref="PgJson.Write"/> when no summary was set.
        /// </summary>
        public PgReport Summarise()
        {
            if (!string.IsNullOrEmpty(Summary)) return this;
            if (!Ok)
            {
                Summary = $"{Tool} could not run: {Error}";
                return this;
            }

            var blockers = Findings.Count(f => f.Severity == PgSeverity.Blocker);
            var fails = Findings.Count(f => f.Severity == PgSeverity.Fail);
            var warns = Findings.Count(f => f.Severity == PgSeverity.Warn);
            Summary = Passed
                ? $"{Tool} passed ({warns} warning{(warns == 1 ? "" : "s")})"
                : $"{Tool} failed: {blockers} blocker{(blockers == 1 ? "" : "s")}, {fails} failure{(fails == 1 ? "" : "s")}, {warns} warning{(warns == 1 ? "" : "s")}";
            return this;
        }

        /// <summary>Human-readable rendering for the console and the Editor window.</summary>
        public string ToConsole(int maxFindings = 50)
        {
            Summarise();
            var lines = new List<string> { Summary };
            foreach (var finding in Findings
                         .OrderByDescending(f => f.Severity)
                         .Take(maxFindings))
            {
                lines.Add("  " + finding);
            }

            if (Findings.Count > maxFindings)
                lines.Add($"  ... and {Findings.Count - maxFindings} more");
            return string.Join("\n", lines);
        }
    }
}
