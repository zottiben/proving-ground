using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using ProvingGround.Contracts;
using ProvingGround.Judgment;

namespace ProvingGround.Verification
{
    /// <summary>
    /// Diffs the live UI against the manifest, as one set, in one pass.
    ///
    /// Whole-set diffing is the point. Checking properties one at a time lets an agent
    /// declare victory after the first three pass; producing the entire set of
    /// disagreements at once makes "it matches the design" a single verifiable claim.
    /// </summary>
    public static class PgUiConformance
    {
        /// <summary>Colour distance beyond which two colours are treated as different.</summary>
        public const float ColorTolerance = 0.01f;

        /// <summary>Pixel tolerance for size and position comparisons.</summary>
        public const float SizeTolerance = 1.5f;

        public static PgReport Check(PgUiManifest manifest = null, IReadOnlyList<PgUiFacts> facts = null)
        {
            var report = new PgReport("ui");
            manifest ??= PgUiManifest.Load();
            facts ??= PgUi.Collect();

            report.Datum("elementsOnScreen", facts.Count);
            report.Datum("collectors", string.Join(",", PgUi.All.Select(c => c.Name)));

            if (manifest == null)
            {
                report.Add(PgFinding
                    .Info("ui.noManifest", "No UI manifest exists, so only the global accessibility rules were applied")
                    .Fix($"Write {PgUiManifest.DefaultPath} to start holding the UI to the design."));

                report.AddRange(PgAccessibility.Check(facts, new PgUiGlobalRules()));
                return report;
            }

            report.Datum("elementsSpecified", manifest.Elements?.Count ?? 0);

            foreach (var entry in manifest.Elements ?? new Dictionary<string, PgUiElementSpec>())
            {
                var id = entry.Key;
                var spec = entry.Value;
                if (spec == null) continue;

                var match = Match(facts, spec.Match ?? id);

                if (match == null)
                {
                    if (spec.Required)
                        report.Add(PgFinding
                            .Fail($"ui.missing.{id}", $"'{id}' is required by the manifest but is not on screen")
                            .Expected(spec.Match ?? id)
                            .Fix("Either the element is not built, is inactive, or the manifest's match string is wrong."));
                    continue;
                }

                if (!match.Active && spec.Required)
                {
                    report.Add(PgFinding
                        .Fail($"ui.inactive.{id}", $"'{id}' exists but is not active")
                        .At(match.Path));
                    continue;
                }

                foreach (var expectation in spec.Expect ?? new Dictionary<string, string>())
                    Compare(report, manifest, id, match, expectation.Key, expectation.Value);

                if (!string.IsNullOrEmpty(spec.ContrastAgainst))
                    CheckContrastPair(report, manifest, id, match, facts, spec.ContrastAgainst);
            }

            report.AddRange(PgAccessibility.Check(facts, manifest.Global ?? new PgUiGlobalRules()));
            return report;
        }

        /// <summary>
        /// Matches by exact path, then path suffix, then name. Suffix matching means
        /// re-parenting a panel does not invalidate the whole manifest.
        /// </summary>
        static PgUiFacts Match(IReadOnlyList<PgUiFacts> facts, string selector)
        {
            if (string.IsNullOrEmpty(selector)) return null;

            return facts.FirstOrDefault(f => f.Path == selector)
                   ?? facts.FirstOrDefault(f => f.Path != null &&
                                                f.Path.EndsWith("/" + selector, StringComparison.Ordinal))
                   ?? facts.FirstOrDefault(f => f.Name == selector);
        }

        static void Compare(PgReport report, PgUiManifest manifest, string id, PgUiFacts actual,
            string property, string expectedRaw)
        {
            var findingId = $"ui.{id}.{property}";

            if (IsColorProperty(property))
            {
                if (!manifest.TryResolveColor(expectedRaw, out var expectedColor))
                {
                    report.Add(PgFinding
                        .Warn(findingId, $"'{expectedRaw}' is not a colour or a known token")
                        .At(actual.Path));
                    return;
                }

                var actualHex = actual.StringProperty(property);
                if (actualHex == null || !PgColor.TryParse(actualHex, out var actualColor))
                {
                    report.Add(PgFinding
                        .Fail(findingId, $"{id}.{property} could not be read from the live UI")
                        .At(actual.Path));
                    return;
                }

                if (PgColor.Distance(expectedColor, actualColor) > ColorTolerance)
                    report.Add(PgFinding
                        .Fail(findingId, $"{id}.{property} does not match the design")
                        .At(actual.Path)
                        .With($"{manifest.Resolve(expectedRaw)} ({expectedRaw})", actualHex));

                return;
            }

            var actualNumber = actual.NumericProperty(property);
            if (actualNumber.HasValue && manifest.TryResolveNumber(expectedRaw, out var expectedNumber))
            {
                if (Math.Abs(actualNumber.Value - expectedNumber) > SizeTolerance)
                    report.Add(PgFinding
                        .Fail(findingId, $"{id}.{property} does not match the design")
                        .At(actual.Path)
                        .With(expectedNumber.ToString("0.##", CultureInfo.InvariantCulture),
                            actualNumber.Value.ToString("0.##", CultureInfo.InvariantCulture)));
                return;
            }

            var actualString = actual.StringProperty(property);
            if (actualString == null)
            {
                report.Add(PgFinding
                    .Warn(findingId, $"'{property}' is not a property Proving Ground can read")
                    .At(actual.Path)
                    .Fix("Supported: color, backgroundColor, text, active, width, height, x, y, fontSize, opacity."));
                return;
            }

            var expectedString = manifest.Resolve(expectedRaw);
            if (!string.Equals(actualString, expectedString, StringComparison.Ordinal))
                report.Add(PgFinding
                    .Fail(findingId, $"{id}.{property} does not match the design")
                    .At(actual.Path)
                    .With(expectedString, actualString));
        }

        static void CheckContrastPair(PgReport report, PgUiManifest manifest, string id, PgUiFacts foreground,
            IReadOnlyList<PgUiFacts> facts, string backgroundId)
        {
            var background = Match(facts, backgroundId);
            if (background?.BackgroundColor == null) return;
            if (foreground.Color == null) return;
            if (!PgColor.TryParse(foreground.Color, out var fg) ||
                !PgColor.TryParse(background.BackgroundColor, out var bg)) return;

            var composited = PgColor.Composite(fg, bg);
            var ratio = PgColor.ContrastRatio(composited, bg);
            var rules = manifest.Global ?? new PgUiGlobalRules();
            var required = (foreground.FontSize ?? 16f) >= 24f
                ? rules.MinContrastRatioLargeText
                : rules.MinContrastRatio;

            if (ratio < required)
                report.Add(PgFinding
                    .Fail($"ui.contrast.{id}", $"'{id}' text is not legible against '{backgroundId}'")
                    .At(foreground.Path)
                    .With($"≥ {required:0.0}:1", $"{ratio:0.00}:1")
                    .Fix("Darken the background or lighten the text. WCAG AA is the floor the accessibility guidelines defer to."));
        }

        static bool IsColorProperty(string property) =>
            property.Equals("color", StringComparison.OrdinalIgnoreCase) ||
            property.Equals("backgroundColor", StringComparison.OrdinalIgnoreCase);
    }

    static class PgFindingExtensions
    {
        /// <summary>Sets only the expected side, for findings where there is no measured value.</summary>
        public static PgFinding Expected(this PgFinding finding, string expected)
        {
            finding.Expected = expected;
            return finding;
        }
    }
}
