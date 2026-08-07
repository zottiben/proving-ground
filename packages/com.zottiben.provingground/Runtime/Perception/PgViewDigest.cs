using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace ProvingGround.Perception
{
    /// <summary>One renderer that is actually on screen, with where it is on screen.</summary>
    [Serializable]
    public sealed class PgVisibleObject
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("path")] public string Path;

        /// <summary>Screen rect in pixels, origin top-left: x, y, width, height.</summary>
        [JsonProperty("rect")] public float[] Rect;

        /// <summary>Fraction of the screen this object covers, 0-1.</summary>
        [JsonProperty("screenCoverage")] public float ScreenCoverage;

        [JsonProperty("distance")] public float Distance;

        [JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
        public string Tag;

        /// <summary>True when another collider sits between the camera and this object's centre.</summary>
        [JsonProperty("occluded")] public bool Occluded;
    }

    /// <summary>
    /// What the player can see right now, as symbols. Pairs with a screenshot: the image
    /// carries how it looks, this carries what is there. Handing a model only the image
    /// forces it to infer both, which is the failure mode this package exists to remove.
    /// </summary>
    [Serializable]
    public sealed class PgViewDigest
    {
        [JsonProperty("schema")] public string Schema = "provingground/view@1";
        [JsonProperty("capturedUtc")] public string CapturedUtc = DateTime.UtcNow.ToString("o");

        [JsonProperty("camera")] public string CameraName;
        [JsonProperty("cameraPosition")] public float[] CameraPosition;
        [JsonProperty("cameraForward")] public float[] CameraForward;
        [JsonProperty("fieldOfView")] public float FieldOfView;
        [JsonProperty("screenWidth")] public int ScreenWidth;
        [JsonProperty("screenHeight")] public int ScreenHeight;

        /// <summary>What a ray through the screen centre hits. This is "what am I looking at".</summary>
        [JsonProperty("crosshairHit", NullValueHandling = NullValueHandling.Ignore)]
        public string CrosshairHit;

        [JsonProperty("crosshairDistance", NullValueHandling = NullValueHandling.Ignore)]
        public float? CrosshairDistance;

        [JsonProperty("visible")] public List<PgVisibleObject> Visible = new List<PgVisibleObject>();

        [JsonProperty("visibleCount")] public int VisibleCount;
        [JsonProperty("truncated")] public bool Truncated;

        /// <summary>
        /// Captures the view from <paramref name="camera"/>, or from Camera.main.
        /// </summary>
        /// <param name="maxObjects">Cap on reported objects, largest on screen first.</param>
        /// <param name="minCoverage">Ignore objects covering less than this fraction of the screen.</param>
        /// <param name="checkOcclusion">Raycast each object to see whether it is actually visible.</param>
        public static PgViewDigest Capture(
            Camera camera = null,
            int maxObjects = 40,
            float minCoverage = 0.0005f,
            bool checkOcclusion = true)
        {
            camera = camera != null ? camera : Camera.main;
            var digest = new PgViewDigest();

            if (camera == null)
            {
                digest.CameraName = "<none>";
                return digest;
            }

            digest.CameraName = camera.name;
            digest.CameraPosition = Round(camera.transform.position);
            digest.CameraForward = Round(camera.transform.forward);
            digest.FieldOfView = Mathf.Round(camera.fieldOfView * 10f) / 10f;
            digest.ScreenWidth = camera.pixelWidth;
            digest.ScreenHeight = camera.pixelHeight;

            var centreRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(centreRay, out var centreHit, 1000f))
            {
                digest.CrosshairHit = PgLocate.PathOf(centreHit.collider.transform);
                digest.CrosshairDistance = Mathf.Round(centreHit.distance * 100f) / 100f;
            }

            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var screenArea = (float)camera.pixelWidth * camera.pixelHeight;
            var found = new List<PgVisibleObject>();

            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;

                var rect = ScreenRect(camera, renderer.bounds);
                if (rect.width <= 0f || rect.height <= 0f) continue;

                var coverage = rect.width * rect.height / screenArea;
                if (coverage < minCoverage) continue;

                var centre = renderer.bounds.center;
                var distance = Vector3.Distance(camera.transform.position, centre);

                var occluded = false;
                if (checkOcclusion)
                {
                    var direction = centre - camera.transform.position;
                    if (Physics.Raycast(camera.transform.position, direction.normalized, out var hit, direction.magnitude - 0.01f))
                        occluded = hit.transform != renderer.transform && !hit.transform.IsChildOf(renderer.transform);
                }

                found.Add(new PgVisibleObject
                {
                    Name = renderer.gameObject.name,
                    Path = PgLocate.PathOf(renderer.transform),
                    Rect = new[]
                    {
                        Mathf.Round(rect.x), Mathf.Round(rect.y),
                        Mathf.Round(rect.width), Mathf.Round(rect.height)
                    },
                    ScreenCoverage = Mathf.Round(coverage * 10000f) / 10000f,
                    Distance = Mathf.Round(distance * 100f) / 100f,
                    Tag = renderer.CompareTag("Untagged") ? null : renderer.tag,
                    Occluded = occluded
                });
            }

            digest.VisibleCount = found.Count;
            digest.Visible = found
                .OrderByDescending(v => v.ScreenCoverage)
                .Take(maxObjects)
                .ToList();
            digest.Truncated = found.Count > maxObjects;
            return digest;
        }

        /// <summary>Axis-aligned screen rect covering a world bounds, in top-left-origin pixels.</summary>
        static Rect ScreenRect(Camera camera, Bounds bounds)
        {
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            var anyInFront = false;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);

                var screen = camera.WorldToScreenPoint(corner);
                if (screen.z <= 0f) continue;
                anyInFront = true;

                // WorldToScreenPoint is bottom-left origin; flip to match image conventions.
                screen.y = camera.pixelHeight - screen.y;
                min = Vector3.Min(min, screen);
                max = Vector3.Max(max, screen);
            }

            if (!anyInFront) return new Rect(0, 0, 0, 0);

            var x = Mathf.Max(min.x, 0f);
            var y = Mathf.Max(min.y, 0f);
            var width = Mathf.Min(max.x, camera.pixelWidth) - x;
            var height = Mathf.Min(max.y, camera.pixelHeight) - y;
            return new Rect(x, y, Mathf.Max(width, 0f), Mathf.Max(height, 0f));
        }


        static float[] Round(Vector3 v) => new[]
        {
            Mathf.Round(v.x * 1000f) / 1000f,
            Mathf.Round(v.y * 1000f) / 1000f,
            Mathf.Round(v.z * 1000f) / 1000f
        };

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"camera: {CameraName} at ({string.Join(", ", CameraPosition ?? new float[3])}) " +
                          $"facing ({string.Join(", ", CameraForward ?? new float[3])}) fov {FieldOfView}");
            sb.AppendLine($"screen: {ScreenWidth}x{ScreenHeight}");
            sb.AppendLine(CrosshairHit != null
                ? $"crosshair: {CrosshairHit} at {CrosshairDistance}m"
                : "crosshair: nothing within 1000m");
            sb.AppendLine($"visible ({Visible.Count} of {VisibleCount}{(Truncated ? ", truncated" : "")}):");

            foreach (var v in Visible)
            {
                sb.AppendLine($"  {v.Path}  rect=({v.Rect[0]},{v.Rect[1]} {v.Rect[2]}x{v.Rect[3]}) " +
                              $"dist={v.Distance}m coverage={v.ScreenCoverage:P2}{(v.Occluded ? " OCCLUDED" : "")}");
            }

            return sb.ToString();
        }
    }
}
