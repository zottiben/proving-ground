---
name: physics-tuning
description: Tune Unity physics so it is stable, reproducible and feels right - fixed timestep choice, interpolation, continuous collision detection, solver iterations, mass and drag, kinematic versus dynamic movement, layer matrices, and the determinism a replayable scenario depends on. Use when objects jitter, tunnel through walls, explode, sink into the floor, behave differently at different frame rates, or when a physics-driven character feels wrong.
---

# Physics tuning

Unity's physics is a fixed-step simulation sampled by a variable-rate renderer, and
almost every physics complaint traces back to that one sentence. Jitter is a sampling
problem. Tunnelling is a step-size problem. "It behaves differently on my machine" is a
frame-rate coupling problem.

Get the timestep, the interpolation and the movement API right and most of the rest
follows. Get them wrong and no amount of tuning masses and drag will help.

## When to use

- Use when objects jitter, vibrate, sink, explode or pass through geometry.
- Use when behaviour changes with frame rate or between machines.
- Use when a rigidbody character feels floaty, heavy or unresponsive.
- Use before relying on a scenario being reproducible.

**When *not* to use:** for the jump arc as a design value, that is the feel contract
and `input-systems`. For the cost of physics rather than its correctness,
`performance-optimization`. For navmesh agents, which are not physics at all, `game-ai`.

## The frame model, which explains everything else

```
Update       once per rendered frame, variable dt        <- read input here
FixedUpdate  zero or more times per frame, fixed dt      <- apply forces here
             (Unity runs it until simulated time catches up with real time)
LateUpdate   once per rendered frame, after everything   <- follow with the camera here
```

`FixedUpdate` running **zero** times in a frame is why polling input there drops
presses. It running **several** times is why applying an impulse there multiplies it.
And the renderer drawing bodies at their last simulated position, rather than where
they would be now, is exactly what interpolation exists to fix.

## Core workflow

1. **Choose the timestep deliberately.** Default `Time.fixedDeltaTime` is 0.02 (50 Hz).
   0.0167 (60 Hz) suits anything action-oriented. Below about 0.01 you are paying a lot
   for very little.
2. **Cap `Time.maximumDeltaTime`.** A frame spike otherwise triggers a burst of catch-up
   steps, which costs more time, which causes a bigger spike. The default 0.333 is
   already a cap; lower it if a hitch can cascade.
3. **Interpolate anything the camera watches.** `Rigidbody.interpolation = Interpolate`.
   This is the single most valuable line in this skill.
4. **Match collision detection to speed.** Discrete is the default and it tunnels.
   Anything fast gets Continuous or ContinuousDynamic.
5. **Move bodies with the physics API.** `AddForce`, `MovePosition`, or setting
   `linearVelocity`. Writing `transform.position` on a non-kinematic rigidbody teleports
   it past the solver and produces exactly the bugs it looks like it should.
6. **Set up the layer collision matrix early.** Most physics cost and most surprise
   interactions are pairs that should never have been tested.
7. **Fix the timestep in scenarios** so a run is reproducible, and treat a scenario that
   does not reproduce as a bug in the game, not in the harness.

## Patterns

### 1. The four settings that fix most complaints

```csharp
// Project-wide, once, deliberately.
Time.fixedDeltaTime    = 1f / 60f;   // matches the target frame rate; 0.02 is the default
Time.maximumDeltaTime  = 0.1f;       // cap the catch-up burst after a hitch

// Per body, on anything the player looks at or that moves quickly.
_body.interpolation          = RigidbodyInterpolation.Interpolate;
_body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
```

Interpolate for the player and anything the camera follows. Extrapolate only where a
frame of predicted position is better than a frame of lag, which in practice is almost
nothing - it overshoots on every direction change.

### 2. Deriving gravity from the jump you want

```csharp
// Author what the player perceives, derive what the engine needs. Tuning a jumpForce
// against a gravityScale is two unknowns fighting; this is one equation with an answer.
[SerializeField] float _apexHeight = 1.2f;     // m
[SerializeField] float _timeToApex = 0.35f;    // s
[SerializeField] float _fallMultiplier = 1.8f; // a symmetric arc is the floaty-jump mistake

float Gravity      => -2f * _apexHeight / (_timeToApex * _timeToApex);
float JumpVelocity =>  2f * _apexHeight /  _timeToApex;

void FixedUpdate() {
    float g = _velocity.y < 0f ? Gravity * _fallMultiplier : Gravity;
    _velocity.y += g * Time.fixedDeltaTime;
}
```

### 3. Kinematic versus dynamic, and the API each needs

