# Subagents - depth for `game-production`

You can fan work out to subagents. A large game is more than one context can hold, and
a focused subagent with a clean window behaves like a specialist rather than a tired
generalist. But the evidence on multi-agent work is genuinely mixed, and one
constraint here is absolute, so the rules below are not bureaucracy.

## The constraint that comes first: one Editor

The Unity Editor is a single process holding a single project, and the agent bridge
serves one Editor. Which means:

- **The open scene is global state.** Two lanes calling `pg_scene_build` against the
  same Editor are overwriting each other's world, not working in parallel.
- **Play mode is global state.** One lane entering play mode ends whatever another
  lane was measuring. `pg_run_scenario` and `pg_run_probe` are exclusive: one at a time,
  full stop.
- **Compilation is global state.** Any script write triggers a domain reload that
  interrupts every other lane's connection.
- **Asset imports serialise.** Two lanes writing assets at once do not import twice as
  fast; they import in some order, with the second one waiting.

So the safe rule is: **one lane touches the Editor. Everything else works on files.**

Recipes, scenarios, contracts, design documents, C# source, and research all live on
disk and parallelise perfectly. Applying them to a live Editor does not. Fan out to
produce artifacts; integrate them yourself, one at a time, through the bridge.

## The contract you write before spawning anyone

Studios use the frozen greybox as the contract: once the layout is locked, encounter
scripting, audio, lighting and art can all proceed against it. Yours has three parts,
and it is written **before** any lane starts:

1. **The file map.** Who owns which files. No two lanes write the same file, ever.
2. **The interfaces.** What each module exposes and consumes: component names, event
   names, the shape of the data passed between systems, the contract files each lane
   reads.
3. **Per-lane acceptance criteria.** What "done" means, measurably, for that lane.

No contract means each lane resolves ambiguity differently, which produces locally
sensible and globally incompatible work that only fails at integration - the most
expensive place to find it.

## Five conditions. Fan out only if all five hold

1. Three or more genuinely independent pieces of work exist.
2. File sets are disjoint.
3. Each lane has a measurable acceptance criterion.
4. The work is big enough to justify the coordination overhead.
5. The lanes need different focus, not just different labels.

## Lanes that actually work in a Unity project

| Lane | Owns | Consumes | Touches the Editor |
|---|---|---|---|
| gameplay code | `Assets/Scripts/<system>/*` | contracts | no, until integration |
| level authoring | `ProvingGround/Scenes/*.json` | metrics, feel contract | no |
| scenarios and tests | `ProvingGround/Scenarios/*.json` | interfaces | no |
| UI | `Assets/UI/*`, `Contracts/ui.json` | design tokens | no |
| audio | `Assets/Audio/*`, `Contracts/audio.json` | event names | no |
| design and writing | `ProvingGround/Design/*` | pillars | no |
| research and sourcing | asset directories, notes | nothing | no |

Every one of those produces files. The orchestrator - you - applies them: builds the
scene, writes the scripts, runs the scenario, reads the report.

## Verification runs free, and this is the best-evidenced win

Read-only critics never conflict with builders, and generate-critique-revise is the
multi-agent pattern with the most support behind it - *provided the critic actually
executes something*. Useful critics here:

- one that runs `pg_capture` and looks at the images, reporting what a stranger would
  call wrong;
- one that runs `pg_check` across every kind and triages the findings by severity;
- one that runs a scenario and diffs the measured feel metrics against the contract;
- one that reads `pg_console` after a run and traces each error to its cause.

A critic that only reads code and opines is worthless. It has to run something, look
at the result, and measure.

Note the Editor constraint still applies: critics that run scenarios queue behind each
other, and behind you.

## Never parallelise

- **Architecture, naming, data model.** One voice, or you get two incompatible games.
- **A bug that spans coupled files.** One agent holding all three beats three agents
  holding one each.
- **Integration.** The wiring pass is single-threaded and has to see everything.
- **Aesthetic consistency.** Art direction, palette, tone, writing voice.
- **Anything that fits comfortably in one context.** Pure overhead.

## Honest limits

On sequential reasoning tasks at equal token budgets, multi-agent setups have measured
*worse* than a single agent. Much of the reported multi-agent advantage is simply more
tokens spent, and coordination costs several times the tokens of doing the work.
Documented failure modes: duplicated work, lanes quietly ignoring the spec, premature
declarations of done, and information the orchestrator never receives.

Practical guidance: four to six lanes maximum. Prefer one strong builder plus running
critics over six builders. Fan out for breadth - independent systems, research, asset
production. Stay single-threaded for depth - architecture, integration, and anything
to do with feel.

## Your loop as orchestrator

Write the contract. Fan out the independent lanes. Integrate their output yourself,
through the Editor, one at a time. Run the critics. Fix what they find. Repeat.

Never let a lane apply its own work to the live Editor, and never end a session with
the project not compiling.
