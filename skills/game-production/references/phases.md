# Phases and their gates - depth for `game-production`

Professional productions move through gates, not vibes. Each gate has an exit
criterion somebody outside the work could check. Compress the durations however your
schedule demands; the **order** is not compressible, and skipping ahead is the most
expensive mistake available in game development.

`pg_milestone <id>` is the machine half of each gate. It reads the artifacts and the
reports that exist and returns findings. The human half - the sign-offs - it records
rather than automates, because nothing can measure "the core loop is worth repeating".

## 0. Pillars

Three non-negotiable statements every later decision is checked against, in
`ProvingGround/Design/pillars.md`. Not genre descriptions: this game's.

Bad: "fun, fast-paced combat." True of a thousand games, rules nothing out.
Good: "you are always outnumbered, and the answer is never more health."

**Exit:** three pillars that survive scrutiny, and a one-pager that describes this
game to somebody who has not seen it. `pg_milestone concept`.

**Why first:** features before pillars guarantees scope creep. Each feature seems
reasonable in isolation; collectively they double the work and none of them is the
game.

## 1. Metrics

Establish and lock the numbers everything downstream is calibrated to: player height
and eye height, walk and run speed, jump height and time to apex, capsule radius,
camera FOV. `pg_init <genre>` seeds `Contracts/feel.json` from measured norms;
`pg_norms <genre>` shows the reasoning behind each figure so you choose rather than
accept.

Build a bare test box - floor, a wall, a step, a gap at the exact width your jump
should clear - and drive it. `pg_run_scenario` measures the arc that actually results
and diffs it against the spec. Reading the intended jump height out of a serialized
field proves the field; measuring the arc proves the game.

**Exit:** a character moves through the test box at correct scale, and the measured
feel metrics sit inside their contract.

**Why here:** changing movement metrics after a layout exists invalidates every
distance, sightline, gap and cover position you built.

## 2. Blockout

The whole playable space in primitives, from a scene recipe, on a grid. One flat grey
for structure, a second grey for ground, one saturated colour for anything the player
touches. No textures, no lighting work, no meshes.

Walk it at player speed. Never judge a blockout from the editor camera at 40 m up:
that view flatters every space and hides every scale error.

**Exit:** you can traverse the whole space and the layout reads without explanation.
`pg_check scene` finds no holes, no unreachable objectives, no spawn inside geometry.

**Why grey:** it must be interesting as grey boxes. If it needs texture to be
legible, it is not a layout problem you can dress your way out of.

## 3. Greybox playable

Placeholder logic on top of the layout: spawns, triggers, objectives, enemies, fail
and success states. Still no final art.

**Exit:** the core loop completes start to finish, driven by a scenario, and someone
who did not build it could navigate without instruction. `pg_run_probe` for 60-120
seconds finds no stuck points or falls out of the world.
`pg_milestone first-playable`.

**Why before art:** this is the last moment at which changing the layout is cheap,
and it is the moment you will most want to change it.

## 4. Mesh pass and set dressing

Real materials and meshes over a frozen layout. Replace primitives; do not move them.
Then the dressing pass - clutter, signage, props, the things that defeat "empty tech
demo". See `detail-density.md`, which is where this pass is actually specified.

**Exit:** no untextured placeholder surface remains in the playable area, and the
sightlines established in blockout still read.

**Rule:** an art pass that breaks a blockout sightline gets reverted, not accepted.

## 5. Lighting

Sun angle and colour, sky, fog, exposure, post-processing. Late, deliberately:
lighting responds to final geometry and final albedo values, so a greybox lighting
pass is iteration you will throw away.

**Exit:** every area is intentionally lit, and materials hold up under **both** your
day and night setups. A material that only works at noon is not finished.

## 6. Audio and effects

Ambience, one-shots, music, particles, weather. Keyed to a world that has stopped
moving.

**Exit:** `pg_check audio` after a real run reports no required event that never
fired, no event firing sixty times a second, no undeclared event. The world should
sound inhabited with the screen off.

## 7. Optimisation

Continuous from the mesh pass onward, not a chore at the end. Profile first, fix what
the profile says rather than what you fear.

**Exit:** inside the budgets in `Contracts/gates.json`, gated on the 95th percentile
frame time rather than the mean, because players feel spikes and not averages.

## 8. Polish

The last stretch: nothing new, only better. Feedback, framing, wording, the one
close-up that still looks wrong, the missing sound on the most-used verb.

**Exit:** a stranger's first thirty seconds contain nothing embarrassing.
`pg_milestone gold` for the full evidence set plus a soak run.

## What gets cut when time is short

Cut content volume, extra systems, extra areas, feature breadth. These are the things
whose absence makes a game smaller rather than broken.

**Never cut:** the metrics pass, one complete blockout-playtest-iterate cycle, a
locked art direction, and verification against real output. Those four are what
separate a slice from a mess, and every one of them is cheap.

## What a failed gate looks like

A gate that fails is doing its job; a gate that passes on nothing is the failure
mode. `pg_milestone` reports three distinct problems, and they want different fixes:

- **Missing artifact.** You have not written the document. Write it; do not delete
  the requirement.
- **Failing check.** The game does not do what the contract says. Fix the game, or
  argue explicitly that the contract is wrong and say why. Never widen a tolerance to
  turn a report green.
- **Check never run.** There is no evidence either way. This is the one people try to
  argue past, and it is the one that matters most.
