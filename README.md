# Proving Ground

A Unity plugin that lets AI agents actually build games. It gives them the ability to
**play** a game, **see** it as engine ground truth rather than pixels, **verify** it
against machine-readable design contracts, and follow real studio production
methodology from idea to ship.

Works on a game started from scratch, and on one that already exists, released or not.

---

## Why

Every AI tool shipping for Unity today is an *authoring* layer. They create and modify
scenes, GameObjects, scripts and assets. Not one of them closes the loop on whether the
result is any good. Authoring is commoditised. Perception, actuation, verification and
judgment are not.

The research is unusually direct about the cause. *See, Symbolize, Act* (AAAI 2026
LMReasoning workshop) tested vision-language agents across Atari, VizDoom and AI2-THOR
with four input pipelines, and found that agents benefit from symbolic scene
representations **only when those symbols are accurate** - while agents left to extract
symbols from raw frames themselves degrade sharply as scenes get complex. It names
perception quality as the central bottleneck.

A game engine already holds the ground truth. Proving Ground's job is to stop the agent
guessing from screenshots and hand it what the engine already knows.

---

## Install

Requires Unity 2022.3 or newer. Developed and tested against Unity 6000.3.

**Unity Package Manager > Add package from git URL:**

```
git@github.com:zottiben/proving-ground.git?path=/packages/com.zottiben.provingground
```

Or add it to `Packages/manifest.json` directly:

```json
"com.zottiben.provingground": "git@github.com:zottiben/proving-ground.git?path=/packages/com.zottiben.provingground"
```

The repository is currently private, so the SSH form above is the one that works, and
it needs an SSH key Unity's git can use. If the repository is made public, swap it for
`https://github.com/zottiben/proving-ground.git?path=/packages/com.zottiben.provingground`,
which needs no credentials.

That single line is enough. The package declares the built-in modules it needs
(physics, audio, UI, image conversion) and pulls in `com.unity.nuget.newtonsoft-json`,
so they resolve on their own. The Input System, uGUI, UI Toolkit, NavMesh and
Animation integrations each compile only when that package is present, so nothing
breaks in a project that does not use them - those assemblies are simply skipped.

Then in Unity: **Tools > Proving Ground > Initialise Project**.

That writes a `ProvingGround/` folder next to `Assets`, containing starter contracts, a
smoke scenario and design templates. Nothing is added to the asset database.

```
ProvingGround/
  Contracts/     feel.json, ui.json, audio.json, gates.json, content.json
  Scenarios/     reproducible play sessions
  Baselines/     reference images and captured baselines
  Design/        pillars, one-pager, GDD, milestones
  Artifacts/     run output; regenerated, not committed
```

### Connect an agent

**Tools > Proving Ground > Agent Bridge > Enable.** It binds to `127.0.0.1:8787`, is
off until you turn it on, and can only invoke named methods on one class. There is no
arbitrary code execution route.

Then either register the MCP server:

```json
{
  "mcpServers": {
    "proving-ground": {
      "command": "uv",
      "args": ["run", "--directory", "/path/to/proving-ground/mcp", "proving-ground-mcp"]
    }
  }
}
```

or use `tools/pg` from a shell, or call `PgApi` methods through an editor bridge you
already have. All three run the same code as the menu items, so a person and an agent
never get different answers.

Copy `skills/proving-ground/SKILL.md` into your harness so the agent knows the
discipline, not just the API.

---

## Authoring: scene recipes

Every other Unity agent bridge authors by imperative mutation - create an object, add a
component, set a property, several hundred times. That works, but the result exists only
as serialized YAML: it cannot be reviewed, cannot be rebuilt, and cannot be diffed when
someone changes it.

Proving Ground's primary authoring path is a **recipe**: the level as a document.

```json
{
  "name": "arena", "seed": 7,
  "objects": [
    { "id": "Floor", "primitive": "Cube", "scale": [60, 1, 60],
      "position": [0, -0.5, 0], "static": true, "material": "#6E7378" },

    { "id": "Pillar", "primitive": "Cylinder", "position": [0, 2, 0], "scale": [2, 2, 2],
      "material": "#8A6F4E", "repeat": { "count": 6, "ring": 12 } },

    { "id": "Player", "position": [0, 1.2, -20], "tag": "Player",
      "components": [
        { "type": "CharacterController", "set": { "height": 1.8, "radius": 0.35 } },
        { "type": "FpsController", "set": { "MoveSpeed": 6.0, "JumpHeight": 1.15 } }
      ],
      "children": [
        { "id": "Eye", "position": [0, 0.7, 0], "tag": "MainCamera",
          "components": [{ "type": "Camera", "set": { "fieldOfView": 75 } }] }
      ] }
  ]
}
```

One round trip instead of two hundred. **Idempotent** - re-running converges rather than
duplicating, applies changed values, and removes objects the recipe no longer declares.
**Seeded**, so `jitter` rebuilds identically. And it only touches what it owns, so
hand-placed work in the same scene survives.

Direct editing (`pg_create`, `pg_modify`, `pg_component`) is there for iterating on what
a recipe produced.

Two things this fixes that the alternatives are documented to get wrong:

- **Compilation is awaited, not slept through.** Editing a script reloads the app domain,
  which drops the agent's connection, and the usual workaround is a fixed sleep that is
  both too long and not long enough. `pg_script` tracks a generation counter across the
  reload, reconnects, and returns the actual compiler errors.
- **Property names are the documented ones.** `fieldOfView`, `isTrigger`, `mass` - not
  Unity's internal `m_FieldOfView`. Values convert to the field's real type, so colours
  take `#RRGGBB`, vectors take `[x, y, z]`, and enums take their name.

## Two ways in

### A new game

