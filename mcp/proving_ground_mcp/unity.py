"""Finding Unity installs, and creating a project ready for agent-driven work.

Creating the project here rather than pointing people at Unity Hub is not just
convenience. A project made by the Hub's default template leaves Active Input Handling
on the old Input Manager, and that single setting makes every controller an agent writes
compile perfectly and respond to nothing. Better to create it correctly than to explain
the trap afterwards.
"""

from __future__ import annotations

import json
import os
import platform
import re
import subprocess
from pathlib import Path


def editor_candidates() -> list[Path]:
    """Every Unity editor executable we can find, newest version first."""
    system = platform.system()
    roots: list[Path] = []

    if system == "Darwin":
        roots = [Path("/Applications/Unity/Hub/Editor")]
        pattern = "*/Unity.app/Contents/MacOS/Unity"
    elif system == "Windows":
        roots = [
            Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity" / "Hub" / "Editor",
        ]
        pattern = "*/Editor/Unity.exe"
    else:
        roots = [Path.home() / "Unity" / "Hub" / "Editor", Path("/opt/unity/editors")]
        pattern = "*/Editor/Unity"

    found = []
    for root in roots:
        if not root.is_dir():
            continue
        found.extend(root.glob(pattern))

    def sort_key(path: Path):
        # .../Editor/<version>/... - sort numerically so 6000.3.16 beats 2022.3.9.
        match = re.search(r"Editor/([^/]+)/", str(path))
        version = match.group(1) if match else ""
        return [int(n) for n in re.findall(r"\d+", version)] or [0]

    return sorted(set(found), key=sort_key, reverse=True)


def version_of(editor: Path) -> str:
    match = re.search(r"Editor/([^/]+)/", str(editor))
    return match.group(1) if match else "unknown"


def create_project(editor: Path, target: Path, timeout: int = 900) -> tuple[bool, str]:
    """
    Creates a Unity project at `target`. Returns (ok, message).

    Runs the editor headless, which takes a couple of minutes on a cold cache.
    """
    target.mkdir(parents=True, exist_ok=True)
    log = target / "create.log"

    try:
        result = subprocess.run(
            [
                str(editor),
                "-batchmode",
                "-quit",
                "-nographics",
                "-createProject",
                str(target),
                "-logFile",
                str(log),
            ],
            timeout=timeout,
            capture_output=True,
        )
    except subprocess.TimeoutExpired:
        return False, f"Unity did not finish within {timeout}s. See {log}."
    except OSError as e:
        return False, f"Could not run {editor}: {e}"

    if not (target / "Assets").is_dir() or not (target / "ProjectSettings").is_dir():
        tail = ""
        if log.is_file():
            tail = "\n".join(log.read_text(errors="replace").splitlines()[-8:])
        return False, f"Unity exited {result.returncode} without creating a project.\n{tail}"

    log.unlink(missing_ok=True)
    return True, "created"


def prepare_for_agents(project: Path) -> list[str]:
    """
    Makes a freshly created project actually usable by an agent.

    Returns a list of what changed, for reporting.
    """
    changed = []

    manifest_path = project / "Packages" / "manifest.json"
    if manifest_path.is_file():
        manifest = json.loads(manifest_path.read_text())
        deps = manifest.setdefault("dependencies", {})
        if "com.unity.inputsystem" not in deps:
            deps["com.unity.inputsystem"] = "1.14.2"
            manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
            changed.append("Added the Input System package")

    settings = project / "ProjectSettings" / "ProjectSettings.asset"
    if settings.is_file():
        text = settings.read_text(errors="replace")
        # 0 = old Input Manager, 1 = Input System, 2 = both. Anything but 0 works;
        # 'both' is the least surprising because legacy Input calls keep working.
        updated = re.sub(r"^(\s*activeInputHandler:\s*)0\s*$", r"\g<1>2", text, flags=re.MULTILINE)
        if updated != text:
            settings.write_text(updated)
            changed.append("Set Active Input Handling to Both")

    return changed
