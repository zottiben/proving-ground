---
name: game-feel
description: Make actions in a Unity game satisfying - hitstop, screen shake, eased and overshooting motion, squash and stretch, knockback, flashes and layered feedback, with the timings that decide whether a mechanic reads as weighty or dead. Use when something is mechanically correct but feels weak, floaty or unsatisfying, when asked to make a game punchier, snappier or juicier, or when adding impact feedback to hits, jumps, pickups and deaths.
---

# Game feel

The difference between a mechanic that works and one that feels good is feedback: the
layered, briefly exaggerated response an action provokes. This skill is the polish on
top of a mechanic, not the mechanic. If the jump arc itself is wrong, fix the arc
first - juice on a bad arc is a bad arc with particles.

The reason this is worth a skill: you cannot feel the game. What you can do is measure
it. Feel constants are numbers, they live in `ProvingGround/Contracts/feel.json`, and
`pg_run_scenario` reports what the game actually produced rather than what the fields
say.

## When to use

- Use when an action is mechanically correct but feels weightless, mushy or dead.
- Use to add hitstop, shake, easing, squash and stretch, knockback or flashes.
- Use to decide *how much* is enough, and where it crosses into noise.

**When *not* to use:** for the controller maths - jump height, coyote time, buffering -
use `input-systems` and the feel contract. For camera follow, deadzone and orbit
framing use `camera-systems`; this skill only feeds it shake. For mixing and ducking
use `audio-design`. For particle authoring use `unity-vfx`.

## Core principle: layered, exaggerated, and brief

One satisfying hit is usually five to eight tiny responses firing together inside
about 100 ms: a sound, a particle burst, a brief freeze, a flash, a knockback, a small
shake, and a number popping up. Each is cheap; stacked, they read as impact.

Two rules stop it becoming a mess. **Exaggerate briefly and return to rest** - juice is
transient, never a new resting state. **Scale to importance** - a footstep is not a
boss death, and a game where everything is maxed is exhausting and hides the moments
that matter.

## Core workflow

1. **Confirm the event hooks exist.** Juice attaches to discrete events: hit, land,
   pickup, death, fire. If the mechanic does not emit them, add them first - and emit
   them through whatever your project uses for audio events too, so `pg_check audio`
   can see them.
2. **Pick channels per event** from the menu: sound, particles, shake, hitstop, flash,
   knockback, tween, number pop. Start with two or three; add until it reads, then stop.
3. **Ease everything.** Route scale, position and UI changes through a curve with an
   ease. Overshoot for a pop, ease-out to settle. Linear motion feels mechanical.
4. **Reserve hitstop and shake for impact.** They are the strongest and most abusable
   tools. Short, scaled to importance, never on routine actions.
5. **Keep feedback off the simulation.** Shake moves the camera or a visual pivot,
   never the character's transform. Hitstop uses time scale, never a gameplay stall.
6. **Tier it.** Define small, medium and large presets and assign every event to a
   tier, so the whole game stays proportional.
7. **Write the constants into `feel.json`** and measure them. `combat.hitstop` is a
   contract term, not a number in somebody's head.

## Patterns

### 1. Hitstop that actually resumes

```csharp
// The classic bug: WaitForSeconds never elapses at timeScale 0, so the game freezes
// forever. WaitForSecondsRealtime is unaffected by timeScale and is the fix.
IEnumerator HitStop(float duration = 0.08f, float scale = 0.05f) {
    Time.timeScale = scale;
    yield return new WaitForSecondsRealtime(duration);
    Time.timeScale = 1f;
}
```

Two Unity specifics worth knowing. `Time.fixedDeltaTime` is *not* scaled automatically,
so physics keeps stepping at its own rate while the world crawls - usually what you
want. And a coroutine on a disabled object never resumes, so run hitstop from a
manager that outlives the thing that got hit.

### 2. Trauma-based shake, through Cinemachine

```csharp
// Events ADD trauma; it decays; shake is trauma squared so small hits barely register
// and big ones punch. Because trauma decays, the shake always ends on its own.
using Unity.Cinemachine;

public class ShakeDriver : MonoBehaviour {
    [SerializeField] CinemachineBasicMultiChannelPerlin _noise;   // on the CinemachineCamera
    [SerializeField] float _decay = 1.2f, _maxAmplitude = 2f, _maxFrequency = 2.5f;
    float _trauma;

    public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

    void Update() {
        _trauma = Mathf.Max(0f, _trauma - _decay * Time.unscaledDeltaTime);  // unscaled: shake survives hitstop
        float shake = _trauma * _trauma;
        _noise.AmplitudeGain = _maxAmplitude * shake;
        _noise.FrequencyGain = _maxFrequency * shake;
    }
}
```