```
pg init fps            # contracts seeded from measured genre norms
                       # write Design/pillars.md and one-pager.md
pg norms fps           # reference values, with the reasoning attached
pg check project       # catch settings that stop gameplay working at all
                       # write the controller, build the level from a recipe
pg scenario smoke      # drive it
pg milestone prototype # judged on evidence, not on your say-so
```

`tools/demo_fps.py` and `tools/demo_play.py` run exactly this end to end against a live
Editor: empty scene, write a controller, await the compile, build a 23-object arena from
a recipe, rebuild to prove idempotency, save it, add it to the build settings, verify the
level, then enter play mode and turn the probe bot loose.

The last run reported `moveSpeed 6.0001` against a recipe asking for 6.0,
`timeToApex 0.335` against a controller configured for 0.35, `input.moveLatency` of one
frame, and a probe that passed clean.

### A game that already exists

This is the harder half, and the order matters.

```
pg survey              # what is actually here
pg init                # never overwrites anything
pg play                # enter play mode
pg probe 60            # exercise the game
pg call CaptureBaseline
```

`CaptureBaseline` writes contracts describing **how the game behaves today**, every
value marked `"Captured, not chosen."` You cannot diff against a spec that was never
written, so the way into a legacy project is to characterise what it currently does and
treat that as the thing to preserve. Then read the captured numbers and turn them into
intentions.

---

## The six layers

| Layer | What it does |
|---|---|
| **0 Contracts** | Design intent as JSON: feel spec, UI manifest, audio contract, content rules, quality gates |
| **A Authoring** | Declarative scene recipes, direct object and component editing, script writing with real compile awaiting, batching |
| **1 Perception** | Scene digest, camera view, annotated capture, frame-stamped event timeline |
| **2 Actuation** | Input injection, deterministic sessions, scenarios, probe bots, session recording and deterministic replay |
| **3 Verification** | Feel metrics, UI conformance, visual regression, scene truth, audio wiring, content and project audits, balance simulation |
| **4 Judgment** | Accessibility heuristics, genre norm library, batched review |
| **5 Process** | Evidence-gated milestones, living design docs, greenfield and brownfield paths |

A few of these deserve explanation.

**Feel is measured, not read.** The probe watches the player move and derives jump apex,
airtime, time to apex, acceleration, coyote time and input latency from observed motion.
Reading the intended jump height out of a serialized field proves the field; measuring
the arc proves the game.

**Genre norms give you a target instead of a vibe.** Asked to "make it snappier", an
agent has nothing to move toward. The shipped library carries measured constants per
genre with their provenance - Celeste's 5-frame coyote window, Halo Infinite's
0.6-1.1s rifle TTK, the 0.05-0.15s band that keeps input buffering invisible.

**Audio verification is about wiring, not taste.** Generation is the easy half. Whether
an event fires, whether anything is bound to it, and whether it fires four times a
second or sixty is fully checkable, and that is what goes wrong.

**Accessibility is the checkable part of "does it look good".** WCAG contrast, a 44px
hit target floor, a legibility floor, clipped text, zero-glyph labels, safe area. None
of it judges whether a design is attractive; all of it catches things that are wrong at
any aesthetic.

**Balance is simulated, not played.** Ten thousand fights run headless in less time than
one fight takes in the Editor, and the answer comes back as a distribution rather than
an anecdote.

---

## Verification

```bash
tools/test.sh all     # EditMode 33/33, PlayMode 9/9
tools/compile.sh      # compile only
```

The PlayMode suite is the one that matters. It builds an ordinary character controller
that knows nothing about Proving Ground, drives it through injected input, and asserts
that the feel probe measures the speed and jump arc it actually has. If that suite
passes, the central claim of this package holds.

### One finding worth repeating

Unity runs batch mode frames unthrottled, so the real interval between them rounds to
zero and `Time.deltaTime` comes back as `0`. Any controller that multiplies by delta
time then does not move, the probe measures a stationary player, and the run reports
nothing wrong.

`PgSession` pins the clock to the frame count with `Time.captureDeltaTime` to prevent
it, and there is a regression test guarding it. Frame timings are taken from a real
stopwatch instead, and are excluded from the feel diff when the clock was captured,
because they would otherwise report a flawless frame rate on a game that stutters.

Any headless verification tool without a controlled clock is measuring nothing. Worth
checking in whatever else you use.

---

## CI

```bash
Unity -batchmode -quit -projectPath . \
  -executeMethod ProvingGround.EditorTools.PgBatch.CheckAll

Unity -batchmode -quit -projectPath . \
  -executeMethod ProvingGround.EditorTools.PgBatch.Gate
```

Both exit non-zero when the check fails. `Gate` also fails when a required check has
never been run, so a gate cannot pass on evidence nobody produced.

For play-mode work in CI, `PgBatch.Serve` starts the bridge headless and keeps the
Editor alive, so scenarios and probes run on a machine with no display.

---

## Limitations

- **Play mode is required** for scenarios, probes, feel measurement and baseline
  capture. Those drive a game that is actually running.
- **Audio level checks are RMS dBFS, not LUFS.** Proper BS.1770 loudness needs
  K-weighting this package does not implement, and reporting an unweighted measurement
  under the LUFS name would be wrong in a way nobody would catch.
- **Frame timings from headless runs are not a frame rate.** See above.
- **Probe bots are heuristic, not learned.** They walk, turn and jump. Reinforcement-
  trained playtest agents are a research programme with a poor record of surviving
  contact with a schedule; a bot that walks into things finds most of the same bugs
  this week.
- **It cannot tell you whether the game is fun.** It tells you whether the game does
  what the contracts say it should.

## License

MIT. See [LICENSE](LICENSE).
