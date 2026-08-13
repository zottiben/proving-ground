---
name: camera-systems
description: Build Unity game cameras that feel good - smooth follow, deadzones, look-ahead, bounds clamping, third-person orbit with occlusion handling, first-person look, and multi-target framing, with and without Cinemachine. Use when working on camera follow, camera smoothing, deadzone, look-ahead, third-person or orbit cameras, first-person look, Cinemachine setup, camera collision or clipping, or camera jitter and motion sickness.
---

# Camera systems

The camera is the player's window, and bad camera work makes a good game feel awful.
It is also the system where the failure mode is most often invisible to the person who
built it: jitter reads as "the game is cheap", clipping reads as "the game is broken",
and neither shows up in a screenshot.

Unity gives you two routes. **Cinemachine** is the default answer for anything beyond a
fixed camera: it is a Unity package, it handles blending, occlusion, noise, damping and
impulse, and hand-rolling those is a week you did not need to spend. Hand-rolled is
fine for a simple follow or when you need exact control, and the rules below apply to
both.

## When to use

- Use when a camera should follow the player smoothly, stay inside the level, lead the
  player's motion, or ignore small movements.
- Use when building third-person orbit with occlusion handling, first-person look, or
  framing several targets at once.
- Use to fix jitter, snapping, clipping through geometry, or motion sickness.

**When *not* to use:** for the trigger and magnitude of shake, use `game-feel` - this
skill owns where shake is applied, not when. For the look input itself - sensitivity,
inversion, acceleration - `input-systems`. For post-processing and FOV as a visual
effect, `unity-rendering`.

## Core workflow

1. **Decide what the camera serves.** Platformer: lead the jump, show the hazard.
   Top-down: centre with a deadzone. Third person: orbit plus occlusion. First person:
   look only. The genre sets the rules and they do not transfer.
2. **Follow in `LateUpdate`.** The camera must move after everything it follows has
   moved. Following in `Update` gives a one-frame lag that reads as jitter and that no
   amount of smoothing fixes.
3. **Smooth frame-rate independently.** `Vector3.SmoothDamp`, or
   `1 - Mathf.Exp(-k * Time.deltaTime)`. Never `Lerp(a, b, 0.1f)` per frame.
4. **Add a deadzone** so small target movements do not nudge the camera. This is the
   difference between a twitchy game and a nauseating one.
5. **Lead the action** by offsetting the follow target in the direction of travel,
   eased in and out so it does not whip on every direction change.
6. **Clamp to bounds** so the camera never shows outside the playable space, and clamp
   the *view*, not the camera position - account for the frustum.
7. **In 3D, separate look from occlusion.** Orbit a pivot; pull the camera in when
   geometry blocks it; clamp pitch.
8. **Verify at both low and high frame rates**, into corners and walls, and at the
   level edges.

## Patterns

### 1. Cinemachine, which is the answer more often than not

```csharp
// Cinemachine 3.x: namespace Unity.Cinemachine, and the virtual camera type is
// CinemachineCamera. A CinemachineBrain on the Camera drives it and blends between shots.
using Unity.Cinemachine;

// Third person, in components rather than code:
//   CinemachineCamera
//     + CinemachineOrbitalFollow      orbit rig, driven by look input
//     + CinemachineRotationComposer   keeps the target framed as it moves
//     + CinemachineDeoccluder         pulls in when geometry blocks the shot
//     + CinemachineBasicMultiChannelPerlin  the shake channel game-feel writes to
//     + CinemachineInputAxisController      binds look axes to the Input System
```

Reach for code only for what the components do not cover: switching cameras by
priority, adjusting damping in response to game state, or driving a custom target.

### 2. Frame-rate-independent follow, hand-rolled

```csharp
// RIGHT: SmoothDamp is a critically damped spring with built-in frame-rate correction,
// and it exposes the one parameter that means something to a designer - time to arrive.
public class FollowCamera : MonoBehaviour {
    [SerializeField] Transform _target;
    [SerializeField] Vector3 _offset = new(0f, 2f, -5f);
    [SerializeField] float _smoothTime = 0.15f;
    Vector3 _velocity;

    void LateUpdate() {                                   // LateUpdate, not Update
        transform.position = Vector3.SmoothDamp(
            transform.position, _target.position + _offset, ref _velocity, _smoothTime);
    }
}
// WRONG: transform.position = Vector3.Lerp(transform.position, goal, 0.1f);
//        0.1 is per frame, so the camera is floatier at 30 fps than at 144.
```

### 3. Deadzone plus look-ahead

```csharp
// The focus only moves once the target leaves the deadzone box, then aims ahead of travel.
Vector3 FocusFor(Vector3 target, Vector3 velocity) {
    var to = target - _focus;
    _focus.x += Mathf.Max(0f, Mathf.Abs(to.x) - _deadzone.x) * Mathf.Sign(to.x);
    _focus.z += Mathf.Max(0f, Mathf.Abs(to.z) - _deadzone.y) * Mathf.Sign(to.z);
    var lead = Vector3.ClampMagnitude(velocity, 1f) * _lookAhead;
    return _focus + lead;
}
```

