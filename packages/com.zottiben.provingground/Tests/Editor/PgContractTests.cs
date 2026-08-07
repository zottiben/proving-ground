using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ProvingGround.Contracts;
using ProvingGround.EditorTools;
using ProvingGround.Judgment;

namespace ProvingGround.Tests
{
    public class PgMetricSpecTests
    {
        [Test]
        public void TargetWithToleranceAcceptsValuesInsideTheBand()
        {
            var spec = PgMetricSpec.Of(1.15, 0.05, "m");
            Assert.IsNull(spec.Violation(1.15));
            Assert.IsNull(spec.Violation(1.19));
            Assert.IsNull(spec.Violation(1.11));
        }

        [Test]
        public void TargetWithToleranceRejectsValuesOutsideTheBand()
        {
            var spec = PgMetricSpec.Of(1.15, 0.05, "m");
            StringAssert.Contains("high", spec.Violation(1.30));
            StringAssert.Contains("low", spec.Violation(1.00));
        }

        [Test]
        public void RangeRejectsBelowMinimumAndAboveMaximum()
        {
            var spec = PgMetricSpec.Range(0.05, 0.15, "s");
            Assert.IsNull(spec.Violation(0.1));
            StringAssert.Contains("below minimum", spec.Violation(0.01));
            StringAssert.Contains("above maximum", spec.Violation(0.9));
        }

        [Test]
        public void EvaluateReturnsNullWhenConformingAndAFindingWhenNot()
        {
            var spec = PgMetricSpec.AtMost(3, "frames");
            Assert.IsNull(spec.Evaluate("input.moveLatency", 2));

            var finding = spec.Evaluate("input.moveLatency", 9);
            Assert.IsNotNull(finding);
            Assert.AreEqual(PgSeverity.Fail, finding.Severity);
            Assert.AreEqual("input.moveLatency", finding.Id);
        }

        [Test]
        public void AnEmptySpecNeverProducesAFinding()
        {
            var spec = new PgMetricSpec();
            Assert.IsTrue(spec.IsEmpty);
            Assert.IsNull(spec.Evaluate("anything", 12345));
        }
    }

    public class PgFeelSpecTests
    {
        static PgFeelSpec TwoMetricSpec() => new PgFeelSpec
        {
            Metrics = new Dictionary<string, PgMetricSpec>
            {
                ["jump.apexHeight"] = PgMetricSpec.Of(1.2, 0.1, "m"),
                ["input.moveLatency"] = PgMetricSpec.AtMost(3, "frames")
            }
        };

        [Test]
        public void ConformingMeasurementsProduceNoFindings()
        {
            var findings = TwoMetricSpec().Diff(new Dictionary<string, double>
            {
                ["jump.apexHeight"] = 1.22,
                ["input.moveLatency"] = 2
            });

            CollectionAssert.IsEmpty(findings);
        }

        [Test]
        public void EveryDeviationIsReportedInOnePassRatherThanTheFirstOnly()
        {
            var findings = TwoMetricSpec().Diff(new Dictionary<string, double>
            {
                ["jump.apexHeight"] = 2.5,
                ["input.moveLatency"] = 11
            });

            Assert.AreEqual(2, findings.Count);
        }

        [Test]
        public void AMetricThatWasNotMeasuredIsReportedRatherThanSilentlyPassing()
        {
            var findings = TwoMetricSpec().Diff(new Dictionary<string, double>
            {
                ["jump.apexHeight"] = 1.2
            });

            Assert.AreEqual(1, findings.Count);
            StringAssert.Contains("not measured", findings[0].Message);
        }
    }

    public class PgQualityGatesTests
    {
        [Test]
        public void SuppressedFindingsAreDowngradedAndKeptRatherThanDeleted()
        {
            var gates = new PgQualityGates();
            gates.Suppress["a11y.contrast"] = "reviewed, the brand colour is fixed";

            var report = new PgReport("test");
            report.Add(PgFinding.Fail("a11y.contrast", "low contrast"));
            report.Add(PgFinding.Fail("a11y.hitTarget", "too small"));

            var passed = gates.Evaluate(report);

            Assert.IsFalse(passed, "the unsuppressed failure should still fail the gate");
            Assert.AreEqual(2, report.Findings.Count, "suppression must not hide the finding");
            Assert.AreEqual(PgSeverity.Info, report.Findings[0].Severity);
            StringAssert.Contains("suppressed", report.Findings[0].Message);
        }

