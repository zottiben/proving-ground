using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using ProvingGround.Authoring;
using ProvingGround.Perception;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// The authoring half of the agent surface: making things, as opposed to measuring
    /// them.
    ///
    /// Kept deliberately small. Most level building should go through a recipe and
    /// <see cref="BuildScene"/> rather than through hundreds of individual calls, and the
    /// direct operations here are for iterating on what a recipe produced.
    /// </summary>
    public static partial class PgApi
    {
        // ---- scenes -------------------------------------------------------------------

        /// <summary>Creates a new scene. Empty by default, which avoids clashing with the default camera and light.</summary>
        public static string NewScene(bool empty = true) => Emit(PgAuthor.NewScene(empty));

        /// <summary>Saves the open scene, creating the asset if it has never been saved.</summary>
        public static string SaveScene(string path = null) => Emit(PgAuthor.SaveScene(path));

        /// <summary>Adds a scene to the build settings.</summary>
        public static string AddSceneToBuild(string scenePath, bool enabled = true) =>
            Emit(PgAuthor.AddSceneToBuild(scenePath, enabled));

        /// <summary>Opens a scene asset.</summary>
        public static string OpenScene(string path)
        {
            var report = new PgReport("author.openScene");

            if (!System.IO.File.Exists(System.IO.Path.Combine(PgPaths.ProjectRoot, path)))
                return Emit(report.Failed($"No scene asset at '{path}'."));

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                path, UnityEditor.SceneManagement.OpenSceneMode.Single);

            report.Add(PgFinding.Info("author.openScene", $"Opened {path}"));
            return Emit(report);
        }

        // ---- recipes ------------------------------------------------------------------

        /// <summary>
        /// Builds the open scene from a named recipe in <c>ProvingGround/Scenes</c>.
        /// Re-running converges rather than duplicating.
        /// </summary>
        public static string BuildScene(string recipe)
        {
            var loaded = PgSceneRecipe.LoadByName(recipe);
            if (loaded == null)
                return Emit(new PgReport("build:" + recipe)
                    .Failed($"No recipe named '{recipe}' in {PgPaths.Relative(PgSceneRecipe.DirectoryPath)}."));

            return Emit(PgSceneBuilder.Build(loaded));
        }

        /// <summary>
        /// Saves a recipe from JSON and builds it in one call. This is the main way to
        /// create a level: one round trip instead of one per object.
        /// </summary>
        public static string WriteAndBuildScene(string recipeJson, bool build = true)
        {
            var report = new PgReport("build");

            PgSceneRecipe recipe;
            try
            {
                recipe = PgJson.Parse<PgSceneRecipe>(recipeJson);
            }
            catch (Exception e)
            {
                return Emit(report.Failed($"The recipe is not valid JSON: {e.Message}"));
            }

            if (recipe == null) return Emit(report.Failed("The recipe parsed to nothing."));
            if (string.IsNullOrEmpty(recipe.Name)) return Emit(report.Failed("The recipe needs a name."));

            recipe.Save();
            AssetDatabase.Refresh();

            if (!build)
            {
                report.Add(PgFinding.Info("build.saved", $"Saved recipe '{recipe.Name}'")
                    .At(PgPaths.Relative(PgSceneRecipe.PathFor(recipe.Name))));
                return Emit(report);
            }

            var built = PgSceneBuilder.Build(recipe);
            built.Add(PgFinding.Info("build.saved", $"Saved recipe '{recipe.Name}'")
                .At(PgPaths.Relative(PgSceneRecipe.PathFor(recipe.Name))));
            return Emit(built);
        }

        /// <summary>Lists the scene recipes in this project.</summary>
        public static string SceneRecipes()
        {
            var files = PgSceneRecipe.All().ToList();
            if (files.Count == 0)
                return $"No recipes in {PgPaths.Relative(PgSceneRecipe.DirectoryPath)}. " +
                       "Create one with WriteAndBuildScene.";

            return string.Join("\n", files.Select(f =>
            {
                var recipe = PgSceneRecipe.Load(f);
                return recipe == null
                    ? $"  {System.IO.Path.GetFileName(f)}  (could not parse)"
                    : $"  {recipe.Name}  {recipe.Flatten().Count()} object spec(s), seed {recipe.Seed}";
            }));
        }

        // ---- direct authoring ---------------------------------------------------------

        public static string CreateObject(string name, string primitive = null, string parent = null,
            float[] position = null, float[] rotation = null, float[] scale = null,
            string prefab = null, string tag = null, string layer = null) =>
            Emit(PgAuthor.Create(name, primitive, parent, position, rotation, scale, prefab, tag, layer));

        public static string DeleteObject(string target) => Emit(PgAuthor.Delete(target));

        public static string ModifyObject(string target, float[] position = null, float[] rotation = null,
            float[] scale = null, string parent = null, string name = null, bool? active = null,
            string tag = null, string layer = null, bool worldSpace = false) =>
            Emit(PgAuthor.Modify(target, position, rotation, scale, parent, name, active, tag, layer, worldSpace));

        public static string AddComponent(string target, string component, JObject set = null) =>
            Emit(PgAuthor.AddComponent(target, component, ToDictionary(set)));

        public static string RemoveComponent(string target, string component) =>
            Emit(PgAuthor.RemoveComponent(target, component));

        public static string SetProperties(string target, JObject set, string component = null) =>
            Emit(PgAuthor.SetProperties(target, component, ToDictionary(set)));

        public static string SetMaterial(string target, string material) =>
            Emit(PgAuthor.SetMaterial(target, material));

        public static string CreatePrefab(string target, string assetPath) =>
            Emit(PgAuthor.CreatePrefab(target, assetPath));

        /// <summary>Finds objects by name, tag, layer or component type.</summary>
        public static string Find(string name = null, string tag = null, string component = null,
            int max = 50)
        {
            var report = new PgReport("author.find");
            var all = UnityEngine.Object
                .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .AsEnumerable();

            if (!string.IsNullOrEmpty(name))
                all = all.Where(t => t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!string.IsNullOrEmpty(tag))
                all = all.Where(t =>
                {
                    try
                    {
                        return t.CompareTag(tag);
                    }
                    catch (UnityException)
                    {
                        return false;
                    }
                });

            if (!string.IsNullOrEmpty(component))
            {
                var type = PgTypes.Component(component);
                if (type == null)
                    return Emit(report.Failed($"No component type '{component}'. " +
                                              $"Close matches: {string.Join(", ", PgTypes.Suggest(component))}"));
                all = all.Where(t => t.GetComponent(type) != null);
            }

            var matches = all.Take(max).Select(t => PgLocate.PathOf(t)).ToList();
            report.Datum("count", matches.Count);
            report.Datum("paths", matches);
            report.Add(PgFinding.Info("author.find", $"{matches.Count} match(es)"));
            return Emit(report);
        }

        /// <summary>Everything on one object, for deciding what to change.</summary>
        public static string Inspect(string target)
        {
            var report = new PgReport("author.inspect");
            var found = PgLocate.Find(target);
            if (found == null) return Emit(report.Failed($"No object matching '{target}'."));

            var components = found.GetComponents<Component>()
                .Select(c => c == null ? "<missing script>" : c.GetType().Name)
                .ToList();

            report.Datum("path", PgLocate.PathOf(found));
            report.Datum("active", found.gameObject.activeInHierarchy);
            report.Datum("tag", found.tag);
            report.Datum("layer", LayerMask.LayerToName(found.gameObject.layer));
            report.Datum("position", new[] { found.position.x, found.position.y, found.position.z });
            report.Datum("localScale", new[] { found.localScale.x, found.localScale.y, found.localScale.z });
            report.Datum("components", components);
            report.Datum("children", found.childCount);

            report.Add(PgFinding.Info("author.inspect",
                $"{found.name}: {string.Join(", ", components)}"));
            return Emit(report);
        }

        // ---- scripts ------------------------------------------------------------------

        /// <summary>
        /// Writes a C# script and asks Unity to rebuild.
        ///
        /// The bridge drops for a moment afterwards, because compiling reloads the app
        /// domain. Poll <see cref="CompileStatus"/> until it reports settled, retrying
        /// through the connection gap.
        /// </summary>
        public static string WriteScript(string path, string contents, bool compile = true) =>
            Emit(PgScriptAuthor.Write(path, contents, compile));

        public static string ReadScript(string path) => Emit(PgScriptAuthor.Read(path));

        public static string DeleteScript(string path) => Emit(PgScriptAuthor.Delete(path));

        public static string ListScripts(string folder = "Assets", string filter = null) =>
            Emit(PgScriptAuthor.List(folder, filter));

        /// <summary>Whether Unity has finished compiling, and what broke if anything did.</summary>
        public static string CompileStatus() => PgJson.Stringify(PgCompile.Status());

        /// <summary>Imports changed files and rebuilds.</summary>
        public static string Refresh(bool forceRecompile = false)
        {
            PgCompile.Reset();
            PgCompile.Request(forceRecompile);
            return "{\"state\":\"refreshing\"}";
        }

        // ---- console ------------------------------------------------------------------

        /// <summary>Editor console output. Unity explains most of its failures here and nowhere else.</summary>
        public static string Console(string minSeverity = null, int max = 60)
        {
            var entries = PgConsole.Entries(minSeverity, max);
            return entries.Count == 0
                ? "console is empty"
                : string.Join("\n", entries.Select(e => e.ToString()));
        }

        public static string ClearConsole()
        {
            PgConsole.Clear();
            return "{\"ok\":true}";
        }

        // ---- batching -----------------------------------------------------------------

        /// <summary>
        /// Runs several operations in one call.
        ///
        /// Round trips dominate the cost of authoring: creating a level one object at a
        /// time means hundreds of them, and the Editor has to be pumped between each.
        /// Pass a JSON array of <c>{"method": "...", "args": {...}}</c> and they all run
        /// against the same frame.
        /// </summary>
        /// <param name="stopOnError">Stop at the first failure rather than pressing on.</param>
        public static string Batch(string operations, bool stopOnError = true)
        {
            var report = new PgReport("batch");

            JArray parsed;
            try
            {
                parsed = JArray.Parse(operations);
            }
            catch (Exception e)
            {
                return Emit(report.Failed($"Operations must be a JSON array: {e.Message}"));
            }

            var succeeded = 0;

            for (var i = 0; i < parsed.Count; i++)
            {
                var operation = parsed[i] as JObject;
                var method = operation?["method"]?.ToString();

                if (string.IsNullOrEmpty(method))
                {
                    report.Add(PgFinding.Fail($"batch.{i}", "Operation has no 'method'"));
                    if (stopOnError) break;
                    continue;
                }

                string raw;
                try
                {
                    raw = PgBridge.InvokeMethod(method, operation["args"] as JObject ?? new JObject());
                }
                catch (Exception e)
                {
                    report.Add(PgFinding.Fail($"batch.{i}", $"{method} threw: {e.InnerException?.Message ?? e.Message}"));
                    if (stopOnError) break;
                    continue;
                }

                var nested = TryParseReport(raw);
                if (nested == null)
                {
                    succeeded++;
                    continue;
                }

                // Roll the nested findings up so one batch produces one readable report.
                foreach (var finding in nested.Findings.Where(f => f.Severity >= PgSeverity.Warn))
                    report.Add(finding);

                if (nested.Ok && nested.Passed)
                {
                    succeeded++;
                    continue;
                }

                report.Add(PgFinding.Fail($"batch.{i}", $"{method} failed: {nested.Error ?? nested.Summary}"));
                if (stopOnError) break;
            }

            report.Datum("requested", parsed.Count);
            report.Datum("succeeded", succeeded);
            report.Summary = succeeded == parsed.Count
                ? $"All {parsed.Count} operation(s) succeeded"
                : $"{succeeded} of {parsed.Count} operation(s) succeeded";

            return Emit(report);
        }

        static PgReport TryParseReport(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.TrimStart().FirstOrDefault() != '{') return null;
            try
            {
                var report = PgJson.Parse<PgReport>(raw);
                return string.IsNullOrEmpty(report?.Tool) ? null : report;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static Dictionary<string, object> ToDictionary(JObject json) =>
            json?.Properties().ToDictionary(p => p.Name, p => (object)p.Value);
    }
}
