using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Records where this project's agent bridge is listening, so a client does not have to
    /// guess.
    ///
    /// The port is Editor-side state: it lives in EditorPrefs and can be moved. Clients
    /// used to assume the default, so once the two disagreed every call failed with
    /// "could not reach the Editor, enable the bridge" while the bridge sat there running,
    /// which is a dead end you cannot debug from the message. The Editor knows the answer,
    /// so it writes it down.
    ///
    /// The entry says where the bridge lives, not whether it is up this instant. It
    /// deliberately survives the domain reload a script compile causes - the listener is
    /// torn down and rebuilt on the same port a few seconds later, and a client polling
    /// across that window needs the address to hold still. Liveness is the caller's health
    /// probe, not this file's job.
    /// </summary>
    public static class PgBridgeRegistry
    {
        /// <summary>
        /// Where entries are kept, mirroring the layout the installer and CLI already use
        /// so both sides resolve the same directory without being told.
        /// </summary>
        public static string Directory
        {
            get
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                var root = string.IsNullOrEmpty(xdg)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".local", "share")
                    : xdg;
                return Path.Combine(root, "proving-ground", "bridges");
            }
        }

        /// <summary>This project's entry. One file per project, so two open Editors coexist.</summary>
        public static string EntryPath => Path.Combine(Directory, EntryName(PgPaths.ProjectRoot));

        /// <summary>Announces that the bridge for this project is answering on <paramref name="port"/>.</summary>
        public static void Publish(int port)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                PruneDeadEntries();

                var entry = new
                {
                    url = $"http://127.0.0.1:{port}",
                    port,
                    project = Normalise(PgPaths.ProjectRoot),
                    projectName = Application.productName,
                    unity = Application.unityVersion,
                    pid = Process.GetCurrentProcess().Id,
                    serving = PgBridge.Serving,
                    updated = DateTime.UtcNow.ToString("o")
                };

                File.WriteAllText(EntryPath, JsonConvert.SerializeObject(entry, Formatting.Indented));
            }
            catch (Exception e)
            {
                // A bridge that is listening but unadvertised still works for anyone who
                // knows the port, so this must never be the thing that stops it starting.
                Debug.LogWarning($"[ProvingGround] Could not record the bridge address: {e.Message}");
            }
        }

        /// <summary>Removes this project's entry, once the bridge is genuinely gone.</summary>
        public static void Withdraw()
        {
            try
            {
                if (File.Exists(EntryPath)) File.Delete(EntryPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProvingGround] Could not remove the bridge address: {e.Message}");
            }
        }

        /// <summary>
        /// Drops entries left behind by Editors that exited without withdrawing, so the
        /// directory does not accumulate one dead file per project ever opened.
        ///
        /// An entry is only removed once its process is gone. A live Editor mid-compile is
        /// unreachable but very much still there, and evicting it would recreate exactly
        /// the bug this registry exists to fix.
        /// </summary>
        static void PruneDeadEntries()
        {
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
            {
                if (string.Equals(file, EntryPath, StringComparison.Ordinal)) continue;

                try
                {
                    var pid = JsonConvert.DeserializeObject<Entry>(File.ReadAllText(file))?.Pid ?? 0;
                    if (pid > 0 && IsAlive(pid)) continue;
                    File.Delete(file);
                }
                catch (Exception)
                {
                    // Unreadable or already gone: nothing here is worth interrupting a
                    // bridge start-up over.
                }
            }
        }

        static bool IsAlive(int pid)
        {
            try
            {
                using (Process.GetProcessById(pid)) return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception)
            {
                // Anything else (permissions, an OS that will not say) is not evidence of
                // death, so leave the entry alone.
                return true;
            }
        }

        /// <summary>
        /// A stable, readable, collision-free file name for a project path.
        ///
        /// The name is for humans reading the directory; clients match on the `project`
        /// field inside the file, so the two sides never have to agree on how this is
        /// derived.
        /// </summary>
        static string EntryName(string projectRoot)
        {
            var normalised = Normalise(projectRoot);
            var safe = new string(Path.GetFileName(normalised)
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray());

            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalised));
                var suffix = string.Concat(hash.Take(4).Select(b => b.ToString("x2")));
                return $"{(safe.Length == 0 ? "project" : safe)}-{suffix}.json";
            }
        }

        static string Normalise(string path) =>
            Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

        class Entry
        {
            [JsonProperty("pid")] public int Pid { get; set; }
        }
    }
}
