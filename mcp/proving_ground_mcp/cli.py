"""The `proving-ground` command line tool.

Two jobs: walk someone through wiring the plugin into their game, and talk to the agent
bridge from a shell once it is running.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

from . import paths, updates

HARNESSES = ("claude", "codex", "pi")

_TTY = sys.stdout.isatty() and not os.environ.get("NO_COLOR")
GREEN = "\033[32m" if _TTY else ""
YELLOW = "\033[33m" if _TTY else ""
RED = "\033[31m" if _TTY else ""
DIM = "\033[2m" if _TTY else ""
BOLD = "\033[1m" if _TTY else ""
OFF = "\033[0m" if _TTY else ""


def ok(msg: str) -> None:
    print(f"  {GREEN}OK{OFF}   {msg}")


def note(msg: str) -> None:
    print(f"  {YELLOW}NOTE{OFF} {msg}")


def bad(msg: str) -> None:
    print(f"  {RED}FAIL{OFF} {msg}")


def heading(text: str) -> None:
    print(f"\n{BOLD}{text}{OFF}")


def prompt(question: str, default: str = "") -> str:
    if not sys.stdin.isatty():
        return default
    suffix = f" [{default}]" if default else ""
    answer = input(f"       {question}{suffix}: ").strip()
    return answer or default


def confirm(question: str, default: bool = True) -> bool:
    if not sys.stdin.isatty():
        return default
    options = "Y/n" if default else "y/N"
    answer = input(f"       {question} [{options}]: ").strip().lower()
    if not answer:
        return default
    return answer.startswith("y")


# --- project discovery ---------------------------------------------------------------


def is_unity_project(path: Path) -> bool:
    return (path / "Assets").is_dir() and (path / "ProjectSettings" / "ProjectVersion.txt").is_file()


def find_project(start: Path) -> Path | None:
    """Walks up from `start` looking for a Unity project."""
    for candidate in (start, *start.parents):
        if is_unity_project(candidate):
            return candidate
        if (candidate / ".git").is_dir() and candidate != start:
            # Stop at the repository boundary rather than wandering into a parent project.
            break
    return None


def describe_not_a_project(cwd: Path) -> None:
    bad(f"{cwd} is not a Unity project.")
    print(f"""
  Run this from inside your game, next to {BOLD}Assets/{OFF} and {BOLD}ProjectSettings/{OFF}:

      cd ~/path/to/your-game
      proving-ground setup

  If you have not created the Unity project yet, make one in Unity Hub first.
  Proving Ground configures an existing project; it does not create one.
