"""Where an installed Proving Ground lives on disk.

Everything resolves through ``current``, a symlink to the active version. Unity project
manifests point at that stable path, so updating swings one symlink instead of rewriting
the manifest of every project you have ever set up.
"""

from __future__ import annotations

import os
from pathlib import Path

REPO = "zottiben/proving-ground"
PACKAGE_ID = "com.zottiben.provingground"
SERVER_NAME = "proving-ground"


def data_home() -> Path:
    return Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share")) / "proving-ground"


def bin_home() -> Path:
    return Path(os.environ.get("PG_BIN_DIR", Path.home() / ".local" / "bin"))


def current() -> Path:
    """The active install root. Stable across updates."""
    return data_home() / "current"


def state_home() -> Path:
    """
    Where cached state lives, alongside the install it describes.

    Derived from the install rather than from XDG_DATA_HOME, because the launcher pins
    PG_HOME while XDG_DATA_HOME is usually unset at run time. Keying the cache off the
    environment instead meant reading a different directory than the one being checked,
    so a freshly discovered update was never seen again.
    """
    override = os.environ.get("PG_HOME")
    if override:
        return Path(os.path.abspath(os.path.expanduser(override))).parent
    return data_home()


def installed_root() -> Path:
    """
    The install this process is running from.

    Falls back to the source tree so the CLI behaves identically when run from a clone
    during development.
    """
    override = os.environ.get("PG_HOME")
    if override:
        # abspath, not resolve: `current` is a symlink into the versioned directory, and
        # resolving it would bake today's version number into every Unity manifest, so an
        # update would break every project that had ever been set up.
        return Path(os.path.abspath(os.path.expanduser(override)))

    marker = current()
    if (marker / "package" / "package.json").is_file():
        return marker

    # mcp/proving_ground_mcp/paths.py -> repo root
    return Path(__file__).resolve().parent.parent.parent


def unity_package(root: Path | None = None) -> Path:
    """The UPM package directory inside an install or a clone."""
    root = root or installed_root()
    packaged = root / "package"
    if (packaged / "package.json").is_file():
        return packaged
    return root / "packages" / PACKAGE_ID


def skill_source(root: Path | None = None) -> Path:
    root = root or installed_root()
    return root / "skills" / SERVER_NAME / "SKILL.md"


def mcp_launcher(root: Path | None = None) -> Path:
    """The MCP server entry point to write into harness configs."""
    root = root or installed_root()
    scripts = "Scripts" if os.name == "nt" else "bin"
    suffix = ".exe" if os.name == "nt" else ""

    for candidate in (
        root / ".venv" / scripts / f"proving-ground-mcp{suffix}",
        root / "mcp" / ".venv" / scripts / f"proving-ground-mcp{suffix}",
    ):
        if candidate.is_file():
            return candidate

    return root / ".venv" / scripts / f"proving-ground-mcp{suffix}"


def version(root: Path | None = None) -> str:
    root = root or installed_root()
    marker = root / "VERSION"
    if marker.is_file():
        return marker.read_text().strip()
    return "dev"
