# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-08-07

First release.

### Added

- **Contracts.** Feel spec, UI manifest, audio contract, content rules and quality
  gates, all as plain JSON under `ProvingGround/Contracts` so an agent can edit them
  without touching the asset database.
- **Perception.** Symbolic scene digest, camera view digest with screen rects and
  occlusion, annotated screenshot capture with a legend, and a bounded frame-stamped
  event log.
- **Actuation.** Input injection through the Input System, deterministic sessions,
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

### Known limitations

- Audio level measurement is RMS dBFS rather than BS.1770 integrated loudness.
- Frame timings recorded under a captured clock or in batch mode describe the host
  machine and are excluded from the feel diff.
- Scenarios, probes, feel measurement and baseline capture require play mode.
