# Pacing, flow and guidance - depth for `level-design`

## 1. The tension curve

A level is paced when its intensity over time forms a **sawtooth that trends upward**:
rises to a peak, drops to a rest, rises higher, drops less far, and finishes at the
climax. Two failure shapes, both common:

- **Flatline high.** Continuous combat. Players adapt within minutes and the peaks
  stop registering, so the ending lands as another fight.
- **Flatline low.** Continuous traversal or exploration with nothing at stake. Reads
  as filler regardless of how good the space looks.

Rest is not empty. A rest beat is where you put a vista, a reward, a conversation, a
save, a new piece of information, or the first sight of where the player is going
next. Its job is to make the next peak legible, and to let anticipation build.

Place a save or checkpoint **before** the climax, never immediately before a
non-skippable sequence. Losing progress is annoying; re-watching a cutscene to retry a
fight is what makes people stop playing.

## 2. The teaching loop

Every mechanic gets four beats, in order, and the order is what makes difficulty feel
fair rather than arbitrary:

1. **Introduce** in a safe space where failure costs nothing and the mechanic is the
   only thing happening.
2. **Develop** by asking for the same thing under mild constraint - a timer, a drop, a
   second object.
3. **Twist** by combining it with something already learned, or inverting it.
4. **Test** under real pressure, where failure has consequences.

Anything skipped is felt. Testing before developing reads as a difficulty spike;
introducing during a fight reads as unfairness.

A mechanic introduced and never revisited reads as a gimmick. If it is worth teaching,
it is worth using at least three more times.

## 3. Readability and guidance

Players go where the level tells them. In rough order of strength:

- **Light.** The eye goes to the brightest thing in frame. This is the strongest tool
  you have, and it means the lighting pass can silently destroy the guidance the
  blockout established. Check the critical path is still the brightest thing after
  lighting.
- **Contrast and colour.** A saturated element against a desaturated environment reads
  as interactive. Pick one colour for "you can touch this" and never use it for
  decoration.
- **Leading lines.** Corridors, railings, cables, road markings, the direction a
  character is facing. Architecture points.
- **Landmarks.** A silhouette visible from many places anchors orientation. It needs a
  unique outline, not a unique texture, because at 200 m only the outline survives.
- **Motion.** Anything animated in a still frame takes the eye. Use deliberately, and
  never for something the player cannot interact with.
- **Framing.** A doorway, arch or window that puts the objective in the middle of the
  frame is the oldest trick there is and still the most reliable.

Walls are the weakest tool: they say "not here" without saying "there".

## 4. The critical path, the golden path, and everything else

- **Critical path** - the minimum route from start to goal. It must be completable by
  a player who never explores.
- **Golden path** - the route most players will actually take. Design it wider, better
  lit and more legible than the alternatives.
- **Optional branches** - shorter dead ends holding rewards. Keep them short: an
  unrewarded five-minute detour teaches players not to explore.
- **Secrets** - genuinely hidden, and worth finding. One good secret beats six that
  are just off-path pickups.

A useful ratio for a linear level: about 70% of playtime on the golden path, 30% on
optional content, and no optional branch longer than about a fifth of the level.

## 5. Encounter design

An encounter is a space, a set of enemies, and a reason. Get the space right first:

- **Cover placement decides everything.** Cover at consistent heights (see the metrics
  reference) that reads at a glance, positioned so that holding one piece is
  survivable but not winning.
- **Multiple entry points** for the enemies, so the fight can develop rather than
  playing out in one direction.
- **A flanking route for the player**, so the answer to pressure is movement rather
  than waiting.
- **An exit**, so a fight the player is losing can become a retreat instead of a
  reload.
- **Vertical variation.** A single-height arena plays the same way every time.

Then the composition: one enemy type that pressures the player forward, one that
punishes standing still, and, occasionally, one that changes the rules. Three types in
one encounter is usually the ceiling before it becomes noise.

## 6. 2D and 3D differ in what they hide

**2D:** the whole space is visible, so surprise comes from timing and from off-screen
approach. Camera framing decides difficulty, because what the player cannot see they
cannot plan around. Vertical layout reads instantly; horizontal distance does not.

**3D:** occlusion is the primary design tool. Sightlines, corners and elevation decide
what the player knows. The camera needs physical room - a corridor sized for a
character is claustrophobic in third person and the camera will clip. Verticality is
expensive to read and needs explicit signposting, because players do not look up.

## 7. Blockout review checklist

Walk the level at player speed and answer these. Any "no" is a layout fix, not an art
fix.

1. Can I tell where to go without being told?
2. Is every required jump inside `SafeGap`, and every `HardGap` optional?
3. Does each encounter space have cover, two entries, a flank and an exit?
4. Is there a rest beat between every pair of peaks?
5. Is there a checkpoint before the hardest thing?
6. Can I see the next objective, or something that implies it, from the current one?
7. Is anything reachable that should not be? Is anything unreachable that should not be?
8. Does the space read at eye height, or only from above?
9. Would this be interesting if it were never textured?

That last one is the whole discipline in a sentence.
