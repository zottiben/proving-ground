# Detail and density - depth for `game-production`

The bar: **nothing in the player's view should look copy-pasted, and every surface
should reward a closer look.** That is what separates a world from a tiled demo.

The trap: trying to hand-author thousands of unique assets. Nobody does that, not even
the studios whose worlds feel infinitely varied. They build **kits, plus combinatorial
variation, plus a detail pass**, and the uniqueness is real *as experienced*, which is
the only kind that counts.

## The combinatorics - where "thousands" actually comes from

Do this arithmetic before you build. A handful of parts becomes a world:

- **Characters:** 8 body variants x 12 tops x 8 legs x 6 heads x 10 colour palettes is
  over 46,000 distinct people from about 44 authored pieces. Add three walk styles and
  two accessory slots and no two people in a frame ever match.
- **Buildings or structures:** 6 archetypes x 10 surface treatments x per-instance
  height x width x tint x 4 roof kits x 3 ground-floor treatments is tens of thousands
  of distinct silhouettes. The instance parameters do the work, not the mesh count.
- **Props:** one crate mesh at 3 scales x 6 tints x 4 rotations x 3 wear decals is 216
  visually distinct crates from one asset.
- **Text:** generate a few hundred names procedurally from word lists and formats, and
  every sign in the world is unique. This is the highest perceived-uniqueness-per-byte
  trick available, and it costs nothing.

Rule of thumb: **if a thing repeats, it must vary on at least three axes** - scale,
tint, rotation, wear, prop set, or signage. One axis of variation still reads as
clones.

## The recipe does this for you

`repeat` plus `jitter` is the combinatorial engine, and because the recipe is seeded,
the variation is stable across rebuilds. Same seed, same world.

```jsonc
{
  "name": "market-street", "seed": 41,
  "objects": [
    // One authored stall, forty distinct-looking ones. Jitter breaks the grid;
    // the seed means it breaks it the same way every rebuild.
    { "id": "Stall", "prefab": "Assets/Props/Stall.prefab",
      "repeat": { "count": 40, "grid": [8, 6], "jitter": [0.6, 0, 0.4], "rotate": [0, 90, 0] } },

    // Three tint bands over the same mesh: the cheapest third axis there is.
    { "id": "CrateA", "primitive": "Cube", "scale": [0.9, 0.9, 0.9], "material": "#6B5540",
      "repeat": { "count": 12, "grid": [4, 3], "jitter": [0.3, 0, 0.3] } },
    { "id": "CrateB", "primitive": "Cube", "scale": [1.1, 0.8, 1.0], "material": "#7A6047",
      "repeat": { "count": 9, "grid": [3, 4], "jitter": [0.3, 0, 0.3] } }
  ]
}
```

Two things this does not do, and you have to: vary **rotation** on anything that has a
front, and make sure the repeat's spacing is not so regular that the eye finds the
grid anyway. A perfectly even 8 m spacing reads as a spreadsheet no matter how much
you jitter the offsets.

## Density targets

Per playable area, at eye height:

- 8-15 distinct points of interest per area - a reason to look at a place
- 20-40 props per street-block-sized space: fixtures, containers, seating, plants,
  signage, cables, vents, pipes, meters
- 3-6 decal types per surface family: stains, cracks, patches, marks, posters, wear
- every doorway or shopfront gets a sign, a door treatment, a window treatment, and
  something outside it
- no two identical objects adjacent, and no identical large object visible twice in
  one frame

## The detail pass

Do this **after** layout and lighting are stable, and do it at eye height, walking.

- **Contact.** Everything must touch the ground believably: kerb transitions, dirt
  gathering at wall-floor joins, a shadow at every contact point, no floating props.
  Floating objects are the single most common tell in agent-built scenes, and
  `pg_check scene` will find some of them but not all.
- **Wear where wear happens.** Grime low on walls, rust at fixings, worn paint on
  handles and thresholds, marks at turns, staining below anything that drips.
- **Edges.** Kerbs, thresholds, window reveals, parapets. A flat 90-degree edge reads
  as fake faster than almost anything else.
- **Verticality.** Cables, pipes, fixtures, awnings, aerials. Empty vertical space is
  the loudest unfinished signal a scene can send.
- **Windows.** Never a flat black plane. Reflection, interior parallax, or lit
  geometry behind them. Vary which are lit; the pattern of lit windows is the
  personality of a night scene.
- **Ground.** Markings, patches, drains, puddles in low points, litter against edges.
  Players look down far more than level designers expect.

## Deeper than the surface

- **Interiors.** A handful of enterable interiors beats fifty fake ones, but every
  non-enterable window needs something behind it so it is not a void.
- **Names.** Name streets, areas, businesses, factions. A world where things have
  names reads deeper than one where they do not, and text is free.
- **Written world.** Signage, posters, notices, menus, graffiti. Hundreds of unique
  strings cost nothing and players read all of them.
- **Sound density.** Each area needs its own ambience bed plus point sources. Silence
  reads as empty even when the visuals are full, and it is the fastest thing to fix.

## Verify it

Capture at eye height in three different areas with `pg_capture`, and read the legend
alongside the image so you know what you are looking at. Then ask:

1. Can I see the same object twice in one frame? Vary instance parameters.
2. Is there a flat, undetailed surface larger than a few metres? Add decals, props, wear.
3. Is any vertical space empty?
4. Does every sign have unique text?
5. Is anything floating, or intersecting something it should rest on?
6. Would a stranger believe someone lives here?

`pg_view` answers the first and last of those more reliably than the image does: it
tells you exactly what is on screen, at what distance, and what is occluded, so
"three copies of the same prop in frame" is a fact you can read rather than a thing
you have to spot.
