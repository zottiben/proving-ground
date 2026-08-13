---
name: unity-vfx
description: Build visual effects in Unity - the built-in Particle System and VFX Graph, choosing between them, emission and shape and lifetime modules, trails and decals, pooling and lifecycle, and the overdraw budget that decides whether an effect ships. Use when adding particles, explosions, impacts, muzzle flashes, weather or environmental effects, when effects do not appear or do not stop, or when VFX is costing frame time.
---

# Unity VFX

Effects are the layer where a game stops looking like a prototype, and they are also
where frame time disappears without anybody noticing. The cost of a particle effect is
almost never the particle count - it is **overdraw**: how many times each pixel gets
shaded by overlapping transparent quads. A hundred small particles are cheap; five
full-screen ones are not.

The other recurring problem is lifecycle. Effects that never stop, effects destroyed
before they finish, effects that were pooled without being reset. All three look like
different bugs and have the same cause.

## When to use

- Use when adding impacts, explosions, muzzle flashes, trails, weather or ambient
  effects.
- Use when particles do not appear, do not stop, or appear in the wrong place.
- Use when VFX is costing frame time, or when effects need pooling.
- Use to decide between the built-in Particle System and VFX Graph.

**When *not* to use:** for *which* effects an event should have and how strong they
should be, `game-feel`. For the shaders the particles are drawn with,
`unity-rendering`. For measuring the cost, `performance-optimization`.

## Particle System or VFX Graph

| | Built-in Particle System | VFX Graph |
|---|---|---|
| Simulation | CPU | GPU |
| Scale | hundreds to a few thousand | hundreds of thousands |
| Gameplay interaction | full - callbacks, collision events, trigger modules | limited; reads depth, not your colliders |
| Requires | nothing | a scriptable render pipeline and the package |
| Authoring | modules in the inspector | a node graph |
| Best for | gameplay-relevant effects, impacts, anything with callbacks | ambient, large-scale, spectacle |

The practical answer for most projects: the built-in system for anything gameplay
touches, VFX Graph for anything large and decorative. Being able to receive
`OnParticleCollision` matters more than particle count for a game effect, and needing
half a million particles is rarer than it sounds.

## Core workflow

1. **Decide whether the effect is gameplay or decoration.** That decides the system, and
   the decision is hard to reverse later.
2. **Author at the right scale.** Set the scaling mode deliberately: an effect authored
   in local scale on a parent that is scaled will surprise you.
3. **Bound the lifetime.** Every effect either loops forever by design or stops by
   itself. There is no third option, and "stops when destroyed" is not stopping.
4. **Pool anything frequent.** Instantiate plus Destroy per impact is both a cost and a
   source of garbage.
5. **Watch the overdraw, not the count.** Fewer, smaller, more opaque particles beat
   more, larger, more transparent ones every time.
6. **Reset on release, not on get.** A pooled effect that clears its state when it is
   fetched shows one frame of the previous effect.
7. **Look at it in motion, at gameplay speed.** An effect judged in a paused scene view
   is judged in the one condition it will never be seen in.

## Patterns

### 1. A pooled one-shot that returns itself

```csharp
// stopAction = Callback fires OnParticleSystemStopped when the system AND all its
// particles have finished, which is the only correct moment to recycle it.
[RequireComponent(typeof(ParticleSystem))]
public class PooledEffect : MonoBehaviour {
    ParticleSystem _system;
    IObjectPool<PooledEffect> _pool;

    void Awake() {
        _system = GetComponent<ParticleSystem>();
        var main = _system.main;
        main.stopAction = ParticleSystemStopAction.Callback;   // not Destroy, not Disable
    }

    public void Play(IObjectPool<PooledEffect> pool, Vector3 at) {
        _pool = pool;
        transform.position = at;
        _system.Clear(true);        // clear BEFORE playing: no leftovers from last time
        _system.Play(true);
    }

    void OnParticleSystemStopped() => _pool?.Release(this);
}
```

`Clear` before `Play`, not on release: clearing on release is also fine, but doing it in
both places is the reliable habit, and doing it in neither is the bug where an explosion
briefly shows the previous explosion's smoke.

### 2. Stopping an effect properly

```csharp
// Three different intentions, three different calls.
_system.Stop(true, ParticleSystemStopBehavior.StopEmitting);      // let existing particles finish
_system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // cut immediately
_system.Clear(true);                                              // remove particles, keep playing state
```

`withChildren: true` on all of them, or the sub-emitters keep going and the effect never
ends. A looping effect on a destroyed object is the most common "particles stuck on
screen" report, and it is always this.

