"""MCP server for Proving Ground.

Talks to the agent bridge running inside a live Unity Editor. The Editor holds the
project open, so calls return in milliseconds instead of paying Unity's start-up cost
on every request, and play-mode operations become possible at all.

Start the bridge from Tools > Proving Ground > Agent Bridge > Enable.
"""

from __future__ import annotations

import json
import os
import time
from typing import Any

import httpx

# The SDK renamed FastMCP to MCPServer in 2.0 while keeping the same decorator and run
# surface. Supporting both means this server works with whichever generation the user
# already has installed, rather than forcing an upgrade to use one tool.
try:
    from mcp.server.mcpserver import MCPServer as _Server
except ImportError:  # pragma: no cover - depends on the installed SDK
    from mcp.server.fastmcp import FastMCP as _Server

BRIDGE_URL = os.environ.get("PROVING_GROUND_URL", "http://127.0.0.1:8787")
TIMEOUT = float(os.environ.get("PROVING_GROUND_TIMEOUT", "600"))

mcp = _Server("proving-ground")


class BridgeError(RuntimeError):
    pass


def _call(method: str, **args: Any) -> str:
    """Invokes a PgApi method in the Editor and returns its raw response."""
    payload = {"method": method, "args": {k: v for k, v in args.items() if v is not None}}
    try:
        response = httpx.post(f"{BRIDGE_URL}/call", json=payload, timeout=TIMEOUT)
    except httpx.ConnectError as exc:
        raise BridgeError(
            f"Could not reach the Unity Editor at {BRIDGE_URL}. Open the project and "
            "enable Tools > Proving Ground > Agent Bridge > Enable."
        ) from exc

    if response.status_code >= 400:
        raise BridgeError(f"{method} failed: {response.text}")
    return response.text


def _await_compile(timeout_seconds: int = 300) -> str:
    """Blocks until Unity has finished compiling, and reports what broke.

    Compiling reloads the app domain, which tears down the bridge and drops this
    connection for a few seconds. A refused connection during that window means the
    Editor is busy, not that it is gone, so it is retried rather than raised. Every
    other Unity agent bridge papers over this with a fixed sleep, which is
    simultaneously too long and not long enough.
    """
    deadline = time.time() + timeout_seconds
    saw_disconnect = False

    while time.time() < deadline:
        try:
            status = json.loads(_call("CompileStatus"))
        except BridgeError:
            saw_disconnect = True
            time.sleep(1.0)
            continue

        if status.get("settled"):
            if status.get("hasErrors"):
                errors = "\n  ".join(status.get("errors", []))
                return f"Compilation FAILED with {status['errorCount']} error(s):\n  {errors}"
            note = " (reconnected after the domain reload)" if saw_disconnect else ""
            return f"Compiled cleanly{note}."

        time.sleep(0.5)

    return f"Unity was still compiling after {timeout_seconds}s. Poll pg_compile_status."


def _summarise(raw: str, max_findings: int = 40) -> str:
    """Renders a report as text.

    Reports are returned whole so nothing is hidden, but a run with three hundred
    findings would swamp the context for no benefit, so the tail is elided with a count
    rather than silently dropped.
    """
    try:
        report = json.loads(raw)
    except json.JSONDecodeError:
        return raw

    if not isinstance(report, dict) or "findings" not in report:
        return raw

    lines = [report.get("summary") or report.get("tool", "report")]

    if not report.get("ok", True):
        lines.append(f"could not run: {report.get('error')}")
        return "\n".join(lines)

    rank = {"Blocker": 3, "Fail": 2, "Warn": 1, "Info": 0}
    findings = sorted(
        report.get("findings", []),
        key=lambda f: rank.get(f.get("severity", "Info"), 0),
        reverse=True,
    )

    for finding in findings[:max_findings]:
        line = f"  [{finding.get('severity')}] {finding.get('id')}: {finding.get('message')}"
        if finding.get("subject"):
            line += f"\n      at {finding['subject']}"
        if finding.get("expected") or finding.get("actual"):
            line += f"\n      expected {finding.get('expected')}, got {finding.get('actual')}"
        if finding.get("remedy"):
            line += f"\n      {finding['remedy']}"
        lines.append(line)

    if len(findings) > max_findings:
        lines.append(f"  ... and {len(findings) - max_findings} more findings")

    data = report.get("data")
    if data:
        lines.append("  data: " + json.dumps(data)[:1500])

    return "\n".join(lines)


# --- authoring -------------------------------------------------------------------


