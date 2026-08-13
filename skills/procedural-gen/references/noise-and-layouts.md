# Noise and layout algorithms - depth for `procedural-gen`

## 1. Noise, and what each kind is for

| Noise | Character | Use for |
|---|---|---|
| Value | blocky, cheap, easy to hash | masks, variation, anything low-frequency |
| Perlin / simplex | smooth, gradient-based | terrain height, clouds, flow |
| Fractal (octaves of the above) | detail at several scales | realistic terrain, erosion masks |
| Ridged (`1 - abs(noise)`) | sharp ridges | mountain ranges, canyons, cracks |
| Worley / cellular | cells and edges | stone, scales, region partitioning, biomes |
| Blue noise / Poisson | evenly spaced, no clumps | scattering objects that should look natural |
| White noise | uncorrelated | never for placement; it clumps |

The single most common mistake is using white noise - plain uniform random - to scatter
objects and being surprised by the clumps. Uniform random *is* clumpy; evenness is a
property you have to construct.

Domain warping is worth knowing about: sample noise at coordinates that are themselves
offset by noise. Two lines, and terrain stops looking like it was made of octaves.

```csharp
float warpX = x + Fractal(x * 0.3f, y * 0.3f, seed + 101) * 8f;
float warpY = y + Fractal(x * 0.3f, y * 0.3f, seed + 202) * 8f;
float height = Fractal(warpX, warpY, seed);
```

## 2. Layout algorithms

**BSP (binary space partition).** Recursively split a rectangle, put a room in each
leaf, connect siblings. Produces rectilinear, architectural layouts with no overlaps by
construction. Best for buildings, bunkers and anything that should look built.

```csharp
void Split(RectInt area, int depth) {
    if (depth == 0 || area.width < _minSplit * 2 && area.height < _minSplit * 2) {
        _leaves.Add(area); return;
    }
    bool horizontal = area.width < area.height;                  // split the long axis
    int at = _rng.Next(_minSplit, (horizontal ? area.height : area.width) - _minSplit);
    // ... two children, recurse, then connect the two child rooms when unwinding
}
```

**Cellular automata.** Fill randomly, then repeatedly replace each cell by the majority
of its neighbours. Four or five iterations turn noise into organic caves. Cheap, and it
needs a connectivity pass afterwards because it happily produces isolated caverns.

**Drunkard's walk.** Carve from a start point, stepping randomly, biased toward
unexplored space. Guarantees connectivity by construction, which is its whole appeal.
Produces winding, cave-like layouts and needs a length limit or it fills everything.

**Wave function collapse.** Constraint propagation over a tileset with adjacency rules,
collapsing the lowest-entropy cell repeatedly. Produces output that looks authored,
because it is authored - the tileset and the rules carry the design. Expensive to tune,
can contradict itself and need a restart, and it is the wrong tool for a first
generator. Right when the aesthetic is the point and the tileset already exists.

**Graph-first.** Author the graph - rooms, connections, and what each room is *for* -
then lay it out geometrically. This is what most shipped roguelikes actually do, because
it lets a designer control pacing and structure while the geometry varies. If you only
implement one approach, implement this one.

## 3. Distribution and scattering

**Poisson disc sampling** produces points no closer than a minimum distance, which is
what "naturally scattered" means. Bridson's algorithm is straightforward: keep an active
list, try candidates in an annulus around a random active point, accept the first that
is far enough from everything.

Cheaper approximations that are usually good enough:

- **Jittered grid.** Divide into cells, place one point randomly within each. Evenness
  from the grid, irregularity from the jitter. This is exactly what a recipe's `repeat`
  with `grid` plus `jitter` produces.
- **Dart throwing with rejection.** Random candidates, reject any too close to an
  existing point, give up after N failures. Simple, and fine for a few hundred points.
- **Relaxation.** Random points, then a few iterations of moving each away from its
  neighbours. Converges toward even, and you can stop early.

Shape the distribution rather than accepting uniform: square the random value to bias
low, take the maximum of two draws to bias high, or sample a curve. An `AnimationCurve`
in the inspector is the most tunable way to author a distribution, because a designer
can see it.

## 4. Constraining rather than filtering

Generating anything and rejecting most of it is slow and, worse, hides the design
inside a rejection rule nobody reads. Prefer constructions that cannot produce invalid
output:

- Connect rooms with a **spanning tree** rather than randomly, and connectivity is
  guaranteed rather than tested.
- Place rooms by **partition** rather than by random placement, and overlaps become
  impossible rather than rejected.
- Carve with a **walk** rather than by sprinkling, and reachability is a property of the
  algorithm.
- Draw from a **weighted list without replacement** rather than re-rolling until you get
  something new.

Keep validation anyway. Construction guarantees the properties you thought of.

## 5. Chunked generation

A generator that takes three seconds freezes the game for three seconds. Two ways out,
and they combine:

```csharp
// Yield periodically so the frame can finish. Simple, keeps everything on the main
// thread, and is enough for most generators.
IEnumerator GenerateAsync() {
    var deadline = Time.realtimeSinceStartup + 0.004f;          // ~4ms of budget per frame
    foreach (var step in _steps) {
        step.Run();
        if (Time.realtimeSinceStartup > deadline) {
            yield return null;
            deadline = Time.realtimeSinceStartup + 0.004f;
        }
    }
}
```

The other half: separate **computation** from **instantiation**. Pure data - the grid,
the room list, the placements - can be computed on a background thread or with the Job
System, because it touches no Unity API. Only the instantiation has to be on the main
thread, and it is the part that most benefits from being spread across frames.

Nothing in the Unity API is thread-safe unless it is documented as such. `Mathf` and
plain maths are fine; anything touching a `GameObject`, `Transform`, `Object` or asset
is not.

## 6. Making generated content feel authored

Variety alone reads as noise. Four techniques that buy intent cheaply:

- **Roles.** Every room gets a purpose before it gets contents: entrance, combat,
  reward, puzzle, rest, boss. Fill according to the role. This single change is most of
  the distance between "random rooms" and "a level".
- **Set pieces.** Hand-authored chunks the generator places whole. Ten authored rooms in
  a generated layout give the whole run a designed feeling.
- **Guaranteed beats.** Force one rest room before the boss, one reward within the first
  three rooms, one teaching room for any new mechanic. Constraints, not chance.
- **A progression curve.** Difficulty, density and reward scale with distance along the
  critical path rather than being drawn per room.

## 7. Debugging a generator

- **Log the seed on every generation**, and put it in the save and in any bug report.
- **Render the abstract structure**, not the geometry. A graph or a grid drawn with
  `Debug.DrawLine` or gizmos shows you what the algorithm did; walking the level shows
  you what it produced.
- **Generate a hundred seeds headlessly** and collect statistics: room count, path
  length, dead ends, rejection rate. Outliers are where the bugs are, and the mean tells
  you whether the generator is doing what you designed.
- **Keep the rejects.** A rejection log with reasons tells you which constraint is doing
  the work, and it is usually one of them doing almost all of it.
