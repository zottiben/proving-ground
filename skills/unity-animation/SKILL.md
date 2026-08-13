---
name: unity-animation
description: Drive character and object animation in Unity - Animator controllers, state machines, layers and masks, blend trees, transitions and responsiveness, root motion versus code-driven movement, animation events, IK, and Timeline for sequences. Use when setting up an Animator, when animation is unresponsive or does not transition, when locomotion slides or stutters, when root motion fights the controller, or when building cutscenes.
---

# Unity animation

Animation is where responsiveness quietly dies. A controller that reacts in one frame
feels tight; the same controller behind a 0.25 s transition with exit time feels
sluggish, and nothing in the code changed. Most "the controls feel bad" complaints in an
animated game are transition settings, not input code.

The second recurring problem is ownership: the Animator and the movement code both want
to move the character, and when both do, the result is sliding, stuttering or a
character that drifts away from its collider.

## When to use

- Use when setting up an Animator controller, states, layers or blend trees.
- Use when animation does not play, does not transition, or plays the wrong thing.
- Use when locomotion slides, stutters or desyncs from movement.
- Use when root motion fights the character controller or the navmesh agent.
- Use for cutscenes and sequenced animation with Timeline.

**When *not* to use:** for the timing constants that decide impact - hitstop, cancel
windows - `game-feel`. For agent movement itself, `game-ai`. For skinning cost and
animator budgets, `performance-optimization`.

## Core workflow

1. **Decide who owns movement first.** Either the code moves the character and animation
   follows, or root motion moves it and the code follows. Never both. This decision is
   the single most consequential one in the system.
2. **Keep the state machine small.** Locomotion in a blend tree, actions as states. A
   controller with forty states and a hundred transitions is unreadable and is where
   "it plays the wrong animation" lives.
3. **Set transitions for responsiveness.** Turn off Has Exit Time on anything the player
   triggers. Keep transition durations at 0.05-0.15 s for actions, longer only for
   deliberate weight.
4. **Hash your parameter names.** `Animator.StringToHash` once, in a static readonly
   field.
5. **Drive from state, not from events.** Set parameters that describe the character -
   speed, grounded, airborne - and let the state machine decide. Firing triggers from
   twelve places is how a controller becomes unmaintainable.
6. **Put contact moments on animation events**, not on timers. A footstep sound at 0.4 s
   is wrong the moment the clip speed changes.
7. **Cull off-screen animators**, and disable them entirely on things that are not
   moving.

## Patterns

### 1. Parameters describe the character; the state machine decides

```csharp
public class CharacterAnimation : MonoBehaviour {
    static readonly int Speed     = Animator.StringToHash("Speed");
    static readonly int Grounded  = Animator.StringToHash("Grounded");
    static readonly int VerticalV = Animator.StringToHash("VerticalVelocity");
    static readonly int Attack    = Animator.StringToHash("Attack");

    [SerializeField] Animator _animator;
    [SerializeField] float _damping = 0.1f;      // smooths the blend tree, not the logic

    void Update() {
        // Normalised, so the blend tree does not care about the top speed changing.
        _animator.SetFloat(Speed, _controller.PlanarSpeed / _controller.MaxSpeed, _damping, Time.deltaTime);
        _animator.SetBool(Grounded, _controller.IsGrounded);
        _animator.SetFloat(VerticalV, _controller.Velocity.y);
    }

    public void OnAttack() => _animator.SetTrigger(Attack);   // an event, so a trigger
}
```

`SetFloat` with a damp time smooths at the animator rather than in your logic, which
keeps the gameplay value crisp and the visual value smooth. Those are different things
and should not share a variable.

### 2. Transitions that do not eat responsiveness

The settings, and what each costs:

| Setting | For a player-triggered action | Why |
|---|---|---|
| Has Exit Time | **off** | on means the transition waits for the current clip to reach a point |
| Exit Time | n/a when off | |
| Transition Duration | 0.05-0.15 s | the blend; longer reads as lag |
| Interruption Source | usually Current State | otherwise the state cannot be cancelled |
| Can Transition To Self | off unless intended | on produces a restarting animation on held input |

Has Exit Time left on is the number one cause of "the attack comes out late". Leave it
on for animation-to-animation flow that is not player-driven - a landing settling into
an idle - and off for everything the player asked for.

```csharp
// In code, CrossFadeInFixedTime takes seconds regardless of clip length, which is what
// you almost always mean. CrossFade takes normalised time and surprises people.
_animator.CrossFadeInFixedTime("Attack", 0.08f);
```

### 3. Root motion, or not