@mcp.tool()
def pg_scene_build(recipe_json: str, build: bool = True) -> str:
    """Create or update a whole scene from a declarative recipe. This is the main way to build a level.

    Pass a JSON recipe: {"name": "arena", "seed": 1, "objects": [...]}. Each object takes
    id, primitive (Cube/Sphere/Capsule/Cylinder/Plane/Quad) or prefab, position, rotation,
    scale, parent, tag, layer, material (asset path or #RRGGBB), components
    ([{"type": "Rigidbody", "set": {"mass": 5}}]), and repeat
    ({"count": 8, "ring": 10} / {"count": 9, "grid": [3, 5]} / {"count": 4, "offset": [3,0,0]}).

    Prefer this over creating objects one at a time. It is a single round trip, it is
    idempotent so re-running converges instead of duplicating, the recipe is committed and
    diffable, and objects it no longer declares get removed. Hand-placed objects in the
    same scene are left alone.
    """
    return _summarise(_call("WriteAndBuildScene", recipeJson=recipe_json, build=build))


@mcp.tool()
def pg_scene(action: str, path: str = "", empty: bool = True) -> str:
    """Manage scenes. action: new, save, open, add_to_build, recipes.

    'new' starts an empty scene, which avoids clashing with the default camera and light.
    'save' needs a path like Assets/Scenes/Main.unity the first time.
    'add_to_build' is required before a build has anything to load.
    """
    action = action.strip().lower()
    if action == "new":
        return _summarise(_call("NewScene", empty=empty))
    if action == "save":
        return _summarise(_call("SaveScene", path=path or None))
    if action == "open":
        return _summarise(_call("OpenScene", path=path))
    if action == "add_to_build":
        return _summarise(_call("AddSceneToBuild", scenePath=path))
    if action == "recipes":
        return _call("SceneRecipes")
    return "Unknown action. Use new, save, open, add_to_build or recipes."


@mcp.tool()
def pg_create(
    name: str,
    primitive: str = "",
    parent: str = "",
    position: list[float] | None = None,
    rotation: list[float] | None = None,
    scale: list[float] | None = None,
    prefab: str = "",
    tag: str = "",
    layer: str = "",
) -> str:
    """Create a single GameObject. For more than a couple, use pg_scene_build instead."""
    return _summarise(_call("CreateObject", name=name, primitive=primitive or None,
                            parent=parent or None, position=position, rotation=rotation,
                            scale=scale, prefab=prefab or None, tag=tag or None,
                            layer=layer or None))


@mcp.tool()
def pg_modify(
    target: str,
    position: list[float] | None = None,
    rotation: list[float] | None = None,
    scale: list[float] | None = None,
    parent: str = "",
    name: str = "",
    active: bool | None = None,
    tag: str = "",
    layer: str = "",
    world_space: bool = False,
) -> str:
    """Move, rotate, scale, re-parent, rename or toggle an object. Undoable in the Editor."""
    return _summarise(_call("ModifyObject", target=target, position=position, rotation=rotation,
                            scale=scale, parent=parent or None, name=name or None,
                            active=active, tag=tag or None, layer=layer or None,
                            worldSpace=world_space))


@mcp.tool()
def pg_delete(target: str) -> str:
    """Delete an object from the scene."""
    return _summarise(_call("DeleteObject", target=target))


@mcp.tool()
def pg_component(action: str, target: str, component: str = "",
                 set: dict[str, Any] | None = None) -> str:
    """Add, remove or configure a component. action: add, remove, set.

    Property names are the documented API names - fieldOfView, isTrigger, mass - not
    Unity's internal serialized names. Values are converted to the field's real type, so
    colours accept '#RRGGBB', vectors accept [x, y, z], and enums accept their name.

    'set' with no component name sets properties on the GameObject itself.
    """
    action = action.strip().lower()
    if action == "add":
        return _summarise(_call("AddComponent", target=target, component=component, set=set))
    if action == "remove":
        return _summarise(_call("RemoveComponent", target=target, component=component))
    if action == "set":
        return _summarise(_call("SetProperties", target=target, component=component or None, set=set))
    return "Unknown action. Use add, remove or set."


@mcp.tool()
def pg_material(target: str, material: str) -> str:
    """Assign a material by asset path, or a colour like '#4488FF' to generate a shared one."""
    return _summarise(_call("SetMaterial", target=target, material=material))


@mcp.tool()
def pg_prefab(target: str, asset_path: str) -> str:
    """Save a scene object as a prefab and reconnect the instance to it."""
    return _summarise(_call("CreatePrefab", target=target, assetPath=asset_path))


@mcp.tool()
def pg_find(name: str = "", tag: str = "", component: str = "", max: int = 50) -> str:
    """Search the scene by name, tag or component type."""
    return _summarise(_call("Find", name=name or None, tag=tag or None,
                            component=component or None, max=max))


@mcp.tool()
def pg_inspect(target: str) -> str:
    """Everything on one object: transform, tag, layer, components, child count."""
    return _summarise(_call("Inspect", target=target))


