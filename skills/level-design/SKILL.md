---
name: level-design
description: Design and build playable levels in Unity - deriving metrics from the character's movement, blockout from a scene recipe, pacing and the critical path, gating, encounter design, readability and guidance. Use when laying out a level, greyboxing or blocking out a space, planning level pacing or encounters, deciding where the player goes and what stops them, or when a level plays badly and the layout is suspected.
---

# Level design

A level is a sequence of intentional experiences delivered through space. The good
part is a *process*, not a talent: derive the metrics movement is built on, block out
the geometry in primitives, play it, then dress it. Never the reverse.

In Unity with Proving Ground, the blockout is a document. `pg_scene_build` takes the
whole level as a recipe and applies it in one round trip, so the layout can be
iterated, diffed and rebuilt while it is still cheap to change - which is exactly the
window in which level design happens.

## When to use

- Use to plan a level's structure: critical path, pacing, gating, encounters, where
  the player learns and where they are tested.
- Use to run blockout, test, iterate, dress on a space that has to play well before
  any art exists.
- Use to derive level metrics from the character's movement so geometry is reachable
  and fair.

**When *not* to use:** to generate levels algorithmically, use `procedural-gen` -
authored and generated design are complementary, not alternatives. For enemy behaviour
in the space, `game-ai`. For how much detail the dressing pass needs, see
`game-production`'s `detail-density.md`. For the movement the metrics come from,
`input-systems` and the feel contract.

## Core workflow

1. **Derive the metrics first.** Measure the character: jump apex and distance, run
   speed, reach, step height, camera distance. Every gap, ledge and corridor is sized
   in those units. Lock them before any geometry, because changing them afterwards
   invalidates the whole layout.
2. **Blockout the whole space** from one recipe, in primitives, at correct scale, on a
   grid. Flat greys for structure, one saturated colour for anything gameplay-critical.
   No art.
3. **Define the critical path** from start to goal, and the golden path you expect
   most players to take. Layer optional and secret routes off it.
4. **Pace it.** Alternate tension and rest in a deliberate curve. Never
   combat-combat-combat. Give the player room to breathe and to anticipate.
5. **Teach, then test.** Introduce each mechanic somewhere safe, let the player
   practise, then test it under pressure. Difficulty rises in a sawtooth, not a line.
6. **Gate with intent.** Locks, abilities and one-way drops control order and pacing.
   Guide with light, lines and landmarks rather than with walls.
7. **Walk it, then let the probe walk it.** `pg_check scene` finds the structural
   faults; `pg_run_probe` finds what a script would not think to try.
8. **Only dress what plays.** A blockout that is not interesting does not get art. It
   gets a new blockout.

## Patterns

### 1. Metrics drive every dimension

```csharp
// Measure once, in one place, and size everything in these units. A ScriptableObject
// means the level tooling and the controller cannot disagree about the numbers.
[CreateAssetMenu(menuName = "Design/Player Metrics")]
public class PlayerMetrics : ScriptableObject {
    public float RunSpeed    = 6f;      // m/s
    public float JumpApex    = 1.2f;    // m
    public float TimeToApex  = 0.35f;   // s
    public float StepHeight  = 0.4f;    // m
    public float CapsuleRadius = 0.3f;  // m

    // A running jump, derived rather than eyeballed. Rise plus a slightly faster fall.
    public float JumpDistance => RunSpeed * (TimeToApex * 1.8f);
    public float SafeGap => JumpDistance * 0.70f;   // a gap the player is meant to clear
    public float HardGap => JumpDistance * 0.95f;   // a deliberate skill check, optional only
}
```

A platform at `JumpDistance + 0.1` is impossible and looks identical to one at
`SafeGap`. Reachability is arithmetic, never judgement.

### 2. The blockout as a recipe

```jsonc
{
  "name": "slice-01", "seed": 3,
  "objects": [
    { "id": "Ground", "primitive": "Cube", "scale": [60, 1, 40], "position": [0, -0.5, 0],
      "static": true, "material": "#6E7378" },

    // Structure: two greys, so walls read as walls at a glance.
    { "id": "Wall", "primitive": "Cube", "scale": [0.4, 4, 12], "material": "#8C9196",
      "static": true, "repeat": { "count": 5, "offset": [10, 0, 0] } },

    // Anything the player interacts with gets one saturated colour, the same one everywhere.
    { "id": "Cover", "primitive": "Cube", "scale": [2, 1.1, 0.8], "material": "#C8452B",
      "repeat": { "count": 8, "grid": [4, 6], "jitter": [0.5, 0, 0.5] } },
    { "id": "Objective", "primitive": "Cylinder", "position": [24, 1, 14], "material": "#E8C33A" },

    { "id": "Spawn", "position": [-24, 1.1, 0], "tag": "Player",
      "components": [{ "type": "CharacterController", "set": { "height": 1.8, "radius": 0.3 } }] }
  ]
}
```

