using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ProvingGround.Verification
{
    /// <summary>
    /// What a UI element actually resolved to on screen, as opposed to what the code that
    /// built it intended.
    ///
    /// The distinction is the whole reason this type exists. Layout systems override each
    /// other, text shrinks to fit or fails to, and a colour set in a prefab can be tinted
    /// by three parents before it reaches a pixel. Only the resolved value is worth
    /// checking.
    /// </summary>
    [Serializable]
    public sealed class PgUiFacts
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("name")] public string Name;

        /// <summary>Source that produced these facts: <c>ugui</c> or <c>uitoolkit</c>.</summary>
        [JsonProperty("source")] public string Source;

        [JsonProperty("active")] public bool Active;

        /// <summary>Screen rect in pixels, top-left origin: x, y, width, height.</summary>
        [JsonProperty("rect")] public float[] Rect;

        [JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
        public string Color;

        [JsonProperty("backgroundColor", NullValueHandling = NullValueHandling.Ignore)]
        public string BackgroundColor;

        [JsonProperty("fontSize", NullValueHandling = NullValueHandling.Ignore)]
        public float? FontSize;

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text;

        [JsonProperty("opacity", NullValueHandling = NullValueHandling.Ignore)]
        public float? Opacity;

        [JsonProperty("interactable")] public bool Interactable;

        /// <summary>True when the element has text that does not fit its box.</summary>
        [JsonProperty("textTruncated")] public bool TextTruncated;

        /// <summary>True when the layout produced zero visible characters for non-empty text.</summary>
        [JsonProperty("textInvisible")] public bool TextInvisible;

        [JsonIgnore] public Rect ScreenRect =>
            Rect != null && Rect.Length == 4 ? new Rect(Rect[0], Rect[1], Rect[2], Rect[3]) : default;

        public float? NumericProperty(string property)
        {
            switch (property.ToLowerInvariant())
            {
                case "width": return Rect?[2];
                case "height": return Rect?[3];
                case "x": return Rect?[0];
                case "y": return Rect?[1];
                case "fontsize": return FontSize;
                case "opacity": return Opacity;
                default: return null;
            }
        }

        public string StringProperty(string property)
        {
            switch (property.ToLowerInvariant())
            {
                case "color": return Color;
                case "backgroundcolor": return BackgroundColor;
                case "text": return Text;
                case "active": return Active.ToString().ToLowerInvariant();
                default: return null;
            }
        }
    }

    /// <summary>
    /// Reads resolved UI facts out of one UI system. Implementations register themselves so
    /// that a project using uGUI, UI Toolkit or both is handled without configuration.
    /// </summary>
    public interface IPgUiCollector
    {
        string Name { get; }
        bool IsAvailable { get; }
        IEnumerable<PgUiFacts> Collect();
    }

    /// <summary>Where UI collectors register, and where callers go to read the live UI.</summary>
    public static class PgUi
    {
        static readonly List<IPgUiCollector> Collectors = new List<IPgUiCollector>();

        public static void Register(IPgUiCollector collector)
        {
            if (collector == null) return;
            if (Collectors.Exists(c => c.Name == collector.Name)) return;
            Collectors.Add(collector);
        }

        public static IReadOnlyList<IPgUiCollector> All => Collectors;

        /// <summary>Every UI element currently on screen, from every registered system.</summary>
        public static List<PgUiFacts> Collect()
        {
            var facts = new List<PgUiFacts>();
            foreach (var collector in Collectors)
            {
                if (!collector.IsAvailable) continue;
                try
                {
                    facts.AddRange(collector.Collect());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ProvingGround] UI collector '{collector.Name}' failed: {e.Message}");
                }
            }

            return facts;
        }
    }
}
