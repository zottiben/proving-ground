#if PG_UITOOLKIT
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProvingGround.Judgment;

namespace ProvingGround.Verification
{
    /// <summary>
    /// Reads resolved facts out of UI Toolkit documents.
    ///
    /// UI Toolkit resolves style through USS the way a browser resolves CSS, so the values
    /// read here are genuinely computed rather than authored. That makes it the easier of
    /// the two UI systems to hold to a manifest.
    /// </summary>
    public sealed class PgUiToolkitCollector : IPgUiCollector
    {
        public string Name => "uitoolkit";

        public bool IsAvailable =>
            Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).Length > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => PgUi.Register(new PgUiToolkitCollector());

        public IEnumerable<PgUiFacts> Collect()
        {
            var results = new List<PgUiFacts>();

            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var root = document.rootVisualElement;
                if (root == null) continue;
                Walk(root, document.name, results);
            }

            return results;
        }

        static void Walk(VisualElement element, string path, List<PgUiFacts> results)
        {
            var label = string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name;
            var currentPath = path + "/" + label;

            results.Add(FactsFor(element, currentPath, label));

            foreach (var child in element.Children())
                Walk(child, currentPath, results);
        }

        static PgUiFacts FactsFor(VisualElement element, string path, string name)
        {
            var bounds = element.worldBound;
            var style = element.resolvedStyle;

            var facts = new PgUiFacts
            {
                Source = "uitoolkit",
                Name = name,
                Path = path,
                Active = element.resolvedStyle.display != DisplayStyle.None && element.visible,
                Rect = new[]
                {
                    Mathf.Round(bounds.x), Mathf.Round(bounds.y),
                    Mathf.Round(bounds.width), Mathf.Round(bounds.height)
                },
                Color = PgColor.ToHex(style.color),
                BackgroundColor = PgColor.ToHex(style.backgroundColor),
                FontSize = style.fontSize,
                Opacity = Mathf.Round(style.opacity * 1000f) / 1000f,
                Interactable = element.enabledInHierarchy && element.pickingMode == PickingMode.Position
            };

            if (element is TextElement textElement)
            {
                facts.Text = textElement.text;

                // Measure the text against the box it was given. UI Toolkit will happily
                // lay out a label that renders as nothing.
                if (!string.IsNullOrEmpty(textElement.text) && bounds.width > 0f)
                {
                    var measured = textElement.MeasureTextSize(
                        textElement.text, 0, VisualElement.MeasureMode.Undefined,
                        0, VisualElement.MeasureMode.Undefined);

                    facts.TextTruncated = measured.x > bounds.width + 0.5f ||
                                          measured.y > bounds.height + 0.5f;
                    facts.TextInvisible = bounds.width < 1f || bounds.height < 1f;
                }
            }

            return facts;
        }
    }
}
#endif
