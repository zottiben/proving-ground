# Feedback recipes - depth for `game-feel`

The detail the skill body defers: shake maths, easing choices in Unity, the rest of
the feedback menu, tier presets, and the accessibility toggles that have to ship.

## 1. The trauma model, and why it feels good

Track one `trauma` value in [0, 1]. Events **add** to it; it **decays** every frame;
the actual shake is `trauma * trauma` (or cubed for a harder knee). Three properties
fall out of that, and all three are why this model beat everything before it:

- small frequent events barely move the screen, because a small number squared is
  tiny;
- big events punch, because near 1 the square is near 1;
- shake **always ends**, because trauma decays to zero without anyone resetting it.

Starting points: decay 1.0-1.5 per second, per-hit trauma 0.15 (light) to 0.8 (heavy).
For a Cinemachine Perlin channel, amplitude gain up to about 2 and frequency gain up
to about 2.5 for a first-person camera; third person tolerates roughly double.

Decay on `Time.unscaledDeltaTime`, not `deltaTime`, or a hitstop that drops timeScale
to 0.05 also freezes the shake for the duration of the freeze - which reads as the
game hanging rather than as impact.

## 2. Easing in Unity, without a tween library

Unity ships no tween system. In order of preference:

| Job | Reach for |
|---|---|
| Spring-like follow, camera, UI slide | `Vector3.SmoothDamp` / `Mathf.SmoothDamp` |
| An authored, art-directable shape | `AnimationCurve` serialized on the component |
| Simple settle | `Mathf.SmoothStep`, or `1 - Mathf.Exp(-k * dt)` |
| Sequenced, chained motion | a coroutine, or the project's existing tween package |

`AnimationCurve` is underrated here: it puts the curve in the inspector where it can
be tuned without recompiling, and the shape is visible rather than implied by a magic
number.

Which curve for which job:

| Goal | Shape | Notes |
|---|---|---|
| Pop in | overshoot past target, settle back | the "alive" curve; use on scale, not position |
| Land, settle | ease-out | the default for anything coming to rest |
| Wind-up, anticipation | ease-in | slow start before a fast action reads as weight |
| A to B, both ends smooth | ease-in-out | camera moves, menu slides |
| Bounce, elastic | use sparingly | reads as cartoonish, and dates quickly |

Frame-rate independence matters here as much as anywhere. `Lerp(a, b, 0.1f)` per
frame is not a curve, it is a frame-rate-dependent approximation of one:

```csharp
// RIGHT: converges identically at any frame rate.
t = Mathf.Lerp(t, target, 1f - Mathf.Exp(-rate * Time.deltaTime));
// WRONG: faster at 144 fps than at 30, so the feel differs per machine.
t = Mathf.Lerp(t, target, 0.1f);
```

## 3. The rest of the feedback menu

- **Flash.** Tint the material or sprite white for one to three frames on hit, then
  ease back. Cheap, and enormously legible. With URP, use a `MaterialPropertyBlock`
  rather than touching `renderer.material`, which instantiates a copy every call.
- **Knockback.** An impulse away from the hit normal, clamped and short, plus a brief
  control lockout - brief. Let `physics-tuning` own stability.
- **Number or text pop.** Rises, drifts sideways by a random amount so stacked hits
  fan out, fades, eases out. Pool them.
- **Particles.** A short burst at the contact point. Pool or pre-warm; instantiating
  and destroying a `ParticleSystem` per hit is a measurable cost. See `unity-vfx`.
- **Freeze frame.** The hitstop from the body, scaled to importance. Optionally freeze
  only the attacker and target by scaling their animator speeds, rather than the world.
- **Anticipation and follow-through.** A few frames of wind-up before a big action and
  a settle afterwards read as weight. This is animation, not code - see
  `unity-animation`.
- **Post-processing punches.** A brief vignette, chromatic aberration or FOV kick for
  big moments. Keep them under 150 ms; anything longer stops being a punch and starts
  being a state.

## 4. Importance tiers

Define three presets and assign every juicy event to one. This is what stops a game
feeling either dead or exhausting.

| Tier | Trauma | Hitstop | Particles | Extra | Events |
|---|:---:|:---:|:---:|---|---|
| light | 0.10-0.20 | none | 0-4 | tick sound | footstep, hover, coin |
| medium | 0.30-0.50 | 0.04-0.06 s | 6-12 | flash | normal hit, land, pickup |
| heavy | 0.70-1.00 | 0.10-0.15 s | 20-40 | flash, FOV punch, number | crit, boss hit, death |

Put the table in code as a `ScriptableObject` so it is data, tunable, and reviewable -
and so the numbers in `feel.json` and the numbers in the game are the same numbers.

## 5. Accessibility, which is not optional

Three toggles, all cheap, all expected:

- **Reduce screen shake** - scale trauma output by a 0-100% setting. Default somewhere
  around 60-80%, not 100%.
- **Reduce flashing** - replace white flashes with a static tint. This one is a
  photosensitivity concern, not a preference.
- **Reduce camera motion** - disable FOV punches, camera roll and shake roll for
  motion sensitivity.

Each is one multiplier applied at the single entry point from the skill body, which is
the practical reason for having a single entry point.

## 6. Diagnosing "it feels bad" from outside the game

You cannot feel it, so convert the complaint into a measurement:

| Complaint | Measure |
|---|---|
| "unresponsive" | `input.moveLatency`, and whether input is buffered during animations |
| "floaty" | `jump.timeToApex`, `jump.fallMultiplier`, ground acceleration |
| "slippery" | `locomotion.accelTime` and `locomotion.stopTime` |
| "weightless hits" | `combat.hitstop`, whether a sound fires, whether anything moves |
| "sluggish" | attack commit time, and how much of it is uncancellable |

Every row is a metric in `feel.json` and a number `pg_run_scenario` reports back. That
is the whole trick: the complaint is subjective, the cause is not.
