using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProvingGround.Contracts
{
    /// <summary>
    /// Measured feel constants from well-regarded games, per genre, so that an agent asked
    /// to "make it feel better" has a number to move toward instead of a vibe it cannot
    /// perceive.
    ///
    /// These are starting points, not laws. Every value carries its provenance in the
    /// note field; where a figure is a community measurement rather than a shipped
    /// constant it is described as a range. A project can override the whole table by
    /// dropping a genre-norms.json into ProvingGround/Baselines.
    /// </summary>
    public static class PgGenreNorms
    {
        public const string FileName = "genre-norms.json";

        public static string OverridePath => Path.Combine(PgPaths.Baselines, FileName);

        static Dictionary<string, Dictionary<string, PgMetricSpec>> _cache;

        public static IReadOnlyList<string> Genres => Built().Keys.ToList();

        /// <summary>Norms for a genre, or an empty set when the genre is unknown.</summary>
        public static Dictionary<string, PgMetricSpec> For(string genre)
        {
            if (string.IsNullOrEmpty(genre)) return new Dictionary<string, PgMetricSpec>();
            var table = Built();
            return table.TryGetValue(genre.Trim().ToLowerInvariant(), out var norms)
                ? new Dictionary<string, PgMetricSpec>(norms)
                : new Dictionary<string, PgMetricSpec>();
        }

        /// <summary>
        /// Compares measurements against genre norms without failing anything. Produces
        /// informational findings only: being outside a norm is a question worth asking,
        /// not a defect.
        /// </summary>
        public static List<PgFinding> Compare(string genre, IReadOnlyDictionary<string, double> measured)
        {
            var findings = new List<PgFinding>();
            if (measured == null) return findings;

            foreach (var pair in For(genre))
            {
                if (!measured.TryGetValue(pair.Key, out var value)) continue;
                var violation = pair.Value.Violation(value);
                if (violation == null) continue;

                findings.Add(new PgFinding
                {
                    Id = "norms." + pair.Key,
                    Severity = PgSeverity.Info,
                    Message = $"{pair.Key} sits outside the {genre} norm ({violation})",
                    Expected = pair.Value.Describe(),
                    Actual = value.ToString("0.###"),
                    Remedy = pair.Value.Note
                });
            }

            return findings;
        }

        public static void InvalidateCache() => _cache = null;

        static Dictionary<string, Dictionary<string, PgMetricSpec>> Built()
        {
            if (_cache != null) return _cache;

            _cache = Defaults();

            var overrides = PgJson.Read<Dictionary<string, Dictionary<string, PgMetricSpec>>>(OverridePath);
            if (overrides != null)
            {
                foreach (var genre in overrides)
                    _cache[genre.Key.ToLowerInvariant()] = genre.Value;
            }

            return _cache;
        }

        static Dictionary<string, Dictionary<string, PgMetricSpec>> Defaults()
        {
            // Shared across anything the player directly drives.
            Dictionary<string, PgMetricSpec> Responsiveness() => new Dictionary<string, PgMetricSpec>
            {
                ["input.moveLatency"] = PgMetricSpec.AtMost(3, "frames",
                    "Frames between an input arriving and the character's velocity changing. Human visual processing already lags reality by roughly 13ms; anything the engine adds is felt as sluggishness."),
                ["input.bufferWindow"] = PgMetricSpec.Range(0.05, 0.15, "s",
                    "Window in which an early press is remembered and fired when it becomes legal. Celeste uses 5 frames (~0.083s at 60fps); 0.05-0.15s is the range that stays invisible while absorbing human timing error."),
                ["perf.frameTimeP95"] = PgMetricSpec.AtMost(16.6, "ms",
                    "95th percentile frame time. Averages hide the spikes players actually feel; gate on the tail.")
            };

            var fps = Responsiveness();
            fps["locomotion.moveSpeed"] = PgMetricSpec.Range(5.0, 8.0, "m/s",
                "Ground speed. Counter-Strike runs ~250 units/s (~6.35 m/s); arena shooters sit well above this band deliberately.");
            fps["locomotion.accelTime"] = PgMetricSpec.AtMost(0.12, "s",
                "Time from standstill to full speed. Shooters bias toward near-instant; long ramps read as ice.");
            fps["locomotion.stopTime"] = PgMetricSpec.AtMost(0.12, "s", "Time from full speed to rest.");
            fps["jump.apexHeight"] = PgMetricSpec.Range(0.9, 1.4, "m", "Roughly waist-to-chest height on a human-scaled character.");
            fps["jump.timeToApex"] = PgMetricSpec.Range(0.28, 0.45, "s");
            fps["jump.coyoteTime"] = PgMetricSpec.Range(0.0, 0.1, "s",
                "Grace period after leaving a ledge. Competitive shooters often set this to zero on purpose; single-player leans forgiving.");
            fps["camera.turnRate"] = PgMetricSpec.Range(140, 500, "deg/s",
                "Controller turn rate at full stick. Mouse aim is unbounded and is not covered by this metric.");
            fps["combat.ttk"] = PgMetricSpec.Range(0.25, 1.2, "s",
                "Time to kill a baseline enemy at effective range. Call of Duty sits at the fast end; Halo Infinite's measured rifle TTK runs 0.6-1.1s. Pick a point and hold it across the arsenal.");
            fps["combat.hitstop"] = PgMetricSpec.Range(0.0, 0.08, "s", "Frame freeze on impact. Shooters use little to none; melee games use much more.");

            var tps = new Dictionary<string, PgMetricSpec>(fps);
            tps["locomotion.moveSpeed"] = PgMetricSpec.Range(3.5, 6.5, "m/s",
                "Slower than first person: the player is watching a character animate, and mismatched speed reads as skating.");
            tps["camera.followLag"] = PgMetricSpec.Range(0.05, 0.2, "s",
                "Time for the camera to settle after the character moves. Zero is rigid and nauseating; too much loses the target.");
            tps["camera.collisionRecovery"] = PgMetricSpec.AtMost(0.3, "s",
                "Time for the camera to return to its rest distance after an obstruction clears.");
            tps["combat.hitstop"] = PgMetricSpec.Range(0.03, 0.15, "s",
                "Third-person melee reads impact largely through hitstop; this is the single highest-leverage juice constant.");

            var platformer = Responsiveness();
            platformer["jump.apexHeight"] = PgMetricSpec.Range(2.0, 4.0, "units",
                "Expressed in tiles or character heights rather than metres; platformers are authored on a grid.");
            platformer["jump.timeToApex"] = PgMetricSpec.Range(0.25, 0.45, "s",
                "Celeste's variable-jump window is 0.2s of held-jump boost on a ground jump.");
            platformer["jump.coyoteTime"] = PgMetricSpec.Range(0.05, 0.15, "s",
                "Celeste allows 5 coyote frames (~0.083s at 60fps). Below 0.05s players report the game as unfair without being able to say why.");
            platformer["jump.fallMultiplier"] = PgMetricSpec.Range(1.4, 2.5, "x",
                "Gravity multiplier applied while descending. A symmetric arc is the classic floaty-jump mistake.");
            platformer["input.bufferWindow"] = PgMetricSpec.Range(0.066, 0.15, "s",
                "Celeste buffers an impossible input for 5 frames and fires it the moment it becomes legal.");

            var actionRpg = Responsiveness();
            actionRpg["locomotion.moveSpeed"] = PgMetricSpec.Range(3.0, 6.0, "m/s");
            actionRpg["combat.ttk"] = PgMetricSpec.Range(2.0, 12.0, "s",
                "Trash mobs at the low end, elites at the high end. A boss belongs in its own metric, not this one.");
            actionRpg["combat.hitstop"] = PgMetricSpec.Range(0.03, 0.15, "s");
            actionRpg["combat.dodgeIFrames"] = PgMetricSpec.Range(0.2, 0.5, "s",
                "Invulnerability inside a dodge. Below 0.2s the dodge stops being a defensive option and becomes decoration.");
            actionRpg["combat.attackCommit"] = PgMetricSpec.Range(0.2, 0.8, "s",
                "How long an attack locks out other actions. This constant, more than damage, decides whether combat feels weighty or unresponsive.");

            var topDown = Responsiveness();
            topDown["locomotion.moveSpeed"] = PgMetricSpec.Range(3.0, 8.0, "m/s");
            topDown["locomotion.accelTime"] = PgMetricSpec.AtMost(0.1, "s");
            topDown["combat.ttk"] = PgMetricSpec.Range(0.3, 3.0, "s");

            return new Dictionary<string, Dictionary<string, PgMetricSpec>>
            {
                ["fps"] = fps,
                ["tps"] = tps,
                ["platformer"] = platformer,
                ["actionrpg"] = actionRpg,
                ["topdown"] = topDown
            };
        }
    }
}
