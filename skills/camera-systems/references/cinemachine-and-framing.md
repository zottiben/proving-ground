# Cinemachine and framing - depth for `camera-systems`

## 1. The Cinemachine 3 component map

Cinemachine 3 renamed most of what people remember from version 2. The namespace is
`Unity.Cinemachine`, and the virtual camera is `CinemachineCamera` rather than
`CinemachineVirtualCamera`. If a snippet references `Cinemachine.CinemachineVirtualCamera`
it is version 2 and the API will not resolve.

| Job | Component |
|---|---|
| The shot itself | `CinemachineCamera` |
| Drives the real Camera, blends between shots | `CinemachineBrain` |
| Follow a target at an offset | `CinemachineFollow` |
| Orbit a target, driven by look input | `CinemachineOrbitalFollow` |
| Keep the target framed as it moves | `CinemachineRotationComposer` (3D), `CinemachinePositionComposer` (2D and framing) |
| Pull in when geometry blocks the shot | `CinemachineDeoccluder` |
| Keep the camera inside a volume or area | `CinemachineConfiner3D`, `CinemachineConfiner2D` |
| Procedural shake channel | `CinemachineBasicMultiChannelPerlin` |
| Event-driven shake with falloff | `CinemachineImpulseSource` plus `CinemachineImpulseListener` |
| Bind look axes to the Input System | `CinemachineInputAxisController` |
| Frame several targets | `CinemachineTargetGroup` as the follow or look-at target |

Cameras are selected by **priority**: the highest-priority active `CinemachineCamera`
wins, and the brain blends to it over the blend time configured on the brain or in a
custom blend asset. That is the whole mechanism - switching cameras is enabling one or
raising its priority, not moving a transform.

## 2. Blending, and when it goes wrong

Two blends worth setting deliberately:

- **Cut** for anything the player caused instantly - a respawn, a teleport, a menu.
  Blending across a teleport sweeps the camera through the level and shows everything
  in between.
- **Ease in-out, 0.3-1.0 s**, for anything narrative or environmental.

A blend that looks like a bug is usually one of: two cameras at equal priority
fighting, a blend long enough that the player has already moved, or a blend from a
camera whose target was destroyed. The brain will happily blend from a shot that no
longer makes sense.

## 3. Impulse, the better shake for anything with a source

`CinemachineBasicMultiChannelPerlin` is continuous noise you scale. Impulse is
event-based, and it does two things the Perlin channel cannot: it falls off with
distance from the source, and it propagates with a delay, so an explosion across the
level shakes less and later than one at your feet.

```csharp
using Unity.Cinemachine;

[SerializeField] CinemachineImpulseSource _source;   // on the thing that generates the impact
public void OnImpact(float force) => _source.GenerateImpulse(force);
// The camera needs a CinemachineImpulseListener for any of this to arrive.
```

Use Perlin for sustained states - a vehicle idling, a low-health tremor, a handheld
feel - and impulse for events. Wiring an event to the Perlin gain works, but you end
up rebuilding trauma decay by hand, which is what `game-feel` describes.

## 4. Hand-rolled third-person orbit, complete

When Cinemachine is not in the project, this is the shape that works. The pivot owns
rotation; the camera owns distance; nothing accumulates.

```csharp
public class OrbitRig : MonoBehaviour {
    [SerializeField] Transform _target;
    [SerializeField] float _height = 1.6f, _restDistance = 4.5f, _minDistance = 0.8f;
    [SerializeField] float _probeRadius = 0.25f, _pitchMin = -35f, _pitchMax = 70f;
    [SerializeField] LayerMask _occluders;
    [SerializeField] float _followSmooth = 0.08f;

    float _yaw, _pitch = 15f, _distance, _distanceVel;
    Vector3 _pivotVel;

    public void Look(Vector2 delta) {                 // fed by input-systems, already scaled
        _yaw += delta.x;
        _pitch = Mathf.Clamp(_pitch - delta.y, _pitchMin, _pitchMax);
    }

    void LateUpdate() {
        var pivot = Vector3.SmoothDamp(_pivotPosition, _target.position + Vector3.up * _height,
                                       ref _pivotVel, _followSmooth);
        _pivotPosition = pivot;

        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        var back = rotation * Vector3.back;

        float desired = _restDistance;
        if (Physics.SphereCast(pivot, _probeRadius, back, out var hit, _restDistance,
                               _occluders, QueryTriggerInteraction.Ignore))
            desired = Mathf.Max(_minDistance, hit.distance - _probeRadius);

        _distance = desired < _distance
            ? desired
            : Mathf.SmoothDamp(_distance, desired, ref _distanceVel, 0.25f);

        transform.SetPositionAndRotation(pivot + back * _distance, rotation);
    }

    Vector3 _pivotPosition;
}
```

Note `SetPositionAndRotation` rather than two assignments: one transform write instead
of two, which matters because this runs every frame on the one object every frame is
waiting for.

## 5. Framing several targets

A `CinemachineTargetGroup` weights members and produces a bounding sphere the camera
frames. Hand-rolled, the same idea:

```csharp
// Centre on the weighted average, then pull back until every target is inside the frustum.
Bounds b = new(targets[0].position, Vector3.zero);
foreach (var t in targets) b.Encapsulate(t.position);

float radius = b.extents.magnitude + _padding;
float vertical = radius / Mathf.Sin(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
float horizontal = radius / Mathf.Sin(Camera.VerticalToHorizontalFieldOfView(
    _camera.fieldOfView, _camera.aspect) * 0.5f * Mathf.Deg2Rad);
float distance = Mathf.Max(vertical, horizontal);
```

Clamp the result. An unclamped group camera pulled to fit a target that ran off across
the level ends up so far back that nothing is legible, and the usual answer is a
maximum distance plus dropping members beyond it from the group.

## 6. Split-screen

Two cameras with `rect` set to half the viewport each. The costs people forget:

- **Everything renders twice.** Draw calls, culling, post-processing, shadows. Budget
  for it before committing to the feature, not after.
- **Post-processing volumes are per-camera** in URP, so each camera needs its own
  volume setup or they share settings that only suit one.
- **UI needs to be per-player.** A single screen-space overlay canvas spans both
  viewports. Screen Space - Camera, one canvas per player camera, is the usual answer.
- **Audio has one listener.** Two `AudioListener`s is an error. Put the listener at the
  midpoint, or on the player who most needs positional accuracy, and accept the
  compromise deliberately.

## 7. Diagnosing camera jitter

Work down this list; it is ordered by how often each is the cause.

1. **Following in `Update`** instead of `LateUpdate`.
2. **A physics target without interpolation.** Set `Rigidbody.interpolation` to
   Interpolate. This is the top cause of "the camera stutters but only when moving".
3. **Two systems writing the transform.** A hand-rolled follow plus a `CinemachineBrain`
   on the same camera, or a follow script left enabled next to a parent constraint.
4. **`Time.timeScale` changes** without unscaled smoothing, so the camera lurches on
   every hitstop.
5. **VSync off with an uncapped frame rate**, which produces tearing that reads as
   jitter and is not a camera bug at all.
6. **Smoothing so tight it oscillates.** A `smoothTime` under about 0.03 s is
   indistinguishable from no smoothing and amplifies any noise in the target.
