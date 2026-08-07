using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ProvingGround.Contracts;
using ProvingGround.Verification;

namespace ProvingGround.Judgment
{
    /// <summary>
    /// The part of "does it actually look good" that is not a matter of taste.
    ///
    /// Nothing here judges whether a design is attractive. It catches the defects that are
    /// objectively wrong at any aesthetic: text nobody can read, targets nobody can hit,
    /// labels that render as nothing, and interface pushed off the edge of a real device.
    /// The rules encode the mechanically checkable subset of the Game Accessibility
    /// Guidelines and the Xbox Accessibility Guidelines.
    /// </summary>
    public static class PgAccessibility
    {
        public static List<PgFinding> Check(IReadOnlyList<PgUiFacts> facts, PgUiGlobalRules rules)
        {
            var findings = new List<PgFinding>();
            if (facts == null) return findings;
            rules ??= new PgUiGlobalRules();

            var visible = facts.Where(f => f.Active && f.Rect != null &&
                                           f.ScreenRect.width > 0f && f.ScreenRect.height > 0f).ToList();

            CheckHitTargets(findings, visible, rules);
            CheckFontSizes(findings, visible, rules);
            CheckTextRendering(findings, visible, rules);
            CheckContrast(findings, visible, rules);

            if (rules.ForbidOverlappingInteractables) CheckOverlaps(findings, visible);
            if (rules.EnforceSafeArea) CheckSafeArea(findings, visible);

            return findings;
        }

        static void CheckHitTargets(List<PgFinding> findings, IReadOnlyList<PgUiFacts> facts, PgUiGlobalRules rules)
        {
            foreach (var element in facts.Where(f => f.Interactable))
            {
                var rect = element.ScreenRect;
                var smallest = Mathf.Min(rect.width, rect.height);
                if (smallest >= rules.MinHitTargetPx) continue;

                findings.Add(PgFinding
                    .Fail("a11y.hitTarget", $"'{element.Name}' is too small to hit reliably")
                    .At(element.Path)
                    .With($"≥ {rules.MinHitTargetPx}px", $"{rect.width:0}x{rect.height:0}px")
                    .Fix("Grow the element, or add padding to the touch area without changing the visual."));
            }
        }

        static void CheckFontSizes(List<PgFinding> findings, IReadOnlyList<PgUiFacts> facts, PgUiGlobalRules rules)
        {
            foreach (var element in facts)
            {
                if (string.IsNullOrEmpty(element.Text) || !element.FontSize.HasValue) continue;
                if (element.FontSize.Value >= rules.MinFontSizePx) continue;

                findings.Add(PgFinding
                    .Fail("a11y.fontSize", $"'{element.Name}' renders text below the legibility floor")
                    .At(element.Path)
                    .With($"≥ {rules.MinFontSizePx}px", $"{element.FontSize.Value:0.#}px")
                    .Fix("Small text is the most common accessibility complaint in shipped games."));
            }
        }

        static void CheckTextRendering(List<PgFinding> findings, IReadOnlyList<PgUiFacts> facts, PgUiGlobalRules rules)
        {
            foreach (var element in facts)
            {
                if (string.IsNullOrEmpty(element.Text)) continue;

                if (element.TextInvisible)
                    findings.Add(PgFinding
                        .Fail("a11y.textInvisible", $"'{element.Name}' has text but lays out zero visible characters")
                        .At(element.Path)
                        .With(Ellipsise(element.Text), "nothing rendered")
                        .Fix("The box is too small for even one glyph. This looks like an empty label, not an error."));

                else if (element.TextTruncated && rules.ForbidClippedText)
                    findings.Add(PgFinding
                        .Fail("a11y.textClipped", $"'{element.Name}' has text that does not fit its box")
                        .At(element.Path)
                        .With(Ellipsise(element.Text), $"clipped to {element.ScreenRect.width:0}x{element.ScreenRect.height:0}px")
                        .Fix("Widen the box, shrink the text, or enable wrapping. Translations run longer than English."));
            }
        }

