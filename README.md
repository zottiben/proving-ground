# Proving Ground

A Unity plugin that lets AI agents actually build games - by giving them the
ability to **play** a game, **see** it as ground truth rather than pixels,
**verify** it against machine-readable design contracts, and follow real studio
production methodology from idea to ship.

Works on a game started from scratch, or on one that already exists - released
or not.

## Why

Every AI tool shipping for Unity today is an *authoring* layer: it creates and
modifies scenes, GameObjects, scripts and assets. None of them close the loop on
whether the result is any good. Authoring is commoditised. Perception,
actuation, verification and judgment are not.

The research backs the approach. *See, Symbolize, Act* (AAAI 2026 LMReasoning
workshop) found that VLM agents benefit from symbolic scene representations
**only when those symbols are accurate**, and that agents extracting symbols
themselves from raw frames degrade sharply with scene complexity - naming
perception quality as the central bottleneck. A game engine already holds the
ground truth. Proving Ground's job is to stop the AI guessing from screenshots
and feed it what the engine already knows.

## Layers

| Layer | Purpose |
|---|---|
| 0 · Contracts | Machine-readable design intent: feel spec, UI manifest, audio contract, content schemas, quality gates |
| 1 · Perception | Scene digest, runtime state query, annotated capture, event timeline |
| 2 · Actuation | Input injection, deterministic sessions, scenario scripts, probe bots, record/replay |
| 3 · Verification | Feel metrics, UI conformance, visual regression, reachability, performance budgets, project health |
| 4 · Judgment | Heuristic quality detectors, genre norm library, batched human review packets |
| 5 · Process | Evidence-gated milestones, living design docs, greenfield and brownfield entry paths |

## Status

Pre-implementation. Design and roadmap in progress.

## License

Not yet chosen.
