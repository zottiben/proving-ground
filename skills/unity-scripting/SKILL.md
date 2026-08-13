---
name: unity-scripting
description: Write Unity C# that behaves - MonoBehaviour lifecycle and execution order, what Unity serializes and what it silently drops, ScriptableObjects for data, prefabs and variants, coroutines versus async, component lookup costs, the fake-null trap, assembly definitions and domain reload. Use when writing or reviewing gameplay code, when a field loses its value, a null check behaves strangely, initialisation order matters, or code works in the Editor and not in a build.
---

# Unity scripting

Unity's C# is C#, and the traps are all in the places where it is not: an engine object
that compares equal to null while still existing, a private field that persists across
a recompile, an event that keeps a destroyed object alive, an `Awake` that runs before
the thing it depends on.

None of these produce compiler errors. Most produce behaviour that is correct in the
Editor and wrong in a build, or correct today and wrong after a domain reload. That is
what this skill is for.

## When to use

- Use when writing or reviewing gameplay code, components or editor tooling.
- Use when a serialized field loses its value, or a reference is unexpectedly null.
- Use when initialisation order matters, or something is null in `Awake` but not in
  `Start`.
- Use when code behaves differently in the Editor and in a build.

**When *not* to use:** for allocation and frame cost, `performance-optimization`. For
physics callbacks specifically, `physics-tuning`. For build configuration and
stripping, `unity-build`.

## The frame, in order

```
Awake            once, on load, before any Start. Set up SELF here.
OnEnable         every time the object is enabled
Start            once, before the first Update. Reach for OTHERS here.
FixedUpdate      0..n times per frame, fixed dt. Physics.
OnTrigger/OnCollision   after the physics step
Update           once per frame, variable dt. Input, game logic.
LateUpdate       once per frame, after all Updates. Cameras, IK, anything following.
OnDisable / OnDestroy   teardown. Unsubscribe here.
```

The one rule that follows: **`Awake` sets up the object itself; `Start` talks to other
objects.** Every `Awake` in a scene runs before any `Start`, so anything you need from
another component is guaranteed to exist by `Start` and is not guaranteed in `Awake`.

## Core workflow

1. **Cache in `Awake`, use everywhere else.** `GetComponent`, `Camera.main`, `Find` - all
   lookups, none belong in `Update`.
2. **Serialize deliberately.** `[SerializeField]` on private fields. Know what Unity will
   not serialize, because it fails silently.
3. **Put data in `ScriptableObject`s.** Tuning values, tables and configuration belong in
   assets, not in fields on prefabs where they get copied and drift.
4. **Unsubscribe from everything you subscribe to.** `OnEnable` and `OnDisable` in
   pairs. This is the most common managed leak in Unity.
5. **Never compare an engine object with `is null` or `?.`.** Unity overloads `==` for a
   reason; the null-conditional operator bypasses it.
6. **Split into assembly definitions** once compile times start hurting, and to make
   dependencies explicit.
7. **Write the script, then read the compiler.** `pg_script write` waits for the domain
   reload and reports the actual errors, so there is never a reason to guess.

## Patterns

### 1. Caching, and the lookups that cost

```csharp
public class Enemy : MonoBehaviour {
    [SerializeField] EnemyStats _stats;          // a ScriptableObject, shared, not copied

    Rigidbody _body;                             // cached, not looked up per frame
    Transform _player;
    static readonly int Hurt = Animator.StringToHash("Hurt");   // hash once, not per call

    void Awake() {
        _body = GetComponent<Rigidbody>();       // self: fine in Awake
    }

    void Start() {
        _player = GameObject.FindWithTag("Player").transform;   // others: Start, and once
    }
}
```

`GetComponent` is a lookup, `Camera.main` is a lookup, `GameObject.Find` walks the
scene. All are fine occasionally and all are wrong in `Update`.

### 2. What Unity serializes, and what it drops silently

```csharp
public class Example : MonoBehaviour {
    [SerializeField] int _health;                    // yes: private with the attribute
    public float Speed;                              // yes: public field
    [SerializeField] List<string> _names;            // yes: List<T> of a serializable type

    public int Score { get; set; }                   // NO: properties are never serialized
    [SerializeField] Dictionary<string, int> _map;   // NO: dictionaries are not serialized
    [SerializeField] IWeapon _weapon;                // NO: interfaces are not serialized
    [SerializeField] Nested _nested;                 // only if Nested is [Serializable]
    static int _count;                               // NO: statics are not serialized
}
```

The failure mode is silence: the field appears in the inspector or it does not, and
nothing tells you why. A `Dictionary` you expected to persist is empty after a reload,
and the bug looks like data loss rather than a serialization rule.

For polymorphism, `[SerializeReference]` serializes by reference and supports interfaces
and derived types - at the cost of a heavier format and some fragility across renames.

### 3. `ScriptableObject` for data, and why

```csharp
// One asset, referenced by every enemy that uses it. Change the asset, every enemy
// changes. Compare with fields on a prefab, which get copied into every variant and
// then drift apart until nobody knows which is authoritative.
[CreateAssetMenu(menuName = "Game/Enemy Stats")]
public class EnemyStats : ScriptableObject {
    public float MaxHealth = 100f;
    public float MoveSpeed = 3.5f;
    public float AttackDamage = 12f;
    [Tooltip("Seconds of wind-up before the attack lands. The player reacts to this.")]
    public float TelegraphSeconds = 0.45f;
}
```

