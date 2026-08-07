# Starter Contracts

Example contracts to copy into `ProvingGround/Contracts/` at your project root.

`Tools > Proving Ground > Initialise Project` writes a version of these for you. These
copies are fuller, and are here to read rather than to run.

| File | What it holds |
|---|---|
| `feel.json` | How the game should feel, as numbers a probe run is measured against |
| `ui.json` | The design system as tokens, and the elements held to it |
| `audio.json` | Named audio events, their rate ceilings and their asset requirements |
| `gates.json` | What "good enough" means: budgets, suppressions, required checks |

## Why these are JSON and not ScriptableObjects

An agent edits text reliably and edits serialized Unity assets badly. Keeping design
intent in plain JSON next to `Assets` rather than inside it means an agent can read a
contract, change one number and commit it, without touching the asset database or
churning a `.meta` file.

It also means the contracts diff properly in review, which matters more than it
sounds: the whole approach depends on a human being able to see, in a pull request,
that someone quietly widened a tolerance instead of fixing the game.

## The one rule

A contract describes what you intend, not what the game currently does. When you
capture a baseline from an existing game the tool writes measurements into these files
to get you started, and marks them `"Captured, not chosen."` Those notes are an
invitation: go through them and decide which numbers you actually meant.
