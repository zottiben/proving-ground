---
name: proving-ground
description: Build, verify and ship Unity games with the Proving Ground plugin. Use when working in a Unity project that has com.zottiben.provingground installed, or when asked to create a game or level from scratch, build scenes or GameObjects, write gameplay code, iterate on an existing game, check whether a game plays or looks right, measure game feel, verify UI against a design, validate audio wiring, find level defects, or judge production readiness.
---

# Proving Ground

You cannot see the game. That is the problem this plugin exists to solve, and the
whole skill follows from taking it seriously.

A screenshot forces you to work out both what is in the scene and how it looks, and
the first of those is where you go wrong. The engine already knows the answer exactly.
Ask it.

## The discipline

**Ask the engine before you look at pixels.** `pg_digest` for what exists and where,
`pg_view` for what the camera can see, `pg_events` for what happened. These are ground
truth. Use `pg_capture` when the question is genuinely aesthetic, and read the legend
it returns alongside the image so you know what you are looking at.

**Never claim something works because the code looks right.** Run it. `pg_run_scenario`
drives real input through the same device layer a player uses. If you did not run it,
say you did not run it.

**Put intent in contracts, not in your memory.** Anything that is a number belongs in
`ProvingGround/Contracts/`. A tolerance you remember is a tolerance you will
misremember three turns from now, and the diff is what turns "looks about right" into
a red test.

**Never widen a tolerance to make a check pass.** If a value is outside its contract,
either fix the game or say plainly that you think the contract is wrong and why.
Quietly editing the spec to match the bug is the single most damaging thing you can do
here, because it destroys the only signal anyone has.

## Building things

**Use a scene recipe, not two hundred calls.** `pg_scene_build` takes a JSON document
describing the whole level and applies it in one round trip. It is idempotent, so
re-running converges instead of duplicating; it is seeded, so procedural parts rebuild
identically; and the recipe is the artifact that gets committed, so a change to the
level shows up in review as a change to a file somebody can read.

Creating objects one at a time with `pg_create` produces a scene that exists only as
YAML: unreviewable, unrebuildable, and impossible to diff. Reach for `pg_create` and
`pg_modify` when you are nudging something that already exists, and fold what you keep
back into the recipe.

`repeat` covers most of what makes a level: `{"count": 8, "ring": 12}` for a circle of
pillars, `{"count": 9, "grid": [3, 2.2]}` for a stack of crates, `{"count": 4,
"offset": [3,0,0]}` for a row, plus `jitter` for seeded variation.

A rebuild only touches what the recipe owns. Hand-placed objects in the same scene are
left alone, and objects the recipe no longer declares are removed.

**After writing a script, wait properly.** `pg_script` already waits for compilation and
reports the errors. Do not add a sleep. The bridge drops for a few seconds during the
domain reload; that is expected and handled.

**Check the console when something did not work.** Unity explains most failures there
and nowhere else - a component that would not attach, a shader that did not compile, a
null reference in someone's `OnValidate`. `pg_console` is often the difference between
knowing why and guessing.

**Run `pg_check("project")` early on a new project.** It catches the settings that make
gameplay silently not work, most importantly an Input System package paired with the
old Input Manager - a combination where controllers compile perfectly and never
respond to anything.

## Starting a new game

1. `pg_survey` to see what is there, then `pg_init <genre>` to write the layout.
2. Write `Design/pillars.md` and `Design/one-pager.md` before any code. A pillar that
   rules nothing out is not a pillar.
3. Tune `Contracts/feel.json`. `pg_norms <genre>` gives measured constants from
   well-regarded games with the reasoning attached, so targets are chosen rather than
   invented.
4. Build the smallest thing that exercises the core loop.
5. Write a scenario in `ProvingGround/Scenarios/`, run it, and diff.
6. `pg_milestone prototype` when you think you are there. It will tell you what is
   missing, and it judges on evidence, so it cannot be talked into passing.

## Working on a game that already exists

This is the harder case and the order matters.

1. `pg_survey`. Read it before touching anything.
2. `pg_init` (it never overwrites) then `pg_play`, `pg_watch_audio`, and run a probe or
   scenario that exercises the systems you care about.
