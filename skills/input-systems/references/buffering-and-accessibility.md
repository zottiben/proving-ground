# Buffering, latency and accessibility - depth for `input-systems`

## 1. The latency budget, frame by frame

Between a finger moving and a pixel changing, the frames stack up. At 60 fps each is
16.7 ms, and players reliably feel the difference between three frames and six.

| Stage | Typical cost | Can you control it |
|---|---|---|
| Device to OS | 1-8 ms | no (a wireless pad is worse than wired) |
| OS to Unity's input update | up to 1 frame | partly - the Input System's update mode |
| Your read, in `Update` | same frame | yes |
| Applied in `FixedUpdate` | 0-1 physics step | yes - latch in `Update` |
| Rendered | 1 frame | no |
| Display, plus buffering | 1-3 frames | no, but do not add to it |

What you actually control: not reading input a frame late, not deferring the response
by a step you did not need, and not adding animation lead-in before the character
moves. A wind-up animation that plays *before* the character responds converts a
design choice into latency, and players read it as lag rather than as weight.

The fix that costs nothing: move first, animate over the top. The character's velocity
changes on frame 0; the animation catches up on frames 1-6.

## 2. Buffer and grace windows beyond jumping

Buffering generalises to every action with a legality condition. Same shape every
time: record the request, honour it briefly.

| Action | Window | Why |
|---|---|---|
| Jump | 0.05-0.15 s | pressed just before landing |
| Attack chain | 0.15-0.30 s | pressed during the previous swing's recovery |
| Dodge or roll | 0.10-0.20 s | pressed during a hit reaction |
| Interact | 0.10-0.20 s | pressed just before entering range |
| Menu confirm | 0.10 s | pressed during a transition |
| Coyote time | 0.05-0.15 s | left the ledge a few frames ago |
| Landing grace | 0.05-0.10 s | tolerate a frame of non-grounded on a slope |

Under 0.05 s players report the game as unfair without being able to say why. Over
about 0.2 s the game starts acting on intentions the player has abandoned, which feels
possessed rather than forgiving. The band in between is genuinely invisible: nobody has
ever noticed a buffer working.

A buffer needs three things to be correct: a timestamp, a consumption on success, and
an expiry. Miss the consumption and one press fires twice; miss the expiry and a press
from a minute ago fires when conditions finally allow.

## 3. Input during animations

The default behaviour of most animation-driven controllers is to swallow input for the
duration of a clip, and it is the largest single source of "unresponsive" complaints.

Three levels of fix, in increasing order of quality:

1. **Buffer through it.** The press is remembered and fires when the animation ends.
   Cheap, and removes most of the complaint.
2. **Cancel windows.** After a defined point in the animation, a new input interrupts
   it. The cancel point is a tuning value, not a constant, and it belongs in the feel
   contract.
3. **Layered response.** The character responds immediately on one animation layer
   while the previous action finishes on another. Most expensive, and the reason
   high-end action games feel the way they do.

The metric that catches this is `combat.attackCommit` - how long an attack locks out
other actions. It, more than damage numbers, decides whether combat reads as weighty
or as unresponsive.

## 4. Deadzones

A stick at rest does not report zero, and how much it lies varies by device and by
wear. Options, in order of how they feel:

- **Radial deadzone** - ignore any input whose magnitude is below a threshold. Correct
  for most 3D movement, because it treats the stick as a direction.
- **Axial deadzone** - per-axis. Cheap, and it produces the classic "movement snaps to
  the cardinal directions near the centre" feel. Avoid for movement; acceptable for
  menu navigation, where snapping is what you want.
- **Scaled radial** - remap the remaining range to 0-1 after the deadzone, so the first
  usable input is not a jump from nothing to 30% speed. This is the one that feels
  right.

The Input System's stick deadzone processor implements the scaled form, with min and
max settings. Expose the minimum in the options menu: a worn controller needs a bigger
deadzone than a new one, and the player is the only one who knows which they have.

## 5. Accessibility, which is expected rather than optional

These are table stakes, and every one is small:

- **Full remapping**, including of every device, with the ability to reset to default.
- **Hold-versus-toggle** for crouch, aim, sprint and any other held input. Holding a
  button for minutes is genuinely painful for some players.
- **Sensitivity per axis**, plus inversion per axis, for both stick and mouse.
- **No mandatory rapid presses.** If a mash exists, offer a hold alternative.
- **No mandatory simultaneous inputs** that need two hands on one side of a controller.
- **Adjustable or disabled quick-time timing windows.**
- **Deadzone adjustment**, as above.
- **Prompt style override**, so a player can pick the glyph set rather than relying on
  detection.

Prompts and glyphs are a UI concern, and `game-ui-ux` covers the legibility half; a
44 px minimum hit target and a legibility floor apply to the rebinding screen exactly
as much as to the HUD.

## 6. Local multiplayer

`PlayerInputManager` handles join-on-press and device assignment, which is most of the
work. What it does not handle, and you have to:

- **Device ownership.** Two players on one keyboard need distinct control schemes, and
  the asset has to define them.
- **A join UI**, because "press any button to join" is not discoverable on its own.
- **Rebinding per player**, since overrides are per-asset by default and a shared asset
  means one player's remap changes everyone's.
- **A leave path** that does not strand the player's character in the level.

## 7. Diagnosing "input does nothing"

In the order these are actually the cause:

1. **Active Input Handling** is set to the old backend. `pg_check project`.
2. **The action map was never enabled.**
3. **The action asset reference is null**, or points at a different asset than the one
   being edited - two copies of an asset with the same name is a real and confusing
   thing that happens.
4. **The binding is on a device that is not present**, so a keyboard-only build with
   gamepad-only bindings reads zero.
5. **Another map is consuming it** - the UI map is enabled at the same time as Player.
6. **`Time.timeScale` is zero** and the response is gated on scaled time. Input arrives;
   nothing acts on it.
7. **The object reading it is disabled**, so `Update` never runs.

`pg_console` after a run catches the exceptions; the rest of the list is silent, which
is why the order matters.
