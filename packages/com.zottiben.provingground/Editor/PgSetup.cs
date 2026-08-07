using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using ProvingGround.Contracts;
using ProvingGround.Actuation;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Creates the folder layout and starter contracts in a project that has just
    /// installed the package.
    ///
    /// Everything written here is plain JSON and text under <c>ProvingGround/</c>, next to
    /// <c>Assets</c> rather than inside it. That keeps design intent out of the asset
    /// database, where an agent editing it would churn GUIDs and .meta files for no reason.
    /// </summary>
    public static class PgSetup
    {
        public const string GitIgnoreContents =
            "# Proving Ground run output. Regenerated on every run.\nArtifacts/\n";

        /// <summary>True when the project has been initialised.</summary>
        public static bool IsInitialised => Directory.Exists(PgPaths.Contracts);

        /// <summary>
        /// Writes the folder layout and any contract that does not already exist. Safe to
        /// re-run: existing contracts are never overwritten.
        /// </summary>
        public static PgReport Initialise(string genre = "fps")
        {
            var report = new PgReport("init");

            foreach (var directory in new[]
                     {
                         PgPaths.Contracts, PgPaths.Baselines, PgPaths.Scenarios,
                         PgPaths.Design, PgPaths.Artifacts
                     })
            {
                PgPaths.EnsureDirectory(directory);
            }

            File.WriteAllText(Path.Combine(PgPaths.ProjectRoot, "ProvingGround", ".gitignore"), GitIgnoreContents);

            Created(report, PgFeelSpec.DefaultPath, () => PgFeelSpec.Starter(genre).Save());
            Created(report, PgQualityGates.DefaultPath, () => PgQualityGates.Starter().Save());
            Created(report, PgContentRules.DefaultPath, () => PgContentRules.Starter().Save());
            Created(report, PgUiManifest.DefaultPath, () => StarterUiManifest().Save());
            Created(report, PgAudioContract.DefaultPath, () => StarterAudioContract().Save());

            var smokePath = PgScenario.PathFor("smoke");
            Created(report, smokePath, () => PgScenario.Smoke().Save(smokePath));

            Created(report, Path.Combine(PgPaths.Design, "pillars.md"), () =>
                File.WriteAllText(Path.Combine(PgPaths.Design, "pillars.md"), PgProcess.PillarsTemplate));

            report.Datum("root", PgPaths.Relative(Path.Combine(PgPaths.ProjectRoot, "ProvingGround")));
            report.Datum("genre", genre);

            if (report.Findings.Count == 0)
                report.Add(PgFinding.Info("init.alreadyDone",
                    "Everything already exists; nothing was overwritten"));

            AssetDatabase.Refresh();
            return report;
        }

        static void Created(PgReport report, string path, System.Action write)
        {
            if (File.Exists(path))
            {
                report.Add(PgFinding.Info("init.kept", $"Kept existing {Path.GetFileName(path)}")
                    .At(PgPaths.Relative(path)));
                return;
            }

            write();
            report.Add(PgFinding.Info("init.created", $"Created {Path.GetFileName(path)}")
                .At(PgPaths.Relative(path)));
        }

        static PgUiManifest StarterUiManifest() => new PgUiManifest
        {
            Note = "Tokens are the design system. Elements are matched by name or by path suffix. " +
                   "Reference a token from an expectation with $name.",
            Tokens = new Dictionary<string, string>
            {
                ["color.ink"] = "#12121AFF",
                ["color.paper"] = "#F5F3EFFF",
                ["color.brand"] = "#3D7BFFFF",
                ["size.body"] = "18",
                ["size.heading"] = "32"
            },
            Elements = new Dictionary<string, PgUiElementSpec>
            {
                ["exampleTitle"] = new PgUiElementSpec
                {
                    Match = "Title",
                    Required = false,
                    Note = "Delete this once you have real elements. It is here to show the shape.",
                    Expect = new Dictionary<string, string>
                    {
                        ["color"] = "$color.ink",
                        ["fontSize"] = "$size.heading"
                    }
                }
            }
        };

        static PgAudioContract StarterAudioContract() => new PgAudioContract
        {
            Note = "Event ids are shared by the code that fires them and the checks that verify them. " +
                   "Call PgAudio.Fire(\"id\") where the sound plays, or run the watcher to infer ids from clip names.",
            Events = new Dictionary<string, PgAudioEventSpec>
            {
                ["footstep"] = new PgAudioEventSpec
                {
                    Category = "sfx",
                    Required = false,
                    MaxPerSecond = 4,
                    MaxLengthSeconds = 1.0,
                    Note = "A footstep firing more than a few times a second is the classic per-frame bug."
                }
            }
        };
    }
}
