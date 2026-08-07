using System.IO;
using UnityEngine;
using ProvingGround.Perception;

namespace ProvingGround.Verification
{
    /// <summary>The outcome of comparing a capture to its baseline.</summary>
    public sealed class PgImageDiff
    {
        public bool BaselineExisted;
        public bool SizeMismatch;
        public int DifferingPixels;
        public int TotalPixels;
        public float Ratio;
        public string DiffImagePath;

        public override string ToString() =>
            SizeMismatch ? "size mismatch"
            : !BaselineExisted ? "no baseline"
            : $"{DifferingPixels}/{TotalPixels} pixels differ ({Ratio:P3})";
    }

    /// <summary>
    /// Golden-image comparison for scenes and UI.
    ///
    /// Deliberately blunt: a per-pixel difference with a tolerance, not a perceptual
    /// metric. A perceptual metric would need tuning per project, and the failure this
    /// catches, something changed that nobody meant to change, does not need subtlety.
    /// </summary>
    public static class PgVisualRegression
    {
        /// <summary>Per-channel difference above which two pixels count as different.</summary>
        public const float ChannelTolerance = 0.02f;

        public static string BaselinePathFor(string name) =>
            Path.Combine(PgPaths.Baselines, "images", name + ".png");

        /// <summary>
        /// Captures the current view and compares it to the stored baseline. When no
        /// baseline exists it writes one and reports that, rather than failing: a first run
        /// on a new shot is not a regression.
        /// </summary>
        public static PgReport Check(string name, Camera camera = null, double threshold = 0.002)
        {
            var report = new PgReport("visual:" + name);
            var currentPath = PgPaths.Capture(name + ".png");

            if (PgCapture.Screenshot(currentPath, camera) == null)
            {
                report.Failed("Could not render a capture. Is there an enabled camera?");
                return report;
            }

            var baselinePath = BaselinePathFor(name);
            var diff = Compare(baselinePath, currentPath, name);

            report.Datum("baseline", PgPaths.Relative(baselinePath));
            report.Datum("current", PgPaths.Relative(currentPath));
            report.Datum("ratio", diff.Ratio);

            if (!diff.BaselineExisted)
            {
                PgPaths.EnsureParent(baselinePath);
                File.Copy(currentPath, baselinePath, true);
                report.Add(PgFinding
                    .Info("visual.baselineCreated", $"No baseline for '{name}'; the current capture was stored as one")
                    .At(PgPaths.Relative(baselinePath))
                    .Fix("Review the image before committing it. Everything afterwards is measured against it."));
                return report;
            }

            if (diff.SizeMismatch)
            {
                report.Add(PgFinding
                    .Fail("visual.sizeMismatch", $"'{name}' was captured at a different resolution to its baseline")
                    .Fix("Pin the capture resolution, or re-baseline deliberately."));
                return report;
            }

            if (diff.Ratio > threshold)
                report.Add(PgFinding
                    .Fail("visual.regression", $"'{name}' differs from its baseline")
                    .With($"≤ {threshold:P3} of pixels", $"{diff.Ratio:P3}")
                    .Datum("diffImage", PgPaths.Relative(diff.DiffImagePath))
                    .Fix("Look at the diff image. If the change was intended, re-baseline."));
            else
                report.Add(PgFinding.Info("visual.match",
                    $"'{name}' matches its baseline ({diff.Ratio:P3} of pixels differ)"));

            return report;
        }

        /// <summary>Compares two PNGs, writing a diff image highlighting changed pixels.</summary>
        public static PgImageDiff Compare(string baselinePath, string currentPath, string name)
        {
            var diff = new PgImageDiff { BaselineExisted = File.Exists(baselinePath) };
            if (!diff.BaselineExisted) return diff;

#if PG_IMAGECONVERSION
            var baseline = Load(baselinePath);
            var current = Load(currentPath);

            try
            {
                if (baseline == null || current == null) return diff;

                if (baseline.width != current.width || baseline.height != current.height)
                {
                    diff.SizeMismatch = true;
                    return diff;
                }

                var baselinePixels = baseline.GetPixels();
                var currentPixels = current.GetPixels();
                var diffPixels = new Color[baselinePixels.Length];

                for (var i = 0; i < baselinePixels.Length; i++)
                {
                    var a = baselinePixels[i];
                    var b = currentPixels[i];
                    var different =
                        Mathf.Abs(a.r - b.r) > ChannelTolerance ||
                        Mathf.Abs(a.g - b.g) > ChannelTolerance ||
                        Mathf.Abs(a.b - b.b) > ChannelTolerance ||
                        Mathf.Abs(a.a - b.a) > ChannelTolerance;

                    if (different) diff.DifferingPixels++;

                    // Changed pixels in red over a dimmed copy of the baseline, so the diff
                    // is readable on its own.
                    diffPixels[i] = different
                        ? new Color(1f, 0f, 0f, 1f)
                        : new Color(a.r * 0.25f, a.g * 0.25f, a.b * 0.25f, 1f);
                }

                diff.TotalPixels = baselinePixels.Length;
                diff.Ratio = diff.TotalPixels > 0 ? (float)diff.DifferingPixels / diff.TotalPixels : 0f;

                if (diff.DifferingPixels > 0)
                {
                    var diffTexture = new Texture2D(baseline.width, baseline.height, TextureFormat.RGBA32, false);
                    diffTexture.SetPixels(diffPixels);
                    diffTexture.Apply();

                    diff.DiffImagePath = PgPaths.Capture(name + ".diff.png");
                    PgPaths.EnsureParent(diff.DiffImagePath);
                    File.WriteAllBytes(diff.DiffImagePath, diffTexture.EncodeToPNG());
                    Object.DestroyImmediate(diffTexture);
                }
            }
            finally
            {
                if (baseline != null) Object.DestroyImmediate(baseline);
                if (current != null) Object.DestroyImmediate(current);
            }
#endif
            return diff;
        }

#if PG_IMAGECONVERSION
        static Texture2D Load(string path)
        {
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
        }
#endif
    }
}
