---
name: game-ai
description: Build enemy and NPC behaviour in Unity - NavMesh setup and agent tuning, state machines, behaviour trees and utility selection, perception with sight cones and memory, group coordination, and the deliberate imperfections that make AI feel fair. Use when adding enemies or NPCs, when agents fail to path or get stuck, when combat feels unfair or lifeless, or when designing how enemies notice, chase and coordinate.
---

# Game AI

Game AI is not artificial intelligence. It is the craft of producing behaviour that
reads as intelligent from the outside while remaining predictable enough to be fair.
The best enemies in games are considerably dumber than they appear and considerably
more legible than a smart one would be.

Two things follow. First, the player must be able to *tell* what the AI is doing -
unreadable behaviour is indistinguishable from broken behaviour. Second, most of what
makes an enemy feel good is restraint: not attacking all at once, not shooting the
instant they see you, not knowing where you are through a wall.

## When to use

- Use when adding enemies, NPCs, companions or wildlife.
- Use when agents will not path, get stuck, jitter, or walk through each other.
- Use when combat feels unfair, chaotic, or lifeless.
- Use to design perception: what the AI can see, hear, remember and forget.

**When *not* to use:** for the space the AI moves through, `level-design` - most
pathing problems are layout problems. For the feel of being hit by them, `game-feel`.
For animation blending on the agents, `unity-animation`.

## Core workflow

1. **Bake navigation before behaviour.** An agent with nowhere to walk cannot be
   debugged. `NavMeshSurface` from the AI Navigation package, baked over the blockout,
   with the agent radius and height matching the character it represents.
2. **Verify the mesh is connected.** `pg_check scene` reports navmesh islands and
   objectives nothing can reach. An island is why one enemy in the level never moves.
3. **Start with a state machine.** Three or four states - idle, patrol, chase, attack -
   is enough for most enemies, and it is legible in a way a behaviour tree is not.
   Reach for a tree when the state count passes about seven, or when you want to reuse
   sub-behaviours across enemy types.
4. **Give it senses with limits.** A sight cone plus a line-of-sight check plus a
   reaction delay. Perfect, instant, omnidirectional awareness is the single largest
   source of "this game is unfair".
5. **Give it memory, and forgetting.** Last known position, investigated, then given
   up on. An enemy that forgets is the reason stealth works at all.
6. **Coordinate at the group level.** Attack tokens, spacing, flanking slots. Ten
   enemies each independently making the optimal choice produces a mob, not a fight.
7. **Telegraph everything.** Every attack needs a wind-up long enough to react to. The
   wind-up is the gameplay; the attack is the consequence.
8. **Let the probe find what you would not.** `pg_run_probe` walks the level and finds
   the corner an agent gets wedged in.

## Patterns

### 1. NavMesh agent setup that matches the character

```csharp
// The agent's radius and height must match the character controller, or the agent
// paths through gaps the body cannot fit and grinds along every wall.
var agent = GetComponent<NavMeshAgent>();
agent.radius         = 0.35f;    // slightly larger than the capsule, not smaller
agent.height         = 1.8f;
agent.speed          = 3.5f;
agent.angularSpeed   = 480f;     // degrees/s; the default 120 looks like a tank turret
agent.acceleration   = 12f;      // low values read as sliding on ice
agent.stoppingDistance = 1.6f;   // inside attack range, or it walks into the player
agent.autoBraking    = true;     // off for patrol waypoints, on for a final destination
```

Two of these are usually wrong by default. `angularSpeed` at 120 makes every turn look
mechanical; `stoppingDistance` at 0 makes melee enemies shove the player around the
level.

### 2. Path validity, checked rather than assumed

```csharp
// SetDestination returning true means the request was accepted, not that a path exists.
bool TryGoTo(Vector3 worldPoint) {
    if (!NavMesh.SamplePosition(worldPoint, out var hit, 2f, NavMesh.AllAreas))
        return false;                                    // nowhere walkable near there

    var path = new NavMeshPath();
    if (!_agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
        return false;                                    // partial or invalid: do not start walking

    _agent.SetPath(path);
    return true;
}
```

`PathPartial` is the status behind most "the enemy walks to a wall and stops"
reports. Handle it explicitly: pick another destination, or give up and do something
else, but never treat it as success.

### 3. Perception with limits and memory

```csharp
public bool CanSee(Transform target) {
    var to = target.position - _eye.position;
    if (to.sqrMagnitude > _viewRange * _viewRange) return false;
    if (Vector3.Angle(_eye.forward, to) > _viewAngle * 0.5f) return false;

    // One ray, from the eye, to a point on the torso - not to the pivot at the feet,
    // which is under the floor as far as a raycast is concerned.
    return !Physics.Linecast(_eye.position, target.position + Vector3.up * 1.2f,
                             _blockers, QueryTriggerInteraction.Ignore);
}

void Update() {
    if (CanSee(_player)) {
        _awareness = Mathf.Min(1f, _awareness + Time.deltaTime / _timeToNotice);  // not instant
        _lastKnown = _player.position;
        _memory = _memoryDuration;
    } else {
        _memory -= Time.deltaTime;                       // forgets, eventually
        if (_memory <= 0f) _awareness = Mathf.Max(0f, _awareness - Time.deltaTime / _timeToForget);
    }
}
```