        /// <summary>
        /// Checks every text element against whatever is actually behind it, which is
        /// found geometrically rather than declared. A label is only legible against the
        /// thing it is really sitting on.
        /// </summary>
        static void CheckContrast(List<PgFinding> findings, IReadOnlyList<PgUiFacts> facts, PgUiGlobalRules rules)
        {
            var backgrounds = facts
                .Where(f => f.BackgroundColor != null && PgColor.TryParse(f.BackgroundColor, out _))
                .ToList();

            foreach (var element in facts)
            {
                if (string.IsNullOrEmpty(element.Text) || element.Color == null) continue;
                if (!PgColor.TryParse(element.Color, out var textColor)) continue;
                if (textColor.a < 0.05f) continue;

                var behind = Behind(element, backgrounds);
                if (behind == null) continue;
                if (!PgColor.TryParse(behind.BackgroundColor, out var backgroundColor)) continue;
                if (backgroundColor.a < 0.05f) continue;

                var composited = PgColor.Composite(textColor, backgroundColor);
                var ratio = PgColor.ContrastRatio(composited, backgroundColor);
                var required = (element.FontSize ?? 16f) >= 24f
                    ? rules.MinContrastRatioLargeText
                    : rules.MinContrastRatio;

                if (ratio >= required) continue;

                findings.Add(PgFinding
                    .Fail("a11y.contrast", $"'{element.Name}' is hard to read against '{behind.Name}'")
                    .At(element.Path)
                    .With($"≥ {required:0.0}:1", $"{ratio:0.00}:1")
                    .Datum("textColor", element.Color)
                    .Datum("backgroundColor", behind.BackgroundColor));
            }
        }

        /// <summary>The smallest background element that fully contains this one.</summary>
        static PgUiFacts Behind(PgUiFacts element, IReadOnlyList<PgUiFacts> backgrounds)
        {
            var rect = element.ScreenRect;
            PgUiFacts best = null;
            var bestArea = float.MaxValue;

            foreach (var candidate in backgrounds)
            {
                if (ReferenceEquals(candidate, element)) continue;
                var candidateRect = candidate.ScreenRect;
                if (!Contains(candidateRect, rect)) continue;

                var area = candidateRect.width * candidateRect.height;
                if (area >= bestArea) continue;
                bestArea = area;
                best = candidate;
            }

            return best;
        }

        static bool Contains(Rect outer, Rect inner) =>
            outer.xMin <= inner.xMin + 0.5f && outer.yMin <= inner.yMin + 0.5f &&
            outer.xMax >= inner.xMax - 0.5f && outer.yMax >= inner.yMax - 0.5f;

        static void CheckOverlaps(List<PgFinding> findings, IReadOnlyList<PgUiFacts> facts)
        {
            var interactables = facts.Where(f => f.Interactable).ToList();

            for (var i = 0; i < interactables.Count; i++)
            for (var j = i + 1; j < interactables.Count; j++)
            {
                var a = interactables[i];
                var b = interactables[j];

                // Nesting is normal (a button inside a scroll view); genuine ambiguity is
                // partial overlap between siblings.
                if (Contains(a.ScreenRect, b.ScreenRect) || Contains(b.ScreenRect, a.ScreenRect)) continue;
                if (!a.ScreenRect.Overlaps(b.ScreenRect)) continue;

                findings.Add(PgFinding
                    .Warn("a11y.overlap", $"'{a.Name}' and '{b.Name}' overlap, so which one receives a tap is ambiguous")
                    .At(a.Path)
                    .Datum("other", b.Path));
            }
        }

        static void CheckSafeArea(List<PgFinding> findings, IReadOnlyList<PgUiFacts> facts)
        {
            var safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) return;
            if (Mathf.Approximately(safe.width, Screen.width) &&
                Mathf.Approximately(safe.height, Screen.height)) return;

            // Convert to the top-left origin the facts use.
            var safeTopLeft = new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height);

            foreach (var element in facts.Where(f => f.Interactable || !string.IsNullOrEmpty(f.Text)))
            {
                if (Contains(safeTopLeft, element.ScreenRect)) continue;

                findings.Add(PgFinding
                    .Fail("a11y.safeArea", $"'{element.Name}' extends outside the device safe area")
                    .At(element.Path)
                    .With($"inside {safeTopLeft}", element.ScreenRect.ToString())
                    .Fix("Notches, rounded corners and gesture bars will clip or steal input from this."));
            }
        }

        static string Ellipsise(string text) =>
            text.Length <= 40 ? text : text.Substring(0, 40) + "...";
    }
}
