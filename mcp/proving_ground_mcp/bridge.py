"""Finding the Editor's agent bridge.

The port the bridge listens on is Editor-side state, and it is not always the default.
Assuming the default meant that the moment the two disagreed every call failed with
"could not reach the Editor, enable the bridge" while the bridge was running perfectly
well a few ports away - an error that sends you to fix the one thing that is not broken.

So a running Editor writes down where it is listening, and this reads that. The default
port remains the fallback, which keeps an Editor running an older package working.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

from . import paths

DEFAULT_URL = "http://127.0.0.1:8787"
PROBE_TIMEOUT = 2.0


def registry_dir() -> Path:
    return paths.data_home() / "bridges"


def entries() -> list[dict]:
    """Every bridge address an Editor has published."""
    directory = registry_dir()
    if not directory.is_dir():
        return []

    found = []
    for file in sorted(directory.glob("*.json")):
        try:
            entry = json.loads(file.read_text())
        except (OSError, ValueError):
            # A half-written or hand-mangled entry should cost us that one Editor, not
            # the ability to find any of the others.
            continue
        if isinstance(entry, dict) and entry.get("url"):
            found.append(entry)
    return found


def entry_for(project: Path, found: list[dict] | None = None) -> dict | None:
    """The entry a given Unity project published, if it has one."""
    target = os.path.normcase(str(project.resolve()))
    for entry in found if found is not None else entries():
        recorded = entry.get("project")
        if not recorded:
            continue
        try:
            if os.path.normcase(str(Path(recorded).resolve())) == target:
                return entry
        except OSError:
            continue
    return None


def health(url: str, timeout: float = PROBE_TIMEOUT) -> dict | None:
    """The Editor's health payload, or None if nothing answers."""
    import httpx

    try:
        response = httpx.get(f"{url}/health", timeout=timeout)
        if response.status_code == 200:
            return response.json()
    except Exception:
        pass
    return None


def resolve(project: Path | None = None) -> str:
    """The base URL of the bridge this process should talk to."""
    override = os.environ.get("PROVING_GROUND_URL")
    if override:
        return override.rstrip("/")

    found = entries()
    if not found:
        return DEFAULT_URL

    root = project or paths.find_project(Path.cwd())
    if root:
        mine = entry_for(root, found)
        # Its own entry is used even when nothing answers on it. An Editor reloading its
        # domain after a compile is unreachable for a few seconds, and quietly retargeting
        # another project's bridge would be far worse than waiting for this one.
        return mine["url"] if mine else DEFAULT_URL

    # No project around this process to match on - the agent is running from a repository
    # root with the game in a subdirectory, say. One published bridge is unambiguous;
    # beyond that, the one that answers is the best evidence available.
    if len(found) == 1:
        return found[0]["url"]

    for entry in found:
        if health(entry["url"]):
            return entry["url"]

    return DEFAULT_URL


def unreachable_message(url: str, project: Path | None = None) -> str:
    """Explains a failed connection in terms of what is actually registered."""
    if os.environ.get("PROVING_GROUND_URL"):
        return (
            f"Could not reach the Unity Editor at {url}, which PROVING_GROUND_URL points "
            "at. Unset it to use the address the Editor publishes."
        )

    root = project or paths.find_project(Path.cwd())
    found = entries()

    mine = entry_for(root, found) if root else None
    if mine:
        return (
            f"The bridge for {_name(mine)} is registered at {mine['url']} but is not "
            "answering. Unity may be compiling, or the Editor may have been closed."
        )

    running = [entry for entry in found if health(entry["url"])]
    if running:
        listed = ", ".join(f"{_name(entry)} at {entry['url']}" for entry in running)
        here = f" for {root.name}" if root else " for this project"
        return (
            f"Could not reach the Unity Editor at {url}. A bridge is running for {listed}, "
            f"but none{here}. Open this project in Unity and enable "
            "Tools > Proving Ground > Agent Bridge > Enable."
        )

    return (
        f"Could not reach the Unity Editor at {url}. Open the project and enable "
        "Tools > Proving Ground > Agent Bridge > Enable."
    )


def _name(entry: dict) -> str:
    recorded = entry.get("project")
    return entry.get("projectName") or (Path(recorded).name if recorded else "a project")
