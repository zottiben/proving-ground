using System;
using System.Globalization;
using Newtonsoft.Json;

namespace ProvingGround.Contracts
{
    /// <summary>
    /// One numeric expectation. Either a <see cref="Target"/> with a <see cref="Tolerance"/>,
    /// or a <see cref="Min"/>/<see cref="Max"/> range, or both. This is the unit that turns
    /// "make it feel snappier" into something a machine can fail.
    /// </summary>
    [Serializable]
    public sealed class PgMetricSpec
    {
        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
        public double? Target;

        /// <summary>Absolute tolerance around <see cref="Target"/>. Ignored when Target is null.</summary>
        [JsonProperty("tolerance", NullValueHandling = NullValueHandling.Ignore)]
        public double? Tolerance;

        [JsonProperty("min", NullValueHandling = NullValueHandling.Ignore)]
        public double? Min;

        [JsonProperty("max", NullValueHandling = NullValueHandling.Ignore)]
        public double? Max;

        [JsonProperty("unit", NullValueHandling = NullValueHandling.Ignore)]
        public string Unit;

        /// <summary>Why this number is what it is. Preserved so the reasoning survives the next agent.</summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>Severity raised when the measurement falls outside the spec.</summary>
        [JsonProperty("severity", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public PgSeverity Severity = PgSeverity.Fail;

        [JsonIgnore] public bool IsEmpty => !Target.HasValue && !Min.HasValue && !Max.HasValue;

        /// <summary>Human description of what would satisfy this spec.</summary>
        public string Describe()
        {
            var unit = string.IsNullOrEmpty(Unit) ? "" : " " + Unit;
            if (Target.HasValue)
            {
                var tolerance = Tolerance ?? 0d;
                return tolerance > 0
                    ? $"{Fmt(Target.Value)} ± {Fmt(tolerance)}{unit}"
                    : $"{Fmt(Target.Value)}{unit}";
            }

            if (Min.HasValue && Max.HasValue) return $"{Fmt(Min.Value)}..{Fmt(Max.Value)}{unit}";
            if (Min.HasValue) return $"≥ {Fmt(Min.Value)}{unit}";
            if (Max.HasValue) return $"≤ {Fmt(Max.Value)}{unit}";
            return "unconstrained";
        }

        /// <summary>Returns null when the value satisfies the spec, otherwise why it does not.</summary>
        public string Violation(double value)
        {
            if (Target.HasValue)
            {
                var tolerance = Tolerance ?? 0d;
                var delta = Math.Abs(value - Target.Value);
                if (delta > tolerance)
                {
                    var direction = value > Target.Value ? "high" : "low";
                    return $"{Fmt(delta)} too {direction}";
                }
            }

            if (Min.HasValue && value < Min.Value) return $"below minimum by {Fmt(Min.Value - value)}";
            if (Max.HasValue && value > Max.Value) return $"above maximum by {Fmt(value - Max.Value)}";
            return null;
        }

        /// <summary>Produces a finding for <paramref name="value"/>, or null when it conforms.</summary>
        public PgFinding Evaluate(string id, double value, string subject = null)
        {
            if (IsEmpty) return null;
            var violation = Violation(value);
            if (violation == null) return null;

            var unit = string.IsNullOrEmpty(Unit) ? "" : " " + Unit;
            return new PgFinding
            {
                Id = id,
                Severity = Severity,
                Message = $"{id} is {violation}",
                Subject = subject,
                Expected = Describe(),
                Actual = Fmt(value) + unit,
                Remedy = Note
            };
        }

        static string Fmt(double value) =>
            Math.Abs(value - Math.Round(value)) < 0.0005
                ? value.ToString("0.###", CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);

        public static PgMetricSpec Range(double min, double max, string unit = null, string note = null) =>
            new PgMetricSpec { Min = min, Max = max, Unit = unit, Note = note };

        public static PgMetricSpec Of(double target, double tolerance, string unit = null, string note = null) =>
            new PgMetricSpec { Target = target, Tolerance = tolerance, Unit = unit, Note = note };

        public static PgMetricSpec AtMost(double max, string unit = null, string note = null) =>
            new PgMetricSpec { Max = max, Unit = unit, Note = note };

        public static PgMetricSpec AtLeast(double min, string unit = null, string note = null) =>
            new PgMetricSpec { Min = min, Unit = unit, Note = note };
    }
}
