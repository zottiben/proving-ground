using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ProvingGround.Contracts;

namespace ProvingGround.EditorTools
{
    /// <summary>Measured properties of one audio clip.</summary>
    public sealed class PgClipAnalysis
    {
        public string Path;
        public string Name;
        public float LengthSeconds;
        public int Channels;
        public int Frequency;
        public double PeakDbfs;
        public double RmsDbfs;
        public double LeadingSilenceSeconds;
        public double TrailingSilenceSeconds;

        /// <summary>Discontinuity between the last and first sample, 0-2. Above ~0.1 clicks on loop.</summary>
        public double LoopDiscontinuity;

        public bool IsSilent;
        public bool IsClipped;
    }

    /// <summary>
    /// Measures the audio assets themselves, as distinct from whether the events that use
    /// them fire correctly.
    ///
    /// None of this judges whether a sound is good. It catches sounds that are broken:
    /// silent, clipped, wildly off level compared to their neighbours, padded with dead air
    /// that reads as input lag, or looping with an audible click.
    /// </summary>
    public static class PgAudioAssetCheck
    {
        /// <summary>Amplitude below which a sample counts as silence.</summary>
        public const float SilenceThreshold = 0.0015f;

        /// <summary>Amplitude at or above which a sample counts as clipped.</summary>
        public const float ClipThreshold = 0.999f;

        public static PgReport Run(PgAudioContract contract = null, string searchFolder = "Assets")
        {
            var report = new PgReport("audioAssets");
            contract ??= PgAudioContract.Load();

            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { searchFolder });
            report.Datum("clipsFound", guids.Length);

            if (guids.Length == 0)
            {
                report.Add(PgFinding.Info("audioAssets.none", $"No audio clips found under {searchFolder}"));
                return report;
            }

            var analyses = new List<PgClipAnalysis>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var analysis = Analyze(path);
                if (analysis == null) continue;
                analyses.Add(analysis);
                Evaluate(report, analysis, contract);
            }

            CheckLevelConsistency(report, analyses);
            report.Datum("clipsAnalysed", analyses.Count);

            if (report.Findings.Count == 0)
                report.Add(PgFinding.Info("audioAssets.clean", $"{analyses.Count} clips analysed with no issues"));

