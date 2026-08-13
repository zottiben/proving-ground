---
name: game-production
description: How professional game production is sequenced - phases and their evidence gates, the blockout-to-polish pipeline, real-world metrics to lock before building, how much content a complete game contains, when to fan out to subagents, and how density is really achieved. Use when planning a game, deciding what to build next, judging whether something is ready to move on, scoping a project, or when a build has stalled and the order of work is the reason.
---

# Game production

Most agent-built games fail in the same way: art before layout, features before
pillars, a level that was dressed before anyone walked it. Not because the work was
bad, but because it happened in the wrong order, and wrong order is the one mistake
you cannot fix later without throwing the work away.

This is the order, the numbers, and the gates. Proving Ground exists to make the gates
real: `pg_milestone` judges on evidence rather than on your account of what you did.

## The order is not negotiable

```
pillars -> metrics -> blockout -> greybox playable -> dress -> light -> audio -> optimise -> polish
```

Each step is cheap to redo and expensive to skip. Deleting a grey box is free.
Deleting a dressed, lit street costs a session. Every rule below follows from that
asymmetry.

## When to use

- Use when starting a game, to decide what the first hour of work is.
- Use when you have something running and are deciding what to do next.
- Use when scoping: how many levels, how many missions, how big a cast.
- Use before `pg_milestone`, to know what the gate will ask for.
- Use when work has stalled, is sprawling, or is being redone repeatedly - that is
  almost always a sequencing fault, not an effort fault.

**When *not* to use:** for the craft inside a phase, load the discipline skill -
`level-design` for layout, `game-feel` for juice, `performance-optimization` for the
optimise pass. For the Proving Ground tool discipline itself, load `proving-ground`.

## Core workflow

1. **Write the pillars first.** Three statements every later decision is checked
   against, in `ProvingGround/Design/pillars.md`. A pillar that rules nothing out is
   not a pillar. Features before pillars guarantees scope creep: every feature is
   reasonable alone, and collectively they double the work.
2. **Lock the metrics before any geometry.** Player height, eye height, walk and run
   speed, jump height, capsule radius, camera FOV. Put them in
   `ProvingGround/Contracts/feel.json` via `pg_init <genre>`, seeded from `pg_norms`.
   Changing movement metrics after a layout exists invalidates every distance,
   sightline and cover position you built.
3. **Blockout the whole space, in primitives, from one recipe.** Not one perfect
   room. `pg_scene_build` with a flat grey material, distinct greys for ground and
   wall, one saturated colour for anything gameplay-critical.
4. **Prove the loop grey.** Placeholder logic on the blockout: spawns, triggers,
   objectives, enemies. `pg_run_scenario` end to end, then `pg_run_probe` for what a
   script would not think to try. It must be interesting as grey boxes.
5. **Only then dress it.** Materials, meshes, props, over a frozen layout. An art
   pass that breaks a sightline established in blockout gets reverted, not accepted.
6. **Light late, deliberately.** Lighting reacts to final geometry and final material
   values, so lighting a greybox is iteration you will throw away. Judge every
   material under both your day and night setups.
7. **Audio after the world is stable**, optimisation continuously from the dress pass
   onward, polish last and additive-free: nothing new, only better.
8. **Gate on evidence.** `pg_milestone <id>` at each transition. It reads the reports
   that exist, so a gate cannot be passed by asserting you did the work.

## The ladder Proving Ground judges

`pg_milestone` implements the standard studio ladder. Know what each gate wants
before you get there, because the artifacts are things you write, not things a tool
produces for you.

| Milestone | Phase | Wants |
|---|---|---|
| `concept` | Conception | `Design/pillars.md`, `Design/one-pager.md` |
| `prototype` | Pre-production | `Contracts/feel.json`, `Scenarios/smoke.json`, a scenario run |
| `first-playable` | Pre-production | plus `Contracts/gates.json`, scene check, probe run |
| `vertical-slice` | Pre-production | plus `Design/gdd.md`, ui and audio contracts, and every check passing |
| `alpha` | Production | every system exists; content may be missing, features may not |
| `beta` | Production | every asset in; from here you only fix, never add |
| `gold` | Polish | the full set plus a soak run |

Pre-production carries the strictest gates on purpose. It is where the pipeline is
won or lost, and it is the phase teams chronically underinvest in.

The vertical slice deserves care: it is a small section at *final* quality, built to
prove both the bar and the pipeline that reaches it. "Nearly final" is a failed slice,
because the thing being proved is that you can actually get there.

## Patterns

### 1. The metrics contract, written before geometry

