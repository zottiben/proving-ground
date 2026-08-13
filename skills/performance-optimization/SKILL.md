---
name: performance-optimization
description: Find and fix performance problems in Unity - profiling before guessing, CPU versus GPU bound diagnosis, draw calls and batching, garbage collection spikes, physics and animation costs, UI rebuilds, memory and load times, and setting frame budgets that gate on the tail rather than the mean. Use when the game stutters, drops frames, hitches, takes too long to load, uses too much memory, or when setting performance budgets.
---

# Performance optimization

The discipline is one rule with everything else downstream of it: **profile first, and
fix what the profile says rather than what you fear.** Programmer intuition about
performance is wrong often enough that acting on it is a coin flip, and optimising the
wrong thing costs the time you needed for the right thing.

The second rule is nearly as important: **gate on the 95th percentile, not the mean.**
Players do not feel averages. They feel the frame that took 40 ms, and a game averaging
a comfortable 9 ms with a spike every second reads as broken.

## When to use

- Use when the game stutters, hitches, drops frames or takes too long to load.
- Use when setting frame budgets, or writing `Contracts/gates.json`.
- Use when memory grows over a session, or a build will not fit its target.
- Use during the dress pass onward - continuously, not as a chore at the end.

**When *not* to use:** before the geometry is stable. Optimising a layout that is about
to change is wasted work, and discovering a budget problem after art is locked is
worse. The window is: from the mesh pass, continuously.

## Core workflow

1. **Reproduce it in a build, not the Editor.** Editor numbers include Editor
   overhead - the profiler, the scene view, domain reload state. Development Build with
   Autoconnect Profiler gives numbers that mean something.
2. **Decide CPU or GPU bound first.** Everything after this branches on it, and the
   answer is one look at the profiler timeline. If the main thread is waiting on the
   render thread, the CPU work is not your problem.
3. **Find the spike, not the average.** Sort by the worst frames. A hitch has a cause;
   a mean does not.
4. **Fix the top item, then re-measure.** Not the top five. Optimisation changes the
   shape of the profile, and the second item on the old list is frequently not on the
   new one.
5. **Write the budget down.** `Contracts/gates.json` holds frame time, allocation, draw
   call and memory ceilings, and `pg_gate` fails on them. A budget that lives in
   somebody's head is not a budget.
6. **Re-measure on the slowest target you support.** The machine you develop on is not
   the machine that has the problem.

## Diagnosis: where the time actually goes

| Symptom | Usual cause | First check |
|---|---|---|
| Steady low frame rate, GPU busy | overdraw, shader cost, resolution | Frame Debugger, transparency count |
| Steady low frame rate, CPU busy | script cost, physics, animation | Profiler hierarchy, sorted by self time |
| Regular hitch, ~1 s apart | garbage collection | GC Alloc column in the profiler |
| Hitch on an event | instantiation, asset load, shader compilation | the frame before the spike |
| Hitch when entering an area | streaming, navmesh, light probes | scene load timing |
| Gradual slowdown over a session | a leak: unreleased assets, growing lists, undestroyed objects | Memory Profiler, two snapshots |
| Fine in Editor, bad in build | shader variants, stripping, IL2CPP differences | build the development player and profile that |

## Patterns

### 1. Allocation-free hot paths

```csharp
// Every one of these allocates every frame, and a frame's worth of garbage per frame
// is a collection every second or so - which is the hitch people report as "stutter".
void Update() {
    _label.text = "Score: " + _score;                     // string concat: allocates
    foreach (var e in _enemies.Where(e => e.IsAlive))      // LINQ: allocates a closure and an iterator
        e.Tick();
    var hits = Physics.OverlapSphere(pos, radius);         // allocates an array per call
}

// The same work, without garbage.
readonly Collider[] _hits = new Collider[16];
static readonly StringBuilder _sb = new();

void Update() {
    if (_score != _shownScore) {                           // only touch the UI when it changed
        _sb.Clear(); _sb.Append("Score: ").Append(_score);
        _label.SetText(_sb);                               // TMP takes a StringBuilder
        _shownScore = _score;
    }
    for (int i = 0; i < _enemies.Count; i++)               // indexed loop over a List<T>: no allocation
        if (_enemies[i].IsAlive) _enemies[i].Tick();

    int count = Physics.OverlapSphereNonAlloc(pos, radius, _hits);   // fills a reused buffer
}
```

The non-alloc physics queries, a reused `StringBuilder`, and not touching UI text that
did not change cover the large majority of per-frame garbage in a typical project.

### 2. Pooling, with the built-in pool

```csharp
using UnityEngine.Pool;

// Instantiate and Destroy are both expensive, and Destroy also produces garbage.
// Anything spawned more than a few times a second gets pooled.
_pool = new ObjectPool<Projectile>(
    createFunc: () => Instantiate(_prefab),
    actionOnGet: p => p.gameObject.SetActive(true),
    actionOnRelease: p => p.gameObject.SetActive(false),
    actionOnDestroy: p => Destroy(p.gameObject),
    defaultCapacity: 32, maxSize: 256);

var shot = _pool.Get();
// ... later
_pool.Release(shot);          // reset its state on release, not on get, or a stale
                              // object is visible for the frame between the two
```