            return report;
        }

        /// <summary>
        /// Reads a clip's samples and measures it. Forces the importer to a readable,
        /// uncompressed state for the duration of the read and restores it afterwards,
        /// because a compressed clip returns nothing useful from GetData.
        /// </summary>
        public static PgClipAnalysis Analyze(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) return null;

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            AudioImporterSampleSettings original = default;
            var restore = false;

            if (importer != null)
            {
                original = importer.defaultSampleSettings;
                if (original.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    var settings = original;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    restore = true;
                }
            }

            try
            {
                var samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0)) return null;

                return Measure(samples, clip, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProvingGround] Could not analyse {path}: {e.Message}");
                return null;
            }
            finally
            {
                if (restore && importer != null)
                {
                    importer.defaultSampleSettings = original;
                    importer.SaveAndReimport();
                }
            }
        }

        static PgClipAnalysis Measure(float[] samples, AudioClip clip, string path)
        {
            var analysis = new PgClipAnalysis
            {
                Path = path,
                Name = clip.name,
                LengthSeconds = clip.length,
                Channels = clip.channels,
                Frequency = clip.frequency
            };

            if (samples.Length == 0)
            {
                analysis.IsSilent = true;
                analysis.PeakDbfs = double.NegativeInfinity;
                analysis.RmsDbfs = double.NegativeInfinity;
                return analysis;
            }

            var peak = 0f;
            var sumSquares = 0.0;
            var clipped = 0;

            foreach (var sample in samples)
            {
                var magnitude = Mathf.Abs(sample);
                if (magnitude > peak) peak = magnitude;
                if (magnitude >= ClipThreshold) clipped++;
                sumSquares += (double)sample * sample;
            }

            analysis.PeakDbfs = ToDbfs(peak);
            analysis.RmsDbfs = ToDbfs(Math.Sqrt(sumSquares / samples.Length));
            analysis.IsSilent = peak < SilenceThreshold;

            // A handful of samples at full scale is normal; a sustained run is clipping.
            analysis.IsClipped = clipped > samples.Length * 0.0001 && clipped > 8;

            var samplesPerSecond = (double)clip.frequency * clip.channels;

            var leading = 0;
            while (leading < samples.Length && Mathf.Abs(samples[leading]) < SilenceThreshold) leading++;
            analysis.LeadingSilenceSeconds = leading / samplesPerSecond;

            var trailing = 0;
            while (trailing < samples.Length &&
                   Mathf.Abs(samples[samples.Length - 1 - trailing]) < SilenceThreshold) trailing++;
            analysis.TrailingSilenceSeconds = trailing / samplesPerSecond;

            if (!analysis.IsSilent)
                analysis.LoopDiscontinuity = Math.Abs(samples[0] - samples[samples.Length - 1]);

            return analysis;
        }

        static void Evaluate(PgReport report, PgClipAnalysis analysis, PgAudioContract contract)
        {
            if (analysis.IsSilent)
            {
                report.Add(PgFinding
                    .Fail("audioAssets.silent", $"'{analysis.Name}' is silent")
                    .At(analysis.Path)
                    .Fix("The file imported empty, or was exported with nothing in it."));
                return;
            }

            if (analysis.IsClipped)
                report.Add(PgFinding
                    .Warn("audioAssets.clipped", $"'{analysis.Name}' is clipping")
                    .At(analysis.Path)
                    .With("< 0 dBFS", $"{analysis.PeakDbfs:0.#} dBFS")
                    .Fix("Reduce the gain before export rather than in the mixer."));

            var spec = MatchSpec(contract, analysis.Name);
            if (spec == null) return;

            if (spec.MaxLengthSeconds.HasValue && analysis.LengthSeconds > spec.MaxLengthSeconds.Value)
                report.Add(PgFinding
                    .Fail("audioAssets.tooLong", $"'{analysis.Name}' is longer than its contract allows")
                    .At(analysis.Path)
                    .With($"≤ {spec.MaxLengthSeconds}s", $"{analysis.LengthSeconds:0.##}s"));

            if (spec.MinLengthSeconds.HasValue && analysis.LengthSeconds < spec.MinLengthSeconds.Value)
                report.Add(PgFinding
                    .Fail("audioAssets.tooShort", $"'{analysis.Name}' is shorter than its contract allows")
                    .At(analysis.Path)
                    .With($"≥ {spec.MinLengthSeconds}s", $"{analysis.LengthSeconds:0.##}s"));

            if (spec.MaxPeakDbfs.HasValue && analysis.PeakDbfs > spec.MaxPeakDbfs.Value)
                report.Add(PgFinding
                    .Warn("audioAssets.peak", $"'{analysis.Name}' peaks above the contract ceiling")
                    .At(analysis.Path)
                    .With($"≤ {spec.MaxPeakDbfs} dBFS", $"{analysis.PeakDbfs:0.#} dBFS"));

            if (spec.LoudnessRmsDbfs != null)
            {
                var finding = spec.LoudnessRmsDbfs.Evaluate("audioAssets.level", analysis.RmsDbfs, analysis.Path);
                if (finding != null) report.Add(finding);
            }

            if (spec.MaxLeadingSilenceSeconds.HasValue &&
                analysis.LeadingSilenceSeconds > spec.MaxLeadingSilenceSeconds.Value)
                report.Add(PgFinding
                    .Fail("audioAssets.leadingSilence", $"'{analysis.Name}' starts with dead air")
                    .At(analysis.Path)
                    .With($"≤ {spec.MaxLeadingSilenceSeconds * 1000:0}ms", $"{analysis.LeadingSilenceSeconds * 1000:0}ms")
                    .Fix("Silence at the head of a one-shot is heard as input latency. Trim it."));

            if (spec.MustLoop && analysis.LoopDiscontinuity > 0.1)
                report.Add(PgFinding
                    .Fail("audioAssets.loopClick", $"'{analysis.Name}' will click where it loops")
                    .At(analysis.Path)
                    .With("start and end matched", $"discontinuity {analysis.LoopDiscontinuity:0.###}")
                    .Fix("Crossfade the seam, or trim to a zero crossing."));
        }

        /// <summary>
        /// Warns when one clip sits far off the level of its peers, which is what makes a
        /// mix feel inconsistent even when every clip is individually within spec.
        /// </summary>
        static void CheckLevelConsistency(PgReport report, IReadOnlyList<PgClipAnalysis> analyses)
        {
            var usable = analyses
                .Where(a => !a.IsSilent && !double.IsNegativeInfinity(a.RmsDbfs))
                .ToList();

            if (usable.Count < 4) return;

            var median = usable.Select(a => a.RmsDbfs).OrderBy(v => v).ElementAt(usable.Count / 2);

            foreach (var analysis in usable)
            {
                var deviation = analysis.RmsDbfs - median;
                if (Math.Abs(deviation) < 12) continue;

                report.Add(PgFinding
                    .Warn("audioAssets.levelOutlier",
                        $"'{analysis.Name}' is {Math.Abs(deviation):0} dB {(deviation > 0 ? "louder" : "quieter")} than the median clip")
                    .At(analysis.Path)
                    .With($"~{median:0.#} dBFS RMS", $"{analysis.RmsDbfs:0.#} dBFS RMS")
                    .Fix("Normalise toward the rest of the set, or confirm the difference is intentional."));
            }
        }

        /// <summary>Matches a clip to a contract event by exact id, then by prefix.</summary>
        static PgAudioEventSpec MatchSpec(PgAudioContract contract, string clipName)
        {
            if (contract?.Events == null || string.IsNullOrEmpty(clipName)) return null;

            var exact = contract.Get(clipName);
            if (exact != null) return exact;

            return contract.Events
                .Where(e => clipName.StartsWith(e.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Key.Length)
                .Select(e => e.Value)
                .FirstOrDefault();
        }

        static double ToDbfs(double amplitude) =>
            amplitude <= 0 ? double.NegativeInfinity : 20 * Math.Log10(amplitude);
    }
}