### 3. The modules that carry most effects

An impact effect needs surprisingly few:

```
Main            start lifetime 0.3-0.6s, start speed, start size, gravity modifier,
                simulation space = World for anything that should not follow its emitter
Emission        Bursts, not a rate: an impact is one burst of 8-20
Shape           Cone or Hemisphere aligned to the surface normal
Velocity over Lifetime / Limit Velocity     drag, so particles slow rather than fly forever
Color over Lifetime    fade the alpha to zero - never let a particle vanish at full alpha
Size over Lifetime     shrink slightly; growth reads as smoke, shrink reads as sparks
Renderer        sort order, and a material that is additive for light, alpha for smoke
```

Simulation Space is the setting people miss. Local means particles follow the emitter,
which is right for a flame on a moving torch and wrong for sparks off a wall - those
should stay where they were made.

### 4. Trails and decals

```csharp
// A Trail Renderer follows a transform; a Line Renderer draws a path you supply.
// For a projectile, the trail belongs on the projectile and must be detached or
// cleared when the projectile is pooled, or the next shot draws a line from the last
// impact point to the new muzzle.
_trail.Clear();                                  // on spawn, always
_trail.emitting = true;
```

Decals in URP need the Decal Renderer Feature added to the renderer before a Decal
Projector renders anything at all. An absent renderer feature is the reason a decal
looks correct in the scene view gizmo and is invisible in play.

### 5. Reading the cost

```csharp
// Particle counts are visible at runtime; overdraw is not, and overdraw is the cost.
int alive = _system.particleCount;
```

Use the Frame Debugger to see how many transparent draws stack over the same pixels, or
switch the scene view draw mode to overdraw. A muzzle flash that fills the screen for
two frames costs more than a thousand sparks that never overlap.

## Pitfalls

- **Judging cost by particle count.** Overdraw is the cost. Big transparent quads are
  expensive whether there are five or five hundred.
- **`Stop` without `withChildren`.** Sub-emitters carry on and the effect never ends.
- **Not clearing a pooled system.** The previous effect's particles appear for a frame.
- **Instantiate and Destroy per impact.** Allocation, garbage, and a hitch under fire.
- **`stopAction = Destroy` on a pooled effect.** It destroys the object you were
  recycling.
- **Local simulation space for world effects.** Sparks that follow the gun around.
- **Particles vanishing at full alpha.** Always fade out over the last part of the
  lifetime.
- **Scaling the emitter and expecting the effect to scale.** Scaling Mode has to be set
  to Hierarchy, or the particles keep their authored size.
- **Sorting fights between effects and transparent geometry.** Transparent sorting is
  per-object by distance, so a large particle system will pop in front of or behind
  glass. Fix with sorting fudge, render order, or by not overlapping them.
- **VFX Graph expected to collide with gameplay colliders.** It collides against the
  depth buffer, which means it ignores anything off-screen and anything behind
  something else.
- **Effects authored at the wrong distance.** An impact designed by looking at it from
  half a metre is invisible at ten.
- **No effect on the most common action.** Everything spectacular has three effects and
  the thing the player does five times a second has none.

## Prove it with Proving Ground

Effects are the case where a screenshot is genuinely the right tool - and even then, the
symbolic view answers more than it looks like it should.

- `pg_capture` writes a screenshot with labelled boxes and returns a legend naming each
  one. Read the legend with the image: the image alone makes you infer what you are
  looking at, which is where vision models go wrong on particle-heavy scenes.
- `pg_visual_check` compares a capture against its stored baseline and writes a diff
  image on failure. For an effect that is supposed to look the same after a refactor,
  this is the check.
- `pg_view` tells you what is actually on screen, at what distance, and what is
  occluded - which answers "is the effect where I think it is" without eyes.
- `pg_events` catches the effect that spawned forty times instead of once.
- `pg_gate` with `frameTimeP95Ms` in `gates.json` catches the effect that costs six
  milliseconds only while it plays, which an average hides completely.

## References

- `references/particles-and-vfx-graph.md` - the module reference with values that work,
  recipes for impacts, explosions, muzzle flashes, weather and environment, VFX Graph
  concepts and its binder system, and an overdraw budget.

## Related skills

- `game-feel` - which events get effects, and how strong each should be.
- `unity-rendering` - particle shaders, blend modes, sorting and post-processing.
- `performance-optimization` - measuring overdraw and fill rate, and pooling.
- `audio-design` - the sound layer that fires with the same event.
