---
name: procedural-gen
description: Generate content procedurally in Unity - seeded determinism, separate random streams, noise, dungeon and layout algorithms, placement and scattering, validating that generated output is actually playable, and combining generated variety with authored intent. Use when generating levels, dungeons, terrain, loot, layouts or variation, when generated output is unplayable or unreproducible, or when deciding what to generate and what to author.
---

# Procedural generation

Generation buys variety. It does not buy design, and the difference is where most
procedural projects fail: a generator produces a space that is *different* every time
without ever producing one that is *good*.

The two disciplines that make it work are unglamorous. **Seed everything**, so a run is
reproducible and a bug is findable. **Validate everything**, because a generator you do
not validate is a generator you do not have - plausible output describes impossible
places, and the failure is silent.

## When to use

- Use when generating levels, dungeons, terrain, layouts, loot or visual variation.
- Use when generated output is unplayable, unreachable, or different every time you run
  it with the same seed.
- Use to decide what to generate and what to author by hand.

**When *not* to use:** for authored pacing and intent, `level-design` - the two are
complementary, and the best results come from generating within authored constraints.
For the visual variation that makes a hand-built space not look copy-pasted, that is
the recipe's `repeat` and `jitter`, covered in `game-production`'s detail-density
reference.

## Generate or author

| Generate | Author |
|---|---|
| Variety within a known-good structure | The structure itself |
| Filler between authored beats | Every authored beat |
| Cosmetic variation - tints, rotations, wear | The critical path |
| Statistical content - loot tables, crowds | The tutorial, the climax, the ending |
| Replayability across many runs | The first hour, which everyone plays |

The reliable hybrid is **authored macro, generated micro**: hand-place the beats and the
connections, generate what fills them. A generator asked to produce pacing produces a
sequence of rooms; a generator asked to fill a room whose role is already decided
produces variety.

## Core workflow

1. **Seed it, from one place.** One seed for the run, derived sub-seeds per system. A
   generator you cannot reproduce is a generator you cannot debug.
2. **Use separate random streams per system.** Layout, decoration and loot each get
   their own. Sharing one stream means adding a decoration changes the dungeon.
3. **Generate structure before detail.** Rooms, then connections, then contents, then
   decoration. Each pass validated before the next.
4. **Validate hard, and reject.** Connectivity, reachability, minimum and maximum sizes,
   required elements present. A failed validation regenerates with the next sub-seed
   rather than shipping a broken level.
5. **Constrain rather than filter.** A generator that produces valid output by
   construction beats one that produces anything and throws most of it away.
6. **Bake navigation after generating.** Generated geometry has no navmesh until you
   build one at runtime.
7. **Keep the seed in the save**, and in every bug report. A reproducible bad level is a
   fixable bad level.

## Patterns

### 1. Seeding, done properly

```csharp
// UnityEngine.Random is global mutable state: anything else touching it changes your
// output. An owned System.Random per system is reproducible regardless of what else runs.
public sealed class Generator {
    readonly System.Random _layout, _decor, _loot;

    public Generator(int seed) {
        // Derived sub-seeds, so adding a system later does not shift the others.
        _layout = new System.Random(Hash(seed, "layout"));
        _decor  = new System.Random(Hash(seed, "decor"));
        _loot   = new System.Random(Hash(seed, "loot"));
    }

    static int Hash(int seed, string stream) {
        unchecked {
            int h = seed * 486187739;
            foreach (char c in stream) h = (h * 31) ^ c;
            return h;
        }
    }
}
```

The rule that keeps this working: **never consume randomness conditionally in a way
that differs between runs.** If a branch draws a number only sometimes, every draw after
it shifts, and the same seed produces a different world after an unrelated change.

### 2. Rooms and corridors, the workhorse

```csharp
// Place non-overlapping rooms, connect them with a spanning tree, add a few loops.
// Unglamorous, predictable, and it produces playable dungeons - which is the bar.
var rooms = new List<RectInt>();
for (int attempt = 0; attempt < _maxAttempts && rooms.Count < _targetRooms; attempt++) {
    var candidate = new RectInt(
        _layout.Next(1, _width  - _maxRoom), _layout.Next(1, _height - _maxRoom),
        _layout.Next(_minRoom, _maxRoom),    _layout.Next(_minRoom, _maxRoom));

    if (rooms.Any(r => Inflate(r, _padding).Overlaps(candidate))) continue;   // constrain, not filter
    rooms.Add(candidate);
}

// Connect via a minimum spanning tree so everything is reachable by construction,
// then re-add a small fraction of the discarded edges so it is not a pure tree -
// a tree means backtracking, and backtracking is what players call boring.
var edges = MinimumSpanningTree(rooms);
edges.AddRange(DiscardedEdges(rooms).OrderBy(_ => _layout.Next()).Take(rooms.Count / 6));
```

### 3. Noise, and the reproducibility trap

