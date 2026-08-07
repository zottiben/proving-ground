using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ProvingGround.Verification
{
    /// <summary>A combatant reduced to the numbers that decide a fight.</summary>
    [Serializable]
    public sealed class PgCombatant
    {
        [JsonProperty("name")] public string Name = "unnamed";
        [JsonProperty("health")] public double Health = 100;
        [JsonProperty("damage")] public double Damage = 10;

        /// <summary>Seconds between attacks.</summary>
        [JsonProperty("attackInterval")] public double AttackInterval = 1.0;

        [JsonProperty("accuracy")] public double Accuracy = 1.0;

        /// <summary>Flat reduction applied to incoming damage, after mitigation.</summary>
        [JsonProperty("armor")] public double Armor;

        /// <summary>Fractional reduction applied before armor, 0-1.</summary>
        [JsonProperty("mitigation")] public double Mitigation;

        [JsonProperty("critChance")] public double CritChance;
        [JsonProperty("critMultiplier")] public double CritMultiplier = 2.0;

        /// <summary>Damage variance as a fraction of <see cref="Damage"/>, 0-1.</summary>
        [JsonProperty("damageVariance")] public double DamageVariance;

        public PgCombatant Clone() => (PgCombatant)MemberwiseClone();
    }

    /// <summary>Distribution summary for one simulated quantity.</summary>
    [Serializable]
    public sealed class PgDistribution
    {
        [JsonProperty("samples")] public int Samples;
        [JsonProperty("mean")] public double Mean;
        [JsonProperty("min")] public double Min;
        [JsonProperty("max")] public double Max;
        [JsonProperty("p05")] public double P05;
        [JsonProperty("p50")] public double P50;
        [JsonProperty("p95")] public double P95;
        [JsonProperty("stdDev")] public double StdDev;

        public static PgDistribution Of(IReadOnlyList<double> values)
        {
            var distribution = new PgDistribution { Samples = values.Count };
            if (values.Count == 0) return distribution;

            var sorted = values.OrderBy(v => v).ToList();
            distribution.Mean = sorted.Average();
            distribution.Min = sorted[0];
            distribution.Max = sorted[sorted.Count - 1];
            distribution.P05 = Percentile(sorted, 0.05);
            distribution.P50 = Percentile(sorted, 0.50);
            distribution.P95 = Percentile(sorted, 0.95);

            var mean = distribution.Mean;
            distribution.StdDev = Math.Sqrt(sorted.Sum(v => (v - mean) * (v - mean)) / sorted.Count);
            return distribution;
        }

        static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            var index = Math.Min(Math.Max((int)Math.Ceiling(percentile * sorted.Count) - 1, 0), sorted.Count - 1);
            return sorted[index];
        }

        public override string ToString() =>
            $"mean {Mean:0.###}, p05 {P05:0.###}, p50 {P50:0.###}, p95 {P95:0.###}, sd {StdDev:0.###}";
    }

    /// <summary>Result of a simulated matchup.</summary>
    [Serializable]
    public sealed class PgDuelResult
    {
        [JsonProperty("attacker")] public string Attacker;
        [JsonProperty("defender")] public string Defender;
        [JsonProperty("iterations")] public int Iterations;
        [JsonProperty("attackerWinRate")] public double AttackerWinRate;
        [JsonProperty("timeToKill")] public PgDistribution TimeToKill;
        [JsonProperty("healthRemaining")] public PgDistribution WinnerHealthRemaining;
    }

    /// <summary>
    /// Headless combat and economy simulation.
    ///
    /// Balance is the one part of game design that genuinely can be settled by
    /// computation, and it is also the part that is most expensive to settle by playing.
    /// Ten thousand fights run here in less time than one fight takes in the editor, and
    /// the answer comes back as a distribution rather than an anecdote.
    /// </summary>
    public static class PgBalanceSim
    {
        /// <summary>Simulates <paramref name="iterations"/> fights and summarises them.</summary>
        public static PgDuelResult Duel(PgCombatant attacker, PgCombatant defender,
            int iterations = 10000, int seed = 12345, double timeoutSeconds = 300)
        {
            var random = new System.Random(seed);
            var timesToKill = new List<double>(iterations);
            var healthRemaining = new List<double>(iterations);
            var attackerWins = 0;

            for (var i = 0; i < iterations; i++)
            {
                var a = attacker.Clone();
                var d = defender.Clone();
                var aHealth = a.Health;
                var dHealth = d.Health;
                var aNext = a.AttackInterval;
                var dNext = d.AttackInterval;
                var time = 0.0;

                while (aHealth > 0 && dHealth > 0 && time < timeoutSeconds)
                {
                    // Advance to whichever combatant swings next.
                    var step = Math.Min(aNext, dNext);
                    time += step;
                    aNext -= step;
                    dNext -= step;

                    if (aNext <= 1e-9)
                    {
                        dHealth -= Swing(a, d, random);
                        aNext = a.AttackInterval;
                    }

                    if (dNext <= 1e-9 && dHealth > 0)
                    {
                        aHealth -= Swing(d, a, random);
                        dNext = d.AttackInterval;
                    }
                }

                var attackerWon = dHealth <= 0 && aHealth > 0;
                if (attackerWon) attackerWins++;

                timesToKill.Add(time);
                healthRemaining.Add(Math.Max(attackerWon ? aHealth : dHealth, 0));
            }

            return new PgDuelResult
            {
                Attacker = attacker.Name,
                Defender = defender.Name,
                Iterations = iterations,
                AttackerWinRate = (double)attackerWins / iterations,
                TimeToKill = PgDistribution.Of(timesToKill),
                WinnerHealthRemaining = PgDistribution.Of(healthRemaining)
            };
        }

        static double Swing(PgCombatant source, PgCombatant target, System.Random random)
        {
            if (random.NextDouble() > source.Accuracy) return 0;

            var damage = source.Damage;

            if (source.DamageVariance > 0)
                damage *= 1.0 + (random.NextDouble() * 2 - 1) * source.DamageVariance;

            if (source.CritChance > 0 && random.NextDouble() < source.CritChance)
                damage *= source.CritMultiplier;

            damage *= 1.0 - Math.Clamp(target.Mitigation, 0, 1);
            damage -= target.Armor;

            return Math.Max(damage, 0);
        }

        /// <summary>
        /// Runs every matchup and reports the ones that fall outside the intended window.
        /// This is where "one weapon is quietly the only correct choice" shows up.
        /// </summary>
        public static PgReport Matrix(IReadOnlyList<PgCombatant> loadouts, PgCombatant target,
            double minTtk, double maxTtk, int iterations = 5000, int seed = 12345)
        {
            var report = new PgReport("balance");
            var results = new List<PgDuelResult>();

            foreach (var loadout in loadouts)
            {
                var result = Duel(loadout, target, iterations, seed);
                results.Add(result);

                report.Datum($"ttk.{loadout.Name}", Math.Round(result.TimeToKill.Mean, 3));

                if (result.AttackerWinRate < 0.999)
                    report.Add(PgFinding
                        .Warn("balance.loses." + loadout.Name,
                            $"'{loadout.Name}' does not reliably beat '{target.Name}'")
                        .With("win rate ~100%", $"{result.AttackerWinRate:P1}"));

                if (result.TimeToKill.Mean < minTtk)
                    report.Add(PgFinding
                        .Fail("balance.tooFast." + loadout.Name,
                            $"'{loadout.Name}' kills faster than the intended window")
                        .With($"{minTtk:0.##}-{maxTtk:0.##}s", $"{result.TimeToKill.Mean:0.##}s"));
                else if (result.TimeToKill.Mean > maxTtk)
                    report.Add(PgFinding
                        .Fail("balance.tooSlow." + loadout.Name,
                            $"'{loadout.Name}' kills slower than the intended window")
                        .With($"{minTtk:0.##}-{maxTtk:0.##}s", $"{result.TimeToKill.Mean:0.##}s"));
            }

            if (results.Count > 1)
            {
                var fastest = results.OrderBy(r => r.TimeToKill.Mean).First();
                var slowest = results.OrderByDescending(r => r.TimeToKill.Mean).First();
                var spread = slowest.TimeToKill.Mean / Math.Max(fastest.TimeToKill.Mean, 1e-6);

                report.Datum("ttkSpread", Math.Round(spread, 2));

                if (spread > 2.0)
                    report.Add(PgFinding
                        .Warn("balance.spread",
                            $"'{fastest.Attacker}' kills {spread:0.#}x faster than '{slowest.Attacker}'")
                        .Fix("A spread this wide usually means only the fast option gets used."));
            }

            return report;
        }

        /// <summary>
        /// Projects an economy forward and reports when a currency runs away or stalls.
        /// </summary>
        public static PgReport Economy(double startingBalance, double incomePerStep, double costPerStep,
            int steps, double growthPerStep = 0, double minBalance = 0, double maxBalance = double.MaxValue)
        {
            var report = new PgReport("economy");
            var balance = startingBalance;
            var income = incomePerStep;
            var history = new List<double>(steps);

            for (var step = 0; step < steps; step++)
            {
                balance += income - costPerStep;
                income *= 1 + growthPerStep;
                history.Add(balance);

                if (balance < minBalance)
                {
                    report.Add(PgFinding
                        .Fail("economy.bankrupt", $"Balance fell below {minBalance} at step {step}")
                        .With($"≥ {minBalance}", balance.ToString("0.##")));
                    break;
                }

                if (balance > maxBalance)
                {
                    report.Add(PgFinding
                        .Fail("economy.runaway", $"Balance exceeded {maxBalance} at step {step}")
                        .With($"≤ {maxBalance}", balance.ToString("0.##"))
                        .Fix("Past this point the currency stops being a constraint on the player."));
                    break;
                }
            }

            report.Datum("finalBalance", Math.Round(history.Count > 0 ? history[history.Count - 1] : startingBalance, 2));
            report.Datum("distribution", PgDistribution.Of(history));

            if (report.Findings.Count == 0)
                report.Add(PgFinding.Info("economy.stable",
                    $"Balance stayed inside [{minBalance}, {maxBalance}] across {steps} steps"));

            return report;
        }
    }
}