""")


# --- unity package -------------------------------------------------------------------


def install_package(project: Path, package: Path) -> bool:
    manifest_path = project / "Packages" / "manifest.json"
    if not manifest_path.is_file():
        bad(f"No {manifest_path}. Is this really a Unity project?")
        return False

    try:
        manifest = json.loads(manifest_path.read_text())
    except json.JSONDecodeError as e:
        bad(f"Packages/manifest.json is not valid JSON: {e}")
        return False

    deps = manifest.setdefault("dependencies", {})
    reference = f"file:{package}"

    if deps.get(paths.PACKAGE_ID) == reference:
        ok("Unity package already installed")
        return True

    existing = deps.get(paths.PACKAGE_ID)
    deps[paths.PACKAGE_ID] = reference
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")

    ok(f"{'Updated' if existing else 'Added'} the Unity package")
    print(f"       {DIM}{reference}{OFF}")
    return True


# --- harness configuration -----------------------------------------------------------


def detect_harnesses(project: Path) -> list[str]:
    found = []
    for name in HARNESSES:
        marks = [Path.home() / f".{name}", project / f".{name}"]
        if name == "pi":
            marks.append(project / ".agents")
        if any(m.exists() for m in marks) or shutil.which(name):
            found.append(name)
    return found


def install_skill(destination: Path, project: Path, source: Path) -> None:
    if not source.is_file():
        bad(f"Skill missing at {source}")
        return

    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)

    try:
        shown = destination.relative_to(project)
    except ValueError:
        shown = destination
    ok(f"Skill installed at {shown}")


def configure_claude(project: Path, launcher: Path, skill: Path) -> None:
    config = project / ".mcp.json"
    data = json.loads(config.read_text()) if config.is_file() else {}
    data.setdefault("mcpServers", {})[paths.SERVER_NAME] = {
        "command": str(launcher),
        "args": [],
    }
    config.write_text(json.dumps(data, indent=2) + "\n")
    ok("MCP server registered in .mcp.json")

    install_skill(project / ".claude" / "skills" / paths.SERVER_NAME / "SKILL.md", project, skill)


def configure_pi(project: Path, launcher: Path, skill: Path) -> None:
    config = project / ".pi" / "mcp.json"
    config.parent.mkdir(parents=True, exist_ok=True)
    data = json.loads(config.read_text()) if config.is_file() else {}
    data.setdefault("mcpServers", {})[paths.SERVER_NAME] = {
        "command": str(launcher),
        "args": [],
        "transport": "stdio",
        "lifecycle": "lazy",
        "directTools": True,
    }
    config.write_text(json.dumps(data, indent=2) + "\n")
    ok("MCP server registered in .pi/mcp.json")

    install_skill(project / ".agents" / "skills" / paths.SERVER_NAME / "SKILL.md", project, skill)


def configure_codex(project: Path, launcher: Path, skill: Path) -> None:
    config = project / ".codex" / "config.toml"
    config.parent.mkdir(parents=True, exist_ok=True)
    existing = config.read_text() if config.is_file() else ""

    block = (
        f"[mcp_servers.{paths.SERVER_NAME}]\n"
        f'command = "{launcher}"\n'
        "args = []\n"
        "startup_timeout_sec = 60\n"
    )

    # Replace an existing block rather than appending: Codex rejects a repeated key.
    pattern = re.compile(
        rf"^\[mcp_servers\.{re.escape(paths.SERVER_NAME)}\]$.*?(?=^\[|\Z)",
        re.MULTILINE | re.DOTALL,
    )

    if pattern.search(existing):
        updated = pattern.sub(block, existing)
    else:
        gap = "" if not existing else ("\n" if existing.endswith("\n") else "\n\n")
        updated = existing + gap + block

    config.write_text(updated)
    ok("MCP server registered in .codex/config.toml")
    note("Codex loads project config only from trusted projects. Run `codex` here once and trust it.")

    # Codex reads skills from its home directory, not from the project.
    codex_home = Path(os.environ.get("CODEX_HOME", Path.home() / ".codex"))
    install_skill(codex_home / "skills" / paths.SERVER_NAME / "SKILL.md", project, skill)

    agents = project / "AGENTS.md"
    marker = "<!-- proving-ground -->"
    text = agents.read_text() if agents.is_file() else ""
    if marker in text:
        ok("AGENTS.md already points at the skill")
        return

    agents.write_text(
        (text + "\n\n" if text else "")
        + f"{marker}\n## Proving Ground\n\n"
        "This project uses the Proving Ground plugin for Unity. Load the `proving-ground` "
        "skill before building, changing or verifying anything in the game.\n"
    )
    ok(f"{'Updated' if text else 'Created'} AGENTS.md")


CONFIGURE = {"claude": configure_claude, "codex": configure_codex, "pi": configure_pi}


# --- commands ------------------------------------------------------------------------


def cmd_setup(args: argparse.Namespace) -> int:
    root = paths.installed_root()
    package = paths.unity_package(root)
    skill = paths.skill_source(root)
    launcher = paths.mcp_launcher(root)

    print(f"\n{BOLD}Proving Ground setup{OFF}  {DIM}{paths.version(root)}{OFF}")

    if not (package / "package.json").is_file():
        bad(f"The Unity package is missing from this install ({package}).")
        print("       Reinstall with: proving-ground update --force")
        return 1

    heading("Your game project")
    cwd = Path(args.project).expanduser().resolve() if args.project else Path.cwd().resolve()
    project = find_project(cwd)

    if project is None:
        describe_not_a_project(cwd)
        return 1

    ok(f"Unity project: {project}")
    if (project / ".git").is_dir():
        ok("Git repository detected")
    else:
        note("Not a git repository. Proving Ground writes files you will want under version control.")

    if project != cwd and not args.yes:
        if not confirm(f"Set up {project}?"):
            print("       Cancelled. Run this from inside the project you want to configure.")
            return 1

    heading("Unity package")
    if not install_package(project, package):
        return 1

    heading("Agent harness")
    if args.harness:
        chosen = [h.strip().lower() for h in args.harness.split(",")]
        unknown = [h for h in chosen if h not in HARNESSES]
        if unknown:
            bad(f"Unknown harness {unknown}. Choose from: {', '.join(HARNESSES)}.")
            return 1
    else:
        detected = detect_harnesses(project)
        if not detected:
            note("No harness detected.")
            answer = prompt(f"Which are you using? ({'/'.join(HARNESSES)}, or none)", "none")
            chosen = [answer] if answer in HARNESSES else []
        elif len(detected) == 1:
            ok(f"Detected {detected[0]}")
            chosen = detected
        else:
            ok(f"Detected {', '.join(detected)}")
            answer = "all" if args.yes else prompt(f"Configure which? ({'/'.join(detected)}/all)", "all")
            chosen = detected if answer == "all" else ([answer] if answer in detected else detected)

    if not launcher.is_file():
        bad(f"The MCP server is not built ({launcher}).")
        print("       Reinstall with: proving-ground update --force")
        return 1

    for harness in chosen:
        print(f"\n       {BOLD}{harness}{OFF}")
        CONFIGURE[harness](project, launcher, skill)

    if not chosen:
        note("No harness configured. See the README for the manual steps.")

    heading("Next")
    print(f"""
  1. Open {project.name} in Unity. The package compiles on import.
  2. {BOLD}Tools > Proving Ground > Agent Bridge > Enable{OFF}
  3. Start your agent in this directory and prompt it, for example:

     {DIM}Check the project settings, then build me a greybox first person
     shooter. Use a scene recipe for the level and verify with Proving
     Ground at every step.{OFF}

  Tag your player {BOLD}Player{OFF} so the harness can find it. Check the wiring any
  time with {BOLD}proving-ground doctor{OFF}.