        [Test]
        public void AReportWithOnlyWarningsPasses()
        {
            var report = new PgReport("test");
            report.Add(PgFinding.Warn("x", "a warning"));
            Assert.IsTrue(new PgQualityGates().Evaluate(report));
            Assert.IsTrue(report.Passed);
        }
    }

    public class PgGlobTests
    {
        [Test]
        public void SingleStarDoesNotCrossDirectories()
        {
            Assert.IsTrue(PgGlob.Matches("Assets/UI/button.png", "Assets/UI/*.png"));
            Assert.IsFalse(PgGlob.Matches("Assets/UI/icons/button.png", "Assets/UI/*.png"));
        }

        [Test]
        public void DoubleStarCrossesDirectoriesAndAlsoMatchesNone()
        {
            Assert.IsTrue(PgGlob.Matches("Assets/Art/UI/button.png", "Assets/**/UI/*.png"));
            Assert.IsTrue(PgGlob.Matches("Assets/UI/button.png", "Assets/**/UI/*.png"));
            Assert.IsTrue(PgGlob.Matches("Assets/a/b/c/UI/button.png", "Assets/**/UI/*.png"));
        }

        [Test]
        public void NonMatchingPathsAreRejected()
        {
            Assert.IsFalse(PgGlob.Matches("Assets/Art/button.jpg", "Assets/**/UI/*.png"));
            Assert.IsFalse(PgGlob.Matches("", "Assets/*"));
        }
    }

    public class PgColorTests
    {
        [Test]
        public void BlackOnWhiteIsTheMaximumWcagRatio()
        {
            var ratio = PgColor.ContrastRatio(Color.black, Color.white);
            Assert.AreEqual(21f, ratio, 0.05f);
        }

        [Test]
        public void AColourAgainstItselfHasNoContrast()
        {
            Assert.AreEqual(1f, PgColor.ContrastRatio(Color.red, Color.red), 0.001f);
        }

        [Test]
        public void CompositingATranslucentForegroundMovesItTowardTheBackground()
        {
            var half = new Color(1f, 1f, 1f, 0.5f);
            var composited = PgColor.Composite(half, Color.black);

            Assert.AreEqual(0.5f, composited.r, 0.001f);
            Assert.AreEqual(1f, composited.a, 0.001f);
        }

        [Test]
        public void ContrastIsSymmetric()
        {
            var a = new Color(0.2f, 0.4f, 0.9f);
            var b = new Color(0.95f, 0.95f, 0.9f);
            Assert.AreEqual(PgColor.ContrastRatio(a, b), PgColor.ContrastRatio(b, a), 0.0001f);
        }
    }

    public class PgReportTests
    {
        [Test]
        public void ReportsSurviveAJsonRoundTripWithSeveritiesIntact()
        {
            var original = new PgReport("roundtrip");
            original.Add(PgFinding.Fail("x.y", "something broke").With("1", "2").Fix("do the thing"));
            original.Datum("count", 3);

            var restored = PgJson.Parse<PgReport>(PgJson.Stringify(original));

            Assert.AreEqual("roundtrip", restored.Tool);
            Assert.AreEqual(1, restored.Findings.Count);
            Assert.AreEqual(PgSeverity.Fail, restored.Findings[0].Severity);
            Assert.AreEqual("do the thing", restored.Findings[0].Remedy);
            Assert.IsFalse(restored.Passed);
        }

        [Test]
        public void AFailedOperationIsDistinctFromAnOperationThatFoundFailures()
        {
            var couldNotRun = new PgReport("a").Failed("no camera");
            Assert.IsFalse(couldNotRun.Ok);
            Assert.IsFalse(couldNotRun.Passed);

            var ranAndFoundNothing = new PgReport("b");
            Assert.IsTrue(ranAndFoundNothing.Ok);
            Assert.IsTrue(ranAndFoundNothing.Passed);
        }
    }
}