Everything on the grid. If a piece does not fit the grid, the grid is wrong: fix the
grid, not the piece. Modular art later only works if the blockout it replaces was
built to snap.

### 3. Pacing as data, before it is geometry

```jsonc
// Author the beats before the rooms. The intensity column should rise overall and dip
// for rests - a sawtooth. A flat high line numbs the player as thoroughly as a flat low one.
[
  { "area": "entry",     "type": "teach",  "intensity": 0.1 },
  { "area": "corridor",  "type": "combat", "intensity": 0.5 },
  { "area": "overlook",  "type": "rest",   "intensity": 0.1 },
  { "area": "gauntlet",  "type": "combat", "intensity": 0.8 },
  { "area": "checkpoint","type": "rest",   "intensity": 0.2 },
  { "area": "arena",     "type": "climax", "intensity": 1.0 }
]
```

Keep it in `ProvingGround/Design/`. It drives spawn counts, music intensity and where
the checkpoints go, and it makes the pacing reviewable before anyone builds a room.

### 4. Gating, validated rather than assumed

```jsonc
// Model the level as areas plus gated connections, then prove the goal is reachable
// with the abilities the player can actually have obtained by that point.
{
  "entry":     { "exits": [{ "to": "hall" }] },
  "hall":      { "exits": [{ "to": "overlook", "needs": "double_jump" },
                           { "to": "side_room" }] },
  "side_room": { "exits": [{ "to": "hall" }], "grants": "double_jump" },
  "overlook":  { "exits": [{ "to": "arena", "needs": "red_key" }] }
}
```

The validation is a flood fill that only traverses an exit when its `needs` is already
satisfiable. It catches the soft-lock where a gate needs an ability obtainable only
past the gate, which is invisible to anyone who already knows the level.

## Pitfalls

- **Dressing before it plays.** The most expensive work lands on a layout you are
  about to change.
- **Geometry that ignores metrics** - gaps the jump cannot clear, ledges below reach,
  corridors too narrow for the camera. Size everything in player units.
- **Judging the layout from the editor camera.** From 40 m up every space reads fine.
  Walk it at player speed, at eye height, or you are reviewing a map, not a level.
- **Flat pacing.** Wall-to-wall combat numbs as fast as wall-to-wall calm. Place a
  breather and a save before the climax.
- **Testing a mechanic before teaching it.** The first meeting with a hazard should
  not be lethal.
- **Soft locks and dead ends.** Validate the ability and key order, not just
  connectivity.
- **No guidance.** Players get lost when nothing draws the eye. Light, leading lines,
  colour and landmarks point the way; walls only prevent.
- **Unsignposted one-way drops.** Telegraph anything irreversible.
- **Building one area to final quality first.** It hides every structural problem the
  rest of the level has, and it is the work you are most likely to throw away.
- **Colliders that do not match the visual.** A blockout is a physics test as much as a
  layout, so keep the collider and the box the same shape while it is still grey.

## Prove it with Proving Ground

```
pg_scene new                 an empty scene, so nothing clashes
pg_scene_build               the whole layout from the recipe
pg_check scene               spawns inside geometry, holes in the floor,
                             navmesh islands, objectives nothing can reach
pg_play  ->  pg_run_probe 90 walks it unsupervised: stuck points, falls, errors
pg_run_scenario traverse     the critical path, driven through the real input layer
```

`pg_digest` answers "what is where" exactly, which is the question a screenshot makes
you infer. `pg_view` answers "what can the player see from here", including what is
occluded and what a ray through the screen centre hits - which is how you check a
sightline or a landmark without guessing from an image.

When a probe finds a stuck point, keep it: turn the sequence into a scenario with an
`assert` step so the fix stays fixed.

## References

- `references/pacing-and-flow.md` - the tension curve in depth, the
  introduce-develop-twist-test teaching loop, readability and guidance techniques, 2D
  and 3D layout differences, and a blockout review checklist.

## Related skills

- `game-production` - where blockout sits in the pipeline, and what freezes when.
- `procedural-gen` - generated variety layered onto authored structure.
- `game-ai` - enemies that have to navigate the space you built.
- `camera-systems` - the camera constrains corridor widths and sightlines more than
  the character does.
- `unity-rendering` - lighting and materials for the dressing pass, once it plays.