```csharp
// Dynamic: the solver owns it. Push it with forces and let collisions resolve.
_body.AddForce(direction * _acceleration, ForceMode.Acceleration);

// Kinematic: you own it, but you still want collisions to be seen by the solver.
// MovePosition sweeps to the target and generates contacts; transform.position does not.
_kinematicBody.MovePosition(_kinematicBody.position + delta);

// A CharacterController is neither: it is a swept capsule with its own solver, it
// ignores forces entirely, and anything it should push has to be pushed by hand in
// OnControllerColliderHit.
_controller.Move(velocity * Time.deltaTime);
```

`ForceMode` matters more than the magnitude. `Force` and `Acceleration` are continuous
and belong in `FixedUpdate`; `Impulse` and `VelocityChange` are instantaneous and
belong at the moment of an event. Applying an `Impulse` every `FixedUpdate` applies it
fifty times a second, which is how objects end up in orbit.

### 4. A scenario that reproduces

```jsonc
// Seed and timestep are pinned, so the same steps produce the same run. If it does not
// reproduce, something in the game is reading unseeded randomness or unfixed time.
{ "name": "drop-test", "seed": 12345, "fixedDeltaTime": 0.0166667, "steps": [
    { "do": "teleport", "target": "Crate", "x": 0, "y": 8, "z": 0 },
    { "do": "wait", "seconds": 3 },
    { "do": "assert", "that": "reached", "target": "Crate", "within": 0.5 }
]}
```

## Pitfalls

- **No interpolation.** The most common cause of "the game stutters". The physics is
  fine; you are seeing 50 Hz positions on a 144 Hz display.
- **`transform.position` on a dynamic rigidbody.** Teleports past the solver. Objects
  end up inside each other and pop out violently.
- **Reading input in `FixedUpdate`.** Drops presses, because `FixedUpdate` can run zero
  times in a frame. Latch in `Update`.
- **`Time.deltaTime` inside `FixedUpdate`.** It returns `fixedDeltaTime` there, so it
  happens to work - until someone moves the code, at which point it silently does not.
  Write `Time.fixedDeltaTime` and mean it.
- **Discrete collision on fast objects.** A projectile at 40 m/s moves 0.8 m per step
  and passes clean through a 0.2 m wall. Continuous, or raycast the movement yourself.
- **Tiny colliders.** Anything under about 0.05 m is unreliable regardless of settings.
  Scale the whole game up rather than fighting it.
- **Non-uniform or negative scale on colliders.** Mesh colliders in particular
  misbehave, and a negative scale inverts normals. Bake the scale into the mesh.
- **Non-convex mesh colliders on moving bodies.** Not supported for non-kinematic
  rigidbodies. Use primitives or a convex hull; a compound of boxes and capsules is
  faster and more stable than any mesh collider.
- **Everything colliding with everything.** The layer collision matrix is free
  performance and free bug prevention. Set it before there are a hundred prefabs.
- **Mass used to make something feel heavy.** Mass affects how things respond to forces
  and to each other, not how fast they fall. "Heavy" is drag, animation and sound.
- **Extreme mass ratios.** A 0.1 kg object against a 1000 kg one is numerically nasty;
  the solver will jitter. Keep ratios inside about 1:100.
- **`Time.timeScale` at zero with physics-driven UI.** `FixedUpdate` stops entirely,
  and anything waiting on it hangs.
- **Assuming determinism across platforms.** Unity's physics is not guaranteed
  reproducible across machines or builds. Fixed timestep and a fixed seed get you
  same-machine reproducibility, which is what a regression test needs.

## Prove it with Proving Ground

- `pg_run_scenario` pins `seed` and `fixedDeltaTime` for the run, so a physics
  regression shows up as a diff rather than as an anecdote. Run one twice: if the
  reports differ, something in the game is unseeded.
- Feel metrics measure the arc rather than the fields: `jump.apexHeight`,
  `jump.timeToApex`, `jump.fallMultiplier`, `locomotion.accelTime`,
  `locomotion.stopTime`. A controller whose serialized jump height is 1.2 and whose
  measured apex is 1.6 is the bug this catches.
- `pg_check scene` finds floor holes and spawns inside geometry, which is the class of
  physics bug that presents as "the player falls forever".
- `pg_run_probe` walks into everything, which is how tunnelling and stuck geometry get
  found without a human doing it.
- `pg_console` after a run: physics errors, non-convex collider warnings and NaN
  transform complaints all land there and nowhere else.

## References

- `references/timestep-and-determinism.md` - choosing the timestep against the frame
  rate, the catch-up death spiral, solver iterations and contact offsets, stable stacks
  and joints, manual simulation, and what reproducibility actually guarantees.

## Related skills

- `input-systems` - why input is read in `Update` and applied in `FixedUpdate`.
- `camera-systems` - interpolation, `LateUpdate`, and physics-driven follow targets.
- `game-feel` - knockback impulses and hitstop, which both interact with the timestep.
- `performance-optimization` - the cost of the simulation, and what to cut first.
- `unity-scripting` - execution order and where physics sits in the frame.
