using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ProvingGround.Contracts
{
    /// <summary>
    /// One UI element's expected appearance. Matched against the live hierarchy, then
    /// diffed property by property against what the element actually resolved to at
    /// runtime, rather than against what the code intended.
    /// </summary>
    [Serializable]
    public sealed class PgUiElementSpec
    {
        /// <summary>
        /// How to find the element. A '/'-separated GameObject path suffix for uGUI, or a
        /// USS selector-ish name/class for UI Toolkit. Matching is by suffix so that
        /// re-parenting does not invalidate the whole manifest.
        /// </summary>
        [JsonProperty("match")] public string Match;

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>True when the element must exist. A missing required element is a failure.</summary>
        [JsonProperty("required")] public bool Required = true;

        /// <summary>
        /// Property name to expected value. Values may reference a token as <c>$token.name</c>.
        /// Recognised properties: color, backgroundColor, fontSize, width, height,
        /// minWidth, minHeight, opacity, text, active.
        /// </summary>
        [JsonProperty("expect")] public Dictionary<string, string> Expect = new Dictionary<string, string>();

        /// <summary>
        /// Element id whose colour this element's text must remain legible against.
        /// Drives the WCAG contrast check.
        /// </summary>
        [JsonProperty("contrastAgainst", NullValueHandling = NullValueHandling.Ignore)]
        public string ContrastAgainst;
    }

    /// <summary>
    /// The design system as data. Replaces "the agent reads the style guide and
    /// misremembers it" with "the agent is measured against the style guide".
    /// </summary>
    [Serializable]
    public sealed class PgUiManifest
    {
        public const string FileName = "ui.json";

        [JsonProperty("schema")] public string Schema = "provingground/ui@1";

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>Named design tokens. Colours as hex, everything else as a number.</summary>
        [JsonProperty("tokens")] public Dictionary<string, string> Tokens = new Dictionary<string, string>();

        /// <summary>Element id to its spec. The id is what findings are reported against.</summary>
        [JsonProperty("elements")] public Dictionary<string, PgUiElementSpec> Elements =
            new Dictionary<string, PgUiElementSpec>();

        /// <summary>Rules applied to every element without being restated per element.</summary>
        [JsonProperty("global")] public PgUiGlobalRules Global = new PgUiGlobalRules();

        public static string DefaultPath => Path.Combine(PgPaths.Contracts, FileName);

        public static PgUiManifest Load(string path = null) =>
            PgJson.Read(path ?? DefaultPath, (PgUiManifest)null);

        public void Save(string path = null) => PgJson.Write(path ?? DefaultPath, this);

        /// <summary>Resolves <c>$token.name</c> references; passes literals through unchanged.</summary>
        public string Resolve(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '$') return value;
            var key = value.Substring(1);
            return Tokens != null && Tokens.TryGetValue(key, out var resolved) ? resolved : value;
        }

        /// <summary>Parses a colour token or literal. Accepts #RGB, #RRGGBB and #RRGGBBAA.</summary>
        public bool TryResolveColor(string value, out Color color)
        {
            color = default;
            var resolved = Resolve(value);
            return !string.IsNullOrEmpty(resolved) && ColorUtility.TryParseHtmlString(resolved, out color);
        }

        public bool TryResolveNumber(string value, out double number) =>
            double.TryParse(Resolve(value), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Blanket UI rules. These encode the parts of the Game Accessibility Guidelines and
    /// the Xbox Accessibility Guidelines that can be checked mechanically.
    /// </summary>
    [Serializable]
    public sealed class PgUiGlobalRules
    {
        /// <summary>Minimum interactive target size in reference-resolution pixels.</summary>
        [JsonProperty("minHitTargetPx")] public float MinHitTargetPx = 44f;

        /// <summary>Minimum rendered font size in reference-resolution pixels.</summary>
        [JsonProperty("minFontSizePx")] public float MinFontSizePx = 16f;

        /// <summary>WCAG 2.x AA contrast ratio for normal body text.</summary>
        [JsonProperty("minContrastRatio")] public float MinContrastRatio = 4.5f;

        /// <summary>WCAG 2.x AA contrast ratio for large text (>=24px, or >=19px bold).</summary>
        [JsonProperty("minContrastRatioLargeText")] public float MinContrastRatioLargeText = 3.0f;

        /// <summary>Fail when text is clipped or laid out to zero visible characters.</summary>
        [JsonProperty("forbidClippedText")] public bool ForbidClippedText = true;

        /// <summary>Fail when interactive elements overlap, which makes hit testing ambiguous.</summary>
        [JsonProperty("forbidOverlappingInteractables")] public bool ForbidOverlappingInteractables = true;

        /// <summary>Fail when any UI element sits outside the safe area on the target aspect.</summary>
        [JsonProperty("enforceSafeArea")] public bool EnforceSafeArea = true;
    }
}
