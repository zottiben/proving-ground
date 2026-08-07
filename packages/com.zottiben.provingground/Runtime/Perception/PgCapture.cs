using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ProvingGround.Perception
{
    /// <summary>One annotated box drawn on a capture, and what it refers to.</summary>
    public sealed class PgAnnotation
    {
        public string Label;
        public Rect Rect;
        public Color Color;
        public string ColorName;
    }

    /// <summary>
    /// Renders the game to a PNG, optionally with boxes drawn around what is on screen.
    ///
    /// The image is never the primary channel. It is paired with a legend naming what each
    /// box is, because a model asked to work out both what is present and how it looks
    /// from pixels alone will get the first part wrong and then reason confidently from
    /// it.
    /// </summary>
    public static class PgCapture
    {
        static readonly (string Name, Color Value)[] Palette =
        {
            ("red", new Color(1f, 0.2f, 0.2f)),
            ("green", new Color(0.2f, 1f, 0.3f)),
            ("blue", new Color(0.3f, 0.5f, 1f)),
            ("yellow", new Color(1f, 0.9f, 0.2f)),
            ("magenta", new Color(1f, 0.3f, 0.9f)),
            ("cyan", new Color(0.2f, 1f, 1f)),
            ("orange", new Color(1f, 0.6f, 0.1f)),
            ("white", Color.white)
        };

        /// <summary>Renders <paramref name="camera"/> to a texture at the requested size.</summary>
        public static Texture2D Render(Camera camera = null, int width = 0, int height = 0)
        {
            camera = camera != null ? camera : PgLocate.Eye();
            if (camera == null) return null;

            width = width > 0 ? width : Mathf.Max(camera.pixelWidth, 1);
            height = height > 0 ? height : Mathf.Max(camera.pixelHeight, 1);

            var renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        /// <summary>Writes a PNG. Returns the path written, or null when capture failed.</summary>
        public static string Screenshot(string path, Camera camera = null, int width = 0, int height = 0)
        {
            var texture = Render(camera, width, height);
            if (texture == null) return null;

            try
            {
                return WritePng(path, texture);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Captures with coloured boxes around the largest on-screen objects, and returns
        /// the legend. Feed the legend to the model alongside the image.
        /// </summary>
        public static List<PgAnnotation> Annotated(string path, Camera camera = null, int maxBoxes = 8)
        {
            camera = camera != null ? camera : PgLocate.Eye();
            var annotations = new List<PgAnnotation>();
            if (camera == null) return annotations;

            var view = PgViewDigest.Capture(camera, maxBoxes);
            var texture = Render(camera);
            if (texture == null) return annotations;

            try
            {
                var index = 0;
                foreach (var visible in view.Visible.Take(maxBoxes))
                {
                    var entry = Palette[index % Palette.Length];
                    var rect = new Rect(visible.Rect[0], visible.Rect[1], visible.Rect[2], visible.Rect[3]);

                    annotations.Add(new PgAnnotation
                    {
                        Label = visible.Path,
                        Rect = rect,
                        Color = entry.Value,
                        ColorName = entry.Name
                    });

                    // Texture space is bottom-left origin; the digest rects are top-left.
                    var flipped = new Rect(rect.x, texture.height - rect.yMax, rect.width, rect.height);
                    DrawRect(texture, flipped, entry.Value, 3);
                    index++;
                }

                texture.Apply();
                WritePng(path, texture);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            return annotations;
        }

        /// <summary>Legend text to send with an annotated capture.</summary>
        public static string LegendText(IEnumerable<PgAnnotation> annotations)
        {
            var lines = annotations
                .Select(a => $"  {a.ColorName} box at ({a.Rect.x:0}, {a.Rect.y:0}) {a.Rect.width:0}x{a.Rect.height:0} = {a.Label}")
                .ToList();

            return lines.Count == 0
                ? "no objects annotated"
                : "annotated boxes:\n" + string.Join("\n", lines);
        }

        static void DrawRect(Texture2D texture, Rect rect, Color color, int thickness)
        {
            var xMin = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, texture.width - 1);
            var xMax = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), 0, texture.width - 1);
            var yMin = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, texture.height - 1);
            var yMax = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), 0, texture.height - 1);

            for (var t = 0; t < thickness; t++)
            {
                for (var x = xMin; x <= xMax; x++)
                {
                    SetPixel(texture, x, yMin + t, color);
                    SetPixel(texture, x, yMax - t, color);
                }

                for (var y = yMin; y <= yMax; y++)
                {
                    SetPixel(texture, xMin + t, y, color);
                    SetPixel(texture, xMax - t, y, color);
                }
            }
        }

        static void SetPixel(Texture2D texture, int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= texture.width || y >= texture.height) return;
            texture.SetPixel(x, y, color);
        }

        static string WritePng(string path, Texture2D texture)
        {
#if PG_IMAGECONVERSION
            PgPaths.EnsureParent(path);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return path;
#else
            Debug.LogWarning("[ProvingGround] com.unity.modules.imageconversion is not installed, so captures cannot be written.");
            return null;
#endif
        }
    }
}
