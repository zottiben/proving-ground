"""Resolving the address of a running Editor.

The bug these cover: the bridge's port lives in EditorPrefs and need not be the default,
but every client assumed the default, so an Editor listening anywhere else was reported
as "could not reach the Editor, enable the bridge" while it was running.
"""

from __future__ import annotations

import json
import os
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

import pytest

from proving_ground_mcp import bridge

# Nothing may listen here: the resolver must never be rescued by a real service, and a
# test that quietly probes a developer's own Editor proves nothing.
CLOSED = 8791
OTHER_CLOSED = 8792


@pytest.fixture(autouse=True)
def data_home(tmp_path, monkeypatch):
    """An empty registry, isolated from whatever Editors the machine is running."""
    monkeypatch.setenv("XDG_DATA_HOME", str(tmp_path / "share"))
    monkeypatch.delenv("PROVING_GROUND_URL", raising=False)
    monkeypatch.chdir(tmp_path)
    bridge.registry_dir().mkdir(parents=True)
    return tmp_path


def make_project(root: Path, name: str) -> Path:
    project = root / name
    (project / "Assets").mkdir(parents=True)
    (project / "ProjectSettings").mkdir(parents=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.16f1\n")
    return project


def publish(project: Path, port: int) -> None:
    """Writes the entry the Editor writes when its bridge starts listening."""
    entry = {
        "url": f"http://127.0.0.1:{port}",
        "port": port,
        "project": str(project),
        "projectName": project.name,
        "pid": os.getpid(),
    }
    (bridge.registry_dir() / f"{project.name}.json").write_text(json.dumps(entry))


@pytest.fixture
def serving():
    """A stand-in Editor answering /health, on a port the OS picks."""
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            body = b'{"ok":true,"project":"stub"}'
            self.send_response(200 if self.path == "/health" else 404)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass

    server = HTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    yield f"http://127.0.0.1:{server.server_port}"
    server.shutdown()


def test_falls_back_to_the_default_port_when_nothing_is_published(data_home):
    project = make_project(data_home, "alpha")
    assert bridge.resolve(project) == bridge.DEFAULT_URL


def test_finds_the_editor_on_a_non_default_port(data_home):
    project = make_project(data_home, "alpha")
    publish(project, CLOSED)
    assert bridge.resolve(project) == f"http://127.0.0.1:{CLOSED}"


def test_keeps_using_its_own_editor_while_it_is_unreachable(data_home):
    """A domain reload takes the listener down for seconds; the address still holds."""
    project = make_project(data_home, "alpha")
    publish(project, CLOSED)
    assert bridge.health(f"http://127.0.0.1:{CLOSED}", timeout=0.5) is None
    assert bridge.resolve(project) == f"http://127.0.0.1:{CLOSED}"


def test_does_not_borrow_another_projects_editor(data_home):
    """Retargeting another game would edit the wrong project, which is worse than failing."""
    mine = make_project(data_home, "alpha")
    theirs = make_project(data_home, "beta")
    publish(theirs, CLOSED)
    assert bridge.resolve(mine) == bridge.DEFAULT_URL


def test_two_editors_each_resolve_to_their_own(data_home):
    alpha = make_project(data_home, "alpha")
    beta = make_project(data_home, "beta")
    publish(alpha, CLOSED)
    publish(beta, OTHER_CLOSED)
    assert bridge.resolve(alpha) == f"http://127.0.0.1:{CLOSED}"
    assert bridge.resolve(beta) == f"http://127.0.0.1:{OTHER_CLOSED}"


def test_uses_the_only_editor_when_there_is_no_project_to_match(data_home):
    """Running from a repository root with the game in a subdirectory."""
    project = make_project(data_home, "alpha")
    publish(project, CLOSED)
    assert bridge.resolve(None) == f"http://127.0.0.1:{CLOSED}"


def test_prefers_the_answering_editor_when_the_project_is_unknown(data_home, serving):
    publish(make_project(data_home, "alpha"), CLOSED)
    (bridge.registry_dir() / "beta.json").write_text(
        json.dumps({"url": serving, "project": str(data_home / "beta"), "projectName": "beta"})
    )
    assert bridge.resolve(None) == serving


def test_an_explicit_url_overrides_the_registry(data_home, monkeypatch):
    project = make_project(data_home, "alpha")
    publish(project, CLOSED)
    monkeypatch.setenv("PROVING_GROUND_URL", "http://127.0.0.1:9999/")
    assert bridge.resolve(project) == "http://127.0.0.1:9999"


def test_a_corrupt_entry_does_not_hide_the_others(data_home):
    project = make_project(data_home, "alpha")
    publish(project, CLOSED)
    (bridge.registry_dir() / "truncated.json").write_text('{"url": ')
    assert bridge.resolve(project) == f"http://127.0.0.1:{CLOSED}"


def test_the_message_names_the_address_it_actually_tried(data_home):
    project = make_project(data_home, "alpha")
    publish(project, CLOSED)
    message = bridge.unreachable_message(bridge.resolve(project), project)
    assert f"127.0.0.1:{CLOSED}" in message
    assert "compiling" in message


def test_the_message_points_at_the_editor_that_is_actually_running(data_home, serving):
    mine = make_project(data_home, "alpha")
    (bridge.registry_dir() / "beta.json").write_text(
        json.dumps({"url": serving, "project": str(data_home / "beta"), "projectName": "beta"})
    )
    message = bridge.unreachable_message(bridge.resolve(mine), mine)
    assert "beta" in message and serving in message


def test_the_message_says_what_to_do_when_no_editor_is_registered(data_home):
    project = make_project(data_home, "alpha")
    message = bridge.unreachable_message(bridge.resolve(project), project)
    assert "Agent Bridge > Enable" in message
