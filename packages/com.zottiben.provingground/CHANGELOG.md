# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