@mcp.tool()
def pg_script(action: str, path: str = "", contents: str = "", folder: str = "Assets",
              wait: bool = True) -> str:
    """Write, read, list or delete C# scripts. action: write, read, list, delete.

    'write' saves the file, asks Unity to rebuild, and by default waits for compilation to
    finish and reports any errors. Do not sleep afterwards - the wait is real, and the
    reconnect through the domain reload is handled.
    """
    action = action.strip().lower()

    if action == "write":
        result = _summarise(_call("WriteScript", path=path, contents=contents))
        if not wait:
            return result
        return result + "\n\n" + _await_compile()

    if action == "read":
        return _summarise(_call("ReadScript", path=path))
    if action == "list":
        return _summarise(_call("ListScripts", folder=folder))
    if action == "delete":
        return _summarise(_call("DeleteScript", path=path)) + "\n\n" + _await_compile()
    return "Unknown action. Use write, read, list or delete."


@mcp.tool()
def pg_compile_status(wait: bool = True) -> str:
    """Whether Unity has finished compiling, and what failed if anything did."""
    if wait:
        return _await_compile()
    return _call("CompileStatus")


@mcp.tool()
def pg_console(min_severity: str = "", max: int = 60) -> str:
    """Read the Unity console. min_severity: warning or error.

    Unity reports most of its failures here and nowhere else - a component that would not
    attach, a shader that did not compile, a null reference in OnValidate. Check it when
    something did not work and the return value did not say why.
    """
    return _call("Console", minSeverity=min_severity or None, max=max)


@mcp.tool()
def pg_batch(operations: str, stop_on_error: bool = True) -> str:
    """Run several operations in one round trip.

    Pass a JSON array of {"method": "CreateObject", "args": {...}}. Method names are the
    PgApi names. Use this when you have a run of small edits; use pg_scene_build when you
    are creating a level.
    """
    return _summarise(_call("Batch", operations=operations, stopOnError=stop_on_error))


# --- perception and verification ---------------------------------------------------


@mcp.tool()
def pg_health() -> str:
    """Check whether the Unity Editor is reachable and what state it is in."""
    try:
        return httpx.get(f"{BRIDGE_URL}/health", timeout=15).text
    except httpx.ConnectError:
        return (
            f"No Editor at {BRIDGE_URL}. Open the Unity project and enable "
            "Tools > Proving Ground > Agent Bridge > Enable."
        )


@mcp.tool()
def pg_init(genre: str = "fps") -> str:
    """Create the Proving Ground folder layout and starter contracts in the project.

    Genres: fps, tps, platformer, actionrpg, topdown. The genre seeds the feel spec
    from measured norms for that kind of game.
    """
    return _summarise(_call("Init", genre=genre))


@mcp.tool()
def pg_survey() -> str:
    """Describe an existing project: scenes, asset counts, packages, whether it is set up.

    Run this first when meeting a codebase you have not seen before.
    """
    return _summarise(_call("Survey"))


@mcp.tool()
def pg_check(kind: str = "all") -> str:
    """Run a verification check.

    kind: project, content, audioassets, audio, scene, ui, or all.
      project      project settings health
      content      broken references, missing scripts, duplicates, import rules
      audioassets  clip level, peaks, silence, loop seams
      audio        audio event wiring, from the last play-mode run
      scene        spawns, floor holes, navmesh islands, objective reachability
      ui           UI conformance against the manifest, plus accessibility
      all          everything that does not need play mode
    """
    methods = {
        "project": "CheckProject",
        "content": "CheckContent",
        "audioassets": "CheckAudioAssets",
        "audio": "CheckAudio",
        "scene": "CheckScene",
        "ui": "CheckUi",
        "all": "CheckAll",
    }
    key = kind.strip().lower()
    if key not in methods:
        return f"Unknown check '{kind}'. Choose from: {', '.join(sorted(methods))}."
    return _summarise(_call(methods[key]))


@mcp.tool()
def pg_gate() -> str:
    """Apply the quality gates to every report written so far and return one verdict.

    This is what CI should call. It fails when a required check has not been run, so a
    gate cannot pass on evidence nobody produced.
    """
    return _summarise(_call("Gate"))


@mcp.tool()
def pg_milestone(milestone: str) -> str:
    """Judge readiness for a production milestone against evidence rather than assertion.

    Milestones: concept, prototype, first-playable, vertical-slice, alpha, beta, gold.
    """
    return _summarise(_call("Milestone", milestoneId=milestone))


@mcp.tool()
def pg_digest(max_nodes: int = 400, include_inactive: bool = False, name_filter: str = "") -> str:
    """Symbolic snapshot of the open scene: hierarchy, positions, components.

    Prefer this over a screenshot whenever the question is about what exists or where it
    is. The engine knows the answer exactly; a screenshot makes you infer it.
    """
    return _call("Digest", maxNodes=max_nodes, includeInactive=include_inactive,
                 nameFilter=name_filter or None)


