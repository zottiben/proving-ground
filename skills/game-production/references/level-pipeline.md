# The level pipeline - depth for `game-production`

One rule above all others: **gameplay before art. The blockout has to be good before
a single final asset is placed.** Every postmortem in the industry says the same
thing, and every project that ignores it produces either a pretty demo that is not a
game, or a dressed area nobody can afford to change.

## The passes, in order

1. **Paper plan.** A written layout before any geometry: the shape of the space, the
   areas and what makes each distinguishable, the critical path, where the player
   starts, where the encounters happen, what the player can see from where. Keep it in
   `ProvingGround/Design/`. Planning only the first room is how you end up with only
   one room.

2. **Blockout.** Primitives on a grid, from one recipe, one flat grey material.
   Distinct greys for ground and wall; a saturated colour for anything gameplay
   critical - cover, objectives, blocking volumes, interactables. Walk it at player
   speed, never fly it.

3. **Greybox playable.** Placeholder logic over the layout: spawns, triggers,
   objectives, enemy placement, fail states. Prove the loop before it is pretty.

4. **Mesh pass.** Real geometry and materials over the frozen layout. The budget split
   professionals use: roughly 60-70% modular repeated pieces, 20-25% hero assets at
   points of interest, 10-15% tiling surfaces. Modular kits are what make a space
   affordable; hero assets are what stop it reading as a kit.

5. **Set dressing.** Clutter, signage, props, the second-order detail. This is the
   pass that defeats "empty tech demo", and ground-level detail buys more than another
   large object. See `detail-density.md`.

6. **Lighting.** Sun angle and colour, sky, fog, exposure, post. Evaluate every
   material under both day and night; a material that only works in one is not done.

7. **VFX and audio.** Particles, weather, ambience, one-shots, music.

8. **Optimisation.** Profile, then fix what the profile says.

9. **Polish.** Framing, feedback, wording, the last close-ups.

## What freezes, and when

Freeze **macro** structure at the end of the greybox: area sizes, the connection
graph, landmark positions, the critical path. Leave **micro** mutable: props, detail,
decoration, exact prop placement.

That freeze is the contract everything else runs against. Audio, encounter scripting,
lighting and art can all proceed in parallel against a frozen macro layout, and none
of them can proceed against one that is still moving. It is the same reason studios
treat the greybox as a milestone rather than a stage.

## The recipe is the level

While the layout is still moving, keep the level in
`ProvingGround/Scenes/<name>.json` and rebuild with `pg_scene_build`. Three properties
matter:

- **Idempotent.** Re-running converges rather than duplicating. You can iterate on the
  document and rebuild as often as you like.
- **Diffable.** A change to the level shows up in review as a change to a file a human
  can read. A scene that exists only as serialized YAML cannot be reviewed and cannot
  be rebuilt.
- **Owned.** A rebuild only touches what the recipe declares. Hand-placed work in the
  same scene survives, and objects the recipe no longer declares are removed.

Once an area is dressed and frozen, direct editing (`pg_create`, `pg_modify`,
`pg_component`) for the last nudges is fine - but fold anything you keep back into the
recipe, or the document stops being the truth and starts being a fossil.

## Grid discipline

Snap everything from the first box. Pick a grid before the first object: 0.5 m for
detail, 1 m or 2 m for structure, 4 m for large-scale layout. If a piece does not fit
the grid, the grid is wrong - fix the grid, not the piece.

This is not tidiness. Modular kits only work if pieces meet exactly, and a blockout
built off-grid cannot be replaced by a kit later without rebuilding it.

## Rules professionals do not break

- Playtest in-game with real physics and real speed, from the first blockout onward.
- A blockout that is not interesting does not get art. It gets a new blockout.
- Sightlines and landmark visibility established in blockout are sacred.
- Every gap, ledge and cover height is derived from the character metrics, never eyeballed.
- Nobody dresses an area whose layout is still under discussion.

## Sequence mistakes that wreck projects

Art before layout. Features before pillars. Optimising before geometry is stable.
Lighting a greybox. Changing movement metrics after the layout exists. Adding systems
during polish. Building one perfect area instead of the whole space.

Each of these is individually reasonable-sounding, which is exactly why they need to
be written down as prohibitions.
