using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace ProvingGround
{
    /// <summary>
    /// Single place JSON is read and written, so every artifact Proving Ground emits has
    /// the same shape rules. Contracts are plain JSON on disk on purpose: an agent edits
    /// text reliably and edits serialised Unity assets badly.
    /// </summary>
    public static class PgJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            FloatFormatHandling = FloatFormatHandling.DefaultValue
        };

        public static string Stringify(object value) =>
            JsonConvert.SerializeObject(value, Settings);

        public static T Parse<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>Reads and deserialises, returning <paramref name="fallback"/> when the file is absent.</summary>
        public static T Read<T>(string path, T fallback = default)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return fallback;
            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path), Settings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProvingGround] Could not parse {path}: {e.Message}");
                return fallback;
            }
        }

        public static void Write(string path, object value)
        {
            if (value is PgReport report) report.Summarise();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, Stringify(value));
        }
    }
}
