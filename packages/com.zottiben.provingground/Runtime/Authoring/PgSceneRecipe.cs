using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ProvingGround.Authoring
{
    /// <summary>How to repeat an object, for the parts of a level that are regular.</summary>
    [Serializable]
    public sealed class PgRepeat
    {
        [JsonProperty("count")] public int Count = 1;

        /// <summary>Translation applied per additional copy.</summary>
        [JsonProperty("offset", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Offset;

        /// <summary>Euler rotation applied per additional copy.</summary>
        [JsonProperty("rotate", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Rotate;

        /// <summary>Arranges copies evenly around a circle instead of along an offset.</summary>
        [JsonProperty("ring", NullValueHandling = NullValueHandling.Ignore)]
        public float? Ring;

        /// <summary>Lays copies out on a grid. Two values: columns, then spacing.</summary>
        [JsonProperty("grid", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Grid;

        /// <summary>Random position jitter per axis, seeded from the recipe.</summary>
        [JsonProperty("jitter", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Jitter;
    }

    /// <summary>One component to put on an object, and what to set on it.</summary>
    [Serializable]
    public sealed class PgComponentSpec
    {
        [JsonProperty("type")] public string Type;

        /// <summary>
        /// Property name to value. Values are parsed against the field's real type, so
        /// numbers, bools, strings, enums, vectors, colours and asset references all work.
        /// Nested paths use dots: <c>material.color</c>.
        /// </summary>
        [JsonProperty("set", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Set;
    }

    /// <summary>
    /// One object in a scene recipe. Deliberately forgiving about how a thing is
    /// specified, because an agent writing these by hand should not have to remember
    /// which of three spellings this tool wanted.
    /// </summary>
    [Serializable]
    public sealed class PgObjectSpec
    {
        /// <summary>Name in the hierarchy, and the identity used when re-applying a recipe.</summary>
        [JsonProperty("id")] public string Id;

        /// <summary>Id of the parent object in this recipe, or a scene path.</summary>
        [JsonProperty("parent", NullValueHandling = NullValueHandling.Ignore)]
        public string Parent;

        /// <summary>Cube, Sphere, Capsule, Cylinder, Plane or Quad. Omit for an empty object.</summary>
        [JsonProperty("primitive", NullValueHandling = NullValueHandling.Ignore)]
        public string Primitive;

        /// <summary>Asset path of a prefab to instantiate instead of a primitive.</summary>
        [JsonProperty("prefab", NullValueHandling = NullValueHandling.Ignore)]
        public string Prefab;

        [JsonProperty("position", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Position;

        [JsonProperty("rotation", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Rotation;

        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Scale;

        [JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
        public string Tag;

        [JsonProperty("layer", NullValueHandling = NullValueHandling.Ignore)]
        public string Layer;

        [JsonProperty("static", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Static;

        [JsonProperty("active", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Active;

        /// <summary>Material asset path, or a colour like <c>#RRGGBB</c> for a generated material.</summary>
        [JsonProperty("material", NullValueHandling = NullValueHandling.Ignore)]
        public string Material;

        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)]
        public List<PgComponentSpec> Components;

        [JsonProperty("repeat", NullValueHandling = NullValueHandling.Ignore)]
        public PgRepeat Repeat;

        [JsonProperty("children", NullValueHandling = NullValueHandling.Ignore)]
        public List<PgObjectSpec> Children;

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;
    }

    /// <summary>
    /// A scene as data.
    ///
    /// Every other tool in this space builds levels by imperative mutation: create an
    /// object, add a component, set a property, several hundred times. That works, but the
    /// result exists only as serialized YAML, which cannot be reviewed, cannot be rebuilt,
    /// and cannot be diffed when someone changes it.
    ///
    /// A recipe is the same level expressed as a document. It applies idempotently, so
    /// re-running it converges rather than duplicating; it is seeded, so procedural parts
    /// are reproducible; and it is the artifact that gets committed, so a change to the
    /// level shows up in review as a change to the recipe.
    ///
    /// It is not a replacement for direct editing. Use <c>PgAuthor</c> to nudge things
    /// while iterating, then fold what you keep back into the recipe.
    /// </summary>
    [Serializable]
    public sealed class PgSceneRecipe
    {
        [JsonProperty("schema")] public string Schema = "provingground/scene@1";

        [JsonProperty("name")] public string Name = "scene";

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note;

        /// <summary>Seed for jitter and any other randomness, so a rebuild is identical.</summary>
        [JsonProperty("seed")] public int Seed = 12345;

        /// <summary>
        /// Removes objects the recipe does not declare before building. Off by default:
        /// wiping a scene someone has been working in is not a thing to do by accident.
        /// </summary>
        [JsonProperty("clearUnmanaged")] public bool ClearUnmanaged;

        /// <summary>Adds a default directional light when the recipe declares none.</summary>
        [JsonProperty("ensureLight")] public bool EnsureLight = true;

        /// <summary>Adds a camera when the recipe declares none.</summary>
        [JsonProperty("ensureCamera")] public bool EnsureCamera = true;

        [JsonProperty("objects")] public List<PgObjectSpec> Objects = new List<PgObjectSpec>();

        public static string DirectoryPath => Path.Combine(PgPaths.ProjectRoot, "ProvingGround", "Scenes");

        public static string PathFor(string name) => Path.Combine(DirectoryPath, name + ".json");

        public static PgSceneRecipe Load(string path) => PgJson.Read(path, (PgSceneRecipe)null);

        public static PgSceneRecipe LoadByName(string name) => Load(PathFor(name));

        public void Save(string path = null) => PgJson.Write(path ?? PathFor(Name), this);

        public static IEnumerable<string> All() =>
            Directory.Exists(DirectoryPath)
                ? Directory.GetFiles(DirectoryPath, "*.json", SearchOption.AllDirectories)
                : Array.Empty<string>();

        /// <summary>Walks every object in the recipe, including children.</summary>
        public IEnumerable<PgObjectSpec> Flatten()
        {
            IEnumerable<PgObjectSpec> Walk(IEnumerable<PgObjectSpec> specs)
            {
                if (specs == null) yield break;
                foreach (var spec in specs)
                {
                    yield return spec;
                    foreach (var child in Walk(spec.Children)) yield return child;
                }
            }

            return Walk(Objects);
        }
    }
}
