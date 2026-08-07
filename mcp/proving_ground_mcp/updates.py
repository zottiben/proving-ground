"""Telling someone a new version exists, without getting in their way.

The rules this follows, in order of importance: never delay a command, never fail a
command, never nag. A version check that adds a second to every invocation is worse
than no version check, so the network call is capped hard, cached for a day, and
swallowed whole on any error.
"""

from __future__ import annotations

import json
import os
import time
from pathlib import Path

from . import paths

CACHE_NAME = "update-check.json"
CHECK_INTERVAL = 60 * 60 * 24  # once a day is plenty for a tool released occasionally
NETWORK_TIMEOUT = 2.0


def _cache_path() -> Path:
    return paths.state_home() / CACHE_NAME


def _read_cache() -> dict:
    try:
        return json.loads(_cache_path().read_text())
    except Exception:
        return {}


def _write_cache(data: dict) -> None:
    try:
        path = _cache_path()
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data))
    except Exception:
        # A cache we cannot write means we check again next time. Not worth a word.
        pass


def parse(version: str) -> tuple[int, ...]:
    """Turns 'v0.1.10' into (0, 1, 10). Unparseable parts become 0."""
    cleaned = (version or "").strip().lstrip("vV")
    parts = []
    for chunk in cleaned.split(".")[:4]:
        digits = "".join(c for c in chunk if c.isdigit())
        parts.append(int(digits) if digits else 0)
    return tuple(parts) or (0,)


def is_newer(candidate: str, current: str) -> bool:
    if not candidate or not current or current == "dev":
        return False
    return parse(candidate) > parse(current)


def fetch_latest() -> str | None:
    """The newest published tag, or None. Never raises."""
    try:
        import httpx

        headers = {"Accept": "application/vnd.github+json"}
        token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
        if token:
            headers["Authorization"] = f"Bearer {token}"

        response = httpx.get(
            f"https://api.github.com/repos/{paths.REPO}/releases/latest",
            headers=headers,
            timeout=NETWORK_TIMEOUT,
        )
        if response.status_code != 200:
            return None
        return response.json().get("tag_name") or None
    except Exception:
        return None


def available(force: bool = False) -> str | None:
    """
    The newer version, if one is known. Consults the network at most once a day.

    Returns None when up to date, when the check is disabled, or when anything at all
    goes wrong.
    """
    if os.environ.get("PG_NO_UPDATE_CHECK"):
        return None

    current = paths.version()
    if current == "dev" and not force:
        # Running from a source tree. Whatever is released is not what is installed.
        return None

    cache = _read_cache()
    fresh = (time.time() - cache.get("checked", 0)) < CHECK_INTERVAL

    if fresh and not force:
        latest = cache.get("latest")
    else:
        latest = fetch_latest()
        if latest:
            _write_cache({"checked": time.time(), "latest": latest})
        else:
            # Back off on failure too, so an offline machine is not retried every command.
            _write_cache({"checked": time.time(), "latest": cache.get("latest")})
            latest = cache.get("latest")

    return latest if is_newer(latest or "", current) else None


def banner(latest: str, colour: bool = True) -> str:
    current = paths.version()
    yellow = "\033[33m" if colour else ""
    bold = "\033[1m" if colour else ""
    dim = "\033[2m" if colour else ""
    off = "\033[0m" if colour else ""

    return (
        f"\n{yellow}Update available{off}  {dim}{current} -> {latest}{off}\n"
        f"  Run {bold}proving-ground update{off} to install it.\n"
        f"  {dim}Silence this with PG_NO_UPDATE_CHECK=1{off}\n"
    )
