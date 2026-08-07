using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Direct scene authoring: create, modify and delete objects, components and
    /// properties.
    ///
    /// Every mutation goes through Unity's Undo system, so a wrong edit is one Ctrl+Z away
    /// rather than something the user has to repair by hand. That matters more than usual
    /// here, because the thing making the edits is not watching the screen.
    ///
    /// For building a whole level, prefer <see cref="PgSceneBuilder"/> and a recipe.
    /// Direct authoring is for iterating on something that already exists.
    /// </summary>
    public static class PgAuthor
    {
        /// <summary>Creates an object. Returns its hierarchy path.</summary>
        public static PgReport Create(string name, string primitive = null, string parent = null,
            float[] position = null, float[] rotation = null, float[] scale = null,
            string prefab = null, string tag = null, string layer = null)
        {
            var report = new PgReport("author.create");

            GameObject created;

            if (!string.IsNullOrEmpty(prefab))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                if (asset == null) return report.Failed($"No prefab at '{prefab}'.");

                created = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                created.name = name ?? asset.name;
            }
            else if (!string.IsNullOrEmpty(primitive))
            {
                if (!Enum.TryParse<PrimitiveType>(primitive, true, out var type))
                    return report.Failed(
                        $"'{primitive}' is not a primitive. Use Cube, Sphere, Capsule, Cylinder, Plane or Quad.");

                created = GameObject.CreatePrimitive(type);
                created.name = name ?? primitive;
            }
            else
            {
                created = new GameObject(name ?? "GameObject");
            }

            Undo.RegisterCreatedObjectUndo(created, "Create " + created.name);

            if (!string.IsNullOrEmpty(parent))
            {
                var parentTransform = PgLocate.Find(parent);
                if (parentTransform == null)
                {
                    report.Add(PgFinding.Warn("author.noParent", $"Parent '{parent}' was not found; left at the root"));
                }
                else
                {
                    Undo.SetTransformParent(created.transform, parentTransform, "Parent " + created.name);
                }
            }

            if (position != null) created.transform.localPosition = ToVector(position);
            if (rotation != null) created.transform.localEulerAngles = ToVector(rotation);
            if (scale != null) created.transform.localScale = ToVector(scale, 1f);

            if (!string.IsNullOrEmpty(tag)) ApplyTag(report, created, tag);
            if (!string.IsNullOrEmpty(layer)) ApplyLayer(report, created, layer);

            MarkSceneDirty();

            report.Datum("path", PgLocate.PathOf(created.transform));
            report.Add(PgFinding.Info("author.created", $"Created '{created.name}'")
                .At(PgLocate.PathOf(created.transform)));
            return report;
        }

        /// <summary>Deletes an object.</summary>
        public static PgReport Delete(string target)
        {
            var report = new PgReport("author.delete");
            var found = PgLocate.Find(target);
            if (found == null) return report.Failed($"No object matching '{target}'.");

            var path = PgLocate.PathOf(found);
            Undo.DestroyObjectImmediate(found.gameObject);
            MarkSceneDirty();

            report.Add(PgFinding.Info("author.deleted", $"Deleted '{target}'").At(path));
            return report;
        }

        /// <summary>Moves, rotates, scales, re-parents, renames or toggles an object.</summary>
        public static PgReport Modify(string target, float[] position = null, float[] rotation = null,
            float[] scale = null, string parent = null, string name = null, bool? active = null,
            string tag = null, string layer = null, bool worldSpace = false)
        {
            var report = new PgReport("author.modify");
            var found = PgLocate.Find(target);
            if (found == null) return report.Failed($"No object matching '{target}'.");

            Undo.RecordObject(found, "Modify " + found.name);
            Undo.RecordObject(found.gameObject, "Modify " + found.name);

            if (parent != null)
            {
                var parentTransform = string.IsNullOrEmpty(parent) ? null : PgLocate.Find(parent);
                if (!string.IsNullOrEmpty(parent) && parentTransform == null)
                    report.Add(PgFinding.Warn("author.noParent", $"Parent '{parent}' was not found"));
                else
                    Undo.SetTransformParent(found, parentTransform, "Reparent " + found.name);
            }

            if (position != null)
            {
                if (worldSpace) found.position = ToVector(position);
                else found.localPosition = ToVector(position);
            }

            if (rotation != null)
            {
                if (worldSpace) found.eulerAngles = ToVector(rotation);
                else found.localEulerAngles = ToVector(rotation);
            }

            if (scale != null) found.localScale = ToVector(scale, 1f);
            if (!string.IsNullOrEmpty(name)) found.gameObject.name = name;
            if (active.HasValue) found.gameObject.SetActive(active.Value);
            if (!string.IsNullOrEmpty(tag)) ApplyTag(report, found.gameObject, tag);
            if (!string.IsNullOrEmpty(layer)) ApplyLayer(report, found.gameObject, layer);

            EditorUtility.SetDirty(found.gameObject);
            MarkSceneDirty();

            report.Datum("path", PgLocate.PathOf(found));
            report.Add(PgFinding.Info("author.modified", $"Modified '{target}'").At(PgLocate.PathOf(found)));
            return report;
        }

        /// <summary>Adds a component and optionally sets properties on it.</summary>
        public static PgReport AddComponent(string target, string componentType,
            Dictionary<string, object> set = null)
        {
            var report = new PgReport("author.addComponent");
            var found = PgLocate.Find(target);
            if (found == null) return report.Failed($"No object matching '{target}'.");

            var type = PgTypes.Component(componentType);
            if (type == null)
            {
                var suggestions = PgTypes.Suggest(componentType);
                return report.Failed($"No component type '{componentType}'." +
                                     (suggestions.Count > 0 ? $" Did you mean: {string.Join(", ", suggestions)}?" : ""));
            }

            // Not '??' - see PgSceneBuilder: Unity's overloaded == means an absent
            // component is not reliably CLR-null.
            var component = found.GetComponent(type);
            if (component == null) component = Undo.AddComponent(found.gameObject, type);
            ApplyProperties(report, component, set, componentType);

            MarkSceneDirty();
            report.Add(PgFinding.Info("author.componentAdded", $"'{type.Name}' on '{target}'")
                .At(PgLocate.PathOf(found)));
            return report;
        }

        public static PgReport RemoveComponent(string target, string componentType)
        {
            var report = new PgReport("author.removeComponent");
            var found = PgLocate.Find(target);
            if (found == null) return report.Failed($"No object matching '{target}'.");

            var type = PgTypes.Component(componentType);
            if (type == null) return report.Failed($"No component type '{componentType}'.");

            var component = found.GetComponent(type);
            if (component == null) return report.Failed($"'{target}' has no {type.Name}.");

            Undo.DestroyObjectImmediate(component);
            MarkSceneDirty();

            report.Add(PgFinding.Info("author.componentRemoved", $"Removed '{type.Name}' from '{target}'"));
            return report;
        }

        /// <summary>Sets properties on a component, or on the GameObject when no type is given.</summary>
        public static PgReport SetProperties(string target, string componentType,
            Dictionary<string, object> set)
        {
            var report = new PgReport("author.set");
            var found = PgLocate.Find(target);
            if (found == null) return report.Failed($"No object matching '{target}'.");

            UnityEngine.Object subject;
            if (string.IsNullOrEmpty(componentType))
            {
                subject = found.gameObject;
            }
            else
            {
                var type = PgTypes.Component(componentType);
                if (type == null)
                {
                    var suggestions = PgTypes.Suggest(componentType);
                    return report.Failed($"No component type '{componentType}'." +
                                         (suggestions.Count > 0 ? $" Did you mean: {string.Join(", ", suggestions)}?" : ""));
                }

                subject = found.GetComponent(type);
                if (subject == null) return report.Failed($"'{target}' has no {type.Name}.");
            }

            ApplyProperties(report, subject, set, componentType ?? "GameObject");
            MarkSceneDirty();
            return report;
        }

        /// <summary>Assigns a material, either an asset path or a colour like <c>#4488FF</c>.</summary>
        public static PgReport SetMaterial(string target, string material)
        {
            var report = new PgReport("author.material");
            var found = PgLocate.Find(target);
            if (found == null) return report.Failed($"No object matching '{target}'.");

            var renderer = found.GetComponent<Renderer>();
            if (renderer == null) return report.Failed($"'{target}' has no Renderer.");

            var asset = ResolveMaterial(material);
            if (asset == null) return report.Failed($"Could not resolve material '{material}'.");

            Undo.RecordObject(renderer, "Set material");
            renderer.sharedMaterial = asset;
            EditorUtility.SetDirty(renderer);
            MarkSceneDirty();

            report.Add(PgFinding.Info("author.material", $"'{asset.name}' on '{target}'"));
            return report;
        }

        /// <summary>
        /// Finds or creates a material. A colour string produces a shared, reusable asset
        /// under ProvingGround so a generated level does not leak a material per object.
        /// </summary>
        public static Material ResolveMaterial(string material)
        {
            if (string.IsNullOrEmpty(material)) return null;

            if (material.StartsWith("Assets/") || material.StartsWith("Packages/"))
                return AssetDatabase.LoadAssetAtPath<Material>(material);

            if (!ColorUtility.TryParseHtmlString(material, out var color)) return null;

            const string folder = "Assets/ProvingGround/Materials";
            var assetPath = $"{folder}/pg_{material.TrimStart('#').ToLowerInvariant()}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null) return existing;

            Directory.CreateDirectory(Path.Combine(PgPaths.ProjectRoot, folder));

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var created = new Material(shader) { name = Path.GetFileNameWithoutExtension(assetPath) };

            // URP and the built-in pipeline disagree about the colour property name.
            if (created.HasProperty("_BaseColor")) created.SetColor("_BaseColor", color);
            if (created.HasProperty("_Color")) created.SetColor("_Color", color);

            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }

        /// <summary>Saves the open scene, creating the asset when it has never been saved.</summary>
        public static PgReport SaveScene(string path = null)
        {
            var report = new PgReport("author.saveScene");
            var scene = SceneManager.GetActiveScene();

            var destination = path ?? (string.IsNullOrEmpty(scene.path) ? null : scene.path);
            if (string.IsNullOrEmpty(destination))
                return report.Failed("This scene has never been saved. Pass a path such as Assets/Scenes/Main.unity.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(PgPaths.ProjectRoot, destination)) ?? ".");

            if (!EditorSceneManager.SaveScene(scene, destination))
                return report.Failed($"Unity refused to save the scene to '{destination}'.");

            report.Datum("path", destination);
            report.Add(PgFinding.Info("author.sceneSaved", $"Saved to {destination}"));
            return report;
        }

        /// <summary>Creates a new empty scene and makes it active.</summary>
        public static PgReport NewScene(bool empty = true)
        {
            var report = new PgReport("author.newScene");

            // Starting from a clean scene avoids colliding with the default camera and
            // light, which is the most common way generated scenes go wrong.
            EditorSceneManager.NewScene(
                empty ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects,
                NewSceneMode.Single);

            report.Add(PgFinding.Info("author.newScene", empty ? "Created an empty scene" : "Created a default scene"));
            return report;
        }

        /// <summary>Adds a scene to the build settings, so a build has something to load.</summary>
        public static PgReport AddSceneToBuild(string scenePath, bool enabled = true)
        {
            var report = new PgReport("author.buildScene");

            if (!File.Exists(Path.Combine(PgPaths.ProjectRoot, scenePath)))
                return report.Failed($"No scene asset at '{scenePath}'. Save the scene first.");

            var scenes = EditorBuildSettings.scenes.ToList();
            var existing = scenes.FirstOrDefault(s => s.path == scenePath);

            if (existing != null) existing.enabled = enabled;
            else scenes.Add(new EditorBuildSettingsScene(scenePath, enabled));

            EditorBuildSettings.scenes = scenes.ToArray();

            report.Add(PgFinding.Info("author.buildScene", $"'{scenePath}' is in the build settings"));
            return report;
        }

        /// <summary>Saves an object as a prefab and replaces the scene instance with it.</summary>
        public static PgReport CreatePrefab(string target, string assetPath)
        {
            var report = new PgReport("author.prefab");
            var found = PgLocate.Find(target);
            if (found == null) return report.Failed($"No object matching '{target}'.");

            if (!assetPath.EndsWith(".prefab")) assetPath += ".prefab";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(PgPaths.ProjectRoot, assetPath)) ?? ".");

            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                found.gameObject, assetPath, InteractionMode.UserAction);

            if (prefab == null) return report.Failed($"Unity refused to save a prefab to '{assetPath}'.");

            report.Datum("path", assetPath);
            report.Add(PgFinding.Info("author.prefab", $"Saved '{target}' as {assetPath}"));
            return report;
        }

        internal static void ApplyProperties(PgReport report, UnityEngine.Object subject,
            Dictionary<string, object> set, string label)
        {
            if (set == null) return;

            foreach (var pair in set)
            {
                var error = PgPropertyBinder.Set(subject, pair.Key, pair.Value);
                if (error == null) continue;

                report.Add(PgFinding
                    .Fail("author.setFailed", $"Could not set {label}.{pair.Key}: {error}")
                    .With(pair.Value?.ToString(), "not applied"));
            }
        }

        static void ApplyTag(PgReport report, GameObject go, string tag)
        {
            try
            {
                go.tag = tag;
            }
            catch (UnityException)
            {
                report.Add(PgFinding
                    .Warn("author.missingTag", $"The tag '{tag}' does not exist in this project")
                    .Fix($"Add it under Project Settings > Tags and Layers, then set it again."));
            }
        }

        static void ApplyLayer(PgReport report, GameObject go, string layer)
        {
            var index = LayerMask.NameToLayer(layer);
            if (index < 0)
            {
                report.Add(PgFinding.Warn("author.missingLayer", $"The layer '{layer}' does not exist"));
                return;
            }

            go.layer = index;
        }

        static Vector3 ToVector(IReadOnlyList<float> values, float fill = 0f)
        {
            var x = values.Count > 0 ? values[0] : fill;
            var y = values.Count > 1 ? values[1] : fill;
            var z = values.Count > 2 ? values[2] : fill;
            return new Vector3(x, y, z);
        }

        internal static void MarkSceneDirty()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
