# Player settings and CI - depth for `unity-build`

## 1. Scripting backend and API level

| Setting | Options | Consequence |
|---|---|---|
| Scripting Backend | Mono, IL2CPP | Mono builds in minutes and supports runtime code generation; IL2CPP builds in tens of minutes, runs faster, and is required on several platforms |
| API Compatibility Level | .NET Standard 2.1, .NET Framework | Standard is smaller and more portable; Framework is wider and needed by some third-party libraries |
| IL2CPP Code Generation | faster runtime, faster (smaller) builds | the first inlines more generics; the second builds quicker and smaller |
| Managed Stripping Level | Disabled, Low, Medium, High | how aggressively unused managed code is removed |

The combination that surprises people: IL2CPP does ahead-of-time compilation, so
anything that generates code at runtime - `System.Reflection.Emit`, some serialization
libraries' fast paths, dynamic proxies - does not work. Libraries that are fine in the
Editor on Mono can fail on an IL2CPP build for this reason alone, and the message is
rarely clear about it.

## 2. Stripping levels

- **Disabled** - nothing removed. Largest build, no surprises.
- **Low** - conservative; unused assemblies only.
- **Medium** - unused types and members within used assemblies.
- **High** - aggressive, including some framework internals.

Higher levels save real size and cost real debugging. The practical approach: develop
at Low, raise it before shipping, and test the raised build properly - particularly
every save/load path, every JSON deserialization, and every place a type is resolved by
name.

`link.xml` preserves by assembly, namespace or type. `[Preserve]` preserves a single
member and has the advantage of living next to the code it protects, so it survives
refactoring in a way a separate XML file does not.

## 3. Build size

Where the size actually is, in the usual order:

1. **Textures.** Almost always the largest line. Check max size and compression format
   per platform, and check that overrides exist - a 4096 texture atlas at default
   settings is megabytes on its own.
2. **Audio.** Uncompressed or PCM clips are enormous. Vorbis for anything long, and Force
   To Mono on anything positional.
3. **Meshes.** Vertex compression, and Read/Write Enabled left on, which doubles a mesh's
   memory by keeping a CPU copy. Turn it off unless something genuinely reads the mesh.
4. **Shader variants.** Can be hundreds of megabytes on a project with many Shader Graph
   materials and no stripping.
5. **Everything in `Resources`.** Included regardless of use.

The Editor log written after a build contains a size breakdown by asset type and a list
of the largest assets. Read it after every release build; it takes a minute and it is
the only view of the build as shipped rather than as authored.

## 4. Shader variants

Every keyword combination is a variant, and the count multiplies. A build compiles them
ahead of time, which is why build times balloon and why the first use of a material can
still hitch.

- Strip variants you do not use with `IPreprocessShaders`, or with the pipeline's own
  stripping settings.
- Collect the variants actually used at runtime into a `ShaderVariantCollection` and
  warm it during a loading screen.
- Watch the variant count on any shader with many keywords; the inspector reports it.

## 5. A CI pipeline outline

```yaml
# The shape, not a specific provider's syntax.
steps:
  - checkout
  - restore Library/ from cache          # by far the biggest time saving; keyed on
                                         # the Unity version and the package manifest
  - activate the Unity licence           # a secret; the step most likely to fail silently
  - run editor tests:
      Unity -batchmode -runTests -testPlatform EditMode -testResults results.xml
  - run play mode tests:
      Unity -batchmode -runTests -testPlatform PlayMode -testResults play.xml
  - run the checks:
      Unity -batchmode -quit -executeMethod ProvingGround.EditorTools.PgBatch.CheckAll
  - build:
      Unity -batchmode -quit -nographics -executeMethod Build.Player
  - gate:
      Unity -batchmode -quit -executeMethod ProvingGround.EditorTools.PgBatch.Gate
  - upload artifacts, including the reports
```

Things that go wrong here, all of them at least once:

- **The licence.** Activation failures often produce a non-obvious error and an
  exit code that some wrappers swallow. Check it explicitly.
- **`Library/` not cached.** Every run re-imports every asset, and a large project takes
  an hour to do it.
- **`-nographics` where rendering is needed.** Fine for tests and for most builds; not
  fine if anything captures or renders. `PgBatch.Serve` exists to keep an Editor alive
  and reachable for play-mode work on a headless machine.
- **Exit codes not checked.** A step that always reports success is decoration.
- **Different settings locally and in CI.** Script the configuration so both use the same
  code path.

## 6. Platform requirements, briefly

- **macOS** - signing and notarisation for distribution outside the store; an unsigned
  build is quarantined and looks broken to the user rather than unsigned.
- **iOS / Android** - bundle identifier, target API levels, orientation, permissions,
  and an app icon set. Android additionally: the target architecture set, and IL2CPP for
  64-bit.
- **Windows** - graphics API order matters; a fallback to a device that does not support
  your shaders fails at runtime.
- **WebGL** - no threads, restricted networking, a long compile, and storage that is
  emulated and needs an explicit flush to persist.
- **Consoles** - separate SDKs, separate build targets, and required certification
  behaviour around saving, suspension and controller disconnection. Budget it as a real
  workstream.

## 7. Diagnosing a build-only failure

In the order that finds it fastest:

1. **Read the player log.** It exists on every platform, and it usually contains the
   exception. People skip this and guess for an hour.
2. **Build a development build** with script debugging and reproduce it there.
3. **Disable stripping** (set it to Disabled) and rebuild. If the problem goes away, it
   is a stripping problem and now you know where to add `[Preserve]`.
4. **Switch to Mono** if the platform allows. If the problem goes away, it is an IL2CPP
   ahead-of-time compilation issue - usually runtime code generation.
5. **Check the scene list**, if the failure is at startup.
6. **Check platform import overrides**, if the failure is visual or audio.
7. **Compare the Editor's asset settings with the platform's**, because they are allowed
   to differ and frequently do.
