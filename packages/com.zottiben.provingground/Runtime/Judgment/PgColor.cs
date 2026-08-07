using UnityEngine;

namespace ProvingGround.Judgment
{
    /// <summary>
    /// Colour maths for the accessibility checks. The contrast ratio here is the WCAG 2.x
    /// definition, which is what both the Game Accessibility Guidelines and the Xbox
    /// Accessibility Guidelines defer to for text legibility.
    /// </summary>
    public static class PgColor
    {
        /// <summary>WCAG relative luminance, with the sRGB transfer function applied.</summary>
        public static float RelativeLuminance(Color color)
        {
            float Channel(float c) =>
                c <= 0.03928f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

            return 0.2126f * Channel(color.r) +
                   0.7152f * Channel(color.g) +
                   0.0722f * Channel(color.b);
        }

        /// <summary>
        /// WCAG contrast ratio between two colours, from 1 (identical) to 21 (black on
        /// white). AA wants 4.5 for body text and 3.0 for large text.
        /// </summary>
        public static float ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            var lighter = Mathf.Max(la, lb);
            var darker = Mathf.Min(la, lb);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        /// <summary>
        /// Composites <paramref name="foreground"/> over <paramref name="background"/>.
        /// Contrast against a translucent colour is meaningless without this: a 40% white
        /// label on a dark panel is not white.
        /// </summary>
        public static Color Composite(Color foreground, Color background)
        {
            var alpha = Mathf.Clamp01(foreground.a);
            return new Color(
                foreground.r * alpha + background.r * (1f - alpha),
                foreground.g * alpha + background.g * (1f - alpha),
                foreground.b * alpha + background.b * (1f - alpha),
                1f);
        }

        public static string ToHex(Color color) =>
            "#" + ColorUtility.ToHtmlStringRGBA(color);

        public static bool TryParse(string value, out Color color) =>
            ColorUtility.TryParseHtmlString(value, out color);

        /// <summary>
        /// Perceptual distance, used to decide whether two colours are "the same" when
        /// diffing against a manifest. Compares in linear space so that dark shades are not
        /// treated as interchangeable.
        /// </summary>
        public static float Distance(Color a, Color b)
        {
            var la = a.linear;
            var lb = b.linear;
            return Mathf.Sqrt(
                (la.r - lb.r) * (la.r - lb.r) +
                (la.g - lb.g) * (la.g - lb.g) +
                (la.b - lb.b) * (la.b - lb.b) +
                (a.a - b.a) * (a.a - b.a));
        }
    }
}