`_timeToNotice` around 0.3-0.8 s is the difference between an enemy that feels alert
and one that feels psychic. Show the awareness value to the player somehow - a sound,
a posture change, an indicator - or the stealth system is invisible.

### 4. Group coordination through tokens

```csharp
// Only N enemies may attack at once. The rest reposition, which reads as tactics and
// is really just a queue. This one pattern carries most of what "good combat AI" means.
public class AttackDirector : MonoBehaviour {
    [SerializeField] int _maxSimultaneous = 2;
    readonly HashSet<Enemy> _attacking = new();

    public bool RequestToken(Enemy e) {
        if (_attacking.Count >= _maxSimultaneous) return false;
        return _attacking.Add(e);
    }
    public void ReleaseToken(Enemy e) => _attacking.Remove(e);
}
```

Add spacing on top: enemies claim a slot on a ring around the player, so they surround
rather than stack. Two systems, and the fight goes from a scrum to something readable.

### 5. A state machine that stays readable

```csharp
// Explicit transitions in one place. The moment transitions are scattered across
// states, nobody can answer "why is it in chase" without reading everything.
enum State { Idle, Patrol, Investigate, Chase, Attack, Flee }

State Next(State current) => current switch {
    State.Idle        when _awareness > 0.5f          => State.Investigate,
    State.Patrol      when _awareness > 0.5f          => State.Investigate,
    State.Investigate when _awareness >= 1f           => State.Chase,
    State.Investigate when _memory <= 0f              => State.Patrol,
    State.Chase       when InAttackRange && HasToken  => State.Attack,
    State.Chase       when _awareness < 0.3f          => State.Investigate,
    State.Attack      when !InAttackRange             => State.Chase,
    _ when _health < _fleeThreshold                   => State.Flee,
    _                                                 => current
};
```

## Pitfalls

- **Agent radius smaller than the character.** The agent paths through gaps the body
  cannot fit, then grinds. Match them, and make the agent slightly larger if anything.
- **Ignoring `NavMeshPathStatus`.** A partial path looks like success and produces an
  enemy walking into a wall forever.
- **Navmesh islands.** One disconnected patch and the enemies standing on it never
  move. `pg_check scene` finds these.
- **Baking before the layout is final.** The mesh is stale the moment geometry moves.
  Re-bake as part of the layout change, not later.
- **Moving agents with `transform.position`.** It fights the agent's own steering and
  produces jitter. Use `SetDestination`, or take control properly with
  `agent.updatePosition = false` and drive `nextPosition` yourself.
- **Root motion and the agent both driving movement.** They will disagree. Pick one:
  either the agent moves and the animator follows, or the animator moves and the agent
  only supplies the path.
- **Perfect perception.** Instant, 360-degree, through-wall awareness. Reads as
  cheating even when it is technically fair.
- **No forgetting.** An enemy permanently aware of the player makes stealth pointless
  and retreat impossible.
- **Everyone attacking at once.** Unreadable, and unfair in a way players cannot
  articulate. Token the attacks.
- **Attacks without a wind-up.** If a player cannot react, the only counter is memory,
  and the fight becomes a quiz.
- **`NavMeshObstacle` and `NavMeshAgent` on the same object.** They fight. Use the
  obstacle only for things that block, and carving only where it is needed - carving is
  not free.
- **Pathfinding every frame for every agent.** `CalculatePath` is not cheap at scale.
  Recalculate on a change of intent, or on a timer with a stagger so they do not all
  land on the same frame.

## Prove it with Proving Ground

```
pg_check scene       navmesh islands, unreachable objectives, spawns inside geometry
pg_play -> pg_run_probe 120   the probe walks where you would not think to
pg_events            what the AI actually did, frame-stamped
pg_console           the null reference inside a coroutine nobody saw
```

`pg_digest` with a name filter answers "where are all the enemies right now" exactly,
which beats inferring positions from an image. For a specific behaviour, write a
scenario: walk into the sight cone, wait, assert the enemy reached you.

```jsonc
{ "name": "guard-notices", "seed": 9, "steps": [
    { "do": "teleport", "target": "Player", "x": 6, "y": 1, "z": 0 },
    { "do": "wait", "seconds": 2 },
    { "do": "assert", "that": "reached", "target": "Guard", "within": 3 }
]}
```

That scenario is also the regression test. Keep it.

## References

- `references/navmesh-and-behaviour.md` - navmesh areas, costs, links and off-mesh
  connections, obstacle carving, behaviour trees versus utility selection with working
  shapes for each, sensor design, and squad tactics.

## Related skills

- `level-design` - most pathing problems are layout problems.
- `unity-animation` - locomotion blending, root motion, and the agent-animator contract.
- `game-feel` - what an enemy's attacks feel like to receive.
- `performance-optimization` - the cost of many agents, and how to stagger it.
- `procedural-gen` - navigation on generated geometry, and why it must be baked at runtime.