Look-ahead should ease in over roughly 0.2-0.4 s and ease out faster than it eases in.
Instant look-ahead whips the camera on every direction change and is worse than none.

### 4. First-person look, with the mistake everyone makes

```csharp
// Mouse delta is ALREADY a per-frame displacement. Multiplying by deltaTime makes
// sensitivity frame-rate dependent - the single most common camera bug in Unity.
void ApplyLook(Vector2 mouseDelta, Vector2 stickInput) {
    _yaw   += mouseDelta.x * _mouseSensitivity;                       // no deltaTime
    _pitch -= mouseDelta.y * _mouseSensitivity;

    _yaw   += stickInput.x * _stickDegreesPerSecond * Time.deltaTime; // stick IS a rate
    _pitch -= stickInput.y * _stickDegreesPerSecond * Time.deltaTime;

    _pitch = Mathf.Clamp(_pitch, -85f, 85f);                          // or it flips over the top
    transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);     // never accumulate into a Quaternion
}
```

Rotate the body by yaw and the camera by pitch, not both on one transform: pitching
the body tilts the character's movement plane and the collider with it.

### 5. Occlusion without a spring arm

```csharp
// Cast from the pivot to the desired camera position and sit just short of the hit.
// Pull in instantly; ease back out, or the camera pumps every time it grazes a corner.
float desired = _restDistance;
if (Physics.SphereCast(_pivot.position, _probeRadius, -_pivot.forward,
                       out var hit, _restDistance, _occluderMask, QueryTriggerInteraction.Ignore))
    desired = Mathf.Max(_minDistance, hit.distance - _probeRadius);

_distance = desired < _distance
    ? desired                                                    // snap in
    : Mathf.SmoothDamp(_distance, desired, ref _distanceVel, 0.25f);  // ease out
```

A `SphereCast` rather than a `Raycast`, because a ray finds the one gap between two
crates and puts the camera inside a wall.

## Pitfalls

- **`Lerp` with a constant per frame.** Frame-rate dependent. Use `SmoothDamp` or the
  exponential form.
- **Following in `Update`.** One-frame lag against a target that has already moved, and
  it looks exactly like jitter. `LateUpdate`, or let Cinemachine's brain order it.
- **Physics-driven target, camera in `LateUpdate`.** If the followed body moves in
  `FixedUpdate`, set its `Rigidbody.interpolation` to Interpolate or the camera
  faithfully reproduces the physics step as stutter.
- **`deltaTime` on mouse delta.** Sensitivity that changes with frame rate. Mouse
  deltas are displacements; sticks are rates.
- **Unclamped pitch.** The camera rolls over the top and the player is upside down.
  Clamp to about 85 degrees.
- **Accumulating rotation into a quaternion** (`transform.Rotate` every frame) drifts
  and gains roll. Keep yaw and pitch as floats and build the rotation from them.
- **`Raycast` for occlusion.** Finds gaps a camera cannot fit through. `SphereCast`.
- **Easing in as slowly as easing out.** Occlusion recovery should be gradual; the pull
  in must be instant, or the camera spends a frame inside the wall.
- **No bounds clamp.** The camera shows past the level edge. Clamp the frustum, not the
  position.
- **Snapping on respawn.** A large `SmoothDamp` distance whips across the level. Cut
  deliberately and reset the smoothing velocity.
- **Shake driving the follow target.** Shake fights follow. Compose: follow first,
  shake last, as an additive offset or a Cinemachine noise channel.
- **Not caching `Camera.main`.** It is a lookup, and it is called from more places than
  anyone expects. Cache it once.

## Prove it with Proving Ground

`pg_view` is the tool that makes camera work checkable without eyes: it returns what
the camera can currently see as symbols, with screen rects, distances and occlusion,
plus what a ray through the screen centre hits. That is the direct answer to "is the
objective visible from the spawn", "is the player centred", and "is the camera inside
geometry".

Camera terms live in `feel.json` and are measured during a run:

```jsonc
{ "metrics": {
    "camera.followLag":          { "min": 0.05, "max": 0.2, "unit": "s" },
    "camera.collisionRecovery":  { "max": 0.3, "unit": "s" },
    "camera.turnRate":           { "min": 140, "max": 500, "unit": "deg/s" }
}}
```

- `pg_run_scenario` with `look` steps to drive the camera and diff those metrics.
- `pg_run_probe` walks the level into corners, which is where clipping lives.
- `pg_capture` when the question is framing and composition rather than geometry.

## References

- `references/cinemachine-and-framing.md` - the Cinemachine 3 component map, blending
  and priority, confiners, impulse, multi-target and split-screen framing, and the
  full hand-rolled orbit rig.

## Related skills

- `game-feel` - owns shake triggers and magnitude; this skill owns where it lands.
- `input-systems` - look input, sensitivity, inversion and accessibility.
- `physics-tuning` - interpolation, and why a physics-driven follow target stutters.
- `level-design` - corridor widths and sightlines are camera constraints first.
- `performance-optimization` - the cost of extra cameras, render textures and split-screen.
