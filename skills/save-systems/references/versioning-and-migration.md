# Versioning, slots and recovery - depth for `save-systems`

## 1. Migration strategy

Three approaches, and the right one depends on how much the format can change.

**Sequential migration** - one function per version bump, applied in order. Best for
most games: each step is small, testable and reviewable, and adding version 7 means
writing one function rather than understanding six.

**Tolerant deserialization** - never remove or rename a field, only add, and treat every
field as optional with a default. Cheaper to maintain, but the format accumulates dead
fields forever and the meaning of "default" drifts.

**Snapshot conversion** - keep the old type around, deserialize into it, convert to the
new one. Verbose, and the right answer when a change is so structural that a field-level
migration cannot express it.

Whichever you pick, two rules hold:

- **Never renumber.** A version number, once shipped, means one exact format forever.
- **Keep a corpus.** One real save file per shipped version, in the repository, loaded
  by a test. Migration chains rot silently; a corpus is the only thing that notices.

## 2. Slots and profiles

Directory layout that avoids most of the mess:

```
persistentDataPath/
  profiles/
    <profileId>/
      profile.json          name, playtime, settings that are per-player
      slot-0.json           the save
      slot-0.json.bak       the previous one
      slot-0.meta.json      small: name, timestamp, screenshot path, progress summary
      screenshots/slot-0.png
```

A separate small metadata file per slot is worth the extra write: the load menu can
list ten saves without deserializing ten full save files, which on a large save is the
difference between an instant menu and a visible pause.

## 3. Cloud saves and conflicts

If saves sync - Steam Cloud, console services, your own backend - conflicts are not an
edge case, they are a Tuesday. A player who plays on a machine that was offline will
produce two divergent saves.

- **Timestamp every save**, with a monotonic counter as well as wall-clock time. Clocks
  are wrong, and they are wrong in both directions.
- **Never merge automatically.** Show both, describe each in terms the player
  understands - "3 hours ago, Chapter 4" versus "yesterday, Chapter 5" - and let them
  choose.
- **Keep the loser.** The player who picks wrong should be able to recover.
- **Keep saves small.** Sync quotas are real, and a 40 MB save that syncs on every
  autosave is a support burden.

## 4. Corruption recovery

Assume every file on disk may be truncated, empty, or from a different game.

```csharp
public bool TryLoad(string slot, out SaveGame save) {
    foreach (var candidate in new[] { Path(slot), Path(slot) + ".bak" }) {
        try {
            if (!File.Exists(candidate)) continue;
            var text = File.ReadAllText(candidate);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var raw = JObject.Parse(text);                      // throws on truncation
            int version = raw["Version"]?.Value<int>() ?? 0;
            if (version == 0 || version > SaveGame.CurrentVersion) continue;

            save = Migrate(raw);
            return true;
        }
        catch (Exception e) { Debug.LogWarning($"Save {candidate} unreadable: {e.Message}"); }
    }
    save = null;
    return false;                                              // tell the player, do not start half-loaded
}
```

The important behaviour is the last line. A load that partially succeeds produces a
game in an impossible state, and the resulting bug reports describe everything except
the load.

A checksum over the payload catches truncation and casual editing. It is not security -
anybody can recompute it - and treating it as security is the mistake in the next
section.

## 5. Encryption, and why it usually is not worth it

Encrypting a single-player save prevents nothing: the key ships with the game. It does
raise the cost of debugging enormously - a player can no longer send you a readable
file, and you can no longer see what is wrong at a glance.

Encrypt when there is a real reason: competitive leaderboards, anti-cheat requirements
on a platform, or purchased content. Otherwise ship readable JSON and accept that
players will edit it. Many of them will enjoy it, and none of it will cost you anything.

If you do encrypt, keep an unencrypted debug path behind a flag, or the first support
request will be unanswerable.

## 6. Platform storage rules

- **`Application.persistentDataPath`** is the only path writable everywhere. Its actual
  location varies by platform and may change between OS versions; never hard-code it or
  show it to players as a stable address.
- **`Application.dataPath`** is read-only in a build. So is `StreamingAssets`.
- **WebGL** persists to browser storage through an emulated filesystem, and writes are
  not durable until the runtime flushes them. Save less often and larger, and understand
  that clearing browser data deletes everything.
- **Consoles** have their own storage APIs, quotas, and required UI for save and load.
  Budget for the platform layer as a real piece of work, not a wrapper.
- **Mobile** may have the app killed at any moment without warning. Save on pause
  (`OnApplicationPause`), not only on quit.

## 7. Autosave design

- **At safe points**, defined by the level rather than by a timer: after a checkpoint,
  after a significant transaction, on entering a new area.
- **Never during** a scene load, a cutscene, or a state transition.
- **Show it**, briefly and unobtrusively. An invisible autosave is one players do not
  trust, and a visible one is how they learn not to quit during it.
- **Never block on it.** Build the snapshot on the main thread, serialize and write off
  it. A 200 ms hitch every autosave is what makes players turn the feature off.
- **Separate autosaves from manual saves.** Overwriting a player's manual save with an
  autosave is a bug that costs somebody hours, and they will not come back.

## 8. Testing a save system

1. Save, quit the process entirely, relaunch, load. Not a domain reload - the process.
2. Kill the process during a write. The previous save must still load.
3. Load every save in the version corpus.
4. Truncate a save to half its length and load it. It must fail cleanly.
5. Hand-edit a value out of range and load. It must clamp or reject, not propagate.
6. Save, edit the level, load. What breaks tells you what was identified by position or
   by name rather than by id.
7. Fill a slot, then load a different slot. State from the first must not survive.

Number six is the one that finds real bugs, and it is the one nobody runs.