One warning: a `ScriptableObject` modified at runtime **stays modified in the Editor**
after play mode ends, because you edited the asset. Treat them as read-only at runtime,
or copy the values into the instance in `Awake`.

### 4. The fake-null trap

```csharp
// Unity overloads == so a destroyed object compares equal to null, even though the C#
// reference is still alive. Operators that bypass the overload see the live reference.
if (_target == null)      { }   // RIGHT: honours Unity's overload
if (_target is null)      { }   // WRONG: true only if the C# reference is genuinely null
_target?.DoSomething();          // WRONG: calls into a destroyed object
var x = _target ?? _fallback;    // WRONG: same reason

// The same trap in reverse: a destroyed object is not "null" to a nullable annotation,
// so nullable reference types do not protect you here.
```

This is the highest-value paragraph in the skill. A destroyed `GameObject` that
`?.` still calls into throws `MissingReferenceException` at a point far from the cause.

### 5. Events, and the leak

```csharp
// Subscribe and unsubscribe in matched pairs. A static or long-lived publisher holding
// a delegate to a destroyed object keeps its whole object graph alive, forever.
void OnEnable()  => GameEvents.PlayerDied += HandleDeath;
void OnDisable() => GameEvents.PlayerDied -= HandleDeath;
```

`OnEnable`/`OnDisable` rather than `Start`/`OnDestroy`, so a pooled object that is
disabled and re-enabled subscribes exactly once.

### 6. Coroutines and async, and which to use

```csharp
// Coroutine: tied to the MonoBehaviour. Stops when the object is disabled or destroyed,
// which is usually what you want for gameplay.
IEnumerator Dash() {
    _dashing = true;
    yield return new WaitForSeconds(0.2f);
    _dashing = false;                              // never runs if the object is disabled mid-dash
}

// async/await: NOT tied to anything. It keeps running after the object is destroyed
// unless you cancel it, and in the Editor it keeps running after play mode stops.
async Task LoadAsync(CancellationToken token) {
    await SomeIO(token);
    token.ThrowIfCancellationRequested();          // check after every await
    if (this == null) return;                      // and check the object still exists
}
```

Coroutines for anything with a lifetime tied to a GameObject. `async` for I/O, loading
and anything that must survive a scene change - with a `CancellationToken` that is
cancelled in `OnDestroy`, every time.

## Pitfalls

- **`is null`, `?.` or `??` on a `UnityEngine.Object`.** Bypasses the overload; calls
  into destroyed objects.
- **Reaching for another component in `Awake`.** It may not have run its own `Awake`.
  Use `Start`, or an explicit initialisation order.
- **Relying on Script Execution Order settings.** It works and it is invisible: the next
  person reads the code and cannot see why order matters. Prefer explicit initialisation.
- **`GetComponent` in `Update`.** Cache it.
- **Assuming a serialized private field is reset on recompile.** It is not - Unity
  serializes it across the domain reload, so stale state survives.
- **Statics surviving play mode in the Editor.** With domain reload disabled, statics
  keep their values between play sessions. Reset them explicitly with
  `[RuntimeInitializeOnLoadMethod]`.
- **Modifying a `ScriptableObject` at runtime** and being surprised the change persisted.
- **Subscribing without unsubscribing.** The leak that keeps whole scenes alive.
- **`Destroy` versus `DestroyImmediate`.** `Destroy` is deferred to end of frame, so the
  object is still findable in the same frame. `DestroyImmediate` is Editor-only in
  practice and will corrupt state if used during iteration.
- **Instantiating inside a loop over the thing you are instantiating from.** Collection
  modified, and the exception points at the loop rather than the cause.
- **`string` comparisons of tags.** `CompareTag` exists and does not allocate;
  `gameObject.tag == "Player"` allocates.
- **One giant assembly.** Every script change recompiles everything. Assembly definitions
  cut compile times and make dependencies visible.
- **Editor-only code outside an Editor folder or `#if UNITY_EDITOR`.** It compiles in
  the Editor and fails the build, which is the worst time to find out.

## Prove it with Proving Ground

`pg_script write` saves the file, asks Unity to rebuild, waits for the domain reload,
reconnects across it, and returns the actual compiler errors. There is no reason to
sleep, and no reason to guess whether it compiled.

```
pg_script write   the file, and the real compile result
pg_console        what Unity said went wrong that the return value did not cover
pg_inspect        everything on one object: components, transform, tag, layer
pg_find           by name, tag or component type
pg_compile_status when you need to know whether the Editor is busy
```

`pg_console` is the difference between knowing and guessing on the whole class of Unity
failures that are reported nowhere else: a component that would not attach, a null
reference inside somebody's `OnValidate`, a shader that did not compile, a missing
script on a prefab.

## References

- `references/lifecycle-and-data.md` - execution order in full including scene load and
  destruction, serialization rules and `[SerializeReference]`, ScriptableObject
  patterns, prefabs and variants, assembly definitions, domain reload settings, and
  static state that outlives play mode.

## Related skills

- `performance-optimization` - allocation, the cost of `Update`, and pooling.
- `physics-tuning` - `FixedUpdate`, collision callbacks, and the frame model.
- `unity-build` - stripping, IL2CPP, and code that only fails in a build.
- `save-systems` - serialization rules again, from the persistence side.
- `proving-ground` - the tool discipline for writing and verifying scripts.
