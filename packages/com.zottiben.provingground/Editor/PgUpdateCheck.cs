using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

// UnityEditor also has a PackageInfo, from the legacy asset store API, so the package
// manager one has to be named explicitly.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Notices when a newer release exists and says so in the Proving Ground window.
    ///
    /// Someone can go months using the plugin entirely from the Editor and never run the
    /// command line, so a shell-only notification would never reach them.
    ///
    /// It is deliberately unobtrusive: one request a day at most, only while the window
    /// is open, never blocking, silent on any failure, and switchable off. An Editor
    /// extension that phones home on every domain reload is a nuisance, and one that
    /// throws a dialog is worse.
    /// </summary>
    public static class PgUpdateCheck
    {
        const string ApiUrl = "https://api.github.com/repos/zottiben/proving-ground/releases/latest";
        const string EnabledKey = "ProvingGround.UpdateCheck.Enabled";
        const string LastCheckKey = "ProvingGround.UpdateCheck.LastUtc";
        const string LatestKey = "ProvingGround.UpdateCheck.Latest";
        const double IntervalHours = 24;

        static bool _inFlight;

        /// <summary>Whether to check at all. On by default; a single toggle turns it off.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        /// <summary>The newest tag seen, or empty.</summary>
        public static string Latest => EditorPrefs.GetString(LatestKey, "");

        /// <summary>The installed package version, read from package.json.</summary>
        public static string Installed
        {
            get
            {
                var info = PackageInfo.FindForAssembly(typeof(PgUpdateCheck).Assembly);
                return info != null ? info.version : "";
            }
        }

        /// <summary>True when a strictly newer release is available.</summary>
        public static bool UpdateAvailable => IsNewer(Latest, Installed);

        /// <summary>
        /// Starts a check if one is due. Returns immediately; the result lands in
        /// EditorPrefs and shows up on the next repaint.
        /// </summary>
        public static void Poll(bool force = false)
        {
            if (!Enabled && !force) return;
            if (_inFlight) return;

            if (!force)
            {
                var last = EditorPrefs.GetString(LastCheckKey, "");
                if (DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when)
                    && (DateTime.UtcNow - when).TotalHours < IntervalHours)
                    return;
            }

            _inFlight = true;
            EditorPrefs.SetString(LastCheckKey, DateTime.UtcNow.ToString("o"));

            var request = UnityWebRequest.Get(ApiUrl);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.timeout = 5;

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var tag = TagFrom(request.downloadHandler.text);
                        if (!string.IsNullOrEmpty(tag)) EditorPrefs.SetString(LatestKey, tag);
                    }
                }
                catch (Exception)
                {
                    // A failed update check is not something to trouble anyone with.
                }
                finally
                {
                    request.Dispose();
                    _inFlight = false;
                }
            };
        }

        /// <summary>Pulls tag_name out without taking a JSON dependency into the Editor assembly.</summary>
        internal static string TagFrom(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var match = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Compares 'v0.2.0' style tags against a package version like '0.1.0'.</summary>
        internal static bool IsNewer(string candidate, string current)
        {
            var a = Parse(candidate);
            var b = Parse(current);
            if (a == null || b == null) return false;

            for (var i = 0; i < Mathf.Max(a.Length, b.Length); i++)
            {
                var left = i < a.Length ? a[i] : 0;
                var right = i < b.Length ? b[i] : 0;
                if (left != right) return left > right;
            }

            return false;
        }

        static int[] Parse(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;

            var cleaned = version.Trim().TrimStart('v', 'V');
            // Drop any pre-release suffix: 0.2.0-pre.1 compares as 0.2.0.
            var dash = cleaned.IndexOf('-');
            if (dash >= 0) cleaned = cleaned.Substring(0, dash);

            var parts = cleaned.Split('.');
            var numbers = new int[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(new string(System.Linq.Enumerable.ToArray(
                        System.Linq.Enumerable.Where(parts[i], char.IsDigit))), out numbers[i]))
                    numbers[i] = 0;
            }

            return numbers;
        }
    }
}