```jsonc
// ProvingGround/Contracts/feel.json - seeded by pg_init, tuned by you.
// Every dimension in the level is derived from these. Never hardcode a distance twice.
{
  "genre": "fps",
  "metrics": {
    "locomotion.moveSpeed": { "min": 5.0, "max": 8.0, "unit": "m/s" },
    "jump.apexHeight":      { "min": 0.9, "max": 1.4, "unit": "m" },
    "jump.timeToApex":      { "min": 0.28, "max": 0.45, "unit": "s" },
    "input.moveLatency":    { "max": 3, "unit": "frames" }
  }
}
```

With `jump.apexHeight` at 1.2 m and `moveSpeed` at 6 m/s, a running jump clears
roughly 3 m. Every gap in the blockout is sized from that number, not from the eye.

### 2. Blockout as one recipe, not two hundred calls

```jsonc
// The whole space, greyed, in one round trip. Re-running converges, so this file
// stays the level's source of truth while the layout is still moving.
{
  "name": "slice-01", "seed": 7,
  "objects": [
    { "id": "Ground", "primitive": "Cube", "scale": [80, 1, 80],
      "position": [0, -0.5, 0], "static": true, "material": "#6E7378" },
    { "id": "Wall", "primitive": "Cube", "scale": [1, 4, 24], "material": "#8C9196",
      "repeat": { "count": 4, "offset": [20, 0, 0] }, "static": true },
    { "id": "Cover", "primitive": "Cube", "scale": [2, 1.1, 1], "material": "#C8452B",
      "repeat": { "count": 9, "grid": [3, 8], "jitter": [0.4, 0, 0.4] } }
  ]
}
```

Grey for structure, one saturated colour for anything the player interacts with. If
the layout only reads once it is textured, the layout does not read.

### 3. A gate you cannot talk your way past

```
pg_check scene          spawns, floor holes, navmesh islands, unreachable objectives
pg_run_scenario smoke   the loop, driven through the real input layer
pg_run_probe 90         what a script would not think to try
pg_milestone first-playable
```

`pg_milestone` fails on a check that was never run, not just on a check that failed.
That is deliberate: a gate passing on evidence nobody produced is worse than no gate.

## Pitfalls

- **Building one perfect area first.** The brief is almost never "one street at
  shipping quality". Blockout the whole space, then improve outward. A single
  polished room hides every structural problem the rest of the space has.
- **Dressing before playing.** The most expensive work lands on a layout you are
  about to change. Walk it grey, at player speed, before a single material.
- **Lighting a greybox.** Lighting responds to final geometry and albedo. Do it early
  and you tune it twice.
- **Changing movement metrics after layout.** Every gap, ledge, cover position and
  sightline was derived from them. Lock them at step 2 or accept rebuilding.
- **Optimising before geometry is stable.** Equally, discovering a budget problem
  after art is locked is worse. Profile from the dress pass onward, continuously.
- **Adding systems during polish.** Polish is a phase, not the tail of production.
  Nothing new goes in; things already in get better.
- **Treating the milestone as paperwork.** The artifacts are the thinking. A one-pager
  you cannot write is a game you cannot describe.
- **Cutting the wrong things under time pressure.** Cut content volume, extra systems,
  feature breadth. Never cut the metrics pass, one full blockout-playtest-iterate
  cycle, or verification on real output.

## Prove it with Proving Ground

| Question | Call |
|---|---|
| What is in this project already | `pg_survey` |
| What targets should I aim at | `pg_norms <genre>` |
| Does the layout hold up | `pg_check scene`, then `pg_run_probe` |
| Does the loop complete | `pg_run_scenario` |
| Am I ready for the next phase | `pg_milestone <id>` |
| One verdict for CI | `pg_gate` |

## References

- `references/phases.md` - each gate in depth: exit criteria, the evidence it wants,
  what a failed gate looks like, and what gets cut when time runs out.
- `references/level-pipeline.md` - the pass order from paper plan to polish, what
  freezes when, and the sequencing mistakes that wreck projects.
- `references/metrics.md` - real-world dimensions in Unity units: character,
  architecture, streets, cover, camera, engagement distances, vehicles.
- `references/content-scope.md` - how much content a complete game contains, mission
  anatomy, cast size, and the minimum viable content set in priority order.
- `references/parallel.md` - when to fan out to subagents, the contract you write
  first, and why the live Editor is the one resource that cannot be shared.
- `references/detail-density.md` - how "thousands of unique things" is actually done:
  combinatorial variation, per-area density targets, and the eye-height detail pass.

## Related skills

- `proving-ground` - the tool discipline every phase here is verified with.
- `level-design` - the craft inside the blockout and greybox phases.
- `game-feel` - the metrics pass and everything that makes step 4 worth repeating.
- `performance-optimization` - the optimise pass, and the budgets in `gates.json`.
- `unity-build` - what shipping actually requires at the far end of the ladder.
