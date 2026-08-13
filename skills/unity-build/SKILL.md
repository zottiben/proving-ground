---
name: unity-build
description: Get a Unity project out of the Editor and onto a machine - build settings and scene lists, scripting backends and managed code stripping, asset delivery with Addressables, build size and load times, headless builds and CI, and the failures that only appear in a player build. Use when making a build, when a build fails or behaves differently from the Editor, when setting up CI, or when a build is too large or loads too slowly.
---

# Unity build

A build is not the Editor with the windows closed. Code is compiled differently, unused
code is stripped, resources resolve differently, and shaders are compiled ahead of time
from a variant list somebody generated. Every one of those is a class of bug that cannot
reproduce in the Editor.

Which leads to the discipline: **build early and build often.** A project that has never
been built is a project with an unknown number of build-only failures, and finding them
all at once, late, is the standard way a project misses its date.

## When to use

- Use when making a build, or setting up build configuration.
- Use when a build fails, crashes on start, or behaves differently from the Editor.
- Use when setting up CI, headless verification or automated builds.
- Use when a build is too large, or takes too long to load.

**When *not* to use:** for runtime performance, `performance-optimization`. For what
platform-specific saving requires, `save-systems`.

## Core workflow

1. **Build on day one, then keep it building.** The first build of a mature project is
   always a bad day. The hundredth is not.
2. **Get the scene list right.** Only scenes in the Build Settings list exist in a build,
   and index 0 is what loads first. `pg_scene add_to_build` exists because forgetting
   this is universal.
3. **Choose the scripting backend deliberately.** Mono builds fast and suits iteration;
   IL2CPP is required on several platforms, runs faster, and takes considerably longer
   to build.
4. **Understand stripping before it bites.** Managed stripping removes code nothing
   appears to reference. Anything reached only by reflection disappears, and the failure
   is a runtime exception in a build that worked in the Editor.
5. **Deliver assets deliberately.** `Resources` folders go into the build wholesale and
   are loaded eagerly; Addressables load on demand and can ship separately.
6. **Automate it.** A build you have to click through is a build nobody runs. Batch mode
   plus `-executeMethod`, exit codes checked.
7. **Verify the build, not the Editor.** Run the checks against the built content, and
   gate on them.

## Patterns

### 1. A scripted build

```csharp
// One entry point, callable from CI with -executeMethod. Returns a non-zero exit code
// on failure, because a CI step that always succeeds is not a CI step.
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class Build {
    public static void Player() {
        var options = new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = Argument("-out") ?? "Builds/game",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        Debug.Log($"Build {summary.result}: {summary.totalSize / 1048576} MB in {summary.totalTime}");
        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
```

```bash
Unity -batchmode -quit -nographics -projectPath . -executeMethod Build.Player -out Builds/game
```

`-quit` after `-executeMethod` matters: without it a batch-mode Editor can sit open
forever, and the CI job times out rather than failing.

### 2. Stripping, and keeping what reflection needs

```xml
<!-- Assets/link.xml - preserved from stripping. Needed for anything resolved by name. -->
<linker>
  <assembly fullname="Game.Runtime">
    <type fullname="Game.Saves.*" preserve="all"/>
  </assembly>
  <assembly fullname="Newtonsoft.Json" preserve="all"/>
</linker>
```

```csharp
// Or per member, which is more surgical and lives next to the thing it protects.
[UnityEngine.Scripting.Preserve]
public class SaveMigrationV3 : ISaveMigration { }
```

Anything constructed by `Activator.CreateInstance`, resolved by `Type.GetType`, or
deserialized into by a JSON library is invisible to the stripper. The symptom is a
`TypeLoadException` or a silently empty object, only in a build, and only at the moment
that code path first runs - which may be hours in.

### 3. Asset delivery

| Mechanism | Behaviour | Use for |
|---|---|---|
| Direct reference | included, loaded with the scene | almost everything |
| `Resources/` | included wholesale, loaded by string path | legacy; avoid in new work |
| Addressables | loaded on demand, local or remote, unloadable | large content, DLC, patchable assets |
| StreamingAssets | copied raw, read as files | data you want to read as files, and platform-specific packaging |

