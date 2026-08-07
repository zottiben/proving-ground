#!/usr/bin/env python3
"""End-to-end proof: build a playable greybox FPS through the agent bridge alone.

Runs the sequence an agent would run when told "build a greybox FPS, use Proving
Ground to verify at every step". Nothing here touches Unity except through the same
HTTP surface an MCP client uses.

Usage: python3 tools/demo_fps.py [bridge-url]
"""

import json
import sys
import time

import httpx

URL = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8787"

CONTROLLER = '''using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class FpsController : MonoBehaviour
{
    public float MoveSpeed = 6f;
    public float JumpHeight = 1.15f;
    public float TimeToApex = 0.35f;
    public float FallMultiplier = 1.8f;
    public float LookSensitivity = 0.12f;

    CharacterController _controller;
    Transform _eye;
    float _vertical;
    float _pitch;

    float Gravity => -2f * JumpHeight / (TimeToApex * TimeToApex);
    float JumpVelocity => 2f * JumpHeight / TimeToApex;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        var camera = GetComponentInChildren<Camera>();
        if (camera != null) _eye = camera.transform;
    }

    void Update()
    {
        var move = Vector2.zero;
        var look = Vector2.zero;
        var jump = false;

#if ENABLE_INPUT_SYSTEM
        var pad = Gamepad.current;
        if (pad != null)
        {
            move += pad.leftStick.ReadValue();
            look += pad.rightStick.ReadValue() * 4f;
            jump |= pad.buttonSouth.isPressed;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            jump |= keyboard.spaceKey.isPressed;
        }

        var mouse = Mouse.current;
        if (mouse != null) look += mouse.delta.ReadValue();
#endif

        if (_eye != null)
        {
            transform.Rotate(Vector3.up, look.x * LookSensitivity, Space.World);
            _pitch = Mathf.Clamp(_pitch - look.y * LookSensitivity, -89f, 89f);
            _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        if (_controller.isGrounded)
        {
            _vertical = -2f;
            if (jump) _vertical = JumpVelocity;
        }
        else
        {
            _vertical += Gravity * (_vertical < 0f ? FallMultiplier : 1f) * Time.deltaTime;
        }

        var motion = (transform.right * move.x + transform.forward * move.y) * MoveSpeed;
        motion.y = _vertical;
        _controller.Move(motion * Time.deltaTime);
    }
}
'''

ARENA = {
    "schema": "provingground/scene@1",
    "name": "arena",
    "note": "Greybox FPS arena. Generated, so it rebuilds identically.",
    "seed": 7,
    "ensureCamera": False,
    "objects": [
        {"id": "Floor", "primitive": "Cube", "position": [0, -0.5, 0],
         "scale": [60, 1, 60], "static": True, "material": "#6E7378"},
        {"id": "Wall", "primitive": "Cube", "scale": [60, 6, 1], "static": True,
         "material": "#4A4E54", "repeat": {"count": 4, "ring": 30}},
        {"id": "Pillar", "primitive": "Cylinder", "position": [0, 2, 0],
         "scale": [2, 2, 2], "static": True, "material": "#8A6F4E",
         "repeat": {"count": 6, "ring": 12}},
        {"id": "Crate", "primitive": "Cube", "position": [8, 0.5, 4],
         "static": True, "material": "#9C6B3C",
         "repeat": {"count": 9, "grid": [3, 2.2], "jitter": [0.3, 0, 0.3]}},
        {"id": "Objective", "primitive": "Sphere", "position": [-18, 1, -18],
         "material": "#E4C13A", "tag": "Respawn"},
        {"id": "Player", "position": [0, 1.2, -20], "tag": "Player",
         "components": [
             {"type": "CharacterController",
              "set": {"height": 1.8, "radius": 0.35, "stepOffset": 0.4, "slopeLimit": 50}},
             {"type": "FpsController", "set": {"MoveSpeed": 6.0, "JumpHeight": 1.15}}
         ],
         "children": [
             {"id": "Eye", "position": [0, 0.7, 0], "tag": "MainCamera",
              "components": [{"type": "Camera", "set": {"fieldOfView": 75}}]}
         ]}
    ]
}


def call(method, **args):
    response = httpx.post(f"{URL}/call", json={"method": method, "args": args}, timeout=600)
    if response.status_code >= 400:
        raise RuntimeError(f"{method}: {response.text}")
    return response.text


def report(raw):
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        return raw
    if not isinstance(data, dict) or "findings" not in data:
        return raw
    lines = [data.get("summary", "")]
    rank = {"Blocker": 3, "Fail": 2, "Warn": 1, "Info": 0}
    for f in sorted(data["findings"], key=lambda f: rank.get(f.get("severity"), 0), reverse=True)[:8]:
        lines.append(f"    [{f.get('severity')}] {f.get('id')}: {f.get('message')}")
    return "\n".join(lines)


def await_compile(timeout=300):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            status = json.loads(call("CompileStatus"))
        except Exception:
            time.sleep(1.0)
            continue
        if status.get("settled"):
            if status.get("hasErrors"):
                return "FAILED: " + "; ".join(status.get("errors", []))
            return "compiled cleanly"
        time.sleep(0.5)
    return "timed out"


def step(number, label, result):
    print(f"\n[{number}] {label}")
    print("    " + str(result).replace("\n", "\n    "))


step(1, "New empty scene", report(call("NewScene", empty=True)))
step(2, "Write the FPS controller", report(call("WriteScript",
                                                path="Assets/Game/FpsController.cs",
                                                contents=CONTROLLER)))
step(3, "Wait for compilation", await_compile())
step(4, "Build the arena from a recipe", report(call("WriteAndBuildScene",
                                                     recipeJson=json.dumps(ARENA))))
step(5, "Rebuild (idempotency check)", report(call("BuildScene", recipe="arena")))
step(6, "Save the scene", report(call("SaveScene", path="Assets/Scenes/Arena.unity")))
step(7, "Add it to the build settings", report(call("AddSceneToBuild",
                                                    scenePath="Assets/Scenes/Arena.unity")))
step(8, "Verify the level", report(call("CheckScene")))
step(9, "What is actually in the scene", call("Digest", maxNodes=25))
step(10, "Console", call("Console", minSeverity="warning", max=10))