```csharp
// Option A - code owns movement. Root motion off. Animation is visual only.
// Simple, responsive, and the character can slide if the animation speed does not match.
_animator.applyRootMotion = false;

// Option B - animation owns movement. Root motion on, and the controller consumes it.
// Weighty, no sliding, and less responsive because the character can only move the way
// the animation moves.
void OnAnimatorMove() {
    var delta = _animator.deltaPosition;
    delta.y = _verticalVelocity * Time.deltaTime;      // keep gravity in code
    _controller.Move(delta);
    transform.rotation *= _animator.deltaRotation;
}
```

With a `NavMeshAgent`, the same choice appears again and it has to be made explicitly:
either the agent moves and the animator follows (`agent.updatePosition = true`, root
motion off), or the animator moves and the agent only supplies the path
(`agent.updatePosition = false`, and you feed `agent.nextPosition` yourself). Both
enabled means the two fight, and the symptom is a character that stutters or drifts.

### 4. Layers and masks

```
Base Layer      full body: locomotion blend tree, jumps, landings
Upper Body      avatar mask from the spine up, weight 1 while aiming or reloading
Additive        small overlays - breathing, flinches, lean - as additive clips
```

An avatar mask limits a layer to a set of bones; the layer's blending mode decides
whether it replaces (Override) or adds to (Additive) the layers below. Aiming while
running is one upper-body override layer, not forty combined states, and that is the
main reason layers exist.

### 5. Animation events for contact moments

```csharp
// Added on the clip at the frame the foot lands. Survives a clip speed change; a timer
// in code does not.
public void OnFootstep() => Audio.Post("player.footstep", transform.position);
public void OnAttackContact() => _weapon.EnableHitbox(0.1f);
```

The method must be public, on a component on the same GameObject as the Animator, with
no arguments or exactly one supported argument type. An event calling a method that does
not exist logs a warning per playback and is easy to miss.

## Pitfalls

- **Has Exit Time on player actions.** The action waits for the current clip. This is
  the top cause of unresponsive-feeling animated characters.
- **Root motion and code movement both enabled.** Sliding, drift, or double speed.
- **`SetTrigger` that is never consumed.** Triggers latch until a transition uses them,
  so a trigger set while in the wrong state fires later, at the worst moment.
  `ResetTrigger` when changing state, or use a bool.
- **String parameter names in `Update`.** Hash them once.
- **Feet sliding.** The animation's stride does not match the movement speed. Either
  drive the blend tree from actual speed, or scale the animation speed by
  `speed / animationStride`.
- **Animator on a disabled GameObject.** It does not tick, and it does not resume where
  it left off in a way anyone expects.
- **Culling mode left at Always Animate.** Off-screen characters cost full price. Use
  Cull Update Transforms, or Cull Completely where a paused animation is acceptable.
- **A giant state machine.** Beyond about fifteen states, use layers, sub-state machines
  or a code-driven approach with the Playables API.
- **Transitions between every pair of states.** Use Any State deliberately - and
  remember Any State transitions can retrigger the state you are already in unless Can
  Transition To Self is off.
- **Humanoid retargeting assumed lossless.** It is not: proportions differ, and hand and
  foot contacts drift. Use IK to fix contacts, or a generic rig if the character is
  unique.
- **Timeline animating something gameplay also controls.** Timeline wins while it plays
  and then hands back a state nobody expected. Disable gameplay control for the duration
  explicitly.
- **Animating a UI element with the Animator when a tween would do.** An Animator per
  button is a real cost for what is usually four keyframes.

## Prove it with Proving Ground

Animation problems are timing problems, and timing is measurable.

- `pg_run_scenario` with `measureFeel` reports what the character actually did.
  `input.moveLatency` catches an animation lead-in that delays the response;
  `combat.attackCommit` catches a lockout that is longer than intended.
- `pg_events` gives the frame-stamped timeline: whether the footstep event fired once
  per step or four times, and whether the hitbox opened when the animation said it did.
- `pg_check audio` catches the footstep event firing sixty times a second, which is
  almost always an animation event on a looping clip that never stops.
- `pg_inspect` on the character reports the components and their state, which answers
  "is root motion on" without opening the Editor.
- `pg_capture` for the visual, with the legend read alongside it.

## References

- `references/state-machines-and-rigs.md` - blend tree types and when each fits, layer
  and mask setup in depth, IK and the Animation Rigging package, the Playables API,
  Timeline for cutscenes, rig import settings, and an animator debugging order.

## Related skills

- `game-feel` - the timings animation has to respect, and anticipation and follow-through.
- `game-ai` - the agent-animator contract, and who owns movement.
- `input-systems` - buffering through animations, and cancel windows.
- `performance-optimization` - animator cost, culling, and skinning budgets.
