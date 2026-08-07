using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProvingGround.EditorTools
{
    /// <summary>One captured Editor log line.</summary>
    [Serializable]
    public sealed class PgLogEntry
    {
        public string Type;
        public string Message;
        public string Stack;
        public string TimeUtc;

        public override string ToString()
        {
            var head = $"[{Type}] {Message}";
            if (Type == "Error" || Type == "Exception")
            {
                var frame = Stack?.Split('\n').FirstOrDefault(l => l.Contains("Assets/"));
                if (!string.IsNullOrEmpty(frame)) head += "\n    " + frame.Trim();
            }

            return head;
        }
    }

    /// <summary>
    /// Captures the Editor console so an agent can read what Unity said.
    ///
    /// Unity reports a great deal through the console and almost nothing through return
    /// values: a component that failed to attach, a shader that did not compile, a null
    /// reference in someone's OnValidate. An agent that cannot read it is working blind
    /// through exactly the channel Unity uses to explain itself.
    /// </summary>
    [InitializeOnLoad]
    public static class PgConsole
    {
        const int Capacity = 512;

        static readonly PgLogEntry[] Buffer = new PgLogEntry[Capacity];
        static int _head;
        static int _count;

        static PgConsole()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            // Proving Ground's own reports come back through the API; echoing them here
            // would bury the project's messages under the tool's.
            if (message != null && message.StartsWith("[ProvingGround]")) return;

            lock (Buffer)
            {
                Buffer[_head] = new PgLogEntry
                {
                    Type = type.ToString(),
                    Message = message,
                    Stack = stack,
                    TimeUtc = DateTime.UtcNow.ToString("HH:mm:ss")
                };

                _head = (_head + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
        }

        /// <summary>Captured entries, oldest first.</summary>
        public static List<PgLogEntry> Entries(string minSeverity = null, int max = 100)
        {
            List<PgLogEntry> snapshot;
            lock (Buffer)
            {
                snapshot = new List<PgLogEntry>(_count);
                var start = _count < Capacity ? 0 : _head;
                for (var i = 0; i < _count; i++)
                {
                    var entry = Buffer[(start + i) % Capacity];
                    if (entry != null) snapshot.Add(entry);
                }
            }

            if (!string.IsNullOrEmpty(minSeverity))
            {
                var wanted = Rank(minSeverity);
                snapshot = snapshot.Where(e => Rank(e.Type) >= wanted).ToList();
            }

            return snapshot.Skip(Math.Max(0, snapshot.Count - max)).ToList();
        }

        public static void Clear()
        {
            lock (Buffer)
            {
                Array.Clear(Buffer, 0, Capacity);
                _head = 0;
                _count = 0;
            }
        }

        static int Rank(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "exception": return 4;
                case "error": return 3;
                case "assert": return 3;
                case "warning": return 2;
                default: return 1;
            }
        }
    }
}
