---
name: audio-design
description: Build game audio in Unity - AudioSource and mixer setup, spatialisation and attenuation, variation so repeated sounds do not fatigue, ducking and mixing, adaptive music, and verifying that events actually fire and are actually bound to something. Use when adding sound effects, ambience or music, when audio is missing, too loud, repetitive or clipping, when setting up an AudioMixer, or when checking that a game's audio is wired correctly.
---

# Audio design

Audio is the highest-return, least-verified system in most games. It carries a
disproportionate share of how a game feels, and it fails silently: a missing sound
produces no error, no warning and no visual difference. Nobody notices until a player
says the game feels dead, which is a description of missing audio far more often than
it is a description of anything visual.

Which makes the checkable half worth taking seriously. **Whether an event fires,
whether anything is bound to it, and whether it fires four times a second or sixty is
fully verifiable**, and that is what actually goes wrong. Generation and taste are the
easy half.

## When to use

- Use when adding sound effects, ambience, music or audio feedback.
- Use when audio is missing, too loud, clipping, repetitive or muddy.
- Use when setting up an AudioMixer, buses, ducking or snapshots.
- Use to verify a game's audio wiring, particularly one you did not build.

**When *not* to use:** for which events *should* exist on a hit or a jump, that is
`game-feel` - this skill covers making them sound right once they exist. For dialogue
timing and subtitles, `dialogue-systems`.

## Core workflow

1. **Build the bus structure before the first sound.** Master, then Music, SFX, UI,
   Ambience, Voice. Retro-fitting a mixer once two hundred `AudioSource`s exist is a
   day nobody plans for.
2. **Give every verb a sound.** Every action the player takes should make a noise. The
   fastest way to make a game feel responsive is to make it audible.
3. **Vary everything repeated.** Random pitch and volume within a small band, and a
   pool of two to four variants for anything frequent. Identical repetition is the
   single most fatiguing thing in game audio.
4. **Spatialise deliberately.** 2D for UI and music, 3D for anything with a position,
   and set the rolloff curve rather than accepting the default.
5. **Duck rather than balance by hand.** Dialogue over music, explosions over ambience:
   a sidechain compressor or a snapshot transition beats individually tuned volumes
   that stop working the moment anything changes.
6. **Emit named events, not `PlayOneShot` calls scattered through gameplay.** One layer
   between the game and the mixer is what makes the wiring checkable.
7. **Declare the contract and check it.** `Contracts/audio.json` lists required events
   and their rate limits. `pg_check audio` after a real run reports what actually fired.

## Patterns

### 1. Mixer routing, and volume sliders that behave

```csharp
// The mixer works in decibels, which are logarithmic. A linear 0-1 slider mapped
// straight to dB means the top 10% of the slider does almost everything and the
// bottom 90% is inaudible. Convert.
public void SetVolume(string exposedParameter, float linear01) {
    float dB = linear01 <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp01(linear01)) * 20f;
    _mixer.SetFloat(exposedParameter, dB);
}
```

Expose one parameter per bus, save them, and restore them at startup. Mixer parameter
values do not persist by themselves, and a settings screen that silently forgets is
worse than no settings screen.

### 2. Variation, so the twentieth footstep is not the first

```csharp
// One clip played identically 200 times is what "machine-gun audio" means. A small
// pool plus pitch and volume jitter fixes it for the cost of three lines.
[SerializeField] AudioClip[] _variants;
[SerializeField] Vector2 _pitchRange  = new(0.92f, 1.08f);
[SerializeField] Vector2 _volumeRange = new(0.85f, 1.0f);

public void Play(AudioSource source) {
    source.pitch  = Random.Range(_pitchRange.x, _pitchRange.y);
    source.volume = Random.Range(_volumeRange.x, _volumeRange.y);
    source.PlayOneShot(_variants[Random.Range(0, _variants.Length)]);
}
```

Keep the pitch band narrow. Beyond roughly plus or minus 10% the sound stops being the
same object and starts being a different, smaller or larger one.

### 3. Spatialisation that matches the space

```csharp
_source.spatialBlend  = 1f;                              // 0 = 2D, 1 = fully 3D
_source.rolloffMode   = AudioRolloffMode.Custom;         // the default logarithmic curve
_source.minDistance   = 1f;                              // is rarely what a level needs
_source.maxDistance   = 25f;
_source.dopplerLevel  = 0f;                              // 1 is very strong; most games want less
_source.priority      = 128;                             // lower number = kept when voices run out
```

`minDistance` is where attenuation *starts*, not where the sound begins - inside it the
sound plays at full volume. A `minDistance` of 1 on a large machine makes it audible
only when you are standing on it; a `minDistance` of 20 makes it fill the level.

