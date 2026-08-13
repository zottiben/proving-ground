---
name: save-systems
description: Build save and load in Unity that survives shipping - what to persist and what to rebuild, serialization choices and their limits, stable identity for objects, atomic writes, corruption recovery, versioning and migration, and checkpoint and autosave design. Use when adding saving, loading, checkpoints or profiles, when a save fails to load or loses data, or when a save format has to change without breaking existing saves.
---

# Save systems

Saving is easy to make work once and hard to make work for two years. The failure that
matters is not "it did not save" - that shows up immediately. It is the update that
makes every existing save unloadable, and the interrupted write that leaves a player
with nothing.

Both are avoidable, and both are avoided by decisions made before the first save is
written. Design for version two on day one, because retro-fitting versioning onto a
format already in the wild means supporting a format you cannot read.

## When to use

- Use when adding save and load, checkpoints, autosave or multiple profiles.
- Use when a save fails to load, loses data, or breaks after an update.
- Use when the save format needs to change and old saves must keep working.

**When *not* to use:** for settings and options, which are not saves - they are
`PlayerPrefs` or a small config file, and they have different rules. For deciding *where*
checkpoints go in a level, `level-design`.

## Core workflow

1. **Decide what is authoritative.** Persist decisions and state that cannot be derived:
   position, inventory, quest flags, unlocks, statistics. Rebuild everything derivable:
   loaded scenes, spawned enemies, UI state, cached references. A save file full of
   things you could have recomputed is a save file full of things that can go stale.
2. **Give everything savable a stable identity.** A GUID assigned at author time, not a
   name, not a sibling index, not an `InstanceID`. Identity is the thing that breaks
   first when a level is edited.
3. **Version the format from the first commit.** A `version` field, and a migration
   path per version. This costs nothing now and is impossible to add later.
4. **Write atomically.** Temp file, flush, then replace. A crash mid-write must leave
   the previous save intact.
5. **Validate on load, and fail loudly but safely.** A corrupt save should fall back to
   a backup and tell the player, not silently produce a half-initialised game.
6. **Autosave at safe points**, never mid-transition, never mid-cutscene, and never so
   often that it hitches.
7. **Test the interruption.** Kill the process during a write and load the result. That
   is the test the whole design exists for.

## Patterns

### 1. A save model that is a model, not a scene dump

```csharp
// Plain serializable data, deliberately separate from the components that produce it.
// The moment the save type references a MonoBehaviour, every refactor is a save break.
[Serializable]
public class SaveGame {
    public int Version = CurrentVersion;
    public const int CurrentVersion = 3;

    public string SceneId;
    public SerializableVector3 PlayerPosition;
    public List<ItemStack> Inventory = new();
    public List<string> CompletedQuests = new();
    public Dictionary<string, bool> WorldFlags = new();   // needs Newtonsoft, not JsonUtility
    public long SavedAtUnixSeconds;
}
```

### 2. Serialization: which one, and why

| Option | Use for | Limits |
|---|---|---|
| `JsonUtility` | small, flat, fully-known data | no dictionaries, no polymorphism, no top-level arrays, no properties, no nulls in nested objects |
| Newtonsoft.Json | anything real | needs the package - which Proving Ground already depends on, so it is present |
| Binary via a writer you control | large or performance-sensitive saves | you own the format and the migration entirely |

`JsonUtility` is fast and it is in the box, and its limits are not obvious until a
dictionary silently serialises as nothing. Newtonsoft handles dictionaries,
polymorphism, nulls and custom converters, and readable JSON is worth a great deal when
somebody sends you a broken save.

### 3. Atomic writes

```csharp
// A crash between opening the file and finishing the write must not destroy the save.
// Write elsewhere, then swap - the swap is the only step that touches the real file.
public void Write(SaveGame save, string slot) {
    var path = Path.Combine(Application.persistentDataPath, $"{slot}.json");
    var temp = path + ".tmp";
    var backup = path + ".bak";

    File.WriteAllText(temp, JsonConvert.SerializeObject(save, Formatting.Indented));

    if (File.Exists(path)) File.Replace(temp, path, backup);   // atomic, keeps a backup
    else File.Move(temp, path);
}
```

`Application.persistentDataPath` is the only location that is writable on every
platform. Anything under `Application.dataPath` is read-only in a real build, and
`StreamingAssets` is not writable either.

