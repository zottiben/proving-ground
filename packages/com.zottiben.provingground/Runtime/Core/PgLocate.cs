using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProvingGround
{
    /// <summary>
    /// Finding things in the scene by the loose identifiers an agent naturally writes,
    /// rather than by exact object references it has no way to obtain.
    /// </summary>
    public static class PgLocate
    {
        /// <summary>Tag checked first when looking for the player.</summary>
        public static string PlayerTag = "Player";

        /// <summary>Explicit override; when set, takes precedence over tag search.</summary>
        public static Transform PlayerOverride;

        /// <summary>
        /// The player, resolved in order: explicit override, then the configured tag, then
        /// the object driving the main camera. Returns null rather than throwing so a
        /// caller can report a useful finding.
        /// </summary>
        public static Transform Player()
        {
            if (PlayerOverride != null) return PlayerOverride;

            if (!string.IsNullOrEmpty(PlayerTag))
            {
                var tagged = GameObject.FindGameObjectsWithTag(PlayerTag).FirstOrDefault();
                if (tagged != null) return tagged.transform;
            }

            var camera = Camera.main;
            if (camera == null) return null;

            // Walk up to the highest ancestor that owns a character controller or rigidbody,
            // which is nearly always the player root rather than the camera pivot.
            var current = camera.transform;
            Transform best = null;
            while (current != null)
            {
                if (current.GetComponent<CharacterController>() != null || current.GetComponent<Rigidbody>() != null)
                    best = current;
                current = current.parent;
            }

            return best;
        }

        /// <summary>
        /// Resolves a target by exact path, then by path suffix, then by name. Suffix
        /// matching keeps a scenario working when an object is re-parented.
        /// </summary>
        public static Transform Find(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;

            var exact = GameObject.Find(target);
            if (exact != null) return exact.transform;

            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            var bySuffix = all.FirstOrDefault(t => PathOf(t).EndsWith("/" + target, System.StringComparison.Ordinal));
            if (bySuffix != null) return bySuffix;

            return all.FirstOrDefault(t => t.name == target);
        }

        /// <summary>
        /// Full '/'-separated hierarchy path. This is the identifier every layer reports
        /// against, so that a finding names something the reader can actually select.
        /// </summary>
        public static string PathOf(Transform transform)
        {
            if (transform == null) return null;
            var stack = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                stack.Push(current.name);
            return string.Join("/", stack);
        }

        /// <summary>The camera a probe should look through: the main camera, else any enabled one.</summary>
        public static Camera Eye()
        {
            if (Camera.main != null) return Camera.main;
            return Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.enabled && c.gameObject.activeInHierarchy);
        }
    }
}
