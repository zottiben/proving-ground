# Proving Ground

Gives AI agents the ability to play, see, verify and ship Unity games.

Full documentation: https://github.com/zottiben/proving-ground

## Quick start

1. **Tools > Proving Ground > Initialise Project** writes `ProvingGround/` next to
   `Assets`, with starter contracts and a smoke scenario.
2. **Tools > Proving Ground > Open Window** for everything else.
3. Tag your player `Player`, or set `PgLocate.PlayerOverride`, so the harness can find
   it.
4. Enter play mode and run a scenario.

## Connecting an agent

**Tools > Proving Ground > Agent Bridge > Enable** opens a loopback endpoint on port
8787 that invokes named `PgApi` methods. It is off by default, bound to `127.0.0.1`
only, and has no arbitrary code execution route.

## Where things live

| Path | Purpose | Commit it? |
|---|---|---|
| `ProvingGround/Contracts` | Design intent as JSON | Yes |
| `ProvingGround/Scenarios` | Reproducible play sessions | Yes |
| `ProvingGround/Baselines` | Reference images, captured baselines | Yes |
| `ProvingGround/Design` | Pillars, one-pager, GDD, milestones | Yes |
| `ProvingGround/Artifacts` | Run output | No |

## Optional integrations

Each compiles only when the package is present, so nothing breaks without them.

| Package | Enables |
|---|---|
| `com.unity.inputsystem` | Input injection, scenarios, probe bots |
| `com.unity.ugui` | uGUI conformance and accessibility checks |
| `com.unity.modules.uielements` | UI Toolkit conformance checks |
| `com.unity.modules.ai` | Navmesh reachability and island analysis |
| `com.unity.modules.imageconversion` | Screenshot capture and visual regression |
