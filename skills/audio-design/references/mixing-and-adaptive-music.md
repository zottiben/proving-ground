# Mixing and adaptive music - depth for `audio-design`

## 1. Bus structure

Build this before the first sound. Five buses cover almost every game:

```
Master
├── Music          the score; ducked under dialogue
├── SFX            gameplay one-shots and loops
│   ├── Player     always audible; give it headroom
│   └── World      everything else
├── Ambience       beds and loops, per area
├── Voice          dialogue and barks; ducks everything else
└── UI             menus and notifications; unaffected by in-world effects
```

Two properties fall out of it. Every category gets a volume slider for free, and any
one of them can be ducked, muted or paused as a group. Retro-fitting this after two
hundred sources exist is a day of work and a lot of missed sources.

Expose one volume parameter per bus in the mixer, and persist them. Mixer parameters do
not save themselves.

## 2. Ducking

Ducking lowers one bus while another is active. Two ways in Unity's mixer:

- **Sidechain compression.** Put a Duck Volume effect on the bus to be lowered and a
  Send from the trigger bus into it. This reacts continuously and is right for dialogue
  over music.
- **Snapshots.** Define a mixer state and transition to it over a time. Right for
  discrete state changes - paused, underwater, in a menu, low health.

```csharp
// Snapshots: a whole mix state, blended over a duration. Cheap, and it reads well.
_pausedSnapshot.TransitionTo(0.25f);
// ... and back
_defaultSnapshot.TransitionTo(0.4f);
```

Ducking values that work: dialogue ducks music by 8-12 dB, ambience by 4-6 dB. Attack
fast (10-30 ms) so the duck happens before the word; release slow (300-800 ms) so the
music does not pump between sentences.

## 3. Occlusion and reverb without a middleware licence

Full occlusion needs geometry-aware audio, which Unity does not ship. A cheap
approximation that reads convincingly:

```csharp
// Raycast from the listener to the source. If it is blocked, lower the cutoff of a
// low-pass filter on that source. Muffled is what "behind a wall" sounds like.
bool blocked = Physics.Linecast(_listener.position, transform.position, _occluders);
float target = blocked ? 900f : 22000f;                  // Hz
_lowPass.cutoffFrequency = Mathf.MoveTowards(_lowPass.cutoffFrequency, target,
                                             12000f * Time.deltaTime);
```

Move toward the target rather than setting it: an instant cutoff change is audible as a
click, and a source crossing a doorway would otherwise chatter.

Reverb zones give you space for free. One per area type - corridor, hall, exterior,
tunnel - and let them blend. Reverb is what stops every sound seeming to happen in the
same place regardless of where the player is.

## 4. Voice management

Unity has a real voice limit (Audio project settings: real and virtual voices). Past
the real limit, sounds are virtualised - they keep their playhead but produce no
output - and which ones survive depends on `priority` and audibility.

- Set `priority` deliberately: player actions and dialogue low (kept), distant ambient
  detail high (dropped).
- Cap concurrent instances per event. Twelve explosions in one frame do not sound twelve
  times as loud, they sound like distortion. Play two or three and drop the rest.
- Pool `AudioSource`s for one-shots rather than creating and destroying them.
- Watch for loops that are never stopped. A looping source on a destroyed-but-pooled
  object consumes a voice forever, and this is the most common cause of "audio stops
  working after a while".

## 5. Import settings by category

| Category | Load type | Format | Notes |
|---|---|---|---|
| Short one-shots (under ~1 s) | Decompress On Load | ADPCM or PCM | decompression cost is trivial, latency is zero |
| Medium SFX and loops | Compressed In Memory | Vorbis, quality 70 | the default for most of the library |
| Music and long ambience | Streaming | Vorbis, quality 70-100 | keeps them out of memory entirely |
| Dialogue | Streaming or Compressed In Memory | Vorbis | depends on how many lines can play at once |

Force To Mono on anything 3D-positioned: a stereo clip played in 3D is downmixed
anyway, and you paid twice the memory for it. Keep stereo for music, ambience beds and
UI.

Preload Audio Data on anything needed at the start of a level; off for everything else,
so the level does not pay for sounds it may never play.

## 6. Adaptive music

Two techniques, and most good implementations use both.

**Vertical layering.** One piece of music, several stems playing in sync, faded in and
out by game state. Combat adds drums and brass; exploration keeps pads and strings.
Because the stems are always playing in sync, transitions are instant and never lose
the beat.

```csharp
// All layers start together and stay in sync; only their volumes change.
void SetIntensity(float intensity01) {
    _drums.volume  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.6f, intensity01));
    _brass.volume  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.9f, intensity01));
    _pads.volume   = 1f - _brass.volume * 0.5f;
}
```

**Horizontal resequencing.** Distinct sections queued after each other, switching at a
musical boundary rather than immediately. Needs a scheduler:

```csharp
// AudioSettings.dspTime is the audio clock; Time.time is not accurate enough to
// schedule music against and will drift audibly within a minute.
double barLength = 60.0 / _bpm * _beatsPerBar;
double nextBar   = _sectionStartDsp + Mathf.Ceil((float)((AudioSettings.dspTime - _sectionStartDsp) / barLength)) * barLength;
_next.PlayScheduled(nextBar);
_current.SetScheduledEndTime(nextBar);
```

`PlayScheduled` and `SetScheduledEndTime` against `AudioSettings.dspTime` are the only
way to get sample-accurate music transitions in Unity. Scheduling on `Time.time` or in
`Update` produces gaps and overlaps that sound exactly like a mistake.

**Stingers** - short musical phrases fired on an event, over whatever is playing. The
cheapest adaptive music there is, and often the most effective: a two-second phrase on
a kill, a discovery or a death does more than an entire layered system nobody notices.

## 7. A mix pass, in order

1. **Set the loudest thing first.** Usually the player's primary weapon or the biggest
   impact. Everything else is relative to it.
2. **Set dialogue second**, and make it the clearest thing in the mix at all times.
3. **Music third**, sitting under both, and ducked when they play.
4. **Ambience fourth**, quiet enough that it is only noticed when it stops.
5. **UI last**, consistent in level, and never louder than gameplay.
6. **Then listen on bad speakers.** Laptop speakers and phone speakers have no bass, and
   a mix balanced only on headphones falls apart on them. If the game is unintelligible
   there, the mix is wrong regardless of how it sounds in the studio.

`pg_check audioassets` measures the files - RMS level, peaks, leading silence, loop
seams - which catches the clip that is 12 dB out before the mix pass starts, rather
than after somebody has compensated for it everywhere else.
