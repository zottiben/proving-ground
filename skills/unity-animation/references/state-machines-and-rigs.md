# State machines, rigs and sequencing - depth for `unity-animation`

## 1. Blend trees

| Type | Parameters | Use for |
|---|---|---|
| 1D | one | speed-based locomotion: idle to walk to run |
| 2D Simple Directional | two | strafing with one clip per direction |
| 2D Freeform Directional | two | strafing where clips are not evenly spaced |
| 2D Freeform Cartesian | two | parameters that are not directions - speed against turn angle |
| Direct | one per child | additive layering under explicit control |

Normalise the parameter. A 1D tree driven by raw metres per second breaks the moment
the character gets a sprint upgrade; one driven by `speed / maxSpeed` does not.

Set the damp time on `SetFloat` rather than smoothing the value you also use for
gameplay. The gameplay value should be immediate and the visual value smooth, and
conflating them makes the character respond as slowly as it looks.

Compute Thresholds in the blend tree inspector sets thresholds from the clips' own root
motion speeds, which is the fastest way to stop feet sliding in a locomotion tree.

## 2. Layers, masks and weights

A layer has a mask (which bones it affects), a blending mode (Override or Additive) and
a weight (0-1).

```csharp
// Blend the upper body in over a fifth of a second rather than snapping it on.
_animator.SetLayerWeight(_upperBodyLayer,
    Mathf.MoveTowards(_animator.GetLayerWeight(_upperBodyLayer), _aiming ? 1f : 0f,
                      Time.deltaTime / 0.2f));
```

An avatar mask is an asset listing the humanoid body parts or transforms the layer may
touch. Two rules avoid most layer confusion: mask *out* everything the layer should not
affect rather than trusting the animation not to touch it, and keep additive layers'
source clips authored as additive (with a reference pose set in the clip's import
settings) or they will double up the base pose.

Sync layers - a layer that copies another's state machine with different clips - are
the clean way to do "the same locomotion, but injured" without duplicating the graph.

## 3. IK and Animation Rigging

Built-in humanoid IK, inside `OnAnimatorIK`:

```csharp
void OnAnimatorIK(int layerIndex) {
    _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, _leftFootWeight);
    _animator.SetIKPosition(AvatarIKGoal.LeftFoot, _leftFootTarget);
    _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, _leftFootWeight);
    _animator.SetIKRotation(AvatarIKGoal.LeftFoot, _leftFootRotation);

    _animator.SetLookAtWeight(_lookWeight, 0.3f, 0.6f);   // body, head weights
    _animator.SetLookAtPosition(_lookTarget);
}
```

Requires the layer to have IK Pass enabled, and it only works on humanoid rigs. Foot IK
for uneven ground and look-at for head tracking are the two uses that pay for
themselves immediately.

The **Animation Rigging** package is the more capable option: constraint components -
two-bone IK, multi-aim, damped transform - evaluated after the animator, working on
generic rigs, and configurable without code. Prefer it for anything beyond feet and
head, particularly weapon-hand attachment, which is fragile with built-in IK.

## 4. The Playables API

Playables let you build and play animation graphs at runtime without an
AnimatorController. Worth reaching for when:

- clips are chosen at runtime from data - a modding system, a card game, procedural
  combos;
- the state machine has become a graph nobody can read;
- you want a state machine in code, where it can be tested, rather than in an asset.

```csharp
var graph = PlayableGraph.Create("Combat");
var output = AnimationPlayableOutput.Create(graph, "Animation", _animator);
var mixer  = AnimationMixerPlayable.Create(graph, 2);
output.SetSourcePlayable(mixer);
mixer.ConnectInput(0, AnimationClipPlayable.Create(graph, _clipA), 0, 1f);
mixer.ConnectInput(1, AnimationClipPlayable.Create(graph, _clipB), 0, 0f);
graph.Play();
// graph.Destroy() in OnDestroy, or it leaks.
```

The cost is that you have given up the visual graph and the transition tooling. That is
a real loss for anyone who is not a programmer, so it is a decision for the project, not
for one feature.

## 5. Timeline

Timeline sequences animation, audio, activation and signals along a track. For
cutscenes and scripted moments it is the right tool, and for gameplay it is usually the
wrong one.

The traps, all of which come from the same root - Timeline takes ownership:

- **It overrides whatever it animates**, and when it finishes, the property stays where
  the last frame left it. Restore it deliberately.
- **Gameplay control must be disabled** for the duration, explicitly, or two systems
  write the same transform.
- **Bindings are per-instance**, so a Timeline referencing scene objects breaks when
  used in another scene. Bind at runtime through the `PlayableDirector` for anything
  reused.
- **Signals need a receiver** on the bound object; a signal with no receiver is silent.

## 6. Rig import settings

- **Humanoid** for anything human-shaped that should share animations. Buys retargeting
  and built-in IK; costs a retargeting step and some fidelity.
- **Generic** for creatures, machines and unique proportions. No retargeting, exact
  fidelity.
- **Optimise Game Objects** removes the transform hierarchy from the skeleton, which is
  a real performance win and means you cannot attach anything to a bone unless you
  expose it explicitly in the import settings.
- **Root motion node** decides which transform drives root motion. Getting this wrong is
  why a character sinks into the floor or drifts sideways every time a clip plays.

Animation compression trades fidelity for size. Keyframe Reduction with a modest error
tolerance is the usual default; check anything with subtle motion afterwards, because
compression takes small movement out first and that is often the movement that was the
point.

## 7. Debugging an animator, in order

1. **Open the Animator window in play mode.** The active state is highlighted and the
   transition progress bar moves. Most "wrong animation" questions are answered here in
   ten seconds.
2. **Check the parameter values live** in the same window. A parameter set on a
   different Animator instance is a common and invisible mistake.
3. **Check Has Exit Time** on the transition that feels late.
4. **Check for a latched trigger** - a trigger set while no transition could consume it
   fires the next time one can.
5. **Check the layer weight** if an upper-body action does nothing.
6. **Check the mask** if a layer plays but the wrong bones move.
7. **Check the clip's own settings** - loop time, root transform baking - if the
   character drifts or snaps at the end of a clip.
8. **Check `pg_console`** for missing animation event methods, which log a warning per
   playback and scroll past unnoticed.
