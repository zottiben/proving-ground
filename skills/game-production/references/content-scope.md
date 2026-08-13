# What a complete game contains - depth for `game-production`

Scope calibration, not a spec to copy. "A game" is not a level: it is a content set
with a beginning, a middle, an end and a shell around it. What is useful here is the
*shape* and the *order of magnitude*, so you neither ship one room nor plan forty
hours you cannot finish.

The story, the systems and the world are yours to invent. The arithmetic is not.

## The shell nobody remembers to build

A playable slice is not a game until it has the parts around the parts. These are
small, they are always underestimated, and their absence is the first thing a stranger
notices:

- a title screen that starts the game, and quits it
- a pause menu that resumes, restarts and quits
- an options screen with, at minimum, volume, sensitivity and one accessibility toggle
- a save that survives quitting, and a load that lands you somewhere sensible
- a win state and a lose state, both of which say what happened
- a way to retry after failure with no more than one input

Budget a full pass for this. Every one of them is checkable: `pg_check ui` against a
`ui.json` manifest catches the half of it that is layout and legibility.

## Orders of magnitude, from shipped games

A tightly-designed single-player campaign is smaller than people assume. Portal
shipped 19 test chambers. Half-Life 2 shipped 13 chapters. A roguelike like Hades
builds its length out of a handful of biomes plus run variety rather than out of
level count.

The pattern across all of them: **a small number of distinct spaces, each used
deliberately, beats a large number of similar ones.** Content volume is the cheapest
thing to cut and the least missed.

Working targets:

| Shape | Complete-feeling minimum | "A full game" |
|---|---|---|
| Level-based | 8-12 levels, 5-15 min each | 20-40 |
| Mission-based | 12-15 missions forming one arc | 40-60 |
| Open / systemic | 3 distinct areas plus a reason to move between them | 6-10 |
| Roguelike | 3-4 biomes, 15-25 min per run | plus meta-progression across runs |
| Puzzle | 20-30 puzzles across 4-5 mechanic families | 60-100 |

Anything over about 25 minutes without a checkpoint is a section players will not
retry. That is a hard ceiling, not a guideline.

## Anatomy of one unit of content

Whatever you call it - level, mission, run, chapter - it decomposes into blocks, and
the blocks should not repeat the same verb twice in a row. A twelve-minute mission
that works:

```
0-2    setup, low intensity, the player is told or shown what this is about
2-5    traversal, a valley, where dialogue or exposition is free
5-8    approach, rising, the first real resistance
8-11   the conflict, the peak
11-12  resolution and a hook into what is next
```

Read the intensity column top to bottom. It should rise overall but dip for rests: a
sawtooth, never a flatline, and never combat-combat-combat. Place a breather and a
save immediately before the climax, and never immediately before a cutscene.

Teach, then test. Introduce each mechanic somewhere safe, let the player practise it,
then test it under pressure. A hazard first met in a lethal spot teaches only that the
game is unfair.

## Cheapest content types first

Reuse before invention. In rough order of cost per minute of play:

**Cheap** (reuses systems you already built): chases, deliveries, defend-the-point,
races, ambushes, timed challenges, harder variants of an existing encounter.

**Medium** (needs new logic but no new systems): escorts, stealth sections,
investigations, boss variants of existing enemies.

**Expensive** (a system of its own): set pieces, vehicles nobody has driven yet,
multi-stage bosses, anything with a bespoke camera or bespoke UI.

Build the cheap ones first, and let them prove the systems the expensive ones will
lean on.

## Narrative, cheap to expensive

Radio, barks, environmental text, signage, item descriptions, loading screens, notes,
graffiti - all effectively free, and read by players far more than anyone expects.
Then phone calls or messages delivered while the player is already moving, which puts
story into dead travel time. Then scripted in-world sequences. Reserve full cutscenes
for first meetings, betrayals, deaths and the finale.

A dozen named speaking characters is a complete cast for most games. Composition that
works: a protagonist, three or four people who want something from them, one
antagonist, one mentor who is compromised or lost, and one person the protagonist
cares about who is at risk. That last one is what makes the third act personal rather
than transactional.

For unnamed NPCs, six to twelve archetype buckets, individualised by silhouette, two
or three verbal tics and one dominant trait, reads as a populated world.

## Minimum viable content set, in priority order

1. One complete loop the player can repeat without being asked to
2. A beginning and an ending, however short
3. The shell above - menus, save, options, retry
4. 8-12 units of content covering the whole difficulty curve
5. One system that reacts to the player and changes the world's behaviour
6. Audio: one ambience bed per area, and a sound on every verb the player has
7. Three visually distinct areas
8. Ten to fifteen ambient events or encounters that are not on the critical path
9. One progression system
10. One thing that is clearly the best moment in the game

Anything after item ten is depth, and depth is what you cut first when the schedule
bites.

## What players notice, in order

Whether the core verb feels good. Whether the world reacts to them. Whether there is
something to do within sight. Whether they know where to go. Whether anything is
happening that they did not cause.

And what they complain about, in order: feature breadth without depth, empty space
between activities, repetition after the first few hours, a world that does not
acknowledge progress, and losing fifteen minutes to a missing checkpoint.

Nobody has ever complained that a good game was short.