### 4. Events, not scattered play calls

```csharp
// One entry point means the audio contract can be checked, the mixer can be changed
// once, and a designer can find every sound in the game by searching one file.
public static class Audio {
    public static void Post(string eventName, Vector3 at) { /* look up, route, play */ }
}

Audio.Post("weapon.fire", muzzle.position);
Audio.Post("player.land", transform.position);
```

If the project has no instrumentation at all, `pg_watch_audio` infers events from
`AudioSource` activity during a run, which gives a legacy project a starting inventory
without touching its code.

### 5. The audio contract

```jsonc
// ProvingGround/Contracts/audio.json
{
  "events": {
    "player.jump":  { "category": "sfx", "required": true, "maxPerSecond": 4 },
    "player.land":  { "category": "sfx", "required": true, "maxPerSecond": 4 },
    "weapon.fire":  { "category": "sfx", "required": true, "maxPerSecond": 12 },
    "ui.confirm":   { "category": "ui",  "required": true, "maxPerSecond": 6 },
    "ambience.bed": { "category": "amb", "required": true, "minLengthSeconds": 20 }
  },
  "forbidDeadEvents": true,          // an event nothing is bound to
  "forbidUndeclaredEvents": true     // a sound firing that nobody declared
}
```

`maxPerSecond` is the one that finds real bugs. A footstep event firing sixty times a
second sounds like a buzz and is usually a state machine calling it from `Update`
rather than from an animation event.

## Pitfalls

- **No mixer.** Every `AudioSource` at its own volume, balanced by hand, and no way to
  duck, mute or provide a settings screen.
- **A linear volume slider driving decibels.** Most of the range does nothing.
- **Identical repeated clips.** Fatiguing within a minute.
- **Pitch variation too wide.** The object appears to change size.
- **Everything 2D.** No positional information, and the world stops having a shape.
- **Default rolloff on everything.** Distant sounds too loud, near ones with no
  presence.
- **Doppler at 1.** Every passing object warbles. Most games want 0 to 0.3.
- **Clips at wildly different levels.** Normalise on import, mix on the bus, and check
  the files rather than trusting the source.
- **Everything at full volume simultaneously.** Voices run out, and Unity drops the ones
  it decides matter least. Set priorities, and duck.
- **`Play` instead of `PlayOneShot` for overlapping sounds.** `Play` restarts the source
  and cuts off the previous sound.
- **An `AudioSource` per object, all preloaded.** Memory and voice pressure. Pool
  sources for one-shots.
- **Decompress On Load on everything.** Large clips should stream; medium ones should be
  compressed in memory. A few dozen decompressed ambience beds is tens of megabytes for
  no benefit.
- **Two `AudioListener`s.** An error, and the second one wins unpredictably. One per
  scene, and split-screen needs a deliberate decision about where it goes.
- **Music that never changes.** Ten minutes of the same loop under every situation is
  worse than silence, and players will mute it.
- **No mute-on-focus-loss.** A game that keeps playing audio in the background is a game
  people close.

## Prove it with Proving Ground

Two different checks, and they answer different questions.

**`pg_check audio`** - the wiring, from the last play-mode run. Required events that
never fired, events firing above their rate limit, events nothing declared, events
nothing is bound to. This is where audio actually goes wrong, and none of it is
audible in a screenshot.

```
pg_watch_audio          before the run, if the game has no instrumentation
pg_play -> pg_run_scenario or pg_run_probe
pg_check audio          what fired, how often, and what did not
```

**`pg_check audioassets`** - the files themselves: level, peaks, leading silence, loop
seams. It catches the clip that is 12 dB louder than everything else and the loop with
a click in it.

One stated limitation worth understanding: **level checks are RMS dBFS, not LUFS.**
Proper BS.1770 loudness needs K-weighting the package does not implement, and reporting
an unweighted measurement under the LUFS name would be wrong in a way nobody would
catch. Use it as a relative measure across your own library, which is what it is good
for, and do a real loudness pass in a DAW before shipping.

## References

- `references/mixing-and-adaptive-music.md` - bus structure and ducking in detail,
  snapshots, occlusion and reverb zones, voice management, adaptive music by layering
  and by transitions, and import settings per clip category.

## Related skills

- `game-feel` - which events exist, and why sound is the strongest feedback layer.
- `dialogue-systems` - voice, subtitles and the timing that goes with them.
- `performance-optimization` - voice counts, memory from load types, and streaming cost.
- `unity-scripting` - animation events, and where audio calls belong in the frame.
