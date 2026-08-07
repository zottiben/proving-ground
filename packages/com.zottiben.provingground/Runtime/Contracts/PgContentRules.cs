using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ProvingGround.Contracts
{
    /// <summary>
    /// Import and hygiene rules for a class of asset, matched by glob.
    /// </summary>
    [Serializable]
    public sealed class PgAssetRule
    {
        /// <summary>Glob against the asset path, e.g. <c>Assets/Art/UI/**/*.png</c>.</summary>
        [JsonProperty("match")] public string Match;

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        [JsonProperty("maxTextureSize", NullValueHandling = NullValueHandling.Ignore)]
        public int? MaxTextureSize;

        /// <summary>Expected texture import type, e.g. <c>Sprite</c>, <c>NormalMap</c>, <c>Default</c>.</summary>
        [JsonProperty("textureType", NullValueHandling = NullValueHandling.Ignore)]
        public string TextureType;

        [JsonProperty("requireMipmaps", NullValueHandling = NullValueHandling.Ignore)]
        public bool? RequireMipmaps;

        [JsonProperty("requireReadWriteDisabled", NullValueHandling = NullValueHandling.Ignore)]
        public bool? RequireReadWriteDisabled = true;

        /// <summary>Audio load type, e.g. <c>DecompressOnLoad</c>, <c>CompressedInMemory</c>, <c>Streaming</c>.</summary>
        [JsonProperty("audioLoadType", NullValueHandling = NullValueHandling.Ignore)]
        public string AudioLoadType;

        [JsonProperty("maxFileSizeMb", NullValueHandling = NullValueHandling.Ignore)]
        public double? MaxFileSizeMb;

        /// <summary>Regex the asset file name must satisfy.</summary>
        [JsonProperty("namePattern", NullValueHandling = NullValueHandling.Ignore)]
        public string NamePattern;

        [JsonProperty("severity")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public PgSeverity Severity = PgSeverity.Warn;
    }

    /// <summary>
    /// Project hygiene as data. Most of this is the class of defect that is invisible in
    /// the Editor and expensive at build time.
    /// </summary>
    [Serializable]
    public sealed class PgContentRules
    {
        public const string FileName = "content.json";

        [JsonProperty("schema")] public string Schema = "provingground/content@1";

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>Missing script references and null serialized references that are marked required.</summary>
        [JsonProperty("forbidMissingReferences")] public bool ForbidMissingReferences = true;

        [JsonProperty("forbidMissingScripts")] public bool ForbidMissingScripts = true;

        /// <summary>Assets referenced by nothing reachable from a scene in the build.</summary>
        [JsonProperty("reportOrphanedAssets")] public bool ReportOrphanedAssets = true;

        /// <summary>Duplicate assets by content hash, which quietly inflate build size.</summary>
        [JsonProperty("reportDuplicateAssets")] public bool ReportDuplicateAssets = true;

        [JsonProperty("forbidEmptyPrefabOverrides")] public bool ForbidEmptyPrefabOverrides;

        /// <summary>Localisation keys used in code or UI but absent from the string tables.</summary>
        [JsonProperty("checkLocalisationCoverage")] public bool CheckLocalisationCoverage = true;

        /// <summary>Folders excluded from every content check.</summary>
        [JsonProperty("ignore")] public List<string> Ignore = new List<string>
        {
            "Assets/Plugins/**", "Assets/ThirdParty**", "Assets/Samples/**"
        };

        [JsonProperty("assetRules")] public List<PgAssetRule> AssetRules = new List<PgAssetRule>();

        public static string DefaultPath => Path.Combine(PgPaths.Contracts, FileName);

        public static PgContentRules Load(string path = null) =>
            PgJson.Read(path ?? DefaultPath, (PgContentRules)null);

        public void Save(string path = null) => PgJson.Write(path ?? DefaultPath, this);

        public static PgContentRules Starter()
        {
            var rules = new PgContentRules
            {
                Note = "Asset rules are matched by glob, first match wins. Add one per art pipeline you run."
            };
            rules.AssetRules.Add(new PgAssetRule
            {
                Match = "Assets/**/UI/**/*.png",
                TextureType = "Sprite",
                RequireMipmaps = false,
                RequireReadWriteDisabled = true,
                Note = "UI sprites do not need mipmaps and should not be readable at runtime."
            });
            return rules;
        }
    }
}
