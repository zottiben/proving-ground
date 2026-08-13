# NavMesh and behaviour architecture - depth for `game-ai`

## 1. Navigation setup in current Unity

Navigation lives in the **AI Navigation** package (`com.unity.ai.navigation`). The
components:

| Component | Job |
|---|---|
| `NavMeshSurface` | bakes a navmesh from the geometry under it |
| `NavMeshModifier` | overrides area type or inclusion for one object and its children |
| `NavMeshModifierVolume` | overrides area type inside a volume, regardless of geometry |
| `NavMeshLink` | connects two points the mesh cannot join - a jump, a ladder, a drop |
| `NavMeshObstacle` | a dynamic blocker, optionally carving a hole in the mesh |

One surface per agent type. An agent baked at radius 0.35 cannot use a mesh baked for
radius 0.6, and the symptom is agents refusing to enter corridors that look fine.

Bake settings that matter more than the rest: **agent radius** decides how close to
walls agents may walk and is the main cause of "will not enter the doorway"; **step
height** decides whether stairs are walkable at all; **max slope** decides whether
ramps are; and **min region area** is what you raise to delete the tiny useless islands
that get baked onto every table and windowsill.

## 2. Areas and costs

Areas let you say "walkable, but avoid". Set the cost per agent so different enemy
types route differently over the same mesh.

```csharp
// Water is walkable but four times as expensive, so agents route round it unless the
// detour is long. Cost is per agent, so a boat-borne enemy can prefer it.
_agent.SetAreaCost(NavMesh.GetAreaFromName("Water"), 4f);

// areaMask excludes an area entirely rather than discouraging it.
_agent.areaMask = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Hazard"));
```

Costs are a design tool, not an optimisation. "Enemies prefer cover" and "civilians
stay on the pavement" are both area costs, and both are cheaper and more reliable than
the behaviour code that would otherwise implement them.

## 3. Links, jumps and drops

A `NavMeshLink` is a directed or bidirectional connection with its own area type. The
agent traverses it automatically, teleporting across unless you handle it - which looks
wrong for anything but a doorway.

```csharp
// Take manual control while the agent is on a link so a jump looks like a jump.
if (_agent.isOnOffMeshLink) {
    var data = _agent.currentOffMeshLinkData;
    StartCoroutine(TraverseAsJump(data.startPos, data.endPos));   // then _agent.CompleteOffMeshLink()
}
```

Give links a distinct area type so you can price them: a link costing 10 means agents
only jump the gap when walking round is genuinely far, which is exactly the behaviour a
player reads as sensible.

## 4. Obstacles and carving

`NavMeshObstacle` in carving mode cuts a hole in the mesh and makes paths route around
it. It is the right answer for a door that closes or a crate the player pushed. It is
the wrong answer for anything that moves continuously: carving triggers a partial
re-bake, and doing that every frame for twenty objects is a real cost.

Rules that avoid the usual mess:

- Carving on for things that stop and stay. `carveOnlyStationary` is the default and
  should stay on.
- Obstacle without carving for things that move constantly - other agents avoid it via
  local avoidance without re-baking anything.
- Never put an obstacle and an agent on the same object. The agent avoids its own
  obstacle and jitters in place.

## 5. Local avoidance

`obstacleAvoidanceType` trades quality for cost, and `avoidancePriority` breaks
deadlocks - a lower number wins, and agents at equal priority ignore each other. Give
every agent a slightly different priority, or a crowd in a corridor will lock solid
with everyone politely waiting.

Avoidance is not pathfinding. It handles the last metre; it will not route an agent
around a blocked corridor, and an agent trying to avoid its way through a wall looks
exactly like a stuck agent.

## 6. State machines, behaviour trees, utility

Three architectures, and the choice is about how the behaviour will be *edited*, not
about which is more powerful.

**Finite state machine.** Explicit states, explicit transitions. Best up to about seven
states. Debuggable by printing the state name. Degrades when transitions start being
written in several places - that is the moment to move.

**Behaviour tree.** A tree of composites (sequence, selector, parallel) over leaves
(conditions, actions), re-evaluated from the root each tick. Best when sub-behaviours
repeat across enemy types, because subtrees are reusable in a way states are not. The
cost is indirection: the answer to "why is it doing that" is a traversal, not a
variable.

```csharp
// The shape, without a framework. A node returns Running, Success or Failure, and the
// composites do the rest. This is the entirety of the idea.
public enum Status { Running, Success, Failure }
public abstract class Node { public abstract Status Tick(); }

public sealed class Sequence : Node {                 // all children, in order, until one fails
    readonly Node[] _children; int _current;
    public override Status Tick() {
        for (; _current < _children.Length; _current++) {
            var status = _children[_current].Tick();
            if (status != Status.Success) return status;
        }
        _current = 0;
        return Status.Success;
    }
}

public sealed class Selector : Node {                 // first child that does not fail
    readonly Node[] _children;
    public override Status Tick() {
        foreach (var child in _children) {
            var status = child.Tick();
            if (status != Status.Failure) return status;
        }
        return Status.Failure;
    }
}
```

**Utility selection.** Every action scores itself against the current world state; the
highest score runs. Best for agents with many competing concerns that are not
hierarchical - a sim character choosing between hunger, boredom and safety. The cost is
tuning: scores interact, and a small weight change in one curve can silently dominate
everything.

A practical hybrid that works well: a state machine at the top for the coarse mode
(patrol, combat, flee), and utility scoring inside the combat state to choose which
attack. Legible where it needs to be, flexible where it pays.

## 7. Sensor design

Model each sense separately, and give each one a limit that a player can learn:

- **Sight.** Range, cone angle, line-of-sight, and a time-to-notice. Consider a shorter
  range with a wider cone for peripheral vision and a longer, narrow one for focus.
- **Hearing.** A radius per noise event, not a continuous check. Loud actions - running,
  gunfire, breaking something - publish an event with a position and a radius, and any
  agent inside it investigates. This is much cheaper and much easier to tune than
  anything continuous.
- **Damage.** Being hit from an unseen direction should turn the agent toward it. Not
  reveal the player - turn toward it.
- **Allies.** An agent that saw something tells nearby agents, with a delay and a
  radius. This is what makes a group feel like a group, and it is three lines.

Every sense should produce the same currency: awareness, plus a last-known position.
Behaviour reads those two values and nothing else, which keeps the senses swappable.

## 8. Squad tactics that are mostly bookkeeping

- **Attack tokens.** Two attackers at a time. The rest reposition.
- **Ring slots.** Claim an angle on a circle around the target; occupied slots are
  unavailable. Produces surrounding rather than stacking.
- **Suppression roles.** One agent holds position and fires while another advances. A
  role assignment, not an algorithm.
- **Flanking.** Path to a point behind the target rather than to the target. Route
  quality does the work; `NavMesh.SamplePosition` behind the player is the whole
  implementation.
- **Retreat and regroup.** A wounded agent falls back toward allies rather than away
  from the player, which makes the fight move rather than dissolve.

None of this is intelligence. All of it reads as intelligence, which is the entire
discipline.
