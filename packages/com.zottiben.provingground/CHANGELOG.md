# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.4] - 2026-08-07

### Changed

- Setup's closing instructions now depend on what the project actually contains. A new
  and empty project is told there is nothing left to configure, because the agent
  creates and marks the player itself. An existing game gets the actual clicks for
  pointing Proving Ground at its player, and is told when it can skip them.

## [0.1.3] - 2026-08-07

### Added

- `proving-ground setup` offers to create the Unity project when there is not one yet,
  instead of stopping at "this is not a Unity project". A project it creates has the
  Input System installed and Active Input Handling set to Both, so agent-written
  controllers respond rather than compiling to nothing.
- Setup finds a project kept in a subdirectory. A repository with the game beside docs
  and design folders is an ordinary layout, and only looking upward turned it into a
  dead end.

## [0.1.2] - 2026-08-07

### Fixed

- `proving-ground update` reinstalls where the tool actually lives. It derived its
  target from XDG_DATA_HOME, which the launcher does not set, so an update from a
  non-default location installed elsewhere and left the command on the old version.
- The update-check cache is stored alongside the install rather than at a path derived
  from the environment, so a discovered update is remembered rather than re-fetched.

## [0.1.1] - 2026-08-07

### Added

- `proving-ground update` installs the latest release in place, so updating no longer
  means re-running the install script from memory.
- An update notice. The CLI prints one after a command when a newer release exists, and
  the Editor window shows one too, since plenty of use never touches a terminal. Both
  check at most once a day, never block, stay silent on failure, and can be switched
  off - with `PG_NO_UPDATE_CHECK=1` and the window's "Stop checking" button.

### Fixed

- The installer no longer refuses to run on a stock Mac. macOS ships Python 3.9 at
  /usr/bin/python3, so a usable interpreter that was not first on PATH went unnoticed.
- Release builds now fail when package.json and the release tag disagree, which would
  otherwise show every user a permanent phantom update.

## [0.1.0] - 2026-08-07

First release.

### Added

- **Authoring.** Declarative scene recipes applied idempotently, direct GameObject and
  component editing wrapped in Unity's Undo system, a property binder that accepts the
  documented API names rather than serialized names, script writing with real
  compilation awaiting across the domain reload, console capture, and batched operations.
- **Contracts.** Feel spec, UI manifest, audio contract, content rules and quality
  gates, all as plain JSON under `ProvingGround/Contracts` so an agent can edit them
  without touching the asset database.
- **Perception.** Symbolic scene digest, camera view digest with screen rects and
  occlusion, annotated screenshot capture with a legend, and a bounded frame-stamped
  event log.
- **Actuation.** Input injection through the Input System, deterministic sessions,
  live session recording into replayable scenarios,
  JSON-defined scenarios with an extensible step and assertion set, and a heuristic
  probe bot.
- **Verification.** Feel metrics derived from observed motion, UI conformance diffing,
  visual regression against stored baselines, scene truth analysis (spawns, floor
  holes, navmesh islands, objective reachability), audio event wiring checks, audio
  asset measurement, content and project audits, and headless balance and economy
  simulation.
- **Judgment.** WCAG contrast, hit target, font size, clipped text, overlap and safe
  area checks, plus a genre norm library of measured feel constants with provenance.
- **Process.** Evidence-gated milestones from concept through release candidate, and
  design document templates.
- **Brownfield support.** Project survey and baseline capture, which writes contracts
  describing how a game already behaves so an existing project becomes diffable.
- **Interfaces.** Editor window, menu items, `PgApi` static surface, `PgBatch` entry
  points for CI, and an opt-in loopback HTTP bridge for agent harnesses.

### Fixed during development

- `PgSession` pins the clock with `Time.captureDeltaTime`. Batch mode runs frames
  unthrottled, so `Time.deltaTime` collapsed to zero and delta-time controllers did not
  move, making every headless measurement meaningless.
- The scene builder no longer clears every component on rebuild, which destroyed the
  `MeshRenderer` and `Collider` that came with a primitive. Object counts stayed correct
  while levels silently lost their visuals and their collision.
- The feel probe no longer reports the settle from a spawn as a jump, which produced a
  confident apex measurement on games where jumping did not work at all.
- The bridge marshals every Unity API call to the main thread; `/health` previously read
  Unity APIs from the listener thread, threw, and left callers hanging.
- Replaced `??` and `?.` on `UnityEngine.Object` values, where the overloaded `==` makes
  a fake-null non-null and short-circuits past the fallback.

### Known limitations

- Audio level measurement is RMS dBFS rather than BS.1770 integrated loudness.
- Frame timings recorded under a captured clock or in batch mode describe the host
  machine and are excluded from the feel diff.
- Scenarios, probes, feel measurement and baseline capture require play mode.
