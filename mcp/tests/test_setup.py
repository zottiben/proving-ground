"""Installing the skill shelf into a project.

Setup used to copy one file. It now copies a shelf of skill packs, each with its own
references, into whichever directory the harness reads skills from - so the failure
these cover is a pack that arrives without its references, or an update that deletes a
skill somebody wrote themselves next to ours.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from proving_ground_mcp import cli


def make_shelf(root: Path, names: tuple[str, ...] = ("proving-ground", "game-feel")) -> Path:
    shelf = root / "skills"
    for name in names:
        pack = shelf / name
        (pack / "references").mkdir(parents=True)
        (pack / "SKILL.md").write_text(f"---\nname: {name}\n---\n")
        (pack / "references" / "depth.md").write_text(f"# {name} depth\n")
    (shelf / "not-a-skill").mkdir()
    (shelf / "not-a-skill" / "README.md").write_text("no SKILL.md here\n")
    return shelf


def make_project(root: Path) -> Path:
    project = root / "game"
    (project / "Assets").mkdir(parents=True)
    return project


def test_installs_every_pack_with_its_references(tmp_path, capsys):
    shelf = make_shelf(tmp_path)
    project = make_project(tmp_path)

    cli.install_skills(project / ".claude" / "skills", project, shelf)

    installed = project / ".claude" / "skills"
    assert (installed / "proving-ground" / "SKILL.md").is_file()
    assert (installed / "game-feel" / "references" / "depth.md").is_file()
    assert not (installed / "not-a-skill").exists()
    assert "2 skills installed" in capsys.readouterr().out


def test_reinstalling_clears_files_an_older_version_left_in_our_packs(tmp_path):
    """A reference file renamed between versions must not linger as orphaned advice."""
    shelf = make_shelf(tmp_path)
    project = make_project(tmp_path)
    installed = project / ".claude" / "skills"

    cli.install_skills(installed, project, shelf)

    stale = installed / "game-feel" / "references" / "renamed-away.md"
    stale.write_text("advice from a previous version that nothing links to\n")

    cli.install_skills(installed, project, shelf)

    assert not stale.exists()
    assert (installed / "game-feel" / "references" / "depth.md").is_file()


def test_reinstalling_updates_in_place_and_keeps_skills_we_did_not_write(tmp_path):
    shelf = make_shelf(tmp_path)
    project = make_project(tmp_path)
    installed = project / ".claude" / "skills"

    cli.install_skills(installed, project, shelf)

    mine = installed / "team-conventions"
    mine.mkdir()
    (mine / "SKILL.md").write_text("ours\n")

    (shelf / "game-feel" / "SKILL.md").write_text("---\nname: game-feel\nv: 2\n---\n")
    cli.install_skills(installed, project, shelf)

    assert "v: 2" in (installed / "game-feel" / "SKILL.md").read_text()
    assert (mine / "SKILL.md").read_text() == "ours\n"


def test_reports_an_empty_shelf_rather_than_installing_nothing_quietly(tmp_path, capsys):
    project = make_project(tmp_path)
    cli.install_skills(project / ".claude" / "skills", project, tmp_path / "skills")

    assert "Skills missing" in capsys.readouterr().out
    assert not (project / ".claude").exists()


def test_claude_setup_registers_the_server_and_the_shelf(tmp_path):
    shelf = make_shelf(tmp_path)
    project = make_project(tmp_path)

    cli.configure_claude(project, tmp_path / "bin" / "proving-ground-mcp", shelf)

    config = json.loads((project / ".mcp.json").read_text())
    assert config["mcpServers"]["proving-ground"]["command"].endswith("proving-ground-mcp")
    assert (project / ".claude" / "skills" / "game-feel" / "SKILL.md").is_file()


def test_codex_setup_installs_to_the_codex_home_and_points_agents_md_at_it(tmp_path, monkeypatch):
    shelf = make_shelf(tmp_path)
    project = make_project(tmp_path)
    codex_home = tmp_path / "codex-home"
    monkeypatch.setenv("CODEX_HOME", str(codex_home))

    cli.configure_codex(project, tmp_path / "bin" / "proving-ground-mcp", shelf)

    assert (codex_home / "skills" / "game-feel" / "SKILL.md").is_file()
    assert "proving-ground" in (project / ".codex" / "config.toml").read_text()
    assert "proving-ground" in (project / "AGENTS.md").read_text()


@pytest.mark.parametrize("harness", ["claude", "pi"])
def test_every_project_harness_gets_the_whole_shelf(tmp_path, harness):
    shelf = make_shelf(tmp_path)
    project = make_project(tmp_path)

    cli.CONFIGURE[harness](project, tmp_path / "bin" / "proving-ground-mcp", shelf)

    where = {"claude": ".claude/skills", "pi": ".agents/skills"}[harness]
    assert len(cli.skill_packs(project / where)) == 2
