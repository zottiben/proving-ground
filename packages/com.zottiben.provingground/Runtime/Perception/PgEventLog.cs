using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace ProvingGround.Perception
{
    /// <summary>A single thing that happened, stamped with when it happened.</summary>
    [Serializable]
    public sealed class PgEvent
    {
        [JsonProperty("frame")] public int Frame;
        [JsonProperty("time")] public double Time;
        [JsonProperty("channel")] public string Channel;
        [JsonProperty("id")] public string Id;

        [JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)]
        public string Detail;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Data;

        public override string ToString() =>
            $"f{Frame} {Time:0.000}s [{Channel}] {Id}{(Detail != null ? " " + Detail : "")}";
    }

    /// <summary>
    /// A frame-stamped record of what the game did. Every other layer writes here, so a
    /// failing run produces one ordered narrative rather than scattered console spam.
    ///
    /// The buffer is bounded and allocation-free in steady state: it is running inside the
    /// game being measured, and a profiler that changes the numbers is worthless.
    /// </summary>
    public static class PgEventLog
    {
        public const string ChannelInput = "input";
        public const string ChannelAudio = "audio";
        public const string ChannelGameplay = "gameplay";
        public const string ChannelScenario = "scenario";
        public const string ChannelError = "error";
        public const string ChannelPerf = "perf";

        static PgEvent[] _buffer = new PgEvent[8192];
        static int _head;
        static int _count;
        static bool _recording;

        public static bool IsRecording => _recording;
        public static int Count => _count;

        /// <summary>Fired on every record, so probes can react without polling.</summary>
        public static event Action<PgEvent> Recorded;

        public static void Start(int capacity = 8192)
        {
            if (capacity != _buffer.Length) _buffer = new PgEvent[Mathf.Max(capacity, 64)];
            _head = 0;
            _count = 0;
            _recording = true;
        }

        public static void Stop() => _recording = false;

        public static void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public static void Record(string channel, string id, string detail = null,
            Dictionary<string, object> data = null)
        {
            if (!_recording) return;

            var entry = _buffer[_head] ??= new PgEvent();
            entry.Frame = Time.frameCount;
            entry.Time = Time.timeAsDouble;
            entry.Channel = channel;
            entry.Id = id;
            entry.Detail = detail;
            entry.Data = data;

            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;

            Recorded?.Invoke(entry);
        }

        public static void Gameplay(string id, string detail = null) => Record(ChannelGameplay, id, detail);
        public static void Audio(string id, string detail = null) => Record(ChannelAudio, id, detail);
        public static void Error(string id, string detail = null) => Record(ChannelError, id, detail);

        /// <summary>Events in chronological order. Allocates, so call it after the run, not during.</summary>
        public static List<PgEvent> Snapshot()
        {
            var result = new List<PgEvent>(_count);
            var start = _count < _buffer.Length ? 0 : _head;
            for (var i = 0; i < _count; i++)
            {
                var entry = _buffer[(start + i) % _buffer.Length];
                if (entry == null) continue;
                result.Add(new PgEvent
                {
                    Frame = entry.Frame, Time = entry.Time, Channel = entry.Channel,
                    Id = entry.Id, Detail = entry.Detail, Data = entry.Data
                });
            }

            return result;
        }

        public static List<PgEvent> Channel(string channel) =>
            Snapshot().Where(e => e.Channel == channel).ToList();

        /// <summary>Ids in a channel with how many times each fired, for rate and dead-event checks.</summary>
        public static Dictionary<string, int> Histogram(string channel)
        {
            var histogram = new Dictionary<string, int>();
            foreach (var entry in Snapshot())
            {
                if (entry.Channel != channel) continue;
                histogram.TryGetValue(entry.Id, out var count);
                histogram[entry.Id] = count + 1;
            }

            return histogram;
        }

        /// <summary>Highest number of occurrences of <paramref name="id"/> inside any one-second window.</summary>
        public static int PeakPerSecond(string channel, string id)
        {
            var times = Snapshot()
                .Where(e => e.Channel == channel && e.Id == id)
                .Select(e => e.Time)
                .OrderBy(t => t)
                .ToList();

            var peak = 0;
            var start = 0;
            for (var end = 0; end < times.Count; end++)
            {
                while (times[end] - times[start] > 1.0) start++;
                peak = Mathf.Max(peak, end - start + 1);
            }

            return peak;
        }

        public static string ToText(int maxEvents = 400)
        {
            var events = Snapshot();
            var sb = new StringBuilder();
            sb.AppendLine($"events: {events.Count}");
            foreach (var entry in events.Skip(Mathf.Max(0, events.Count - maxEvents)))
                sb.AppendLine("  " + entry);
            return sb.ToString();
        }
    }
}
