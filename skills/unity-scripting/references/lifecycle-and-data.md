# Lifecycle, serialization and data - depth for `unity-scripting`

## 1. Execution order in full

On scene load, for every object in the scene:

```
Awake            all of them, before any Start
OnEnable         per object, immediately after its own Awake (if active)
Start            all of them, before the first Update
```

Then, per frame:

```
FixedUpdate (0..n)  ->  physics simulation  ->  OnTrigger*/OnCollision*
Update
coroutines resume (yield return null, WaitForSeconds, and friends)
LateUpdate
rendering  ->  OnPreCull, OnBecameVisible, OnWillRenderObject, OnRenderImage
end of frame  ->  deferred Destroy actually happens
```

On destruction: `OnDisable`, then `OnDestroy`. On application exit,
`OnApplicationQuit` first, then `OnDisable`/`OnDestroy` per object - which matters for
anything that saves on quit.

Two consequences worth internalising. **A coroutine resumes after `Update` and before
`LateUpdate`**, so a camera in `LateUpdate` sees this frame's coroutine result.
**Deferred `Destroy` happens at end of frame**, so a destroyed object is still findable,
still in a list, and still returns from `GetComponent` for the rest of the frame - while
comparing equal to null.

An object instantiated during `Update` gets its `Awake` and `OnEnable` immediately,
inside that `Instantiate` call, but its `Start` waits until before the next `Update`.
That gap is where "the field is set in `Awake` but null when I read it" comes from.

## 2. Serialization rules

Unity serializes a field when **all** of these hold:

- it is public, or private with `[SerializeField]`
- it is not `static`, `const` or `readonly`
- its type is serializable: a primitive, a string, an enum, a `UnityEngine.Object`
  reference, a struct or class marked `[Serializable]`, or an array or `List<T>` of any
  of those

It does **not** serialize: properties, dictionaries, interfaces, abstract types by
value, multi-dimensional arrays, jagged arrays, nullable types, or generic types other
than `List<T>`.

Workarounds, in order of how often they are the right answer:

```csharp
// A dictionary, serialized as parallel lists and rebuilt on deserialize.
public class Lookup : MonoBehaviour, ISerializationCallbackReceiver {
    [SerializeField] List<string> _keys = new();
    [SerializeField] List<int> _values = new();
    public Dictionary<string, int> Map = new();

    public void OnBeforeSerialize() {
        _keys.Clear(); _values.Clear();
        foreach (var pair in Map) { _keys.Add(pair.Key); _values.Add(pair.Value); }
    }
    public void OnAfterDeserialize() {
        Map = new Dictionary<string, int>();
        for (int i = 0; i < Mathf.Min(_keys.Count, _values.Count); i++) Map[_keys[i]] = _values[i];
    }
}

// Polymorphism, by reference. Supports interfaces and derived types; heavier, and
// renaming a type breaks existing references unless you add [MovedFrom].
[SerializeReference] IWeapon _weapon;
```

Note that `OnAfterDeserialize` runs on a background thread during asset loading, so it
must not touch the Unity API. Rebuilding a dictionary is fine; calling
`GetComponent` there is not.

## 3. ScriptableObject patterns

**Configuration.** Tuning values as an asset, referenced by many objects. The default
and best use.

**Shared runtime state.** A `ScriptableObject` holding a value that several systems read
and one writes, decoupling them without a singleton. Powerful and easy to overuse: state
in an asset is global state with extra steps, and it persists in the Editor between play
sessions.

**Event channels.** A `ScriptableObject` with a `UnityEvent` or a C# event that
publishers raise and subscribers listen to. Removes direct references between systems,
at the cost of a call graph you cannot follow in the IDE.

**Behaviour as data.** An abstract `ScriptableObject` with a method, subclassed per
variant, referenced where the behaviour is needed. A designer swaps the asset to change
the behaviour. Excellent for attack patterns, movement styles and AI decisions.

```csharp
public abstract class AttackPattern : ScriptableObject {
    public abstract IEnumerator Execute(Enemy self, Transform target);
}
```

The runtime-modification warning applies to all of them: changes made in play mode
persist in the Editor and do not exist in a build, which is a difference that produces
"it works in the Editor" bugs.

## 4. Prefabs and variants

- **A variant** inherits from a base prefab and overrides specific properties.
  Structural, and the right way to express "the same enemy, but armoured".
- **An override** on an instance is per-instance and shows in bold in the inspector.
  Useful, and a common source of drift when someone tweaks an instance instead of the
  prefab.
- **Nested prefabs** are supported, and a change to the inner prefab propagates.

The failure mode to watch is override sprawl: fifty instances each with a slightly
different value, none of which came from a decision. Periodically check what is
overridden, and push anything deliberate back to the prefab or into a
`ScriptableObject`.

## 5. Assembly definitions

An `.asmdef` makes a folder its own assembly. Two benefits and one cost.

- **Compile time.** Only the changed assembly and its dependents rebuild. On a large
  project this is the difference between two seconds and forty.
- **Explicit dependencies.** An assembly can only reference what it declares, so
  "gameplay accidentally depends on editor tooling" becomes a compile error.
- **Cost:** more configuration, and a reference you have to add before you can use it.

A layout that works: `Game.Core` (no dependencies), `Game.Gameplay`, `Game.UI`,
`Game.Editor` (editor-only, referencing the rest). Anything referencing an
`UNITY_EDITOR`-only API belongs in an assembly with the Editor platform constraint, not
behind an `#if` in a runtime assembly.

Tests get their own assemblies, referencing the code under test plus the test
framework - which is exactly how this package's own `ProvingGround.Tests.Editor` and
`ProvingGround.Tests.Runtime` assemblies are set up.

## 6. Domain reload and static state

Entering play mode normally reloads the app domain, which resets every static field. In
Project Settings you can turn that off to enter play mode faster - and then statics keep
their values between play sessions.

```csharp
// Explicit reset, so the code behaves identically whether or not domain reload is on.
static int _spawnCount;

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void ResetStatics() => _spawnCount = 0;
```

Write this for every static that holds mutable state. It costs three lines and it makes
the fast-enter-play-mode setting safe, which is worth a great deal of iteration time.

Note also that a domain reload drops the Proving Ground bridge for a few seconds. That
is expected and handled: `pg_script` tracks a generation counter across the reload,
reconnects, and reports the compiler result rather than a connection error.

## 7. Editor code

- `#if UNITY_EDITOR` around any use of the `UnityEditor` namespace in a runtime script,
  or the build fails.
- Anything in a folder named `Editor` is automatically excluded from builds.
- `OnValidate` runs in the Editor when a value changes in the inspector, and on load. It
  runs outside play mode, so it must not assume a running game, and it can be called
  during serialization - so it must not call anything that dirties the scene without a
  guard.
- `[ExecuteAlways]` runs the component's callbacks outside play mode. Useful for tools,
  and a good way to write an infinite loop that hangs the Editor. Guard with
  `Application.isPlaying` where behaviour should differ.