```csharp
// Mathf.PerlinNoise is convenient and is NOT guaranteed identical across platforms or
// engine versions. For anything that must reproduce - a shared seed, a saved world, a
// regression test - hash your own.
static float ValueNoise(int x, int y, int seed) {
    unchecked {
        int h = seed;
        h = (h ^ x) * 374761393;
        h = (h ^ y) * 668265263;
        h ^= h >> 13; h *= 1274126177; h ^= h >> 16;
        return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;         // 0..1, deterministic everywhere
    }
}

// Octaves: each one doubles the frequency and halves the amplitude. Three or four is
// almost always enough; more is cost without visible difference.
float Fractal(float x, float y, int seed, int octaves = 4) {
    float value = 0f, amplitude = 1f, frequency = 1f, total = 0f;
    for (int i = 0; i < octaves; i++) {
        value += SmoothNoise(x * frequency, y * frequency, seed + i) * amplitude;
        total += amplitude;
        amplitude *= 0.5f; frequency *= 2f;
    }
    return value / total;
}
```

### 4. Validation that actually rejects

```csharp
// Generate, validate, regenerate. The validation list is the design document: every
// rule here is something the level must be true of, stated as code.
public Level Generate(int seed) {
    for (int attempt = 0; attempt < 32; attempt++) {
        var level = Build(seed + attempt);
        if (Validate(level, out var reason)) return level;
        Debug.LogWarning($"Seed {seed + attempt} rejected: {reason}");
    }
    return Fallback();      // an authored level, so a bad seed is never a broken game
}

bool Validate(Level level, out string reason) {
    reason = null;
    if (!FloodFill(level.Start).Contains(level.Exit))     { reason = "exit unreachable"; return false; }
    if (level.Rooms.Count < _minRooms)                    { reason = "too few rooms"; return false; }
    if (level.DeadEnds > level.Rooms.Count / 3)           { reason = "too many dead ends"; return false; }
    if (!level.Rooms.Any(r => r.Role == RoomRole.Boss))   { reason = "no boss room"; return false; }
    if (PathLength(level.Start, level.Exit) < _minPath)   { reason = "critical path too short"; return false; }
    return true;
}
```

The fallback matters. A generator that occasionally cannot satisfy its constraints is
normal; a game that ships a broken level because of it is not.

### 5. Navigation on generated geometry

```csharp
// Generated geometry has no navmesh. Build one after generating, before spawning
// anything that walks - and give the surface a moment, because it is not instant.
using Unity.AI.Navigation;

_surface.BuildNavMesh();                       // synchronous; UpdateNavMesh for async
if (!NavMesh.SamplePosition(spawnPoint, out var hit, 2f, NavMesh.AllAreas))
    throw new InvalidOperationException("Spawn is not on the generated navmesh");
```

## Pitfalls

- **Using `UnityEngine.Random` for generation.** It is global state; anything else that
  touches it changes your world. Own a `System.Random` per stream.
- **One shared stream for everything.** Adding decoration changes the dungeon, which
  makes every seed in every bug report worthless.
- **Consuming randomness conditionally.** The same seed diverges after an unrelated
  change. Draw the same number of values on every path, or use a separate stream.
- **`Mathf.PerlinNoise` where reproducibility matters.** Not guaranteed identical across
  platforms or versions.
- **No validation.** The generator that produces an unreachable exit produces it
  silently, and the player finds it.
- **Validating by regenerating forever.** An unbounded retry loop hangs the game on a
  bad constraint set. Bound it and fall back.
- **Uniform distribution mistaken for good distribution.** Uniform random placement
  clumps. Use Poisson disc sampling, jittered grids or blue noise for anything that
  should look evenly scattered.
- **Generating pacing.** A generator produces a sequence, not a curve. Author the beats
  and generate inside them.
- **Forgetting the navmesh.** Enemies stand still in a perfectly good dungeon.
- **Not saving the seed.** A player reports a broken level and there is no way to see it.
- **Generating on the main thread at scale.** A three-second generation is a three-second
  freeze. Chunk it across frames, or move the pure computation off the main thread and
  keep only the Unity API calls on it.
- **Everything generated.** The first hour of the game is the one everybody plays, and
  it is the one that should be hand-made.

## Prove it with Proving Ground

Seeded generation and Proving Ground's seeded recipes and scenarios line up exactly.

- The recipe carries a `seed`, and `repeat` with `jitter` gives you seeded scatter with
  no code: same seed, same layout, every rebuild. For cosmetic variation over authored
  structure this is often the whole generator you need.
- `pg_scene_build` is idempotent, so a generator that emits a recipe can be re-run
  against the same scene and will converge rather than pile up.
- `pg_check scene` is external validation of what your validator claims: spawns inside
  geometry, holes in the floor, navmesh islands, objectives nothing can walk to. Run it
  on generated output, not just on hand-built levels.
- `pg_run_probe` on several seeds is the closest thing to a playtest a generator can
  get. Generate ten levels, walk each of them, and read the stuck points.
- Fix a seed in a scenario and the generated level becomes a regression test.

```
for each seed in 1..10:
    generate -> pg_scene_build -> pg_check scene -> pg_play -> pg_run_probe 60
```

Any seed that fails is now a reproducible bug rather than an anecdote about a dungeon
that no longer exists.

## References

- `references/noise-and-layouts.md` - noise types and what each is for, BSP, cellular
  automata, drunkard's walk, wave function collapse and when each is worth it, Poisson
  disc sampling, distribution shaping, and chunked generation across frames.

## Related skills

- `level-design` - the authored structure generation should serve, not replace.
- `game-ai` - navigation on generated geometry, and why it must be baked at runtime.
- `performance-optimization` - generation cost, chunking, and pooling generated objects.
- `save-systems` - saving a generated world as a seed plus the player's deltas.
