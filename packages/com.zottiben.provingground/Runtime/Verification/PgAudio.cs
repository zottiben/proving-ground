using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ProvingGround.Contracts;
using ProvingGround.Perception;

namespace ProvingGround.Verification
{
    /// <summary>
    /// The audio event registry, and the watcher that makes it work on a game that was
    /// never instrumented.
    ///
    /// Generating a sound is the easy half of game audio. The hard half is wiring: which
    /// event fires when, whether anything is bound to it, and whether it fires once or
    /// sixty times a second. That half is fully checkable, which is why it is what this
    /// package addresses.
    /// </summary>
    public static class PgAudio
    {
        /// <summary>
        /// Explicit instrumentation. Call this alongside playing a sound and the event
        /// becomes checkable by name rather than by which clip happened to be assigned.
        /// </summary>
        public static void Fire(string eventId, string detail = null)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            PgEventLog.Record(PgEventLog.ChannelAudio, eventId, detail);
        }

        /// <summary>
        /// Starts polling every AudioSource in the scene and logging clip starts.
        ///
        /// This exists for games that were built before anyone thought about verifying
        /// audio. It infers events from clip names, which is less precise than calling
        /// <see cref="Fire"/>, but it needs no changes to the game and it catches the
        /// defect that matters most: a sound firing far more often than it should.
        /// </summary>
        public static PgAudioWatcher Watch()
        {
            var host = new GameObject("[ProvingGround] AudioWatcher") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(host);
            return host.AddComponent<PgAudioWatcher>();
        }

        /// <summary>
        /// Diffs the audio events recorded during a run against the contract.
        /// </summary>
        public static PgReport Check(PgAudioContract contract = null)
        {
            var report = new PgReport("audio");
            contract ??= PgAudioContract.Load();

            var fired = PgEventLog.Histogram(PgEventLog.ChannelAudio);
            report.Datum("distinctEventsFired", fired.Count);
            report.Datum("totalEventsFired", fired.Values.Sum());

            if (contract == null)
            {
                report.Add(PgFinding
                    .Info("audio.noContract", $"No audio contract exists; {fired.Count} distinct events were observed")
                    .Fix($"Write {PgAudioContract.DefaultPath}, or run the baseline capture to generate one from this run."));

                foreach (var pair in fired.OrderByDescending(p => p.Value))
                    report.Add(PgFinding.Info("audio.observed." + pair.Key,
                        $"'{pair.Key}' fired {pair.Value} time(s)"));

                return report;
            }

            foreach (var entry in contract.Events ?? new Dictionary<string, PgAudioEventSpec>())
            {
                var id = entry.Key;
                var spec = entry.Value;
                if (spec == null) continue;

                fired.TryGetValue(id, out var count);

                if (count == 0)
                {
                    if (spec.Required)
                        report.Add(PgFinding
                            .Fail("audio.silent." + id, $"'{id}' is required but never fired during the run")
                            .Fix("Either the code path was not exercised, or nothing is wired to this event."));
                    else if (contract.ForbidDeadEvents)
                        report.Add(PgFinding
                            .Warn("audio.dead." + id, $"'{id}' is declared but never fired")
                            .Fix("Remove it from the contract, or find the code that should be firing it."));
                    continue;
                }

                if (!spec.MaxPerSecond.HasValue) continue;

                var peak = PgEventLog.PeakPerSecond(PgEventLog.ChannelAudio, id);
                if (peak > spec.MaxPerSecond.Value)
                    report.Add(PgFinding
                        .Fail("audio.rate." + id, $"'{id}' fired far more often than the contract allows")
                        .With($"≤ {spec.MaxPerSecond}/s", $"{peak}/s")
                        .Fix("Usually a per-frame call that should be gated on a state change."));
            }

            if (contract.ForbidUndeclaredEvents)
            {
                foreach (var pair in fired)
                {
                    if (contract.Get(pair.Key) != null) continue;
                    report.Add(PgFinding
                        .Warn("audio.undeclared." + pair.Key,
                            $"'{pair.Key}' fired {pair.Value} time(s) but is not in the contract")
                        .Fix("Add it to the contract, or stop firing it."));
                }
            }

            return report;
        }
    }

    /// <summary>
    /// Polls AudioSources and records clip starts as audio events. Attached by
    /// <see cref="PgAudio.Watch"/>; there is no reason to add it by hand.
    /// </summary>
    public sealed class PgAudioWatcher : MonoBehaviour
    {
        readonly Dictionary<AudioSource, bool> _wasPlaying = new Dictionary<AudioSource, bool>();
        readonly Dictionary<AudioSource, AudioClip> _lastClip = new Dictionary<AudioSource, AudioClip>();

        /// <summary>How the clip name is turned into an event id. Replace to match a project's naming.</summary>
        public System.Func<AudioClip, AudioSource, string> EventIdFor { get; set; } =
            (clip, source) => clip != null ? clip.name : source.name;

        void LateUpdate()
        {
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (source == null) continue;

                _wasPlaying.TryGetValue(source, out var previously);
                _lastClip.TryGetValue(source, out var previousClip);

                var playing = source.isPlaying;
                var clip = source.clip;

                // A start is either silence turning into sound, or the clip changing
                // underneath a source that was already playing (PlayOneShot reuse).
                var started = playing && (!previously || clip != previousClip);
                if (started && clip != null)
                    PgAudio.Fire(EventIdFor(clip, source), PgLocate.PathOf(source.transform));

                _wasPlaying[source] = playing;
                _lastClip[source] = clip;
            }
        }

        public void StopWatching()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
