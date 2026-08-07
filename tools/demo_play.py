#!/usr/bin/env python3
"""Second half of the proof: does the generated FPS actually play?

Enters play mode in the scene the recipe built, turns the probe bot loose, and reports
what it measured. Nothing here is a mock - the probe drives the real controller through
synthetic device input.

Usage: python3 tools/demo_play.py [bridge-url]
"""

import json
import sys
import time

import httpx

URL = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8787"


def call(method, **args):
    for _ in range(60):
        try:
            response = httpx.post(f"{URL}/call", json={"method": method, "args": args}, timeout=600)
        except httpx.ConnectError:
            time.sleep(1.0)
            continue
        if response.status_code >= 400:
            raise RuntimeError(f"{method}: {response.text}")
        return response.text
    raise RuntimeError(f"{method}: the Editor never came back")


def summarise(raw):
    data = json.loads(raw)
    if "findings" not in data:
        return raw
    lines = [data.get("summary", "")]
    rank = {"Blocker": 3, "Fail": 2, "Warn": 1, "Info": 0}
    for f in sorted(data["findings"], key=lambda f: rank.get(f.get("severity"), 0), reverse=True)[:10]:
        lines.append(f"    [{f.get('severity')}] {f.get('id')}: {f.get('message')}")
        if f.get("expected") or f.get("actual"):
            lines.append(f"        expected {f.get('expected')}, got {f.get('actual')}")
    for key, value in (data.get("data") or {}).items():
        if key.startswith("feel."):
            lines.append(f"    {key} = {value}")
    return "\n".join(lines)


def wait_idle(timeout=600):
    deadline = time.time() + timeout
    while time.time() < deadline:
        status = json.loads(call("RunStatus"))
        if not status.get("busy"):
            return status
        time.sleep(1.0)
    raise RuntimeError("the run never finished")


print("\n[1] Enter play mode")
call("EnterPlayMode")
for _ in range(120):
    if json.loads(call("RunStatus")).get("isPlaying"):
        break
    time.sleep(1.0)
else:
    raise SystemExit("Unity never entered play mode")
print("    playing")

print("\n[2] What the player can see")
print("    " + call("View", maxObjects=6).replace("\n", "\n    "))

print("\n[3] Turn the probe bot loose for 20s")
call("RunProbe", seconds=20, seed=99)
finished = wait_idle()
report = finished.get("lastReport")
print("    " + (summarise(json.dumps(report)) if report else "no report").replace("\n", "\n    "))

print("\n[4] Console during play")
print("    " + call("Console", minSeverity="warning", max=10).replace("\n", "\n    "))

print("\n[5] Leave play mode")
call("ExitPlayMode")
print("    done")
