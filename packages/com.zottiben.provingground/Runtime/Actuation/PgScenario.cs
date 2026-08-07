using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ProvingGround.Actuation
{
    /// <summary>
    /// One instruction in a scenario. Deliberately loose: an agent writes these by hand,
    /// and a schema that rejects a plausible spelling is a schema that wastes a turn.
    /// </summary>
    [Serializable]
    public sealed class PgStep
    {
        /// <summary>
        /// What to do. Built in: wait, move, look, press, release, tap, mouse, teleport,
        /// capture, assert, measure, log, reload.
        /// </summary>
        [JsonProperty("do")] public string Do;

        [JsonProperty("seconds", NullValueHandling = NullValueHandling.Ignore)]
        public float? Seconds;

        [JsonProperty("frames", NullValueHandling = NullValueHandling.Ignore)]
        public int? Frames;

        [JsonProperty("x", NullValueHandling = NullValueHandling.Ignore)]
        public float? X;

        [JsonProperty("y", NullValueHandling = NullValueHandling.Ignore)]
        public float? Y;

        [JsonProperty("z", NullValueHandling = NullValueHandling.Ignore)]
        public float? Z;

        /// <summary>Button or action name for press/release/tap.</summary>
        [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]
        public string Action;

        /// <summary>Object path or name the step operates on.</summary>
        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
        public string Target;

        /// <summary>Assertion kind for <c>assert</c> steps: reached, visible, exists, absent, alive.</summary>
        [JsonProperty("that", NullValueHandling = NullValueHandling.Ignore)]
        public string That;

        /// <summary>Radius for proximity assertions, in metres.</summary>
        [JsonProperty("within", NullValueHandling = NullValueHandling.Ignore)]
        public float? Within;

        /// <summary>Label for capture and measure steps.</summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name;

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        public override string ToString()
        {
            var parts = new List<string> { Do };
            if (Action != null) parts.Add(Action);
            if (Target != null) parts.Add(Target);
            if (That != null) parts.Add("that=" + That);
            if (X.HasValue || Y.HasValue) parts.Add($"({X ?? 0}, {Y ?? 0})");
            if (Seconds.HasValue) parts.Add($"{Seconds}s");
            if (Frames.HasValue) parts.Add($"{Frames}f");
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// A reproducible play session, as data. This is the unit an agent writes, commits and
    /// re-runs: the same seed and the same steps produce the same run, which is what turns
    /// "it broke once" into a bug someone can fix.
    /// </summary>
    [Serializable]
    public sealed class PgScenario
    {
        [JsonProperty("schema")] public string Schema = "provingground/scenario@1";

        [JsonProperty("name")] public string Name = "unnamed";

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>Scene to load before running. Null runs against whatever is already open.</summary>
        [JsonProperty("scene", NullValueHandling = NullValueHandling.Ignore)]
        public string Scene;

        [JsonProperty("seed")] public int Seed = 12345;

        [JsonProperty("fixedDeltaTime")] public float FixedDeltaTime = 1f / 60f;

        /// <summary>Hard ceiling on run length. A scenario that hangs must still produce a report.</summary>
        [JsonProperty("timeoutSeconds")] public float TimeoutSeconds = 120f;

        /// <summary>Fail the run when any exception or error log occurs during it.</summary>
        [JsonProperty("failOnError")] public bool FailOnError = true;

        /// <summary>Measure feel metrics for the duration and diff them against the feel spec.</summary>
        [JsonProperty("measureFeel")] public bool MeasureFeel = true;

        [JsonProperty("steps")] public List<PgStep> Steps = new List<PgStep>();

        public static string DirectoryPath => PgPaths.Scenarios;

        public static string PathFor(string name) =>
            Path.Combine(DirectoryPath, name + ".json");

        public static PgScenario Load(string path) => PgJson.Read(path, (PgScenario)null);

        public static PgScenario LoadByName(string name) => Load(PathFor(name));

        public void Save(string path = null) =>
            PgJson.Write(path ?? PathFor(Name), this);

        public static IEnumerable<string> All()
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();
            return Directory.GetFiles(DirectoryPath, "*.json", SearchOption.AllDirectories);
        }

        /// <summary>Walk forward, jump, and look around. The smallest scenario that exercises a controller.</summary>
        public static PgScenario Smoke() => new PgScenario
        {
            Name = "smoke",
            Note = "Minimal controller exercise. Confirms input reaches the game and nothing throws.",
            TimeoutSeconds = 30,
            Steps = new List<PgStep>
            {
                new PgStep { Do = "wait", Seconds = 0.5f, Note = "let the scene settle" },
                new PgStep { Do = "measure", Name = "start" },
                new PgStep { Do = "move", X = 0, Y = 1, Seconds = 2f },
                new PgStep { Do = "tap", Action = "jump" },
                new PgStep { Do = "wait", Seconds = 1.5f },
                new PgStep { Do = "look", X = 1, Y = 0, Seconds = 1f },
                new PgStep { Do = "move", X = 0, Y = 0 },
                new PgStep { Do = "capture", Name = "after-smoke" },
                new PgStep { Do = "measure", Name = "stop" }
            }
        };
    }
}
