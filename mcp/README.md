# Proving Ground MCP server

Connects an agent harness to a live Unity Editor running the Proving Ground package.

## Install

```bash
cd mcp
uv sync          # or: pip install -e .
```

## Wire it up

In Unity: **Tools > Proving Ground > Agent Bridge > Enable**. The bridge binds to
`127.0.0.1:8787` and stays on across Editor restarts once enabled.

Then register the server with your harness. Claude Code:

```json
{
  "mcpServers": {
    "proving-ground": {
      "command": "uv",
      "args": ["run", "--directory", "/absolute/path/to/proving-ground/mcp", "proving-ground-mcp"]
    }
  }
}
```

The Editor records where it is listening, so the server finds it on whatever port it is
using, and picks the Editor holding this project when several are open. Set
`PROVING_GROUND_URL` to point somewhere else entirely.

## Why it talks to a running Editor

The obvious alternative is to launch `Unity -batchmode -executeMethod` per call. That
works, and Proving Ground supports it through `PgBatch` for CI, but it pays Unity's
start-up and asset-import cost on every request, which turns a thirty second
investigation into a twenty minute one.

More importantly, it cannot enter play mode and stay there. Scenarios, the probe bot
and baseline capture all drive a game that is actually running, across many frames.
Those are the tools worth having, and they need a live Editor.

## Tools

| Tool | What it does |
|---|---|
| `pg_health` | Is the Editor reachable, and what is it doing |
| `pg_survey` | Describe an unfamiliar project |
| `pg_init` | Create contracts and folder layout |
| `pg_check` | Run a verification check |
| `pg_gate` | One pass/fail verdict across every report |
| `pg_milestone` | Production readiness, judged on evidence |
| `pg_digest` | Symbolic scene snapshot |
| `pg_view` | What the camera can see, as symbols |
| `pg_capture` | Annotated screenshot plus legend |
| `pg_visual_check` | Compare against a stored baseline |
| `pg_events` | Frame-stamped event timeline |
| `pg_play` | Enter or leave play mode |
| `pg_run_scenario` | Drive a scripted play session |
| `pg_run_probe` | Turn the probe bot loose |
| `pg_run_status` | Poll a run in progress |
| `pg_watch_audio` | Infer audio events without instrumenting the game |
| `pg_capture_baseline` | Write contracts from how the game behaves today |
| `pg_norms` | Measured feel constants per genre |
| `pg_scenarios` | List defined scenarios |

## Security

The bridge listens on loopback only, is off until you turn it on, and executes named
methods on one class. There is no route that runs arbitrary code in your Editor. That
is a deliberate limit: a general code-execution endpoint would be more convenient and
considerably more dangerous, and this tool does not need one.
