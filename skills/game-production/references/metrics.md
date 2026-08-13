# Real-world metrics - depth for `game-production`

Unity's convention is **1 unit = 1 metre**. Everything below is in metres and seconds.
Wrong scale is the single most common reason a space "feels off" while every
individual asset looks fine, and it is invisible until a human walks it.

Put these in one place - a `ScriptableObject`, a static config class, or the feel
contract - and reference them everywhere. Never hardcode a dimension twice.

## Character

| | value |
|---|---|
| height | 1.8 m |
| eye / camera height | 1.5-1.65 m (deliberately below anatomical eye level) |
| collision radius | 0.3 m (0.6 m wide) |
| crouch height | 0.9-1.0 m |
| step offset | 0.3-0.45 m |
| slope limit | 45 degrees |
| walk speed | 3 m/s |
| run / sprint | 6 m/s |
| jump apex | 1.0-1.5 m |

A `CharacterController` with `height` 1.8 and `radius` 0.3 sits its pivot at the
centre by default, so the camera child goes at roughly +0.65 to +0.75 local, not at
+1.6.

## Architecture

| | value |
|---|---|
| floor to floor | 3.0-4.0 m |
| wall thickness | 0.2 m |
| door | 1.1-1.4 m wide, 2.1-2.3 m tall |
| stair step | 0.15 m rise, 0.30 m run |
| corridor, minimum / comfortable | 1.5 m / 2.0-2.4 m |
| ceiling, interior | 2.4-3.0 m |
| grid sizes | 0.5 m detail, 2 m standard, 4 m coarse |

A corridor sized to a human is claustrophobic in third person: the camera needs room
behind the character, so widen to 2.4-3 m wherever the camera has to follow.

## Cover, which must be visually unambiguous

low 0.6-0.8 m (hides a crouched character) - medium 1.0-1.2 m - high 1.5-1.8 m (hides
a standing one) - vaultable 0.9-1.1 m.

Ambiguity here is a bug, not a style. A 1.35 m block that neither hides you nor lets
you vault teaches the player that cover cannot be read at a glance, and they stop
trying.

## Distances

| | value |
|---|---|
| close engagement | up to 3 m |
| medium | up to 10 m |
| long | 20-25 m |
| landmark legibility | readable at 200-500 m, so give landmarks unique silhouettes |
| interaction prompt range | 2-3 m |

## Camera

- First person: 60-75 degrees vertical-equivalent FOV; Unity's `Camera.fieldOfView` is
  vertical, so a "90 degree" figure quoted from a shooter is usually horizontal and
  translates to roughly 59 degrees vertical at 16:9.
- Third person action: camera roughly 3.5-5 m back and 1.5-2 m up, aimed slightly
  above the character's centre.
- Add 5-10 degrees of FOV at sprint, eased in and out, and take it away again at rest.
  It is the cheapest speed cue there is.
- Damp with `1 - exp(-k * dt)` (k around 4-8) or `Vector3.SmoothDamp`, never a raw
  per-frame `Lerp(a, b, 0.1f)` - that constant is per frame and changes with frame rate.

## Timing constants that decide feel

| | value |
|---|---|
| input to visible response | under 3 frames, and it is felt above that |
| input buffer window | 0.05-0.15 s |
| coyote time | 0.05-0.15 s (0 in competitive shooters, deliberately) |
| jump time to apex | 0.28-0.45 s |
| fall gravity multiplier | 1.4-2.5x the rise |
| hitstop, light to heavy | 0.04-0.15 s |
| dodge invulnerability | 0.2-0.5 s |

`pg_norms <genre>` carries these with their provenance and the reasoning attached, and
`pg_run_scenario` measures what your controller actually produces rather than what its
fields claim.

## Physics

- Default `Time.fixedDeltaTime` is 0.02 s (50 Hz). 0.0167 (60 Hz) is a common choice
  for anything action-oriented; going below 0.01 costs more than it returns for most
  games.
- Gravity in a game is almost never -9.81. Platformers commonly run -20 to -40 to keep
  the arc snappy at a readable jump height; derive it from your apex height and time to
  apex rather than picking it.
- Rigidbodies that the camera watches need `interpolation` set to Interpolate, or
  they visibly stutter between physics steps.

## Vehicles, if you have them

top speed around 50 m/s - acceleration 12-18 m/s squared - braking around 25 m/s
squared - wheelbase 2.7 m - steering lock 35 degrees at rest falling to around 8 at
top speed - body roll no more than 5-7 degrees, pitch 3-5 degrees under acceleration
and braking.

## Deriving instead of guessing

Two derivations are worth doing by hand every time:

```
gravity      = -2 * apexHeight / (timeToApex^2)
jumpVelocity =  2 * apexHeight /  timeToApex
```

Author the jump in the units a player perceives - "1.2 m high, 0.35 s to the top" -
and let the code derive gravity and impulse. Tuning a magic `jumpForce` against a
magic `gravityScale` is two unknowns fighting each other, and neither is the thing you
actually care about.

Then size the level from what falls out: at 6 m/s with a 0.35 s rise and a slightly
faster fall, a running jump clears roughly 3 m. Required gaps get 70% of that;
optional, skill-check gaps get up to 95%.
