using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProvingGround.Authoring;
using Random = System.Random;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Builds a scene from a recipe, idempotently.
    ///
    /// Re-applying converges: objects the recipe already made are updated in place,
    /// objects it no longer declares are removed, and anything it never made is left
    /// alone. That is the property that makes a recipe usable as a source of truth rather
    /// than a one-shot generator - you can change one number, rebuild, and see only that
    /// change.
    /// </summary>
    public static class PgSceneBuilder
    {
        /// <summary>One object the recipe wants, after repeats have been expanded.</summary>
        readonly struct Planned
        {
            public readonly string Id;
            public readonly PgObjectSpec Spec;
            public readonly Vector3 Position;
            public readonly Vector3 Rotation;
            public readonly string ParentId;

            public Planned(string id, PgObjectSpec spec, Vector3 position, Vector3 rotation, string parentId)
            {
                Id = id;
                Spec = spec;
                Position = position;
                Rotation = rotation;
                ParentId = parentId;
            }
        }

        public static PgReport Build(PgSceneRecipe recipe)
        {
            var report = new PgReport("build:" + (recipe?.Name ?? "?"));
            if (recipe == null) return report.Failed("No recipe was given.");

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return report.Failed("No valid scene is open.");

            var random = new Random(recipe.Seed);
            var planned = new List<Planned>();
            Expand(recipe.Objects, null, planned, random, report);

            report.Datum("planned", planned.Count);

            var existing = UnityEngine.Object
                .FindObjectsByType<PgManaged>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(m => m.Recipe == recipe.Name)
                .ToDictionary(m => m.Id, m => m, StringComparer.Ordinal);

            var built = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var kept = new HashSet<string>(StringComparer.Ordinal);

            // Two passes: create everything, then parent it. Parenting as we go would fail
            // whenever a recipe lists a child before its parent, which is a reasonable
            // thing to do and not worth making the author think about.
            foreach (var item in planned)
            {
                var transform = Realise(item, recipe, existing, report);
                if (transform == null) continue;
                built[item.Id] = transform;
                kept.Add(item.Id);
            }

            foreach (var item in planned)
            {
                if (!built.TryGetValue(item.Id, out var transform)) continue;
                if (string.IsNullOrEmpty(item.ParentId)) continue;

                var parent = built.TryGetValue(item.ParentId, out var byId)
                    ? byId
                    : PgLocate.Find(item.ParentId);

                if (parent == null)
                {
                    report.Add(PgFinding.Warn("build.noParent",
                        $"'{item.Id}' asked for parent '{item.ParentId}', which does not exist"));
                    continue;
                }

                Undo.SetTransformParent(transform, parent, "Parent " + transform.name);
            }

            // Local transforms are only meaningful once parenting is settled.
            foreach (var item in planned)
            {
                if (!built.TryGetValue(item.Id, out var transform)) continue;
                transform.localPosition = item.Position;
                transform.localEulerAngles = item.Rotation;
                if (item.Spec.Scale != null) transform.localScale = ToVector(item.Spec.Scale, 1f);
            }

            // The auto-added light and camera are owned by the recipe but never appear in
            // its object list, so without this they would be swept and re-added on every
            // single rebuild.
            kept.Add("__light");
            kept.Add("__camera");

            var removed = 0;
            foreach (var pair in existing)
            {
                if (kept.Contains(pair.Key)) continue;
                if (pair.Value == null || !pair.Value.Rebuild) continue;

                Undo.DestroyObjectImmediate(pair.Value.gameObject);
                removed++;
            }

            if (removed > 0)
                report.Add(PgFinding.Info("build.removed", $"Removed {removed} object(s) no longer in the recipe"));

            if (recipe.ClearUnmanaged) ClearUnmanaged(report, recipe.Name);
            if (recipe.EnsureLight) EnsureLight(report, recipe);
            if (recipe.EnsureCamera) EnsureCamera(report, recipe);

            PgAuthor.MarkSceneDirty();

            report.Datum("built", built.Count);
            report.Add(PgFinding.Info("build.done",
                $"Built {built.Count} object(s) from recipe '{recipe.Name}'"));

            return report;
        }

        static void Expand(List<PgObjectSpec> specs, string parentId, List<Planned> planned,
            Random random, PgReport report)
        {
            if (specs == null) return;

            foreach (var spec in specs)
            {
                if (string.IsNullOrEmpty(spec.Id))
                {
                    report.Add(PgFinding.Fail("build.noId", "An object in the recipe has no id and was skipped")
                        .Fix("Every object needs an id; it is how a rebuild recognises it."));
                    continue;
                }

                var effectiveParent = spec.Parent ?? parentId;
                var basePosition = ToVector(spec.Position);
                var baseRotation = ToVector(spec.Rotation);
                var count = Math.Max(spec.Repeat?.Count ?? 1, 1);

                for (var i = 0; i < count; i++)
                {
                    var id = count > 1 ? $"{spec.Id}_{i}" : spec.Id;
                    var position = basePosition;
                    var rotation = baseRotation;

                    if (spec.Repeat != null) Place(spec.Repeat, i, count, ref position, ref rotation, random);

                    planned.Add(new Planned(id, spec, position, rotation, effectiveParent));
                }

                // Children hang off the unrepeated id, which keeps a repeated group's
                // children attached to the group rather than to one arbitrary copy.
                Expand(spec.Children, spec.Id, planned, random, report);
            }
        }

        static void Place(PgRepeat repeat, int index, int count, ref Vector3 position, ref Vector3 rotation,
            Random random)
        {
            if (repeat.Ring.HasValue)
            {
                var angle = 360f / count * index;
                var radians = angle * Mathf.Deg2Rad;
                position += new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * repeat.Ring.Value;
                rotation += new Vector3(0f, angle, 0f);
            }
            else if (repeat.Grid != null && repeat.Grid.Length >= 2)
            {
                var columns = Mathf.Max((int)repeat.Grid[0], 1);
                var spacing = repeat.Grid[1];
                position += new Vector3(index % columns * spacing, 0f, index / columns * spacing);
            }
            else if (repeat.Offset != null)
            {
                position += ToVector(repeat.Offset) * index;
            }

            if (repeat.Rotate != null) rotation += ToVector(repeat.Rotate) * index;

            if (repeat.Jitter == null) return;

            var jitter = ToVector(repeat.Jitter);
            position += new Vector3(
                (float)(random.NextDouble() * 2 - 1) * jitter.x,
                (float)(random.NextDouble() * 2 - 1) * jitter.y,
                (float)(random.NextDouble() * 2 - 1) * jitter.z);
        }

        static Transform Realise(Planned item, PgSceneRecipe recipe,
            IReadOnlyDictionary<string, PgManaged> existing, PgReport report)
        {
            var spec = item.Spec;
            GameObject go;

            var declared = (spec.Components ?? new List<PgComponentSpec>())
                .Select(c => PgTypes.Component(c.Type))
                .Where(t => t != null)
                .Select(t => t.Name)
                .ToList();

            PgManaged marker;

            if (existing.TryGetValue(item.Id, out var managed) && managed != null)
            {
                if (!managed.Rebuild) return managed.transform;
                go = managed.gameObject;
                marker = managed;

                StripUndeclared(go, managed, declared);
            }
            else
            {
                go = Instantiate(spec, item.Id, report);
                if (go == null) return null;
                Undo.RegisterCreatedObjectUndo(go, "Build " + item.Id);

                marker = go.AddComponent<PgManaged>();
                marker.Recipe = recipe.Name;
                marker.Id = item.Id;
            }

            marker.AppliedComponents = declared;

            go.name = item.Id;
            if (spec.Active.HasValue) go.SetActive(spec.Active.Value);
            if (spec.Static.HasValue) GameObjectUtility.SetStaticEditorFlags(go,
                spec.Static.Value ? (StaticEditorFlags)~0 : 0);

            if (!string.IsNullOrEmpty(spec.Tag))
            {
                try
                {
                    go.tag = spec.Tag;
                }
                catch (UnityException)
                {
                    report.Add(PgFinding
                        .Warn("build.missingTag", $"The tag '{spec.Tag}' does not exist in this project")
                        .At(item.Id)
                        .Fix("Add it under Project Settings > Tags and Layers. Proving Ground finds the player by tag."));
                }
            }

            if (!string.IsNullOrEmpty(spec.Layer))
            {
                var layer = LayerMask.NameToLayer(spec.Layer);
                if (layer >= 0) go.layer = layer;
                else report.Add(PgFinding.Warn("build.missingLayer", $"The layer '{spec.Layer}' does not exist"));
            }

            foreach (var component in spec.Components ?? new List<PgComponentSpec>())
            {
                if (string.IsNullOrEmpty(component.Type)) continue;

                var type = PgTypes.Component(component.Type);
                if (type == null)
                {
                    var suggestions = PgTypes.Suggest(component.Type);
                    report.Add(PgFinding
                        .Fail("build.unknownComponent", $"No component type '{component.Type}'")
                        .At(item.Id)
                        .Fix(suggestions.Count > 0 ? $"Did you mean: {string.Join(", ", suggestions)}?" : null));
                    continue;
                }

                // Not '??'. UnityEngine.Object overloads ==, so a destroyed or absent
                // component can be a non-null reference that compares equal to null. The
                // null-coalescing operator uses real CLR null and would hand back that
                // husk instead of adding the component.
                var instance = go.GetComponent(type);
                if (instance == null) instance = go.AddComponent(type);
                PgAuthor.ApplyProperties(report, instance, component.Set, $"{item.Id}.{type.Name}");
            }

            if (!string.IsNullOrEmpty(spec.Material))
            {
                var renderer = go.GetComponent<Renderer>();
                var material = PgAuthor.ResolveMaterial(spec.Material);

                if (renderer == null)
                    report.Add(PgFinding.Warn("build.noRenderer", $"'{item.Id}' has no Renderer for a material"));
                else if (material == null)
                    report.Add(PgFinding.Warn("build.noMaterial", $"Could not resolve material '{spec.Material}'")
                        .At(item.Id));
                else renderer.sharedMaterial = material;
            }

            return go.transform;
        }

        /// <summary>
        /// Removes the components a previous build added that this one no longer declares,
        /// and nothing else.
        ///
        /// Scripts go first. A MonoBehaviour with [RequireComponent] blocks removal of the
        /// thing it requires, so destroying a CharacterController before the controller
        /// script that depends on it fails with a console error and leaves the object half
        /// updated.
        /// </summary>
        static void StripUndeclared(GameObject go, PgManaged managed, List<string> declared)
        {
            var previous = managed.AppliedComponents ?? new List<string>();
            var stale = previous.Where(name => !declared.Contains(name)).ToList();
            if (stale.Count == 0) return;

            var doomed = go.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform) && !(c is PgManaged))
                .Where(c => stale.Contains(c.GetType().Name))
                .OrderByDescending(c => c is MonoBehaviour)
                .ToList();

            foreach (var component in doomed) UnityEngine.Object.DestroyImmediate(component);
        }

        static GameObject Instantiate(PgObjectSpec spec, string id, PgReport report)
        {
            if (!string.IsNullOrEmpty(spec.Prefab))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Prefab);
                if (asset == null)
                {
                    report.Add(PgFinding.Fail("build.noPrefab", $"No prefab at '{spec.Prefab}'").At(id));
                    return null;
                }

                return (GameObject)PrefabUtility.InstantiatePrefab(asset);
            }

            if (string.IsNullOrEmpty(spec.Primitive)) return new GameObject(id);

            if (Enum.TryParse<PrimitiveType>(spec.Primitive, true, out var primitive))
                return GameObject.CreatePrimitive(primitive);

            report.Add(PgFinding
                .Fail("build.unknownPrimitive", $"'{spec.Primitive}' is not a primitive")
                .At(id)
                .Fix("Use Cube, Sphere, Capsule, Cylinder, Plane or Quad."));
            return null;
        }

        static void ClearUnmanaged(PgReport report, string recipeName)
        {
            var removed = 0;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var managed = root.GetComponent<PgManaged>();
                if (managed != null && managed.Recipe == recipeName) continue;

                Undo.DestroyObjectImmediate(root);
                removed++;
            }

            if (removed > 0)
                report.Add(PgFinding.Info("build.cleared", $"Removed {removed} object(s) the recipe does not own"));
        }

        static void EnsureLight(PgReport report, PgSceneRecipe recipe)
        {
            if (UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Any(l => l.type == LightType.Directional)) return;

            var go = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(go, "Add light");

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var marker = go.AddComponent<PgManaged>();
            marker.Recipe = recipe.Name;
            marker.Id = "__light";

            report.Add(PgFinding.Info("build.light", "Added a directional light; the recipe declared none"));
        }

        static void EnsureCamera(PgReport report, PgSceneRecipe recipe)
        {
            if (UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any()) return;

            var go = new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(go, "Add camera");

            go.AddComponent<Camera>();
            go.transform.position = new Vector3(0f, 3f, -10f);

            try
            {
                go.tag = "MainCamera";
            }
            catch (UnityException)
            {
                // A project without the built-in tag is unusual but not fatal.
            }

            var marker = go.AddComponent<PgManaged>();
            marker.Recipe = recipe.Name;
            marker.Id = "__camera";

            report.Add(PgFinding.Info("build.camera", "Added a camera; the recipe declared none"));
        }

        static Vector3 ToVector(IReadOnlyList<float> values, float fill = 0f)
        {
            if (values == null) return fill == 0f ? Vector3.zero : Vector3.one * fill;
            var x = values.Count > 0 ? values[0] : fill;
            var y = values.Count > 1 ? values[1] : fill;
            var z = values.Count > 2 ? values[2] : fill;
            return new Vector3(x, y, z);
        }
    }
}
