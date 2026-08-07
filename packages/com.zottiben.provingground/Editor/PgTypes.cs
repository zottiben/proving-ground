using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Resolves a component type from the name a person would write.
    ///
    /// An agent writes "Rigidbody" or "PlayerController", not
    /// "UnityEngine.Rigidbody, UnityEngine.PhysicsModule". Making it guess assembly
    /// qualified names is a good way to waste a turn per component, so this searches
    /// every loaded assembly and reports the near misses when it fails.
    /// </summary>
    public static class PgTypes
    {
        static Dictionary<string, Type> _cache;

        /// <summary>The component type, or null when nothing matches.</summary>
        public static Type Component(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            var table = Table();
            if (table.TryGetValue(name, out var exact)) return exact;

            // Namespace-qualified names still work when only the leaf was indexed.
            var leaf = name.Substring(name.LastIndexOf('.') + 1);
            return table.TryGetValue(leaf, out var byLeaf) ? byLeaf : null;
        }

        /// <summary>Names close to <paramref name="name"/>, for a useful error message.</summary>
        public static List<string> Suggest(string name, int limit = 5)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<string>();
            var leaf = name.Substring(name.LastIndexOf('.') + 1);

            return Table().Keys
                .Where(k => k.IndexOf(leaf, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            leaf.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(k => Math.Abs(k.Length - leaf.Length))
                .Take(limit)
                .ToList();
        }

        public static void InvalidateCache() => _cache = null;

        static Dictionary<string, Type> Table()
        {
            if (_cache != null) return _cache;

            _cache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    // A partially loaded assembly still has usable types in it.
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract) continue;
                    if (!typeof(Component).IsAssignableFrom(type)) continue;

                    // Project types win ties: if a game defines its own Light, that is
                    // almost certainly the one being asked for.
                    var isProjectType = !type.Assembly.FullName.StartsWith("Unity");
                    if (!_cache.ContainsKey(type.Name) || isProjectType) _cache[type.Name] = type;

                    if (!string.IsNullOrEmpty(type.FullName)) _cache[type.FullName] = type;
                }
            }

            return _cache;
        }
    }
}
