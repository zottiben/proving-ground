# Proving Ground

A Unity plugin that lets AI agents actually build games. It gives them the ability to
**play** a game, **see** it as engine ground truth rather than pixels, **verify** it
against machine-readable design contracts, and follow real studio production
methodology from idea to ship.

Works on a game started from scratch, and on one that already exists, released or not.

---

## Install

**macOS & Linux:**

```sh
curl -fsSL https://zottiben.github.io/proving-ground/install.sh | sh
```

That puts `proving-ground` (and the short alias `pg`) on your PATH. Needs Python 3.10+
and Unity 2022.3 or newer.

### Set up a game

Go to your game and run setup, from inside the project directory.

```sh
cd ~/path/to/your-game
proving-ground setup
```

**Starting a brand new game?** Make an empty directory and run setup there. When there
is no Unity project yet, it offers to create one:

```sh
mkdir ~/Games/my-game && cd ~/Games/my-game
git init
proving-ground setup
```

A project created this way comes with the Input System installed and Active Input
Handling set to Both. That combination matters more than it sounds: with the default
setting, input code compiles to nothing, so a controller an agent writes will build
without a single error and never respond to anything.

You can equally make the project in Unity Hub (**New project > 3D**) and run setup
inside it afterwards. Setup also finds a project kept in a subdirectory, which is a
common layout when a repository holds design docs alongside the game.

Setup adds the Unity package to the project, works out which agent harness you use,
registers the MCP server and installs the skill where that harness reads it. Re-running
is safe: entries are updated in place and nothing else in your config files is touched.

```
proving-ground setup --harness claude    skip detection (claude, codex, pi)
proving-ground setup --yes               accept defaults, never prompt
proving-ground doctor                    check the install, the project and the bridge
```

### Updating

```sh
proving-ground update           # install the latest release
proving-ground update --check   # only report whether a newer one exists
```

You do not need to remember the install command. Updating swings the `current` symlink,
so projects already set up keep working without being touched.

You will usually be told first: when a newer release exists, the CLI prints a notice
after a command and the Editor window shows one in its panel. Both check at most once a
day, never block, and stay quiet on failure. Turn the CLI notice off with
`PG_NO_UPDATE_CHECK=1`, and the Editor one with its **Stop checking** button.

### Turn on the bridge

Open the project in Unity, then **Tools > Proving Ground > Agent Bridge > Enable**.

It binds to `127.0.0.1:8787`, stays off until you enable it, and can only invoke named
methods on one class. There is no arbitrary code execution route.

The Editor records the address it is listening on, so the CLI and the MCP server find it
without being told, including on a non-default port and with several projects open at
once. Each agent talks to the Editor holding its own project.

### First prompt

Start your agent in the project directory and ask for something:

> Check the project settings, then build me a greybox first person shooter. Use a scene
> recipe for the level and verify with Proving Ground at every step.

If the project is new, there is nothing else to do: your agent creates the player and
marks it as it builds.

If you are pointing this at an existing game, tell Proving Ground which object is the
player so it can drive it and measure how it feels. Select the player in the Hierarchy
and set the **Tag** dropdown at the top of the Inspector to **Player** - it is one of
Unity's built-in tags, so it is just a dropdown choice. You can skip even that if the
player has a CharacterController or Rigidbody above your main camera, which is found
automatically.

---

## What setup writes

Nothing is hidden, and all of it is safe to edit by hand.

| Harness | MCP server | Skill |
|---|---|---|
| **Claude Code** | `.mcp.json` in the project | `.claude/skills/proving-ground/SKILL.md` |
| **Codex** | `.codex/config.toml` in the project | `~/.codex/skills/proving-ground/SKILL.md`, plus a pointer in `AGENTS.md` |
| **Pi** | `.pi/mcp.json` in the project | `.agents/skills/proving-ground/SKILL.md` |

Plus one line in the project's `Packages/manifest.json` pointing at the installed
package.

Codex only loads project config from trusted projects, so run `codex` in the project
once and trust it. Its skills live in the Codex home directory rather than the project,
which is why setup also adds a line to `AGENTS.md`.

The package reference points at `~/.local/share/proving-ground/current`, a symlink to
the installed version. Updating swings that symlink, so projects keep working without
being touched.

---

## Manual setup

If you would rather not run the installer.

Add the package in Unity with **Package Manager > + > Add package from git URL**:

```
https://github.com/zottiben/proving-ground.git?path=/packages/com.zottiben.provingground
```

One line is enough. The package declares the built-in modules it needs (physics, audio,
UI, image conversion) and pulls in `com.unity.nuget.newtonsoft-json`. The Input System,
uGUI, UI Toolkit, NavMesh and Animation integrations compile only when those packages
are present, so nothing breaks in a project without them.

For the agent bridge, build the server and register it by hand:

```sh
cd mcp && python3 -m venv .venv && .venv/bin/pip install .
```

**Claude Code** - `.mcp.json`:

```json
{
  "mcpServers": {
    "proving-ground": {
      "command": "/absolute/path/to/mcp/.venv/bin/proving-ground-mcp",
      "args": []
    }
  }
}
```

**Codex** - `.codex/config.toml`:

```toml
[mcp_servers.proving-ground]
command = "/absolute/path/to/mcp/.venv/bin/proving-ground-mcp"
args = []
startup_timeout_sec = 60
```

**Pi** - `.pi/mcp.json`:

```json
{
  "mcpServers": {
    "proving-ground": {
      "command": "/absolute/path/to/mcp/.venv/bin/proving-ground-mcp",
      "args": [],
      "transport": "stdio",
      "lifecycle": "lazy",
      "directTools": true
    }
  }
}
```

Then copy `skills/proving-ground/SKILL.md` to wherever your harness reads skills from.
Without it an agent has the API but not the discipline, and will reach for screenshots
anyway.

Set `PROVING_GROUND_URL` to override the address entirely. You do not need it just
because the bridge is on another port - that is discovered.

---

## Project layout

`proving-ground init` (or **Tools > Proving Ground > Initialise Project**, or just
asking your agent) writes a `ProvingGround/` folder next to `Assets`. Nothing is added
to the asset database.

```
ProvingGround/
  Contracts/     feel.json, ui.json, audio.json, gates.json, content.json
  Scenes/        scene recipes
  Scenarios/     reproducible play sessions
  Baselines/     reference images and captured baselines
  Design/        pillars, one-pager, GDD, milestones
  Artifacts/     run output; regenerated, not committed
```

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
