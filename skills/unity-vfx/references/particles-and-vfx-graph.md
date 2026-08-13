# Particles and VFX Graph - depth for `unity-vfx`

## 1. Module reference, with values that work

| Module | What it does | Values worth starting from |
|---|---|---|
| Main | lifetime, speed, size, gravity, simulation space, max particles | set Max Particles to a real ceiling; the default 1000 hides runaway emission |
| Emission | rate over time, rate over distance, bursts | bursts for events, rate over distance for trails and dust |
| Shape | where particles are born | Cone for directional impacts, Sphere for explosions, Mesh for shaped emission |
| Velocity over Lifetime | adds velocity | in world space for wind, local for swirl |
| Limit Velocity over Lifetime | drag | 1-4 for anything that should slow rather than fly off |
| Inherit Velocity | takes the emitter's motion | essential for effects on moving objects |
| Force over Lifetime | constant force | wind, updraught |
| Color over Lifetime | tint and alpha curve | always fade alpha to zero over the last 20-30% |
| Size over Lifetime | scale curve | sparks shrink, smoke grows |
| Rotation over Lifetime | spin | small random spin breaks up repeated textures |
| Noise | turbulence | strength 0.2-1, frequency 0.3-1; the cheapest way to stop particles looking linear |
| Collision | world collision | expensive - use World mode with a limited layer mask, and only when it matters |
| Sub Emitters | spawn on birth, death or collision | debris on collision, smoke on death |
| Texture Sheet Animation | flipbooks | the standard way to do smoke and fire from a sprite sheet |
| Trails | ribbons behind particles | cheap and very effective on sparks |
| Renderer | material, sorting, alignment | Billboard for most things, Stretched Billboard for sparks and rain |

Two settings people leave wrong: **Max Particles**, which should be a deliberate ceiling
so a runaway emitter cannot eat the frame, and **Simulation Space**, which decides
whether particles follow their emitter.

## 2. Recipes

**Impact on a hard surface.** Burst of 10-16, Cone shape aligned to the surface normal
with a 25-40 degree angle, lifetime 0.25-0.5 s, high start speed with strong drag,
Stretched Billboard renderer, additive material, sub-emitter for a single soft dust
puff. World simulation space.

**Explosion.** Three systems, not one: a fast bright core (0.1-0.2 s, additive), an
expanding smoke ball (1.5-3 s, alpha-blended, growing, rotating), and debris (mesh
particles with gravity and collision). The layering is what makes explosions read; one
system trying to be all three never does.

**Muzzle flash.** Two frames of a bright quad plus a burst of 3-6 sparks. Total lifetime
under 0.08 s. If it lasts long enough to look at, it is too long - and this is the effect
most often over-built.

**Rain.** One system, Stretched Billboard, Box shape above the camera, parented to the
camera with world simulation space so drops do not slide sideways when the player turns.
Collision off; a separate splash system emitting from a shape at ground level is far
cheaper than per-particle collision.

**Fire.** A flipbook flame at the base, a separate smoke system above with a longer
lifetime and slower speed, and a light with a subtle flicker. The light is what sells
it, and it is also the expensive part - one flickering light for a campfire, not one per
flame particle.

**Footstep dust.** Rate over distance rather than rate over time, so it emits when the
character moves and stops when they stand still, with no code.

## 3. Overdraw budget

Overdraw is fill rate: pixels shaded per frame. A rough working budget for a mid-range
PC target is around 2-3x the screen area in transparent overdraw, and mobile wants well
under that.

What blows it, in order:

1. **Large soft particles that fill the screen.** One full-screen smoke quad is one
   screen of overdraw, and ten stacked is ten.
2. **Long lifetimes with high emission.** Particles accumulate; the count at steady
   state is rate times lifetime, and people forget to do the multiplication.
3. **Soft particles.** They sample the depth buffer per pixel, which is not free.
4. **Many small systems each with their own material.** Draw call cost rather than fill
   cost, but it adds up.

Ways to buy it back: shrink the particles and add more of them, fade out earlier, use
alpha-clipped rather than blended where the shape allows, use a flipbook rather than
overlapping quads, and cap emission by distance from the camera.

## 4. VFX Graph concepts

The graph is built from contexts in a fixed order: **Spawn** (how many, when),
**Initialize** (per-particle starting attributes), **Update** (per-frame simulation), and
**Output** (how they are drawn). Blocks inside each context do the work, and attributes -
position, velocity, colour, size, age, lifetime - flow between them.

Because simulation is on the GPU, the CPU cannot read particle state back cheaply. That
is the fundamental trade: enormous counts, no gameplay callbacks.

Getting data in from the game uses **properties**, exposed on the graph and set from
script:

```csharp
_effect.SetVector3("TargetPosition", target.position);
_effect.SetFloat("Intensity", intensity);
_effect.SendEvent("OnHit");                    // triggers a Spawn context
```

Collision in VFX Graph is against the **depth buffer**, not against colliders. Particles
therefore collide with what the camera can see and pass through everything else,
including anything off-screen or occluded. For effects that must respect the real world,
use the built-in system.

## 5. Lifecycle checklist

Every effect in the project should have an answer for each:

1. Does it stop by itself, or is it deliberately looping?
2. If pooled, is it cleared before it plays again?
3. If it is on a moving object, is the simulation space right?
4. If it has sub-emitters or trails, do they stop with the parent?
5. What happens if the emitting object is destroyed mid-effect? (Detach it, or let it
   finish on a pooled object.)
6. What is its Max Particles ceiling?
7. Is there a distance beyond which it should not spawn at all?

Number five is the one that produces the "effect frozen in mid-air forever" bug, and it
has to be decided per effect: an explosion should outlive its source, a flame should
not.

## 6. Judging an effect

- Watch it at gameplay speed, not paused. Most effects that look good paused are too
  long.
- Watch it from the distance the player will actually be at.
- Watch it against the busiest background in the game, not against grey.
- Watch it five times in a row. Effects that are satisfying once and irritating on the
  fifth repeat are the ones attached to the most common action.
- Watch it with the sound. Half of what people call a good effect is the audio, and an
  effect judged silent will be over-built to compensate.
