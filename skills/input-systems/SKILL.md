---
name: input-systems
description: Wire up responsive input in Unity - the Input System package, action maps and bindings, input buffering, coyote time, deadzones, rebinding, device switching, and the responsiveness budget that decides whether a game feels tight or sluggish. Use when setting up player input or controls, when input feels laggy or drops presses, when adding gamepad or rebinding support, or when a controller compiles perfectly and does nothing.
---

# Input systems

Input is where responsiveness is won or lost, and it is the one system whose defects
players describe in words that sound like something else. "Floaty" is often a missing
buffer. "Unfair" is often missing coyote time. "Unresponsive" is usually three frames
of avoidable latency, which nobody can name but everybody feels.

There is also one Unity-specific trap severe enough to lead with, because it costs
whole sessions: **an Input System package paired with the old input backend compiles
perfectly and responds to nothing.**

## The trap, first

Player Settings has **Active Input Handling** with three values: Input Manager (Old),
Input System Package (New), and Both. The `ENABLE_INPUT_SYSTEM` define is only set for
the last two. Everything inside `#if ENABLE_INPUT_SYSTEM` compiles to nothing
otherwise - no error, no warning, and a controller that builds cleanly and never moves.

`pg_check project` catches exactly this, which is why the discipline is to run it on a
project you have not verified before writing input code, not after wondering why the
character will not move. A project created by `proving-ground setup` is already set to
Both.

## When to use

- Use when setting up player input, action maps, or gamepad support.
- Use when input feels laggy, drops presses, or misses inputs during animations.
- Use to add buffering, coyote time, rebinding, or accessibility options.
- Use when nothing responds and the code looks correct.

**When *not* to use:** for what a press should *feel* like once it registers, use
`game-feel`. For look sensitivity as a camera concern, `camera-systems`. For the jump
arc itself, the feel contract and `physics-tuning`.

## Core workflow

1. **Verify the backend before writing input code.** `pg_check project`.
2. **Define actions, not keys.** An `InputActionAsset` with maps per context - Player,
   UI, Vehicle, Menu - and bindings per device. Code reads actions; only the asset
   knows about keys.
3. **Read in `Update`, act in `FixedUpdate`.** Polling a button in `FixedUpdate` misses
   presses, because a press can begin and end between two physics steps.
4. **Buffer intent.** Record when an action was requested and honour it for a short
   window. This is the difference between a game that punishes early presses and one
   that absorbs them.
5. **Forgive at the edges.** Coyote time after leaving a ledge, a grace window at the
   end of an animation, generous rather than exact ground checks.
6. **Switch device hints, not schemes.** Detect the last-used device and change the
   prompts. Do not reset bindings or lock the player into one device.
7. **Rebind everything, and ship it.** Rebinding is an accessibility requirement, not a
   feature.
8. **Measure the latency.** `input.moveLatency` is a contract term with a number, not
   an impression.

## Patterns

### 1. Actions read once, in one place

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputReader : MonoBehaviour {
    [SerializeField] InputActionAsset _actions;
    InputAction _move, _look, _jump;

    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }

    void Awake() {
        var map = _actions.FindActionMap("Player", throwIfNotFound: true);
        _move = map.FindAction("Move", true);
        _look = map.FindAction("Look", true);
        _jump = map.FindAction("Jump", true);
    }

    void OnEnable()  => _actions.FindActionMap("Player").Enable();   // actions do nothing until enabled
    void OnDisable() => _actions.FindActionMap("Player").Disable();

    void Update() {
        Move = _move.ReadValue<Vector2>();
        Look = _look.ReadValue<Vector2>();
        if (_jump.WasPressedThisFrame()) JumpBuffer.Press();          // record intent, do not act here
    }
}
```

An action that is never enabled silently returns zero forever, which is the second
most common "input does nothing" cause after the backend setting.

### 2. Buffering and coyote time, which are the same idea twice

```csharp
// Both are "remember something briefly": buffering remembers the press, coyote time
// remembers the ground. Together they absorb the human timing error either side of a jump.
public class JumpController : MonoBehaviour {
    [SerializeField] float _bufferWindow = 0.1f;   // 0.05-0.15s stays invisible
    [SerializeField] float _coyoteWindow = 0.1f;

    float _lastPressed = -99f, _lastGrounded = -99f;

    public void Press() => _lastPressed = Time.time;

    void Update() {
        if (_controller.isGrounded) _lastGrounded = Time.time;

        bool wants   = Time.time - _lastPressed  <= _bufferWindow;
        bool allowed = Time.time - _lastGrounded <= _coyoteWindow;

        if (wants && allowed) {
            Jump();
            _lastPressed = -99f;      // consume both, or one press fires twice
            _lastGrounded = -99f;
        }
    }
}
```

Consume both timestamps on success. A buffer that is not consumed produces a second
jump the instant the player lands, which reads as the controls being possessed.

### 3. Read in Update, apply in FixedUpdate

```csharp
// A press that begins and ends between two fixed steps is invisible to FixedUpdate.
// Latch it in Update, clear it after the step that used it.
bool _jumpQueued;

