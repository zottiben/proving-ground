using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Writing and reading C# in the project.
    ///
    /// An agent can already write files directly, so this exists for the parts it cannot
    /// do from outside: importing the result, triggering a rebuild, and reporting the
    /// compiler errors back in the same breath. Writing a script and not knowing whether
    /// it compiled is how an agent ends up confidently building on top of broken code.
    /// </summary>
    public static class PgScriptAuthor
    {
        /// <summary>Writes a script and asks Unity to rebuild. Poll CompileStatus afterwards.</summary>
        public static PgReport Write(string path, string contents, bool compile = true)
        {
            var report = new PgReport("script.write");

            if (string.IsNullOrEmpty(path)) return report.Failed("No path was given.");
            if (!path.EndsWith(".cs")) path += ".cs";

            if (!path.StartsWith("Assets/"))
                return report.Failed($"Scripts must live under Assets/. Got '{path}'.");

            if (contents == null) return report.Failed("No contents were given.");

            var typeName = DeclaredTypeName(contents);
            var fileName = Path.GetFileNameWithoutExtension(path);

            // Unity requires a MonoBehaviour's file name to match its class name, and a
            // mismatch fails at import with a message that does not say so plainly.
            if (typeName != null && typeName != fileName && MentionsMonoBehaviour(contents))
                report.Add(PgFinding
                    .Warn("script.nameMismatch",
                        $"The file is '{fileName}.cs' but declares '{typeName}'")
                    .Fix("Unity will refuse to attach this component until the names match."));

            var absolute = Path.Combine(PgPaths.ProjectRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? ".");

            var existed = File.Exists(absolute);
            File.WriteAllText(absolute, contents);

            report.Datum("path", path);
            report.Datum("existed", existed);
            report.Add(PgFinding.Info("script.written", $"{(existed ? "Updated" : "Created")} {path}").At(path));

            if (!compile) return report;

            PgCompile.Reset();
            var generation = PgCompile.Request();
            report.Datum("compileRequestedAt", generation);
            report.Add(PgFinding
                .Info("script.compiling", "Unity is rebuilding")
                .Fix("Poll CompileStatus until settled is true; the bridge drops briefly during the domain reload."));

            return report;
        }

        public static PgReport Read(string path)
        {
            var report = new PgReport("script.read");
            var absolute = Path.Combine(PgPaths.ProjectRoot, path);

            if (!File.Exists(absolute)) return report.Failed($"No file at '{path}'.");

            report.Datum("path", path);
            report.Datum("contents", File.ReadAllText(absolute));
            return report;
        }

        public static PgReport Delete(string path)
        {
            var report = new PgReport("script.delete");

            if (!AssetDatabase.DeleteAsset(path)) return report.Failed($"Unity refused to delete '{path}'.");

            PgCompile.Reset();
            PgCompile.Request();
            report.Add(PgFinding.Info("script.deleted", $"Deleted {path}").At(path));
            return report;
        }

        /// <summary>Lists scripts under a folder, for orienting in an unfamiliar project.</summary>
        public static PgReport List(string folder = "Assets", string filter = null)
        {
            var report = new PgReport("script.list");
            var absolute = Path.Combine(PgPaths.ProjectRoot, folder);

            if (!Directory.Exists(absolute)) return report.Failed($"No folder at '{folder}'.");

            var files = Directory.GetFiles(absolute, "*.cs", SearchOption.AllDirectories)
                .Select(PgPaths.Relative)
                .Where(p => string.IsNullOrEmpty(filter) ||
                            p.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p)
                .ToList();

            report.Datum("count", files.Count);
            report.Datum("scripts", files.Take(300).ToList());
            report.Add(PgFinding.Info("script.list", $"{files.Count} script(s) under {folder}"));
            return report;
        }

        static string DeclaredTypeName(string contents)
        {
            var match = Regex.Match(contents,
                @"\b(?:public|internal|sealed|abstract|partial|\s)*\bclass\s+(\w+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        static bool MentionsMonoBehaviour(string contents) =>
            contents.IndexOf("MonoBehaviour", StringComparison.Ordinal) >= 0;
    }
}