If the project uses Cinemachine's impulse system instead, `CinemachineImpulseSource.
GenerateImpulse()` with a `CinemachineImpulseListener` on the camera gives you the
same result with propagation and falloff for free. Either way the shake rides on the
camera, never on the player.

### 3. Squash, stretch and overshoot

```csharp
// Conserve volume on the event, then spring back past 1 and settle. The overshoot is
// what reads as life; a linear return reads as a machine resetting.
IEnumerator Pop(Transform t, float duration = 0.18f, float squash = 0.3f) {
    var rest = t.localScale;
    t.localScale = new Vector3(rest.x * (1f + squash), rest.y * (1f - squash), rest.z);
    for (float e = 0f; e < duration; e += Time.unscaledDeltaTime) {
        float k = e / duration;
        float overshoot = 1f + 0.25f * Mathf.Sin(k * Mathf.PI) * (1f - k);   // peaks early, dies at the end
        t.localScale = Vector3.LerpUnclamped(t.localScale, rest * overshoot, k * k);
        yield return null;
    }
    t.localScale = rest;
}
```

Unity ships no tween library. `Vector3.SmoothDamp` covers spring-like follow,
`AnimationCurve` covers authored shapes and is inspector-tunable, and a coroutine
covers the rest. Whichever you use, keep the *curve choice* consistent across the game.

### 4. One call per event, tiered

```csharp
public enum Impact { Light, Medium, Heavy }

public void Feedback(Impact tier, Vector3 at) {
    switch (tier) {
        case Impact.Light:                                   // footstep, UI, pickup
            Audio.Play("tick", at); Shake.AddTrauma(0.15f); break;
        case Impact.Medium:                                  // a normal hit, a landing
            Audio.Play("hit", at); Shake.AddTrauma(0.40f);
            StartCoroutine(HitStop(0.05f)); Particles.Burst(at, 8); break;
        case Impact.Heavy:                                   // a crit, a death, an explosion
            Audio.Play("boom", at); Shake.AddTrauma(0.80f);
            StartCoroutine(HitStop(0.12f)); Particles.Burst(at, 30); Flash(0.06f); break;
    }
}
```

One entry point per event keeps the whole game proportional, and makes an
accessibility scale on shake and flashing a one-line change rather than a hunt.

## Pitfalls

- **`WaitForSeconds` inside hitstop.** At `timeScale` 0 it never elapses and the game
  is frozen permanently. Use `WaitForSecondsRealtime`.
- **Shaking the player transform.** Desyncs collision, aim and the camera's follow
  target. Shake the camera or a visual-only child.
- **A fresh `Random` offset every frame.** That buzzes like static. Drive shake from
  smooth noise and decaying trauma so it is continuous and self-ending.
- **Hitstop every frame of a held attack.** Locks the game. Fire it once per impact,
  and guard against re-entry while one is running.
- **Linear tweens everywhere.** Ease almost everything; reserve overshoot for pops and
  ease-out for settles.
- **Permanent exaggeration.** Scale that never returns, shake that never decays. It
  becomes the new normal and stops reading as feedback at all.
- **Over-juicing routine actions.** Full shake on every footstep causes nausea and
  buries the impacts that matter.
- **Feedback that blocks input.** A long freeze or an uncancellable animation hurts
  responsiveness more than the juice helps. Keep it short, and let input buffer
  through it - see `input-systems`.
- **Scaling `Time.timeScale` without scaling audio.** Pitch-shifting the whole mix on
  every hit is a distinctive and unwelcome sound. Decide deliberately.

## Prove it with Proving Ground

Feel constants belong in `Contracts/feel.json`, where they are diffed rather than
remembered:

```jsonc
{ "metrics": {
    "combat.hitstop":     { "min": 0.03, "max": 0.15, "unit": "s" },
    "input.moveLatency":  { "max": 3, "unit": "frames" },
    "jump.apexHeight":    { "min": 0.9, "max": 1.4, "unit": "m" }
}}
```

- `pg_norms <genre>` for what the band should be, with the reasoning attached.
- `pg_run_scenario` to drive the action and diff every measured metric in one pass.
  The probe derives apex, airtime, acceleration and input latency from observed motion,
  so it reports what the game does rather than what its fields claim.
- `pg_events` to confirm each feedback channel actually fired, once, in the frame you
  expected - a flash that never fires and a flash that fires forty times both look
  plausible in code.
- `pg_capture` only when the question is genuinely aesthetic.

Never widen a tolerance to make a feel check pass. If the number is wrong, say the
number is wrong and why.

## References

- `references/feedback-recipes.md` - the trauma maths, an easing cheat sheet with
  `AnimationCurve` shapes, the rest of the feedback menu, importance-tier presets, the
  accessibility toggles to ship, and where each piece binds in Unity.

## Related skills

- `camera-systems` - owns follow and framing; this skill only feeds it trauma.
- `input-systems` - responsiveness, buffering, and the latency budget juice must respect.
- `unity-vfx` - the particle half of every feedback bundle.
- `audio-design` - the sound half, and why it is usually the strongest of the layers.
- `physics-tuning` - knockback impulses and the timestep juice must not destabilise.
