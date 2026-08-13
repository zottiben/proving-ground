# Timestep, stability and determinism - depth for `physics-tuning`

## 1. Choosing the timestep

`Time.fixedDeltaTime` is a trade between accuracy and cost, and both ends have real
failure modes.

| Value | Rate | Suits |
|---|---|---|
| 0.02 (default) | 50 Hz | most games; the default is a reasonable default |
| 0.0166667 | 60 Hz | action games, anything where a physics character is the player |
| 0.0111 | 90 Hz | VR, or fast projectiles you refuse to raycast |
| 0.008 or less | 125 Hz+ | rarely worth it; cost scales linearly, stability does not |

Two rules of thumb. Match the timestep to your target frame rate where you can, so
each rendered frame consumes roughly one physics step and interpolation has the least
work to do. And never set it below what the slowest target machine can sustain: a
timestep that machine cannot keep up with produces the spiral below.

## 2. The catch-up spiral

Unity runs `FixedUpdate` repeatedly until simulated time catches up with real time. If
a frame takes 100 ms at a 10 ms timestep, that is ten physics steps in one frame. If
ten steps take longer than 100 ms, the next frame is worse. The simulation falls
further behind on every frame and the game locks up.

`Time.maximumDeltaTime` is the cap that stops it: time in excess of that is simply
lost, the simulation runs slow for a moment, and the game recovers. The default of
0.333 s allows a very long burst. On anything with a heavy physics load, 0.1 is a
better number - it means at most six steps at 60 Hz, and a hitch stays a hitch instead
of becoming a hang.

Symptoms of the spiral: the game runs fine, something expensive happens once, and
performance never recovers until the scene reloads.

## 3. Solver settings

```csharp
Physics.defaultSolverIterations         = 6;   // position; the default
Physics.defaultSolverVelocityIterations = 1;   // velocity; the default
_body.solverIterations = 12;                   // per-body override for one difficult object
```

Raise iterations when constraints are being violated: a stack that sinks, a joint that
stretches, a wheel that sinks into the ground under load. Raise them **per body**
rather than globally - the cost is real, and the problem is almost always one object,
not the scene.

`Physics.defaultContactOffset` (default 0.01) is the distance at which contacts start
being generated. Raising it makes contacts more stable and objects appear to hover;
lowering it makes contacts crisper and jitterier. Change it only after interpolation
and iteration count have failed to fix the problem.

`Physics.bounceThreshold` (default 2) is the relative velocity below which collisions
do not bounce. Raise it if light objects vibrate on the floor forever.

## 4. Stable stacks, ragdolls and joints

Stacks and joints are where a solver's limits show up first.

- **Keep mass ratios sane.** Inside 1:100 between connected or stacked bodies. A ragdoll
  with a 30 kg torso and a 0.2 kg hand will shake the hand apart.
- **Sleep is your friend.** Bodies at rest sleep and stop costing anything. Anything
  that never sleeps - because something is nudging it every frame - is both a
  performance problem and a jitter problem. `Rigidbody.IsSleeping()` tells you.
- **Use fewer, larger colliders.** A compound of three boxes is more stable and much
  cheaper than a twelve-piece mesh approximation of the same shape.
- **Configure joint limits before joint forces.** An unlimited joint that is being held
  in place by a strong spring is always going to fight.
- **Ragdolls: disable the animator, do not fight it.** Both writing the same bones is
  the classic ragdoll explosion.

## 5. Manual simulation

Unity can step physics under your control, which is what deterministic replay and
rollback need:

```csharp
Physics.simulationMode = SimulationMode.Script;   // Unity stops stepping automatically
// ... then, when you decide:
Physics.Simulate(Time.fixedDeltaTime);
```

Worth knowing it exists; worth not reaching for it casually. Everything that assumes
`FixedUpdate` runs - other systems, third-party packages, the animator in physics mode -
now depends on you calling `Simulate` correctly, and forgetting a call looks exactly
like the game freezing.

## 6. What determinism actually guarantees

Unity's physics runs PhysX, and Unity does not guarantee identical results across
platforms, architectures or engine versions. Floating point differences in compilation
and instruction selection are enough to diverge two runs over a few seconds.

What you *can* rely on, and what a regression test needs:

- **Same machine, same build, same seed, same fixed timestep, same input sequence** is
  reproducible in practice for the length of a test.
- **Order matters.** If object creation order varies - because a `Dictionary` was
  iterated, or objects were found with a scene query whose order is not defined - the
  simulation diverges even on one machine.
- **Unseeded randomness breaks it.** `Random.Range` without a seeded state, `Time.time`
  used as a source of variation, or anything reading real wall-clock time.

That last one is the practical test. Run the same scenario twice with the same seed. If
the two reports differ, you have found unseeded state in the game, and that is worth
finding regardless of whether you cared about determinism - it is also why the bug is
irreproducible.

## 7. A checklist for each symptom

| Symptom | Look at, in this order |
|---|---|
| Stutter while moving | interpolation, then camera in `LateUpdate`, then vsync |
| Passes through walls | collision detection mode, then speed per step, then collider thickness |
| Sinks into the floor | solver iterations, then contact offset, then mass ratio |
| Vibrates at rest | bounce threshold, then sleep threshold, then something writing the transform |
| Explodes on contact | mass ratio, then overlapping colliders at spawn, then an impulse applied per step |
| Different at 30 and 144 fps | `deltaTime` used where `fixedDeltaTime` was meant, or forces applied in `Update` |
| Slows down and never recovers | the catch-up spiral: cap `maximumDeltaTime` |
| Character feels floaty | fall multiplier, then air control, then the arc itself in `feel.json` |

Work down the column rather than guessing. Every row's first entry is the cause more
often than the rest of the row combined.