""")
    return 0


def cmd_doctor(args: argparse.Namespace) -> int:
    root = paths.installed_root()
    print(f"\n{BOLD}Proving Ground doctor{OFF}  {DIM}{paths.version(root)}{OFF}")

    healthy = True

    heading("Install")
    package = paths.unity_package(root)
    launcher = paths.mcp_launcher(root)

    if (package / "package.json").is_file():
        ok(f"Unity package: {package}")
    else:
        bad(f"Unity package missing: {package}")
        healthy = False

    if launcher.is_file():
        ok(f"MCP server: {launcher}")
    else:
        bad(f"MCP server not built: {launcher}")
        healthy = False

    if paths.skill_source(root).is_file():
        ok("Skill present")
    else:
        bad("Skill missing")
        healthy = False

    heading("Project")
    project = find_project(Path.cwd().resolve())
    if project is None:
        note("Not inside a Unity project, so project checks were skipped.")
    else:
        ok(f"Unity project: {project}")

        manifest = project / "Packages" / "manifest.json"
        deps = json.loads(manifest.read_text()).get("dependencies", {}) if manifest.is_file() else {}
        if paths.PACKAGE_ID in deps:
            ok("Package is in Packages/manifest.json")
        else:
            bad("Package is not installed. Run: proving-ground setup")
            healthy = False

        for name, config in (
            ("claude", project / ".mcp.json"),
            ("codex", project / ".codex" / "config.toml"),
            ("pi", project / ".pi" / "mcp.json"),
        ):
            if config.is_file() and paths.SERVER_NAME in config.read_text():
                ok(f"{name}: MCP server registered")

    heading("Bridge")
    url = os.environ.get("PROVING_GROUND_URL", "http://127.0.0.1:8787")
    try:
        import httpx

        response = httpx.get(f"{url}/health", timeout=5)
        ok(f"Editor reachable at {url}")
        print(f"       {DIM}{response.text.strip()}{OFF}")
    except Exception:
        note(f"No Editor at {url}. Open Unity and enable Tools > Proving Ground > Agent Bridge.")

    print()
    return 0 if healthy else 1


def cmd_update(args: argparse.Namespace) -> int:
    if args.check:
        # Ask the network directly rather than trusting the daily cache: someone who
        # typed this wants the current answer.
        latest = updates.available(force=True)
        current = paths.version()
        if latest:
            print(f"Proving Ground {current} -> {latest} available. Run: proving-ground update")
            return 0
        print(f"Proving Ground {current} is up to date.")
        return 0

    script = paths.installed_root() / "install.sh"
    installer = str(script) if script.is_file() else None

    forwarded = []
    if args.force:
        forwarded.append("--force")
    if args.version:
        forwarded += ["--version", args.version]

    if installer:
        command = ["sh", installer, *forwarded]
    else:
        url = os.environ.get(
            "PG_INSTALL_URL", f"https://raw.githubusercontent.com/{paths.REPO}/main/install.sh"
        )
        command = ["sh", "-c", f"curl -fsSL {url} | sh -s -- {' '.join(forwarded)}"]

    env = dict(os.environ, PG_CURRENT_VERSION=paths.version())
    return subprocess.run(command, env=env).returncode


# --- bridge passthrough --------------------------------------------------------------


def bridge_call(method: str, **arguments) -> str:
    import httpx

    url = os.environ.get("PROVING_GROUND_URL", "http://127.0.0.1:8787")
    payload = {"method": method, "args": {k: v for k, v in arguments.items() if v is not None}}

    try:
        response = httpx.post(f"{url}/call", json=payload, timeout=900)
    except Exception:
        print(
            f"No Editor at {url}.\n"
            "Open the Unity project and enable Tools > Proving Ground > Agent Bridge > Enable.",
            file=sys.stderr,
        )
        raise SystemExit(2)

    if response.status_code >= 400:
        print(f"{method} failed: {response.text}", file=sys.stderr)
        raise SystemExit(1)
    return response.text


def render_report(raw: str) -> int:
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        print(raw)
        return 0

    if not isinstance(data, dict) or "findings" not in data:
        print(json.dumps(data, indent=2) if isinstance(data, (dict, list)) else raw)
        return 0

    print(data.get("summary") or data.get("tool", ""))
    if not data.get("ok", True):
        print(f"  could not run: {data.get('error')}")
        return 1

    rank = {"Blocker": 3, "Fail": 2, "Warn": 1, "Info": 0}
    for finding in sorted(data["findings"], key=lambda f: rank.get(f.get("severity"), 0), reverse=True):
        print(f"  [{finding.get('severity')}] {finding.get('id')}: {finding.get('message')}")
        if finding.get("subject"):
            print(f"      at {finding['subject']}")
        if finding.get("expected") or finding.get("actual"):
            print(f"      expected {finding.get('expected')}, got {finding.get('actual')}")
        if finding.get("remedy"):
            print(f"      {finding['remedy']}")

    return 0 if data.get("passed", True) else 1


CHECKS = {
    "project": "CheckProject",
    "content": "CheckContent",
    "audioassets": "CheckAudioAssets",
    "audio": "CheckAudio",
    "scene": "CheckScene",
    "ui": "CheckUi",
    "all": "CheckAll",
}


def cmd_check(args: argparse.Namespace) -> int:
    method = CHECKS.get(args.what)
    if method is None:
        print(f"Unknown check '{args.what}'. Choose from: {', '.join(CHECKS)}.", file=sys.stderr)
        return 2
    return render_report(bridge_call(method))


def cmd_init(args: argparse.Namespace) -> int:
    return render_report(bridge_call("Init", genre=args.genre))


def cmd_gate(args: argparse.Namespace) -> int:
    return render_report(bridge_call("Gate"))


def cmd_milestone(args: argparse.Namespace) -> int:
    return render_report(bridge_call("Milestone", milestoneId=args.id))


def cmd_digest(args: argparse.Namespace) -> int:
    print(bridge_call("Digest"))
    return 0


def cmd_norms(args: argparse.Namespace) -> int:
    print(bridge_call("Norms", genre=args.genre))
    return 0


def cmd_console(args: argparse.Namespace) -> int:
    print(bridge_call("Console", minSeverity=args.severity or None))
    return 0


def cmd_survey(args: argparse.Namespace) -> int:
    return render_report(bridge_call("Survey"))


def cmd_play(args: argparse.Namespace) -> int:
    return render_report(bridge_call("ExitPlayMode" if args.off else "EnterPlayMode"))


def _wait_for_run(timeout: int) -> int:
    """Polls until the run finishes, then prints its report."""
    import time

    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(1.0)
        status = json.loads(bridge_call("RunStatus"))
        if status.get("busy"):
            continue
        report = status.get("lastReport")
        if not report:
            print("Run finished with no report.")
            return 1
        return render_report(json.dumps(report))

    print(f"Still running after {timeout}s. Check again with: proving-ground status", file=sys.stderr)
    return 1


def cmd_probe(args: argparse.Namespace) -> int:
    started = bridge_call("RunProbe", seconds=args.seconds, seed=args.seed)
    if '"state":"running"' not in started:
        return render_report(started)
    print(f"Probing for {args.seconds:g}s...")
    return _wait_for_run(int(args.seconds) + 120)


def cmd_scenario(args: argparse.Namespace) -> int:
    if not args.name:
        print(bridge_call("Scenarios"))
        return 0

    started = bridge_call("RunScenario", name=args.name)
    if '"state":"running"' not in started:
        return render_report(started)
    print(f"Running '{args.name}'...")
    return _wait_for_run(args.timeout)


def cmd_status(args: argparse.Namespace) -> int:
    status = json.loads(bridge_call("RunStatus"))
    print(f"playing={status.get('isPlaying')} compiling={status.get('isCompiling')} "
          f"busy={status.get('busy')}")
    report = status.get("lastReport")
    return render_report(json.dumps(report)) if report else 0


def cmd_baseline(args: argparse.Namespace) -> int:
    return render_report(bridge_call("CaptureBaseline", overwrite=args.overwrite))


def cmd_view(args: argparse.Namespace) -> int:
    print(bridge_call("View"))
    return 0


def cmd_events(args: argparse.Namespace) -> int:
    print(bridge_call("Events"))
    return 0


def cmd_call(args: argparse.Namespace) -> int:
    """Escape hatch for any PgApi method the CLI does not wrap."""
    try:
        arguments = json.loads(args.args) if args.args else {}
    except json.JSONDecodeError as e:
        print(f"args must be JSON: {e}", file=sys.stderr)
        return 2
    return render_report(bridge_call(args.method, **arguments))


def cmd_version(args: argparse.Namespace) -> int:
    print(paths.version())
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        prog="proving-ground",
        description="Set up and drive the Proving Ground Unity plugin.",
    )
    sub = parser.add_subparsers(dest="command")

    p = sub.add_parser("setup", help="Wire the plugin into the game project you are standing in.")
    p.add_argument("--project", help="Use this project instead of the working directory.")
    p.add_argument("--harness", help=f"Comma separated: {', '.join(HARNESSES)}.")
    p.add_argument("--yes", action="store_true", help="Accept defaults, never prompt.")
    p.set_defaults(func=cmd_setup)

    p = sub.add_parser("doctor", help="Check the install, the project wiring and the bridge.")
    p.set_defaults(func=cmd_doctor)

    p = sub.add_parser("update", help="Install the latest release.")
    p.add_argument("--check", action="store_true", help="Only report whether an update exists.")
    p.add_argument("--force", action="store_true", help="Reinstall the current version.")
    p.add_argument("--version", help="Install a specific release tag.")
    p.set_defaults(func=cmd_update)

    p = sub.add_parser("init", help="Create contracts and folders in the project.")
    p.add_argument("genre", nargs="?", default="fps")
    p.set_defaults(func=cmd_init)

    p = sub.add_parser("check", help="Run a verification check.")
    p.add_argument("what", nargs="?", default="all", choices=sorted(CHECKS))
    p.set_defaults(func=cmd_check)

    p = sub.add_parser("gate", help="One pass/fail verdict across every report.")
    p.set_defaults(func=cmd_gate)

    p = sub.add_parser("milestone", help="Production readiness for a milestone.")
    p.add_argument("id")
    p.set_defaults(func=cmd_milestone)

    p = sub.add_parser("digest", help="Symbolic snapshot of the open scene.")
    p.set_defaults(func=cmd_digest)

    p = sub.add_parser("norms", help="Measured feel constants for a genre.")
    p.add_argument("genre")
    p.set_defaults(func=cmd_norms)

    p = sub.add_parser("console", help="Read the Unity console.")
    p.add_argument("severity", nargs="?", help="warning or error")
    p.set_defaults(func=cmd_console)

    p = sub.add_parser("survey", help="Describe an existing project.")
    p.set_defaults(func=cmd_survey)

    p = sub.add_parser("play", help="Enter play mode, or leave it with --off.")
    p.add_argument("--off", action="store_true")
    p.set_defaults(func=cmd_play)

    p = sub.add_parser("probe", help="Turn the probe bot loose on the running game.")
    p.add_argument("seconds", nargs="?", type=float, default=60)
    p.add_argument("--seed", type=int, default=12345)
    p.set_defaults(func=cmd_probe)

    p = sub.add_parser("scenario", help="Run a scenario, or list them when given no name.")
    p.add_argument("name", nargs="?")
    p.add_argument("--timeout", type=int, default=300)
    p.set_defaults(func=cmd_scenario)

    p = sub.add_parser("status", help="Whether a run is in progress, and the last report.")
    p.set_defaults(func=cmd_status)

    p = sub.add_parser("baseline", help="Write contracts describing how the game behaves today.")
    p.add_argument("--overwrite", action="store_true")
    p.set_defaults(func=cmd_baseline)

    p = sub.add_parser("view", help="What the camera can see, as symbols.")
    p.set_defaults(func=cmd_view)

    p = sub.add_parser("events", help="The event timeline from the last run.")
    p.set_defaults(func=cmd_events)

    p = sub.add_parser("call", help="Call any PgApi method directly.")
    p.add_argument("method")
    p.add_argument("args", nargs="?", help='JSON object, e.g. \'{"name":"arena"}\'')
    p.set_defaults(func=cmd_call)

    p = sub.add_parser("version", help="Print the installed version.")
    p.set_defaults(func=cmd_version)

    args = parser.parse_args()
    if not args.command:
        parser.print_help()
        return 0

    code = args.func(args)

    # After the work, never before it, and never for the commands that would make it
    # noise or a contradiction.
    if args.command not in ("update", "version") and sys.stdout.isatty():
        latest = updates.available()
        if latest:
            print(updates.banner(latest, colour=bool(BOLD)), file=sys.stderr)

    return code


if __name__ == "__main__":
    sys.exit(main())
