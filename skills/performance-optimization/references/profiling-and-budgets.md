# Profiling and budgets - depth for `performance-optimization`

## 1. The tools, and what each is for

| Tool | Answers |
|---|---|
| Profiler window | where the frame went, per frame, over time |
| Profiler timeline view | which thread is waiting on which - the CPU/GPU question |
| Profile Analyzer package | compares two profile captures; the only honest before/after |
| Frame Debugger | every draw call in order, and why the batch broke |
| Memory Profiler package | what is resident, and what changed between two snapshots |
| `ProfilerRecorder` | counters at runtime, in a build, for automated checks |

The two most underused are Profile Analyzer and the Memory Profiler's snapshot
comparison. "It feels faster" is not a measurement; a median frame time across a
thousand frames before and after is.

## 2. CPU bound or GPU bound

Open the timeline view and look at the main thread and render thread.

- **Main thread busy, render thread idle** - CPU bound on your code. Look at the
  hierarchy view sorted by self time.
- **Main thread waiting** (a long `Gfx.WaitForPresent` or similar sync marker) - GPU
  bound, or vsync. Reduce resolution by half and re-measure: if the frame time drops
  proportionally you are fill-rate bound; if it barely moves you are bound by geometry
  or draw call submission.
- **Both busy, neither dominant** - usually draw call submission cost, which is CPU work
  caused by GPU-facing decisions. Batching is the fix.
- **Spikes only** - not a throughput problem at all. Find the frame and look at what is
  new in it.

Turn vsync off while profiling. With it on, everything quantises to the refresh
interval and a 9 ms frame and a 15 ms frame look identical.

## 3. Hunting a hitch

A hitch is a single frame, so measure single frames. In the profiler, sort by frame
time, jump to the worst, and look at what appears there that is not in a normal frame.
The usual suspects, in order of frequency:

1. **Garbage collection.** Look for the GC spike marker and the GC Alloc column in the
   frames before it. The fix is upstream: stop allocating.
2. **Instantiate or Destroy** of something large, or many things at once. Pool, or
   spread the work over frames.
3. **Asset loading.** A synchronous `Resources.Load` or an Addressables load completing.
   Load asynchronously, and earlier.
4. **Shader compilation** on first use of a material. Warm up variants at load.
5. **Physics** re-baking - a navmesh carve, a mesh collider rebuilt at runtime.
6. **A canvas rebuild** after a large UI change.
7. **`SetActive` on a large hierarchy**, which is more expensive than it looks.

## 4. Allocation: what allocates and what does not

| Allocates | Does not |
|---|---|
| string concatenation, interpolation, `ToString()` | a cached string, `SetText` with a `StringBuilder` |
| LINQ (`Where`, `Select`, `OrderBy`) | an indexed `for` loop |
| `foreach` over an `IEnumerable<T>`-typed variable | `foreach` over a `List<T>` typed as `List<T>` |
| boxing a struct into `object` or a non-generic interface | generics with a constraint |
| a lambda capturing a local | a static lambda, or a cached delegate |
| `Physics.OverlapSphere`, `RaycastAll`, `GetComponents<T>()` | the `NonAlloc` variants, and a reused buffer |
| `new` anything, per frame | a pool |

The `foreach` row surprises people: `List<T>`'s enumerator is a struct, so iterating a
`List<T>` directly is allocation-free, but assigning it to an `IEnumerable<T>` boxes
that enumerator and it is not.

Target zero steady-state allocation in gameplay. Not "small" - zero. Once it is zero,
any allocation is a regression you can see, which is far easier than defending a
threshold.

## 5. Memory and leaks

Take two Memory Profiler snapshots, minutes apart, doing the same thing in each. Then
compare. What tends to grow:

- **Event subscriptions never removed.** Subscribe in `OnEnable`, unsubscribe in
  `OnDisable`. A static event holding a reference to a destroyed object is the most
  common managed leak in Unity, and it keeps the whole object graph alive.
- **Lists that only ever grow.** Every registry, cache and pool needs a removal path.
- **`DontDestroyOnLoad` objects duplicated on scene reload.** Check for an existing
  instance and destroy the newcomer.
- **Materials and textures instantiated at runtime.** `renderer.material` creates a copy
  each call, and copies are not collected until explicitly destroyed.
- **Async operations whose handles are never released.** Addressables in particular:
  every `LoadAssetAsync` needs a matching `Release`.

Unity's managed heap does not return to the OS readily, so watch the trend rather than
the absolute number, and compare like for like.

## 6. Budget starting points

Frame time is the budget that matters; everything else is a proxy. At 60 fps the whole
frame is 16.6 ms, and a reasonable split on a mid-range PC target:

| Slice | Budget |
|---|---|
| Gameplay scripts | 3-5 ms |
| Physics | 2-3 ms |
| Animation | 1-2 ms |
| Rendering, CPU side | 3-5 ms |
| UI | under 1 ms |
| Everything else, plus headroom | the remainder |

Draw calls and triangles vary enormously by platform, so derive them rather than
copying a number: profile a representative scene, see what the GPU actually costs, and
set the ceiling somewhat above it. What matters is that a ceiling exists and that
crossing it fails a gate, because the alternative is finding out in the last week.

Mobile changes the shape rather than the numbers: fill rate and bandwidth dominate,
overdraw is the first thing to fix, and thermal throttling means a sustained target
well below the peak the device can hit for thirty seconds.

## 7. Triage order

When everything is slow and you do not know where to start:

1. Build a development player and attach the profiler. Editor numbers first will send
   you after ghosts.
2. Establish CPU or GPU bound.
3. If GPU: halve the resolution to test fill rate, then check overdraw and transparency,
   then shadow settings, then shader complexity.
4. If CPU: hierarchy view by self time. Fix the top entry. Re-measure.
5. Check GC allocation per frame independently of the above - it is a spike problem, not
   a throughput problem, and it hides in the average.
6. Only then look at draw calls and batching, which is where people usually start and
   which is rarely the top cost in a project that has never been profiled.
7. Re-measure on the slowest supported device before believing any of it.