`Resources` is the trap. Everything in any folder named `Resources` ships regardless of
whether anything references it, it is loaded into memory eagerly at startup, and it
cannot be unloaded selectively. A project that accumulates Resources folders has a build
size and a startup time nobody can explain.

Addressables cost more setup and repay it: an explicit dependency graph, per-group
packing, and a `Release` for every `LoadAssetAsync` - which is the leak to watch for.

### 4. Headless verification in CI

```bash
# The plugin's own batch entry points. Both exit non-zero when the check fails, and
# Gate also fails when a required check has never been run - so a gate cannot pass on
# evidence nobody produced.
Unity -batchmode -quit -projectPath . \
  -executeMethod ProvingGround.EditorTools.PgBatch.CheckAll

Unity -batchmode -quit -projectPath . \
  -executeMethod ProvingGround.EditorTools.PgBatch.Gate
```

For anything that needs play mode - scenarios, probes, feel measurement -
`PgBatch.Serve` starts the bridge headless and keeps the Editor alive, so a machine with
no display can still drive the game.

### 5. Development builds, and profiling a real build

```
Development Build          enables the profiler and stack traces; slower and larger
Autoconnect Profiler       attaches on launch, so you profile from the first frame
Script Debugging           attach a debugger to the running player
Deep Profiling             instruments everything; enormous overhead, use for locating only
```

Editor numbers are not build numbers. Any performance claim made from the Editor is a
claim about the Editor.

## Pitfalls

- **Never building until late.** The bugs accumulate and arrive together.
- **Scenes not in the build list.** `SceneManager.LoadScene` fails at runtime with a
  message people misread as a path problem.
- **Code stripped that reflection needed.** Works in the Editor, throws in the build.
  `link.xml` or `[Preserve]`.
- **`Resources` folders as a habit.** Everything ships, everything loads, nothing can be
  removed.
- **Addressables loaded without release.** A leak that only shows in long sessions.
- **Editor-only API in runtime code.** `UnityEditor` in a runtime assembly fails the
  build, always at the worst moment. `#if UNITY_EDITOR`, or an Editor assembly.
- **Shader variants not accounted for.** Thousands of variants make builds enormous and
  first-use hitchy. Strip them, or collect them into a variant collection and warm it.
- **Platform-specific settings left at defaults.** Bundle identifier, orientation,
  graphics APIs, signing. Each is a rejection or a crash on one platform.
- **No `-quit` in batch mode.** The job hangs instead of failing.
- **Ignoring the exit code.** A CI step that reports success regardless is decorative.
- **Different settings between local and CI.** Then a green CI proves nothing about what
  you will ship. Script the configuration, do not click it.
- **Assuming the Editor's asset import matches the build's.** Platform overrides on
  texture and audio import settings mean the build has different assets than the Editor
  showed you.

## Prove it with Proving Ground

The plugin's process layer is designed for exactly this end of the project.

```
pg_check project     settings that stop gameplay working at all
pg_check content     broken references, missing scripts, duplicates, import rule violations
pg_gate              one verdict, applied to every report written so far
pg_milestone beta    every asset in, every check passing, judged on evidence
```

`pg_gate` is what CI should call, and its most important property is that it **fails
when a required check has never been run**. A gate that can be passed by not producing
evidence is worse than no gate, and that is the specific failure it exists to prevent.

`pg_check content` deserves a run before every release build: missing script references
and broken asset references are exactly the class of problem that survives the Editor
and breaks the build, and they accumulate silently.

## References

- `references/player-settings-and-ci.md` - scripting backends and API compatibility,
  stripping levels in detail, build size analysis, shader variant management, a CI
  pipeline outline with licensing and caching, and platform-specific requirements.

## Related skills

- `unity-scripting` - assembly definitions, editor-only code, and what stripping sees.
- `performance-optimization` - profiling a development build rather than the Editor.
- `save-systems` - platform storage paths and what is writable in a build.
- `game-production` - the milestone ladder this sits at the end of.