### 3. Batching, which is where draw calls go

Draw calls collapse when consecutive objects share material state. In URP, in order of
how much they buy:

- **SRP Batcher** - on by default; batches by *shader variant* rather than by material,
  so many materials on one shader are cheap. It requires the shader to declare its
  properties in a `UnityPerMaterial` constant buffer, which Shader Graph and the URP
  shaders do and a hand-written shader may not.
- **GPU instancing** - one draw for many copies of the same mesh and material. Enable it
  on the material. The thing that silently defeats it: writing to `renderer.material`,
  which instantiates a unique material per object. Use a `MaterialPropertyBlock` for
  per-instance colour instead.
- **Static batching** - mark non-moving objects Static. Costs memory (combined meshes)
  and buys draw calls.
- Anything transparent sorts back to front and batches far less readily. Transparency
  is expensive twice: fill rate and lost batching.

### 4. Reading the numbers at runtime

```csharp
using Unity.Profiling;

// ProfilerRecorder reads counters in a build, so a HUD or an automated check can watch
// them. Dispose them; they are unmanaged handles.
ProfilerRecorder _mainThread, _drawCalls, _gcAlloc;

void OnEnable() {
    _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
    _drawCalls  = ProfilerRecorder.StartNew(ProfilerCategory.Render,   "Draw Calls Count");
    _gcAlloc    = ProfilerRecorder.StartNew(ProfilerCategory.Memory,   "GC Allocated In Frame");
}
void OnDisable() { _mainThread.Dispose(); _drawCalls.Dispose(); _gcAlloc.Dispose(); }
```

`UnityEditor.UnityStats` gives similar numbers but is Editor-only, so anything built on
it cannot report from a player build.

## Pitfalls

- **Optimising without profiling.** The most expensive habit in the discipline.
- **Profiling in the Editor and believing it.** Editor overhead is large and unevenly
  distributed. Profile a development build.
- **Deep Profile numbers taken literally.** It instruments everything and its overhead
  dwarfs what it measures. Use it to find *where*, not *how much*.
- **Gating on the mean.** Hides exactly the frames players feel. Gate on p95, and look
  at max.
- **Fixing five things at once.** You learn nothing about which mattered, and one of
  them probably made it worse.
- **`renderer.material` instead of `sharedMaterial` or a property block.** Silently
  instantiates a material per object, defeats instancing, and leaks.
- **`Camera.main`, `GameObject.Find` or `FindObjectOfType` in `Update`.** All are
  lookups. Cache in `Awake`.
- **`Update` on hundreds of objects that mostly do nothing.** The per-call overhead is
  real. One manager ticking a list beats five hundred `Update` methods.
- **Animators on everything, always animating.** Set culling mode so off-screen
  animators stop, and disable animators on things that are not moving.
- **One canvas for the whole UI.** Any change rebuilds the whole thing. Split static
  from dynamic, and turn off Raycast Target on everything that is not interactive.
- **Uncompressed textures and no mipmaps.** Texture memory is usually the largest single
  line in a build, and missing mipmaps costs both memory bandwidth and image quality at
  distance.
- **Shader compilation hitching on first use.** Warm up variants at load, or prewarm
  with a shader variant collection.
- **Assuming the Editor's frame rate means anything at all.** It does not, and neither
  does a headless run's.

## Prove it with Proving Ground

Budgets are a contract, in `ProvingGround/Contracts/gates.json`:

```jsonc
{ "performance": {
    "frameTimeP95Ms":        16.6,     // gate on the tail
    "frameTimeMaxMs":        33.0,     // and cap the worst frame
    "gcAllocPerFrameBytes":  0,        // steady state should allocate nothing
    "maxDrawCalls":          800,
    "maxTriangles":          2000000,
    "maxTextureMemoryMb":    512,
    "maxSceneLoadSeconds":   5.0
}}
```

- `pg_gate` applies every gate to every report written so far and returns one verdict.
  It fails when a required check has never been run, so a green gate means evidence
  exists.
- `pg_check content` finds the asset-side causes: oversized textures, import settings
  that ignore your rules, duplicates.
- `pg_run_probe` for a soak - a long run is how leaks and gradual degradation surface.

One honest limitation, stated in the plugin and worth repeating: **frame timings from a
headless run describe the build machine, not the game.** The report says so when that
is the case. Measure performance from the Editor with a development build attached, or
from a player build on target hardware, and treat headless timings as a regression
signal only.

## References

- `references/profiling-and-budgets.md` - reading the profiler timeline, CPU versus GPU
  bound in detail, memory snapshots and leak hunting, per-platform budget starting
  points, and a triage order for each symptom.

## Related skills

- `unity-rendering` - shaders, lighting and the render pipeline settings behind GPU cost.
- `unity-scripting` - allocation, execution order, and the cost of `Update`.
- `physics-tuning` - the simulation's cost, and the catch-up spiral.
- `game-ui-ux` - canvas rebuilds, which are a top-five CPU cost in most projects.
- `game-production` - where the optimisation pass sits, and why it is continuous.