3. `pg_capture_baseline`. This writes contracts describing **how the game behaves
   today**, marked `"Captured, not chosen."`
4. Read the captured contracts and turn the numbers into intentions. This is a
   conversation with the user, not a decision for you: they know which values are
   deliberate and which are accidents.
5. Only now start changing things. Every change is a diff against the baseline.

You cannot diff against a spec that was never written. Capturing first is what makes an
existing game safe to iterate on.

## The loop

```
contract  ->  build or change the game  ->  run  ->  diff  ->  repeat
```

Building a playable slice from nothing looks like this:

```
pg_check project        catch settings that stop gameplay working
pg_scene new            an empty scene, so nothing clashes
pg_script write         the controller; it waits for the compile
pg_scene_build          the level, from one recipe
pg_scene save + add_to_build
pg_check scene          spawns, floor holes, reachability
pg_play  ->  pg_run_probe    does it actually play
```

For feel: edit the controller, `pg_run_scenario jump-arc`, read the diff against
`feel.json`. The report tells you the apex is 0.4m too high. Change the number, run
again.

For UI: `pg_check ui`. It reports every disagreement with the manifest in one pass,
plus contrast, hit target size, clipped text and safe area.

For levels: `pg_check scene` finds spawns inside geometry, holes in the floor,
disconnected navmesh and objectives nothing can walk to. `pg_run_probe` walks around
unsupervised and finds the rest.

For audio: `pg_check audio` after a run checks wiring, not taste. Required events that
never fired, events firing sixty times a second, events nothing declared.
`pg_check audioassets` measures the files themselves.

## Reading a report

Everything returns the same shape. Severity is `Info`, `Warn`, `Fail` or `Blocker`;
`Fail` and above break a gate. Each finding carries what was expected, what was
actually measured, and usually what to do about it.

`data` holds the measurements. When you have changed something and want to know
whether it moved, the numbers are there.

## When the user describes a bug

Do not try to reconstruct it from the description. `pg_record` starts recording, they
play until it happens, and stopping writes a deterministic scenario that reproduces it.
Now you have something to iterate against, and something to keep afterwards: add an
`assert` step and the reproduction becomes a test that stays green.

## What this cannot tell you

It cannot tell you whether the game is fun, whether the art is good, or whether a
design decision was right. It tells you whether the game does what the contracts say.

When a judgment genuinely needs taste, gather everything into one batch and ask the
user once, rather than asking forty times whether each individual thing looks correct.
The accessibility checks are the exception worth trusting on their own: text below the
legibility floor and targets under 44px are wrong at any aesthetic.

## Performance numbers are conditional

Frame timings from a headless run describe the build machine, not the game. The report
says so when that is the case. Measure performance from the Editor or a player build,
and gate on the 95th percentile rather than the mean, because players feel spikes and
not averages.

## Tools

| Need | Tool |
|---|---|
| Build or rebuild a level | `pg_scene_build` |
| New / save / open a scene, add to build | `pg_scene` |
| Write gameplay code and know it compiled | `pg_script` |
| Nudge one object | `pg_create`, `pg_modify`, `pg_delete` |
| Add or configure a component | `pg_component` |
| Several small edits at once | `pg_batch` |
| What Unity said went wrong | `pg_console` |
| What is in the scene | `pg_digest` |
| What the camera sees | `pg_view` |
| An image, with a legend | `pg_capture` |
| What happened during a run | `pg_events` |
| Drive a scripted play session | `pg_run_scenario` |
| Find defects unsupervised | `pg_run_probe` |
| Feel, UI, audio, scene, content, project checks | `pg_check` |
| One verdict for CI | `pg_gate` |
| Production readiness | `pg_milestone` |
| Reference values for a genre | `pg_norms` |
| Describe an unfamiliar project | `pg_survey` |
| Turn an existing game into a spec | `pg_capture_baseline` |
| Capture a bug the user can reproduce | `pg_record` |

Without MCP, the same surface is available as `tools/pg` on the command line, as menu
items under **Tools > Proving Ground**, and as static methods on `PgApi` for any
editor bridge you already use.
