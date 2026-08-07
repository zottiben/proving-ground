#if PG_UGUI
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using ProvingGround.Judgment;

namespace ProvingGround.Verification
{
    /// <summary>
    /// Reads resolved facts out of a uGUI hierarchy.
    ///
    /// TextMeshPro is reached by reflection rather than by an assembly reference. TMP has
    /// moved package and assembly across Unity versions, and a hard reference would make
    /// this package fail to compile on half the versions it claims to support for the sake
    /// of four properties.
    /// </summary>
    public sealed class PgUguiCollector : IPgUiCollector
    {
        public string Name => "ugui";

        public bool IsAvailable =>
            UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length > 0;

        static Type _tmpType;
        static PropertyInfo _tmpText, _tmpFontSize, _tmpColor, _tmpIsTruncated, _tmpTextInfo;
        static FieldInfo _tmpCharacterCount;
        static bool _tmpProbed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => PgUi.Register(new PgUguiCollector());

        public IEnumerable<PgUiFacts> Collect()
        {
            var results = new List<PgUiFacts>();

            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

                foreach (var graphic in canvas.GetComponentsInChildren<Graphic>(true))
                    results.Add(FactsFor(graphic, camera));

                // Selectables without a Graphic of their own still define hit targets.
                foreach (var selectable in canvas.GetComponentsInChildren<Selectable>(true))
                {
                    if (selectable.GetComponent<Graphic>() != null) continue;
                    results.Add(FactsForRect(selectable.transform as RectTransform, camera, selectable.name,
                        selectable.IsInteractable()));
                }
            }

            return results;
        }

        PgUiFacts FactsFor(Graphic graphic, Camera camera)
        {
            var rectTransform = graphic.rectTransform;
            // Explicit check rather than '?.': Unity's overloaded == means a Selectable
            // that is absent or destroyed is not reliably CLR-null, and '?.' would call
            // straight into it.
            var selectable = graphic.GetComponent<Selectable>();
            var interactable = selectable != null && selectable.IsInteractable();

            var facts = FactsForRect(rectTransform, camera, graphic.name, interactable);

            facts.Color = PgColor.ToHex(graphic.color);
            facts.Opacity = ResolvedAlpha(graphic);

            if (graphic is Image image && image.sprite == null)
                facts.BackgroundColor = PgColor.ToHex(image.color);

            switch (graphic)
            {
                case Text text:
                    facts.Text = text.text;
                    facts.FontSize = text.fontSize;
                    // Text overflowing its box is only a defect when the component is set
                    // to clip rather than to grow.
                    facts.TextTruncated = !string.IsNullOrEmpty(text.text) &&
                                          (text.preferredWidth > rectTransform.rect.width + 0.5f ||
                                           text.preferredHeight > rectTransform.rect.height + 0.5f);
                    facts.TextInvisible = !string.IsNullOrEmpty(text.text) && text.cachedTextGenerator.characterCount <= 1;
                    break;

                default:
                    ApplyTmpFacts(graphic, facts, rectTransform);
                    break;
            }

            return facts;
        }

        PgUiFacts FactsForRect(RectTransform rectTransform, Camera camera, string name, bool interactable)
        {
            var facts = new PgUiFacts
            {
                Source = Name,
                Name = name,
                Path = rectTransform != null ? PgLocate.PathOf(rectTransform) : name,
                Active = rectTransform != null && rectTransform.gameObject.activeInHierarchy,
                Interactable = interactable
            };

            if (rectTransform == null) return facts;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            foreach (var corner in corners)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            // Flip to a top-left origin so rects agree with screenshots and with UI Toolkit.
            var top = Screen.height - max.y;
            facts.Rect = new[]
            {
                Mathf.Round(min.x), Mathf.Round(top),
                Mathf.Round(max.x - min.x), Mathf.Round(max.y - min.y)
            };

            return facts;
        }

        /// <summary>Alpha after every CanvasGroup between this element and the root.</summary>
        static float ResolvedAlpha(Graphic graphic)
        {
            var alpha = graphic.color.a;
            var current = graphic.transform;
            while (current != null)
            {
                var group = current.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    alpha *= group.alpha;
                    if (group.ignoreParentGroups) break;
                }

                current = current.parent;
            }

            return Mathf.Round(alpha * 1000f) / 1000f;
        }

        static void ApplyTmpFacts(Graphic graphic, PgUiFacts facts, RectTransform rectTransform)
        {
            ProbeTmp();
            if (_tmpType == null || !_tmpType.IsInstanceOfType(graphic)) return;

            facts.Text = _tmpText?.GetValue(graphic) as string;
            if (_tmpFontSize?.GetValue(graphic) is float size) facts.FontSize = size;
            if (_tmpColor?.GetValue(graphic) is Color color) facts.Color = PgColor.ToHex(color);
            if (_tmpIsTruncated?.GetValue(graphic) is bool truncated) facts.TextTruncated = truncated;

            // TMP lays out zero characters when the box is too small for even one glyph,
            // which renders as an empty label rather than as an error.
            if (!string.IsNullOrEmpty(facts.Text) && _tmpTextInfo != null)
            {
                var textInfo = _tmpTextInfo.GetValue(graphic);
                if (textInfo != null)
                {
                    _tmpCharacterCount ??= textInfo.GetType().GetField("characterCount");
                    if (_tmpCharacterCount?.GetValue(textInfo) is int count)
                        facts.TextInvisible = count == 0;
                }
            }
        }

        static void ProbeTmp()
        {
            if (_tmpProbed) return;
            _tmpProbed = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _tmpType = assembly.GetType("TMPro.TMP_Text");
                if (_tmpType != null) break;
            }

            if (_tmpType == null) return;

            _tmpText = _tmpType.GetProperty("text");
            _tmpFontSize = _tmpType.GetProperty("fontSize");
            _tmpColor = _tmpType.GetProperty("color");
            _tmpIsTruncated = _tmpType.GetProperty("isTextTruncated");
            _tmpTextInfo = _tmpType.GetProperty("textInfo");
        }
    }
}
#endif
