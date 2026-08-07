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
https://github.com/zottiben/proving-ground.git?path=/packages/com.zottiben.provingground
```

Or add it to `Packages/manifest.json` directly:

```json
"com.zottiben.provingground": "https://github.com/zottiben/proving-ground.git?path=/packages/com.zottiben.provingground"
```

`com.unity.nuget.newtonsoft-json` resolves automatically. The Input System, uGUI, UI
Toolkit and NavMesh integrations each compile only when that package is present, so
nothing breaks in a project that does not use them.

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

## Two ways in

### A new game

```
pg init fps            # contracts seeded from measured genre norms
                       # write Design/pillars.md and one-pager.md
pg norms fps           # reference values, with the reasoning attached
                       # build the smallest thing that exercises the loop
pg scenario smoke      # drive it
pg milestone prototype # judged on evidence, not on your say-so
```

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
| **1 Perception** | Scene digest, camera view, annotated capture, frame-stamped event timeline |
| **2 Actuation** | Input injection, deterministic sessions, scenarios, probe bots, record and replay |
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
