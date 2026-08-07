using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ProvingGround.Contracts
{
    /// <summary>
    /// One named audio event. The key is shared by the code that fires it, the asset that
    /// answers it and the test that checks it, which is what makes the wiring verifiable
    /// even though the sound itself is not.
    /// </summary>
    [Serializable]
    public sealed class PgAudioEventSpec
    {
        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
        public string Category;

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>The event must have a clip bound and must fire at least once in a probe run.</summary>
        [JsonProperty("required")] public bool Required = true;

        /// <summary>Rate ceiling. Catches the classic footstep-per-frame bug.</summary>
        [JsonProperty("maxPerSecond", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxPerSecond;

        [JsonProperty("maxLengthSeconds", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxLengthSeconds;

        [JsonProperty("minLengthSeconds", NullValueHandling = NullValueHandling.Ignore)]
        public double? MinLengthSeconds;

        /// <summary>
        /// Acceptable RMS level window in dBFS, e.g. -24 to -12.
        ///
        /// This is RMS, not BS.1770 integrated loudness. LUFS requires K-weighting that
        /// this package does not implement, and reporting an unweighted measurement under
        /// the LUFS name would be wrong in a way nobody would catch. RMS is enough to hold
        /// a set of one-shots to a consistent level, which is what the check is for.
        /// </summary>
        [JsonProperty("loudnessRmsDbfs", NullValueHandling = NullValueHandling.Ignore)]
        public PgMetricSpec LoudnessRmsDbfs;

        /// <summary>Sample peak ceiling in dBFS. -1.0 leaves headroom for codec overshoot.</summary>
        [JsonProperty("maxPeakDbfs", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxPeakDbfs = -1.0;

        /// <summary>Clip must loop cleanly: start and end samples must match within a threshold.</summary>
        [JsonProperty("mustLoop")] public bool MustLoop;

        /// <summary>Reject leading silence beyond this, which is heard as latency on a one-shot.</summary>
        [JsonProperty("maxLeadingSilenceSeconds", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxLeadingSilenceSeconds = 0.02;
    }

    /// <summary>
    /// The audio event registry as data. Generation of the sound itself is the easy half;
    /// this contract exists because the hard half is wiring, and wiring is checkable.
    /// </summary>
    [Serializable]
    public sealed class PgAudioContract
    {
        public const string FileName = "audio.json";

        [JsonProperty("schema")] public string Schema = "provingground/audio@1";

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        [JsonProperty("events")] public Dictionary<string, PgAudioEventSpec> Events =
            new Dictionary<string, PgAudioEventSpec>();

        /// <summary>Fail when an event is bound to a clip but never fires during probe runs.</summary>
        [JsonProperty("forbidDeadEvents")] public bool ForbidDeadEvents = true;

        /// <summary>Fail when code fires an event id that the contract does not declare.</summary>
        [JsonProperty("forbidUndeclaredEvents")] public bool ForbidUndeclaredEvents = true;

        public static string DefaultPath => Path.Combine(PgPaths.Contracts, FileName);

        public static PgAudioContract Load(string path = null) =>
            PgJson.Read(path ?? DefaultPath, (PgAudioContract)null);

        public void Save(string path = null) => PgJson.Write(path ?? DefaultPath, this);

        public PgAudioEventSpec Get(string id) =>
            Events != null && Events.TryGetValue(id, out var spec) ? spec : null;
    }
}
