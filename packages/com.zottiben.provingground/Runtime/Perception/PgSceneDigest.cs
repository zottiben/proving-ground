using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProvingGround.Perception
{
    /// <summary>
    /// What to include when digesting a scene. Defaults are tuned to stay inside a few
    /// thousand tokens on a real scene, because a digest that blows the context window is
    /// no more useful than a screenshot.
    /// </summary>
    [Serializable]
    public sealed class PgDigestOptions
    {
        [JsonProperty("maxNodes")] public int MaxNodes = 400;
        [JsonProperty("maxDepth")] public int MaxDepth = 8;
        [JsonProperty("includeInactive")] public bool IncludeInactive;
        [JsonProperty("includeTransforms")] public bool IncludeTransforms = true;
        [JsonProperty("includeComponents")] public bool IncludeComponents = true;
        [JsonProperty("includeBounds")] public bool IncludeBounds;

        /// <summary>Only include objects within this distance of <see cref="Origin"/>. Zero disables.</summary>
        [JsonProperty("radius")] public float Radius;

        [JsonProperty("origin")] public Vector3 Origin;

        /// <summary>Only include subtrees rooted at an object whose name contains one of these.</summary>
        [JsonProperty("nameFilter")] public List<string> NameFilter = new List<string>();

        /// <summary>Component type names to omit, to cut noise from engine plumbing.</summary>
        [JsonProperty("excludeComponents")] public List<string> ExcludeComponents = new List<string>
        {
            "Transform", "RectTransform", "CanvasRenderer"
        };

        public static PgDigestOptions Compact => new PgDigestOptions
        {
            MaxNodes = 150, MaxDepth = 5, IncludeComponents = true, IncludeTransforms = false
        };

        public static PgDigestOptions Full => new PgDigestOptions
        {
            MaxNodes = 4000, MaxDepth = 32, IncludeInactive = true, IncludeBounds = true
        };
    }

    /// <summary>One GameObject, flattened to the facts an agent can act on.</summary>
    [Serializable]
    public sealed class PgObjectNode
    {
        [JsonProperty("name")] public string Name;

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path;

        [JsonProperty("active")] public bool Active;

        [JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
        public string Tag;

        [JsonProperty("layer", NullValueHandling = NullValueHandling.Ignore)]
        public string Layer;

        [JsonProperty("position", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Position;

        [JsonProperty("rotation", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Rotation;

        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Scale;

        [JsonProperty("bounds", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Bounds;

        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Components;

        [JsonProperty("children", NullValueHandling = NullValueHandling.Ignore)]
        public List<PgObjectNode> Children;

        /// <summary>Set when children were dropped to stay inside the node budget.</summary>
        [JsonProperty("elidedChildren", NullValueHandling = NullValueHandling.Ignore)]
        public int? ElidedChildren;
    }

    /// <summary>
    /// A symbolic snapshot of a scene, emitted from inside the engine.
    ///
    /// This exists because of a measured result rather than a preference: VLM agents given
    /// accurate symbolic scene state outperform the same agents given raw frames, but
    /// agents asked to extract those symbols from frames themselves degrade sharply as
    /// scenes get complex. The engine already knows the answer, so it should be the one
    /// to say it.
    /// </summary>
    [Serializable]
    public sealed class PgSceneDigest
    {
        [JsonProperty("schema")] public string Schema = "provingground/digest@1";
        [JsonProperty("scene")] public string Scene;
        [JsonProperty("capturedUtc")] public string CapturedUtc = DateTime.UtcNow.ToString("o");
        [JsonProperty("isPlaying")] public bool IsPlaying;
        [JsonProperty("nodeCount")] public int NodeCount;
        [JsonProperty("truncated")] public bool Truncated;
        [JsonProperty("roots")] public List<PgObjectNode> Roots = new List<PgObjectNode>();

        /// <summary>
        /// Builds a digest of the active scene, or of <paramref name="scene"/> when given.
        /// </summary>
        public static PgSceneDigest Capture(PgDigestOptions options = null, Scene? scene = null)
        {
            options ??= new PgDigestOptions();
            var target = scene ?? SceneManager.GetActiveScene();

            var digest = new PgSceneDigest
            {
                Scene = target.IsValid() ? target.name : "<invalid>",
                IsPlaying = Application.isPlaying
            };

            if (!target.IsValid() || !target.isLoaded) return digest;

            var budget = options.MaxNodes;
            foreach (var root in target.GetRootGameObjects())
            {
                if (budget <= 0)
                {
                    digest.Truncated = true;
                    break;
                }

                if (!Included(root, options)) continue;

                var node = Build(root.transform, options, 0, ref budget, root.name);
                if (node != null) digest.Roots.Add(node);
            }

            digest.NodeCount = options.MaxNodes - Math.Max(budget, 0);
            digest.Truncated |= budget <= 0;
            return digest;
        }

        static bool Included(GameObject go, PgDigestOptions options)
        {
            if (!options.IncludeInactive && !go.activeInHierarchy) return false;

            if (options.Radius > 0f)
            {
                var distance = Vector3.Distance(go.transform.position, options.Origin);
                // Keep the object when any descendant could still be in range.
                if (distance > options.Radius && go.transform.childCount == 0) return false;
            }

            if (options.NameFilter != null && options.NameFilter.Count > 0)
            {
                var inSubtree = go.GetComponentsInChildren<Transform>(true)
                    .Any(t => options.NameFilter.Any(f =>
                        t.name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0));
                if (!inSubtree) return false;
            }

            return true;
        }

        static PgObjectNode Build(Transform transform, PgDigestOptions options, int depth, ref int budget, string path)
        {
            if (budget <= 0) return null;
            budget--;

            var go = transform.gameObject;
            var node = new PgObjectNode
            {
                Name = go.name,
                Path = path,
                Active = go.activeInHierarchy,
                Tag = go.CompareTag("Untagged") ? null : go.tag,
                Layer = go.layer == 0 ? null : LayerMask.LayerToName(go.layer)
            };

            if (options.IncludeTransforms)
            {
                node.Position = Round(transform.position);
                var euler = transform.eulerAngles;
                if (euler.sqrMagnitude > 0.0001f) node.Rotation = Round(euler);
                if ((transform.localScale - Vector3.one).sqrMagnitude > 0.0001f)
                    node.Scale = Round(transform.localScale);
            }

            if (options.IncludeBounds)
            {
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var b = renderer.bounds;
                    node.Bounds = new[]
                    {
                        R(b.center.x), R(b.center.y), R(b.center.z),
                        R(b.size.x), R(b.size.y), R(b.size.z)
                    };
                }
            }

            if (options.IncludeComponents)
            {
                var names = new List<string>();
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        names.Add("<missing script>");
                        continue;
                    }

                    var typeName = component.GetType().Name;
                    if (options.ExcludeComponents != null && options.ExcludeComponents.Contains(typeName)) continue;
                    names.Add(Describe(component, typeName));
                }

                if (names.Count > 0) node.Components = names;
            }

            if (depth < options.MaxDepth && transform.childCount > 0)
            {
                var children = new List<PgObjectNode>();
                var elided = 0;
                for (var i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (!Included(child.gameObject, options)) continue;

                    if (budget <= 0)
                    {
                        elided += transform.childCount - i;
                        break;
                    }

                    var built = Build(child, options, depth + 1, ref budget, path + "/" + child.name);
                    if (built != null) children.Add(built);
                }

                if (children.Count > 0) node.Children = children;
                if (elided > 0) node.ElidedChildren = elided;
            }
            else if (transform.childCount > 0)
            {
                node.ElidedChildren = transform.childCount;
            }

            return node;
        }

        /// <summary>
        /// Adds the handful of component properties that actually change what an agent
        /// would do. Kept deliberately short: a full property dump is noise.
        /// </summary>
        static string Describe(Component component, string typeName)
        {
            switch (component)
            {
                case Camera camera:
                    return $"{typeName}(fov={R(camera.fieldOfView)}, depth={R(camera.depth)})";
                case Light light:
                    return $"{typeName}({light.type}, intensity={R(light.intensity)})";
                case Rigidbody rigidbody:
                    return $"{typeName}(mass={R(rigidbody.mass)}, kinematic={rigidbody.isKinematic})";
                case Collider collider:
                    return $"{typeName}(trigger={collider.isTrigger}, enabled={collider.enabled})";
                case Renderer renderer:
                    return $"{typeName}(materials={renderer.sharedMaterials?.Length ?? 0}, visible={renderer.isVisible})";
#if PG_ANIMATION
                // Guarded because the Animation module is genuinely optional; a project
                // without it should still get a scene digest, just without this one line.
                case Animator animator:
                    return $"{typeName}(controller={(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")})";
#endif
                case AudioSource audio:
                    return $"{typeName}(clip={(audio.clip != null ? audio.clip.name : "none")}, playing={audio.isPlaying})";
                case Canvas canvas:
                    return $"{typeName}({canvas.renderMode}, order={canvas.sortingOrder})";
                case Behaviour behaviour when !behaviour.enabled:
                    return typeName + "(disabled)";
                default:
                    return typeName;
            }
        }

        static float[] Round(Vector3 v) => new[] { R(v.x), R(v.y), R(v.z) };
        static float R(float value) => Mathf.Round(value * 1000f) / 1000f;

        /// <summary>
        /// Indented text rendering. Preferred over JSON when handing a digest to a model:
        /// it carries the same facts in roughly half the tokens.
        /// </summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"scene: {Scene}  ({(IsPlaying ? "playing" : "edit mode")}, {NodeCount} nodes{(Truncated ? ", truncated" : "")})");
            foreach (var root in Roots) Render(sb, root, 0);
            return sb.ToString();
        }

        static void Render(StringBuilder sb, PgObjectNode node, int indent)
        {
            var pad = new string(' ', indent * 2);
            sb.Append(pad).Append(node.Active ? "" : "~").Append(node.Name);

            if (node.Position != null)
                sb.Append($" @({node.Position[0]}, {node.Position[1]}, {node.Position[2]})");
            if (!string.IsNullOrEmpty(node.Tag)) sb.Append($" #{node.Tag}");
            if (!string.IsNullOrEmpty(node.Layer)) sb.Append($" [{node.Layer}]");
            if (node.Components != null && node.Components.Count > 0)
                sb.Append("  ").Append(string.Join(", ", node.Components));
            sb.AppendLine();

            if (node.Children != null)
                foreach (var child in node.Children)
                    Render(sb, child, indent + 1);

            if (node.ElidedChildren.HasValue)
                sb.Append(pad).AppendLine($"  ... {node.ElidedChildren.Value} more children");
        }
    }
}
