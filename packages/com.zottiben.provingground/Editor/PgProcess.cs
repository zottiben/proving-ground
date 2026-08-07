using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ProvingGround.EditorTools
{
    /// <summary>One production gate, and the evidence that has to exist to pass it.</summary>
    [Serializable]
    public sealed class PgMilestone
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("phase")] public string Phase;
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("intent")] public string Intent;

        /// <summary>Design artifacts that must exist, relative to <c>ProvingGround/</c>.</summary>
        [JsonProperty("requiredArtifacts")] public List<string> RequiredArtifacts = new List<string>();

        /// <summary>Checks that must pass, by tool name: feel, ui, audio, scene, content, project.</summary>
        [JsonProperty("requiredChecks")] public List<string> RequiredChecks = new List<string>();

        /// <summary>Human judgments the harness cannot make. Recorded, not automated.</summary>
        [JsonProperty("humanSignOff")] public List<string> HumanSignOff = new List<string>();
    }

    /// <summary>
    /// The production methodology, encoded so that a milestone is passed by producing
    /// evidence rather than by an agent asserting it finished.
    ///
    /// The phases follow standard studio practice: a conception phase that stays cheap and
    /// disposable, a pre-production phase whose real output is a vertical slice at final
    /// quality, a production phase that scales content against a locked design, and a
    /// polish phase held separate from production rather than assumed to happen inside it.
    /// Pre-production is where pipelines are won or lost and is chronically underinvested,
    /// so its gates here are the strictest.
    /// </summary>
    public static class PgProcess
    {
        public const string FileName = "milestones.json";

        public static string DefaultPath => Path.Combine(PgPaths.Design, FileName);

        /// <summary>The standard milestone ladder, used when a project has not defined its own.</summary>
        public static List<PgMilestone> Standard() => new List<PgMilestone>
        {
            new PgMilestone
            {
                Id = "concept",
                Phase = "Conception",
                DisplayName = "Concept",
                Intent = "Establish what the game is and who it is for, cheaply enough to throw away.",
                RequiredArtifacts = new List<string> { "Design/pillars.md", "Design/one-pager.md" },
                HumanSignOff = new List<string> { "The pillars describe this game and not a genre in general." }
            },
            new PgMilestone
            {
                Id = "prototype",
                Phase = "Pre-production",
                DisplayName = "Proof of concept",
                Intent = "Prove the core loop is fun before anything is built to last.",
                RequiredArtifacts = new List<string> { "Contracts/feel.json", "Scenarios/smoke.json" },
                RequiredChecks = new List<string> { "scenario" },
                HumanSignOff = new List<string> { "The core loop is worth repeating without being asked to." }
            },
            new PgMilestone
            {
                Id = "first-playable",
                Phase = "Pre-production",
                DisplayName = "First playable",
                Intent = "A player can start, play and finish a slice unaided, at any quality.",
                RequiredArtifacts = new List<string> { "Contracts/feel.json", "Contracts/gates.json" },
                RequiredChecks = new List<string> { "scenario", "scene", "probe" },
                HumanSignOff = new List<string> { "Someone who has never seen it completed the slice without help." }
            },
            new PgMilestone
            {
                Id = "vertical-slice",
                Phase = "Pre-production",
                DisplayName = "Vertical slice",
                Intent = "A small section at final quality, proving the bar and the pipeline that reaches it.",
                RequiredArtifacts = new List<string>
                {
                    "Design/gdd.md", "Contracts/feel.json", "Contracts/ui.json",
                    "Contracts/audio.json", "Contracts/gates.json"
                },
                RequiredChecks = new List<string> { "scenario", "scene", "ui", "audio", "content", "probe" },
                HumanSignOff = new List<string>
                {
                    "This section is genuinely at shipping quality, not nearly.",
                    "The pipeline that produced it can produce the rest of the game."
                }
            },
            new PgMilestone
            {
                Id = "alpha",
                Phase = "Production",
                DisplayName = "Alpha / feature complete",
                Intent = "Every system exists. Content may be missing; features may not.",
                RequiredArtifacts = new List<string> { "Design/gdd.md" },
                RequiredChecks = new List<string> { "scenario", "scene", "ui", "audio", "content", "project", "probe" },
                HumanSignOff = new List<string> { "No feature is still described as coming later." }
            },
            new PgMilestone
            {
                Id = "beta",
                Phase = "Production",
                DisplayName = "Beta / content complete",
                Intent = "Every asset is in. From here the only work is fixing, not adding.",
                RequiredChecks = new List<string> { "scenario", "scene", "ui", "audio", "content", "project", "probe" },
                HumanSignOff = new List<string> { "Nothing is placeholder." }
            },
            new PgMilestone
            {
                Id = "gold",
                Phase = "Polish",
                DisplayName = "Release candidate",
                Intent = "Shippable. Polish is a phase in its own right, not the tail of production.",
                RequiredChecks = new List<string>
                {
                    "scenario", "scene", "ui", "audio", "content", "project", "probe", "soak"
                },
                HumanSignOff = new List<string>
                {
                    "A full playthrough was completed on target hardware.",
                    "The accessibility findings were reviewed and consciously accepted."
                }
            }
        };

        public static List<PgMilestone> Load()
        {
            var loaded = PgJson.Read<List<PgMilestone>>(DefaultPath);
            return loaded != null && loaded.Count > 0 ? loaded : Standard();
        }

        public static void SaveStandard() => PgJson.Write(DefaultPath, Standard());

        /// <summary>
        /// Reports how close the project is to a milestone. Artifacts are checked directly;
        /// checks are read from the reports the other layers last wrote, so a gate cannot be
        /// passed with a stale claim that something was verified.
        /// </summary>
        public static PgReport Evaluate(string milestoneId, TimeSpan? maxReportAge = null)
        {
            var report = new PgReport("milestone:" + milestoneId);
            var milestone = Load().FirstOrDefault(m =>
                string.Equals(m.Id, milestoneId, StringComparison.OrdinalIgnoreCase));

            if (milestone == null)
            {
                report.Failed($"Unknown milestone '{milestoneId}'. Known: {string.Join(", ", Load().Select(m => m.Id))}");
                return report;
            }

            report.Datum("phase", milestone.Phase);
            report.Datum("intent", milestone.Intent);

            var root = Path.Combine(PgPaths.ProjectRoot, "ProvingGround");
            foreach (var artifact in milestone.RequiredArtifacts ?? new List<string>())
            {
                var path = Path.Combine(root, artifact);
                if (File.Exists(path))
                    report.Add(PgFinding.Info("milestone.artifact", $"{artifact} exists").At(artifact));
                else
                    report.Add(PgFinding
                        .Fail("milestone.missingArtifact", $"{artifact} is required for {milestone.DisplayName}")
                        .At(artifact));
            }

            var age = maxReportAge ?? TimeSpan.FromDays(1);
            foreach (var check in milestone.RequiredChecks ?? new List<string>())
            {
                var path = FindReport(check);

                if (path == null)
                {
                    report.Add(PgFinding
                        .Fail("milestone.checkNotRun", $"'{check}' has never been run")
                        .Fix($"Run it, then re-evaluate. Evidence is what passes a gate."));
                    continue;
                }

                var written = File.GetLastWriteTimeUtc(path);
                if (DateTime.UtcNow - written > age)
                {
                    report.Add(PgFinding
                        .Fail("milestone.checkStale", $"'{check}' was last run {(DateTime.UtcNow - written).TotalHours:0} hours ago")
                        .At(PgPaths.Relative(path))
                        .Fix("Re-run it. A gate passed on stale evidence is not passed."));
                    continue;
                }

                var previous = PgJson.Read<PgReport>(path);
                if (previous == null)
                {
                    report.Add(PgFinding.Warn("milestone.checkUnreadable", $"'{check}' report could not be parsed")
                        .At(PgPaths.Relative(path)));
                    continue;
                }

                if (previous.Passed)
                    report.Add(PgFinding.Info("milestone.checkPassed", $"'{check}' passed").At(PgPaths.Relative(path)));
                else
                    report.Add(PgFinding
                        .Fail("milestone.checkFailed", $"'{check}' is failing: {previous.Summary}")
                        .At(PgPaths.Relative(path)));
            }

            foreach (var signOff in milestone.HumanSignOff ?? new List<string>())
                report.Add(PgFinding
                    .Info("milestone.humanSignOff", signOff)
                    .Fix("This one cannot be automated. Someone has to look and decide."));

            report.Summary = report.Passed
                ? $"{milestone.DisplayName}: all automatable evidence is present. {(milestone.HumanSignOff?.Count ?? 0)} human judgment(s) remain."
                : $"{milestone.DisplayName}: not ready. {report.CountAtLeast(PgSeverity.Fail)} requirement(s) outstanding.";

            return report;
        }

        /// <summary>Finds the most recent report for a tool, allowing for name suffixes like <c>scenario:smoke</c>.</summary>
        static string FindReport(string check)
        {
            var directory = Path.Combine(PgPaths.Artifacts, "reports");
            if (!Directory.Exists(directory)) return null;

            var exact = Path.Combine(directory, check + ".json");
            if (File.Exists(exact)) return exact;

            return Directory.GetFiles(directory, check + "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        public const string PillarsTemplate = @"# Design pillars

Three to five. Each one has to be able to settle an argument, which means a pillar
that nobody could disagree with is not a pillar.

A good pillar rules things out. ""Fun combat"" rules nothing out. ""Every fight is
survivable without taking damage"" rules out a great deal, and can be checked.

## 1. <pillar>

**What it means in practice:** <the decision this pillar makes for you>

**What it rules out:** <what you will not do because of it>

**How we would know it is working:** <observable, ideally measurable>

## 2. <pillar>

## 3. <pillar>

---

When a pillar starts losing arguments, change it deliberately rather than quietly.
";

        public const string OnePagerTemplate = @"# <Game title>

**One line:** <what the player does, in one sentence a stranger would understand>

**Genre / camera:** <e.g. first person, single player>
**Platform / target:** <e.g. PC, 60fps at 1080p>
**Audience:** <who this is for, specifically>
**Comparable to:** <two or three games, and how this differs from each>

## The loop

1. <what the player does>
2. <what the game does back>
3. <why they do it again>

## Why it is worth making

<the one thing this does that the comparables do not>

## Biggest risk

<the thing most likely to make this not work, and how the prototype will test it>
";

        public const string GddTemplate = @"# <Game title> - design

> This document is the contract for everything not expressed as a machine-readable
> contract under `ProvingGround/Contracts`. Anything that can be a number belongs
> there instead, where it is enforced rather than remembered.

## Pillars

See `Design/pillars.md`.

## Player experience

**Fantasy:** <what the player is pretending to be>
**Core verbs:** <the three or four things they actually do>
**Session shape:** <how long, how it starts, how it ends>

## Systems

### <System name>

**Purpose:** <which pillar this serves>
**Rules:** <how it works>
**Numbers:** <which contract file holds its tuning>
**Failure mode:** <how this system gets abused or ignored>

## Content plan

| Content | Quantity | Source | Status |
|---|---|---|---|
| <levels, enemies, weapons> | | authored / generated / purchased | |

## Out of scope

<the things being deliberately not built, so the argument only happens once>
";
    }
}
