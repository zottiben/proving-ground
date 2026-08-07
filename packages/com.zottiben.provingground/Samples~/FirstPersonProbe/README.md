# First Person Probe

A working first-person controller and a test arena, wired to Proving Ground scenarios.
Use it to see the loop end to end before pointing the tool at your own game.

## Setup

1. Create an empty scene.
2. Add an empty GameObject and put `PgSampleArena` on it. The arena builds itself on
   Awake, including a player, so there is nothing else to place.
3. Make sure a `Player` tag exists in the project. Proving Ground finds the player by
   tag; without it you would set `PgLocate.PlayerOverride` instead.
4. Copy `Scenarios/*.json` into `ProvingGround/Scenarios/` at your project root.
5. Copy `feel.json` from the Starter Contracts sample into `ProvingGround/Contracts/`.

## Run it

Open **Tools > Proving Ground > Open Window**, enter play mode, pick a scenario and
press **Run scenario**.

`jump-arc` is the interesting one. It measures the jump, then diffs what it measured
against `feel.json`. Change `JumpHeight` on the controller from 1.15 to 2.0, run it
again, and the report tells you the apex is 0.85m too high rather than leaving you to
notice it looks a bit odd.

`reach-objective` shows the other half: drive the player somewhere and assert that it
arrived. That assertion is answered by the engine, not by looking at the screen.

## The gap is deliberate

The arena is missing a floor tile. Run **Run probe bot** for thirty seconds and it
will eventually walk into the hole and report falling out of the world, with the
coordinates. That is the class of defect a screenshot will never show you and a
player will find in the first minute.

Set `IncludeGap` to false and the probe comes back clean.

## What to take from it

The controller knows nothing about Proving Ground. There is no test hook in it, no
interface it implements, no harness-only code path. Input arrives through the same
device layer a real gamepad uses, so what the scenario proves is what a player would
experience. When you point this at your own controller, you do not need to change it
either.
