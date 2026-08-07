using System.IO;
using UnityEngine;

namespace ProvingGround
{
    /// <summary>
    /// Where Proving Ground keeps things. Contracts are committed; artifacts are not.
    /// </summary>
    public static class PgPaths
    {
        /// <summary>Project root, i.e. the folder containing <c>Assets</c>.</summary>
        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>Design intent, hand- or agent-authored, committed to version control.</summary>
        public static string Contracts => Path.Combine(ProjectRoot, "ProvingGround", "Contracts");

        /// <summary>Captured baselines: reference images, characterization snapshots, genre norms.</summary>
        public static string Baselines => Path.Combine(ProjectRoot, "ProvingGround", "Baselines");

        /// <summary>Recorded input traces and scenario definitions.</summary>
        public static string Scenarios => Path.Combine(ProjectRoot, "ProvingGround", "Scenarios");

        /// <summary>Design docs the process layer maintains.</summary>
        public static string Design => Path.Combine(ProjectRoot, "ProvingGround", "Design");

        /// <summary>Run output. Regenerated, never committed.</summary>
        public static string Artifacts => Path.Combine(ProjectRoot, "ProvingGround", "Artifacts");

        public static string Report(string tool) =>
            Path.Combine(Artifacts, "reports", tool + ".json");

        public static string Capture(string name) =>
            Path.Combine(Artifacts, "captures", name);

        public static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        public static string EnsureParent(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            return filePath;
        }

        /// <summary>Path relative to the project root, with forward slashes, for stable reporting.</summary>
        public static string Relative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;
            var root = ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var normalised = Path.GetFullPath(absolutePath).Replace('\\', '/');
            return normalised.StartsWith(root) ? normalised.Substring(root.Length) : normalised;
        }
    }
}