### 4. Migration, one step at a time

```csharp
// Each migration handles exactly one version bump. A chain of small, testable steps
// beats one function that tries to understand every historical format at once.
static SaveGame Migrate(JObject raw) {
    int version = raw["Version"]?.Value<int>() ?? 1;

    if (version < 2) {
        raw["Inventory"] = MergeStacks(raw["Items"]);      // v1 stored one entry per item
        raw.Remove("Items");
        version = 2;
    }
    if (version < 3) {
        raw["SceneId"] = LegacySceneNames.ToId(raw["SceneName"]?.Value<string>());
        raw.Remove("SceneName");
        version = 3;
    }

    raw["Version"] = version;
    return raw.ToObject<SaveGame>();
}
```

Keep a real save file from every shipped version in the repository, and load each of
them in a test. That is the only way to know a migration chain still works, and it
costs one file per release.

### 5. Stable identity

```csharp
// Authored once, serialized with the object, never regenerated. A name is not an
// identity: two objects can share one, and renaming one breaks every save.
public class SaveableId : MonoBehaviour {
    [SerializeField, HideInInspector] string _id;
    public string Id => _id;

#if UNITY_EDITOR
    void OnValidate() {
        if (string.IsNullOrEmpty(_id) || IsDuplicate(_id)) {
            _id = Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif
}
```

The duplicate check matters: duplicating a prefab instance in the editor copies the
GUID, and two objects with the same identity is a save bug that appears months later
and looks like teleportation.

## Pitfalls

- **No version field.** Every future format change breaks every existing save, and there
  is no way to detect which format you are holding.
- **Serializing `UnityEngine.Object` references.** They do not survive a session. Store
  an id and resolve it on load.
- **Identity by name, index or `InstanceID`.** All three change. GUIDs, authored once.
- **Writing directly over the save file.** An interrupted write loses everything. Temp,
  then replace.
- **No backup.** One corrupt file and the player has nothing. Keep at least the previous
  save.
- **Saving the whole scene.** Enormous, fragile, and it breaks whenever the scene is
  edited. Save decisions, rebuild the world.
- **`JsonUtility` with a `Dictionary`.** Serialises as nothing, silently. Also true of
  properties, top-level arrays and polymorphic lists.
- **`PlayerPrefs` as a save system.** It is small, it is not designed for structured
  data, it lives in the registry on Windows, and it is trivially editable. Settings
  only.
- **Autosaving during a transition.** Save mid-load and you persist a half-loaded world.
  Save at defined safe points.
- **Autosaving so often it hitches.** Serialising a large save on the main thread every
  ten seconds is a visible stutter. Save less often, or build the snapshot on the main
  thread and write it off it.
- **Saving on quit only.** A crash loses the session, and crashes are exactly when
  players most want their progress.
- **No handling for a save from a newer version.** A player who rolls back a build gets
  an unreadable file. Detect it and say so rather than throwing.
- **Floating point round-trips assumed exact.** Use enough precision, and never compare
  a loaded float for equality against an authored one.

## Prove it with Proving Ground

Saving is one of the few systems where the reproduction is more valuable than the fix,
because the bugs are sequence-dependent.

- `pg_record` while the user plays through the sequence that loses data, then stop: you
  get a deterministic scenario that reproduces it. Add an `assert` step and the
  reproduction becomes a regression test that stays.
- `pg_run_scenario` for the round trip - play, save, reload, assert the world matches.
  A scenario with a fixed seed makes "the same game state" a checkable claim.
- `pg_digest` before and after a load. Two symbolic snapshots diff exactly; two
  screenshots do not.
- `pg_console` after a load. Deserialization failures usually surface as an exception
  that is caught and logged somewhere nobody is looking.
- `pg_check content` finds missing script references, which is the asset-side cause of
  a save that references a component that no longer exists.

## References

- `references/versioning-and-migration.md` - migration strategy in depth, save slots and
  profiles, cloud saves and conflicts, corruption recovery, encryption and why it is
  usually not worth it, and platform-specific storage rules.

## Related skills

- `unity-scripting` - serialization rules, `ScriptableObject` lifetime, and what Unity
  will and will not serialize.
- `level-design` - where checkpoints belong, and why one before the climax matters.
- `procedural-gen` - saving a generated world by saving its seed and its deltas.
- `game-production` - saves are part of the shell, and the shell is always underestimated.
