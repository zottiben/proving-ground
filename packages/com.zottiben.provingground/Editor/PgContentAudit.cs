using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProvingGround.Contracts;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Project hygiene: the defects that are invisible in the Editor and expensive at
    /// build time or, worse, at runtime on someone else's machine.
    ///
    /// The broken-reference check is the one worth understanding. A serialized reference
    /// that is deliberately null and one whose target has been deleted look identical in
    /// the inspector, but they are distinguishable in the serialized data: a broken
    /// reference still carries an instance id. Only those are reported, so the check does
    /// not drown in intentional nulls.
    /// </summary>
    public static class PgContentAudit
    {
        public static PgReport Run(PgContentRules rules = null)
        {
            var report = new PgReport("content");
            rules ??= PgContentRules.Load() ?? PgContentRules.Starter();

            var assetPaths = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/"))
                .Where(p => !PgGlob.MatchesAny(p, rules.Ignore))
                .ToList();

            report.Datum("assetsScanned", assetPaths.Count);

            if (rules.ForbidMissingScripts || rules.ForbidMissingReferences)
                CheckPrefabs(report, assetPaths, rules);

            if (rules.ForbidMissingScripts) CheckOpenScene(report);
            if (rules.ReportDuplicateAssets) CheckDuplicates(report, assetPaths);
            if (rules.ReportOrphanedAssets) CheckOrphans(report, assetPaths);
            if (rules.AssetRules != null && rules.AssetRules.Count > 0) CheckImportSettings(report, assetPaths, rules);

            if (report.Findings.Count == 0)
                report.Add(PgFinding.Info("content.clean", $"{assetPaths.Count} assets scanned with no issues"));

            return report;
        }

        static void CheckPrefabs(PgReport report, IEnumerable<string> assetPaths, PgContentRules rules)
        {
            foreach (var path in assetPaths.Where(p => p.EndsWith(".prefab")))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        if (rules.ForbidMissingScripts)
                            report.Add(PgFinding
                                .Fail("content.missingScript", "Prefab has a component whose script is missing")
                                .At(path)
                                .Fix("The script was deleted or renamed. Restore it, or remove the component."));
                        continue;
                    }

                    if (rules.ForbidMissingReferences) CheckBrokenReferences(report, component, path);
                }
            }
        }

        static void CheckOpenScene(PgReport report)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;

            foreach (var root in scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null) continue;
                report.Add(PgFinding
                    .Fail("content.missingScript", "An object in the open scene has a missing script")
                    .At($"{scene.name}/{root.name}"));
            }
        }

        static void CheckBrokenReferences(PgReport report, Component component, string assetPath)
        {
            using var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();

            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                // An id with no resolvable object is a reference whose target is gone.
                // A genuinely empty field has neither.
                if (property.objectReferenceValue != null) continue;
#if UNITY_6000_5_OR_NEWER
                // 6.5 retired instance ids on SerializedProperty in favour of EntityId, and
                // retired them as an error rather than a warning, so the old spelling does
                // not merely warn there - it fails the compile.
                if (property.objectReferenceEntityIdValue == default) continue;
#else
                if (property.objectReferenceInstanceIDValue == 0) continue;
#endif

                report.Add(PgFinding
                    .Fail("content.brokenReference",
                        $"'{component.GetType().Name}.{property.displayName}' points at an asset that no longer exists")
                    .At($"{assetPath} :: {PgLocate.PathOf(component.transform)}")
                    .Fix("Reassign it, or clear the field deliberately."));
            }
        }

        static void CheckDuplicates(PgReport report, IEnumerable<string> assetPaths)
        {
            var byHash = new Dictionary<string, List<string>>();

            foreach (var path in assetPaths)
            {
                if (Directory.Exists(path)) continue;
                if (path.EndsWith(".meta") || path.EndsWith(".cs") || path.EndsWith(".asmdef")) continue;

                var full = Path.Combine(PgPaths.ProjectRoot, path);
                if (!File.Exists(full)) continue;

                var info = new FileInfo(full);
                // Hashing every asset is slow and pointless for tiny files.
                if (info.Length < 4096) continue;

                using var stream = File.OpenRead(full);
                using var sha = SHA256.Create();
                var hash = System.Convert.ToBase64String(sha.ComputeHash(stream));

                if (!byHash.TryGetValue(hash, out var list))
                    byHash[hash] = list = new List<string>();
                list.Add(path);
            }

            foreach (var group in byHash.Values.Where(g => g.Count > 1))
                report.Add(PgFinding
                    .Warn("content.duplicate", $"{group.Count} assets have identical contents")
                    .At(group[0])
                    .Datum("paths", group)
                    .Fix("Each copy ships separately. Keep one and repoint the references."));
        }

        static void CheckOrphans(PgReport report, IReadOnlyList<string> assetPaths)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                report.Add(PgFinding
                    .Warn("content.noBuildScenes", "No scenes are enabled in the build settings")
                    .Fix("Nothing can be reported as orphaned, and the build will have nothing to load."));
                return;
            }

            var reachable = new HashSet<string>(AssetDatabase.GetDependencies(scenes, true));

            // Resources and StreamingAssets are loaded by name at runtime, so being
            // unreferenced from a scene proves nothing about them.
            var orphans = assetPaths
                .Where(p => !reachable.Contains(p))
                .Where(p => !Directory.Exists(p))
                .Where(p => !p.Contains("/Resources/") && !p.Contains("/StreamingAssets/") &&
                            !p.Contains("/Editor/") && !p.EndsWith(".cs") && !p.EndsWith(".asmdef"))
                .ToList();

            if (orphans.Count == 0) return;

            report.Add(PgFinding
                .Info("content.orphans", $"{orphans.Count} assets are not reachable from any enabled build scene")
                .Datum("sample", orphans.Take(20).ToList())
                .Fix("Some will be loaded dynamically. The rest are dead weight in the project, though not in the build."));
        }

        static void CheckImportSettings(PgReport report, IEnumerable<string> assetPaths, PgContentRules rules)
        {
            foreach (var path in assetPaths)
            {
                var rule = rules.AssetRules.FirstOrDefault(r => PgGlob.Matches(path, r.Match));
                if (rule == null) continue;

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;

                if (!string.IsNullOrEmpty(rule.NamePattern))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (!Regex.IsMatch(name, rule.NamePattern))
                        Add(report, rule, "content.naming", $"'{name}' does not match the required naming pattern", path)
                            .With(rule.NamePattern, name);
                }

                if (rule.MaxFileSizeMb.HasValue)
                {
                    var full = Path.Combine(PgPaths.ProjectRoot, path);
                    if (File.Exists(full))
                    {
                        var megabytes = new FileInfo(full).Length / 1024d / 1024d;
                        if (megabytes > rule.MaxFileSizeMb.Value)
                            Add(report, rule, "content.fileSize", "Asset is larger than the rule allows", path)
                                .With($"≤ {rule.MaxFileSizeMb}MB", $"{megabytes:0.##}MB");
                    }
                }

                if (importer is TextureImporter texture) CheckTexture(report, rule, texture, path);
                else if (importer is AudioImporter audio) CheckAudio(report, rule, audio, path);
            }
        }

        static void CheckTexture(PgReport report, PgAssetRule rule, TextureImporter importer, string path)
        {
            if (rule.MaxTextureSize.HasValue && importer.maxTextureSize > rule.MaxTextureSize.Value)
                Add(report, rule, "content.textureSize", "Texture import size exceeds the rule", path)
                    .With($"≤ {rule.MaxTextureSize}", importer.maxTextureSize.ToString());

            if (!string.IsNullOrEmpty(rule.TextureType) &&
                !string.Equals(importer.textureType.ToString(), rule.TextureType, System.StringComparison.OrdinalIgnoreCase))
                Add(report, rule, "content.textureType", "Texture is imported as the wrong type", path)
                    .With(rule.TextureType, importer.textureType.ToString());

            if (rule.RequireMipmaps.HasValue && importer.mipmapEnabled != rule.RequireMipmaps.Value)
                Add(report, rule, "content.mipmaps",
                        rule.RequireMipmaps.Value ? "Texture should have mipmaps" : "Texture should not have mipmaps", path)
                    .With(rule.RequireMipmaps.Value.ToString(), importer.mipmapEnabled.ToString());

            if (rule.RequireReadWriteDisabled == true && importer.isReadable)
                Add(report, rule, "content.readWrite", "Texture is readable, which doubles its memory cost", path)
                    .Fix("Turn off Read/Write unless the game reads pixels from it at runtime.");
        }

        static void CheckAudio(PgReport report, PgAssetRule rule, AudioImporter importer, string path)
        {
            if (string.IsNullOrEmpty(rule.AudioLoadType)) return;

            var settings = importer.defaultSampleSettings;
            if (!string.Equals(settings.loadType.ToString(), rule.AudioLoadType, System.StringComparison.OrdinalIgnoreCase))
                Add(report, rule, "content.audioLoadType", "Audio clip uses the wrong load type", path)
                    .With(rule.AudioLoadType, settings.loadType.ToString());
        }

        static PgFinding Add(PgReport report, PgAssetRule rule, string id, string message, string path)
        {
            var finding = new PgFinding
            {
                Id = id,
                Severity = rule.Severity,
                Message = message,
                Subject = path,
                Remedy = rule.Note
            };

            report.Add(finding);
            return finding;
        }
    }
}
