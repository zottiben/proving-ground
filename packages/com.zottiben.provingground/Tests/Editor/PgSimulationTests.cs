using System.Collections.Generic;
using NUnit.Framework;
using ProvingGround.Verification;

namespace ProvingGround.Tests
{
    public class PgBalanceSimTests
    {
        static PgCombatant Fighter(string name, double health, double damage, double interval) =>
            new PgCombatant { Name = name, Health = health, Damage = damage, AttackInterval = interval };

        [Test]
        public void ADeterministicDuelMatchesTheHandCalculatedTimeToKill()
        {
            // 100hp, 10 damage a second, no variance: ten swings, and the tenth lands at t=10.
            var attacker = Fighter("a", 1000, 10, 1.0);
            var defender = Fighter("b", 100, 0, 1.0);

            var result = PgBalanceSim.Duel(attacker, defender, 100);

            Assert.AreEqual(1.0, result.AttackerWinRate, 0.001);
            Assert.AreEqual(10.0, result.TimeToKill.Mean, 0.001);
        }

        [Test]
        public void TheSameSeedProducesTheSameResult()
        {
            var attacker = Fighter("a", 100, 12, 0.8);
            attacker.DamageVariance = 0.4;
            attacker.CritChance = 0.2;
            var defender = Fighter("b", 100, 9, 1.0);

            var first = PgBalanceSim.Duel(attacker, defender, 500, 999);
            var second = PgBalanceSim.Duel(attacker, defender, 500, 999);

            Assert.AreEqual(first.AttackerWinRate, second.AttackerWinRate);
            Assert.AreEqual(first.TimeToKill.Mean, second.TimeToKill.Mean);
        }

        [Test]
        public void ArmorReducesDamageAndCanNeutraliseAWeakAttack()
        {
            var attacker = Fighter("weak", 1000, 5, 1.0);
            var defender = Fighter("armoured", 100, 0, 1.0);
            defender.Armor = 5;

            var result = PgBalanceSim.Duel(attacker, defender, 20, 1, 30);

            // Every swing is fully absorbed, so the defender never dies.
            Assert.AreEqual(0.0, result.AttackerWinRate, 0.001);
        }

        [Test]
        public void TheBalanceMatrixFlagsLoadoutsOutsideTheIntendedWindow()
        {
            var target = Fighter("grunt", 100, 0, 1.0);
            var loadouts = new List<PgCombatant>
            {
                Fighter("pistol", 1000, 10, 1.0),   // 10s, far too slow
                Fighter("rifle", 1000, 100, 1.0)    // 1s, inside the window
            };

            var report = PgBalanceSim.Matrix(loadouts, target, 0.5, 2.0, 50);

            Assert.IsFalse(report.Passed);
            CollectionAssert.Contains(
                report.Findings.ConvertAll(f => f.Id),
                "balance.tooSlow.pistol");
        }

        [Test]
        public void DistributionPercentilesAreOrdered()
        {
            var values = new List<double>();
            for (var i = 1; i <= 100; i++) values.Add(i);

            var distribution = PgDistribution.Of(values);

            Assert.AreEqual(100, distribution.Samples);
            Assert.AreEqual(1, distribution.Min);
            Assert.AreEqual(100, distribution.Max);
            Assert.LessOrEqual(distribution.P05, distribution.P50);
            Assert.LessOrEqual(distribution.P50, distribution.P95);
            Assert.AreEqual(50.5, distribution.Mean, 0.001);
        }
    }

    public class PgEconomySimTests
    {
        [Test]
        public void AnEconomyThatSpendsMoreThanItEarnsGoesBankrupt()
        {
            var report = PgBalanceSim.Economy(100, 5, 10, 100, 0, 0);

            Assert.IsFalse(report.Passed);
            CollectionAssert.Contains(report.Findings.ConvertAll(f => f.Id), "economy.bankrupt");
        }

        [Test]
        public void CompoundingIncomeIsCaughtAsRunaway()
        {
            var report = PgBalanceSim.Economy(100, 10, 5, 200, 0.10, 0, 1_000_000);

            Assert.IsFalse(report.Passed);
            CollectionAssert.Contains(report.Findings.ConvertAll(f => f.Id), "economy.runaway");
        }

        [Test]
        public void ABalancedEconomyPasses()
        {
            var report = PgBalanceSim.Economy(100, 10, 10, 500, 0, 0, 1000);
            Assert.IsTrue(report.Passed, report.ToConsole());
        }
    }
}