void Update() {
    if (_jump.WasPressedThisFrame()) _jumpQueued = true;
    _move = _moveAction.ReadValue<Vector2>();      // axes can be sampled either place
}

void FixedUpdate() {
    _body.MovePosition(_body.position + Direction(_move) * _speed * Time.fixedDeltaTime);
    if (_jumpQueued) { _body.AddForce(Vector3.up * _impulse, ForceMode.VelocityChange); _jumpQueued = false; }
}
```

### 4. Rebinding, including the parts people forget

```csharp
// PerformInteractiveRebinding listens for the next control and writes an override.
_rebind = action.PerformInteractiveRebinding(bindingIndex)
    .WithControlsExcluding("<Mouse>/position")     // or the rebind completes instantly
    .WithControlsExcluding("<Mouse>/delta")
    .WithCancelingThrough("<Keyboard>/escape")
    .OnComplete(op => { op.Dispose(); Save(); })
    .Start();

// Persist and restore overrides yourself; they are not saved automatically.
PlayerPrefs.SetString("bindings", _actions.SaveBindingOverridesAsJson());
_actions.LoadBindingOverridesFromJson(PlayerPrefs.GetString("bindings", ""));
```

Also handle: showing the *current* binding in the UI rather than the default, detecting
a duplicate binding and telling the player which action it clashes with, and a reset
to defaults.

### 5. Device switching changes prompts, not state

```csharp
// The player picks up a controller mid-game. Change the glyphs; change nothing else.
void OnEnable()  => InputSystem.onActionChange += OnActionChange;
void OnActionChange(object obj, InputActionChange change) {
    if (change != InputActionChange.ActionPerformed) return;
    var device = ((InputAction)obj).activeControl?.device;
    if (device is Gamepad) Prompts.Show(PromptStyle.Gamepad);
    else if (device is Keyboard or Mouse) Prompts.Show(PromptStyle.KeyboardMouse);
}
```

Debounce this. A player resting a hand on a controller with stick drift will otherwise
flicker the whole UI between prompt sets.

## Pitfalls

- **Active Input Handling set to the old backend** with the new package installed.
  Compiles clean, does nothing. Check it first, always.
- **Forgetting to enable the action map.** Everything reads zero and nothing errors.
- **Polling in `FixedUpdate`.** Drops presses. Latch in `Update`.
- **`Time.deltaTime` on a mouse delta.** Mouse deltas are displacements already; the
  multiply makes sensitivity frame-rate dependent.
- **No buffer.** Every press made a few frames early is discarded, and the game feels
  like it is ignoring the player. It is.
- **A buffer that is never consumed**, firing the action again on the next legal frame.
- **Ground checks that are exactly correct.** A raycast that is exactly the capsule
  height flickers on slopes and steps. Add margin, and use a small sphere rather than a
  ray.
- **Input blocked during animations.** If a two-second attack swallows every press, the
  game feels unresponsive no matter how good the animation is. Buffer through it and
  allow cancels.
- **No deadzone, or the same deadzone everywhere.** Sticks drift. Use the Input
  System's stick deadzone processor, and expose it - a worn controller needs a bigger
  one than a new one.
- **Hard-coded key checks alongside the action asset.** `Keyboard.current.spaceKey`
  scattered through gameplay code is a rebinding feature that silently does not work.
- **UI and gameplay actions enabled at once.** The player moves while navigating a
  menu. Enable exactly one map per context.

## Prove it with Proving Ground

Scenario steps drive real input through the same device layer a player uses, so a
scenario that passes proves the game's own input path - not a test double.

```jsonc
{ "name": "buffered-jump", "seed": 4, "steps": [
    { "do": "move", "x": 1, "seconds": 1.0 },
    { "do": "tap",  "action": "jump" },
    { "do": "wait", "seconds": 0.05 },
    { "do": "tap",  "action": "jump" },          // early: only lands if the buffer works
    { "do": "wait", "seconds": 1.5 },
    { "do": "assert", "that": "alive" }
]}
```

The action names the harness knows how to press: `jump`, `sprint`, `crouch`,
`interact`, `reload`, `melee`, `fire`, `aim`, `cancel`, `submit`, `inventory`, `map`.
Both a key and a gamepad button are driven for each, because the harness cannot know
which one your game reads.

| Question | Call |
|---|---|
| Is the backend set correctly | `pg_check project` |
| How much latency does the game actually have | `pg_run_scenario`, then read `input.moveLatency` |
| Is the buffer window inside its band | `input.bufferWindow` in `feel.json` |
| What should the window be | `pg_norms <genre>` |
| The user can reproduce a control bug | `pg_record` - they play, you get a deterministic scenario |

## References

- `references/buffering-and-accessibility.md` - the responsiveness budget frame by
  frame, buffer and grace windows beyond jumping, input remapping requirements,
  accessibility options that are expected rather than optional, and local multiplayer.

## Related skills

- `game-feel` - what happens once the press registers.
- `camera-systems` - look input, sensitivity and inversion.
- `physics-tuning` - why input is read in `Update` and applied in `FixedUpdate`.
- `game-ui-ux` - menu navigation, prompts, and the rebinding screen itself.
- `unity-scripting` - execution order, and where input fits in the frame.