@mcp.tool()
def pg_view(max_objects: int = 40) -> str:
    """What the camera can currently see, as symbols: screen rects, distances, occlusion.

    Includes what a ray through the screen centre hits, which is the direct answer to
    "what is the player looking at".
    """
    return _call("View", maxObjects=max_objects)


@mcp.tool()
def pg_capture(name: str = "capture", max_boxes: int = 8) -> str:
    """Write a screenshot with labelled boxes and return the legend naming each one.

    Read the legend alongside the image. The image alone forces you to work out what you
    are looking at, which is exactly where vision models go wrong on game scenes.
    """
    return _call("Capture", name=name, maxBoxes=max_boxes)


@mcp.tool()
def pg_visual_check(name: str) -> str:
    """Compare a capture against its stored baseline, writing a diff image on failure."""
    return _summarise(_call("VisualCheck", name=name))


@mcp.tool()
def pg_events(max_events: int = 200) -> str:
    """The frame-stamped event timeline from the most recent run."""
    return _call("Events", maxEvents=max_events)


@mcp.tool()
def pg_scenarios() -> str:
    """List the scenarios defined in this project."""
    return _call("Scenarios")


@mcp.tool()
def pg_norms(genre: str) -> str:
    """Measured feel constants for a genre, with the reasoning behind each number.

    Use these when asked to make something feel better: they give a target to move
    toward instead of a judgment you cannot make from outside the game.
    """
    return _call("Norms", genre=genre)


@mcp.tool()
def pg_play(enter: bool = True) -> str:
    """Enter or leave play mode. Scenarios and probes need play mode."""
    return _call("EnterPlayMode" if enter else "ExitPlayMode")


@mcp.tool()
def pg_run_scenario(name: str, wait: bool = True, timeout_seconds: int = 300) -> str:
    """Run a scenario against the running game and return its report.

    The scenario drives real input through the same device layer a player uses, so what
    it proves is what a player would experience. Requires play mode.
    """
    started = _call("RunScenario", name=name)
    if "\"state\":\"running\"" not in started:
        return _summarise(started)
    return _wait_for_run(timeout_seconds) if wait else started


@mcp.tool()
def pg_run_probe(seconds: float = 60, seed: int = 12345, wait: bool = True,
                 timeout_seconds: int = 600) -> str:
    """Turn the probe bot loose on the level to find stuck points, falls and errors.

    Finds the class of defect that only appears when someone actually walks into things.
    Requires play mode.
    """
    started = _call("RunProbe", seconds=seconds, seed=seed)
    if "\"state\":\"running\"" not in started:
        return _summarise(started)
    return _wait_for_run(timeout_seconds) if wait else started


@mcp.tool()
def pg_run_status() -> str:
    """Whether a run is in progress, and the report from the last one."""
    return _summarise_status(_call("RunStatus"))


@mcp.tool()
def pg_watch_audio() -> str:
    """Start inferring audio events from AudioSource activity.

    For a game with no explicit audio instrumentation. Call before a run, then use
    pg_check("audio") afterwards.
    """
    return _call("WatchAudio")


@mcp.tool()
def pg_record(start: bool = True, name: str = "recorded") -> str:
    """Record a live play session and save it as a replayable scenario.

    Call with start=True, ask the user to play until the thing they are describing
    happens, then call with start=False. The result is a deterministic scenario file
    that reproduces the sequence, which is far more useful than a prose description of
    a bug. Requires play mode.
    """
    if start:
        return _call("StartRecording")
    return _summarise(_call("StopRecording", name=name))


@mcp.tool()
def pg_capture_baseline(overwrite: bool = False) -> str:
    """Write contracts describing the game as it currently behaves.

    This is the way into an existing game. You cannot diff against a spec that was never
    written, so capture what the game does now, and treat later changes as deviations
    from it. Run a scenario or probe first so there is something to capture.
    """
    return _summarise(_call("CaptureBaseline", overwrite=overwrite))


def _wait_for_run(timeout_seconds: int) -> str:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        time.sleep(1.0)
        status = json.loads(_call("RunStatus"))
        if not status.get("busy"):
            report = status.get("lastReport")
            return _summarise(json.dumps(report)) if report else "Run finished with no report."
    return f"Run did not finish within {timeout_seconds}s. Poll pg_run_status."


def _summarise_status(raw: str) -> str:
    status = json.loads(raw)
    lines = [
        f"playing={status.get('isPlaying')} compiling={status.get('isCompiling')} "
        f"busy={status.get('busy')}"
    ]
    if status.get("lastReport"):
        lines.append(_summarise(json.dumps(status["lastReport"])))
    return "\n".join(lines)


def main() -> None:
    mcp.run()


if __name__ == "__main__":
    main()
