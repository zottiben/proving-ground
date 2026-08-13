# URP and lighting - depth for `unity-rendering`

## 1. The URP asset and the renderer

Two assets, and the split confuses people. The **URP asset** holds quality-level
settings - shadow distance and cascades, render scale, HDR, anti-aliasing, light limits.
The **Renderer Data** asset holds the rendering path and the renderer features. One URP
asset references one renderer.

Quality levels each reference a URP asset, so "low" and "high" are two assets with
different numbers, and switching quality switches assets. This is the mechanism for
platform scaling, and it is worth setting up before shipping rather than after.

Settings that matter most:

| Setting | Effect |
|---|---|
| Rendering Path (Forward / Forward+ / Deferred) | Forward+ removes the per-object light limit; Deferred suits many lights on opaque geometry |
| Shadow Distance | how far shadows are drawn; the single biggest shadow quality and cost lever |
| Cascade Count and splits | how resolution is distributed across that distance |
| Render Scale | renders below native and upscales; the fastest way to buy GPU headroom |
| HDR | required for bloom and tonemapping to behave |
| Additional Lights | per-object limit in Forward; the reason a light "stops working" in a crowded scene |

## 2. Renderer features

A renderer feature injects a pass into the pipeline at a defined point. Decals, screen
space ambient occlusion, and any custom pass are all renderer features, and none of them
do anything until they are added to the Renderer Data asset in use.

That last clause is the trap: a project with three URP assets for three quality levels
has three renderers, and adding a feature to one leaves the other two without it. The
symptom is an effect that works in the Editor and not in a build, or on high quality and
not on low.

Custom passes are `ScriptableRendererFeature` plus `ScriptableRenderPass`. Worth
knowing: the render pass API has changed across URP versions, so a snippet from an
older version may not compile - check the version in the manifest before copying
anything.

## 3. Lightmapping in practice

The order that avoids wasted bakes:

1. Mark static geometry as **Contribute GI**. Nothing else bakes.
2. Ensure lightmap UVs exist. Generate Lightmap UVs in the model importer, or author
   them. Overlapping UVs produce light leaking and blotches, and it is the most common
   baking fault.
3. Set **Lightmap Resolution** per scene, and override **Scale in Lightmap** per object -
   high for hero surfaces, low for anything distant or hidden.
4. Bake with a low sample count to iterate. Only raise samples once the lighting is
   decided, because a final-quality bake is measured in hours, not minutes.
5. Check for leaking - light through walls - which usually means geometry is too thin.
   Lightmappers need walls with thickness.

Baked lighting is static: doors that open, objects that move and time-of-day changes all
need realtime or mixed lighting. Deciding which parts of the scene are static is a
design decision that has to happen before the bake, not after.

## 4. Probe placement

**Light probes** sample baked lighting so dynamic objects receive it. Place them:

- through every volume a dynamic object can occupy, not just at floor level;
- denser where lighting changes quickly - doorways, under overhangs, at the edge of a
  shadow;
- sparse where lighting is uniform. Uniform space needs almost none.

Probes only interpolate between themselves, so a character walking through a region with
no probes is lit by whatever the nearest tetrahedron says, which is usually wrong in a
noticeable way.

**Reflection probes** capture the surroundings into a cubemap. One per lighting
environment: each room, each distinct outdoor area, anywhere the reflected content
changes. Baked for static environments, realtime only where it is genuinely needed and
budgeted - a realtime probe renders the scene six times.

Box projection on a probe makes the reflection respect the room's shape rather than
appearing infinitely distant, which matters enormously in interiors and costs nothing.

## 5. Camera stacking

URP cameras are Base or Overlay. An Overlay camera renders on top of a Base camera's
output, in the order listed in the Base camera's stack. This is how a weapon viewmodel
that must not clip into walls is done, and how a 3D UI element renders over the scene.

Costs to know: each camera in the stack is a full render pass over its content, and post
processing applies per camera stack rather than per camera. A viewmodel camera is cheap
because it renders three objects; a second full-scene camera is not cheap at all.

## 6. Debugging tools

- **Frame Debugger.** Steps through every draw call in order, showing what was drawn,
  with which shader, and why the batch broke. The first tool for "why is this not
  rendering" and for batching questions.
- **Rendering Debugger.** Isolates channels - albedo, normals, lighting only, overdraw -
  and validates material properties against expected ranges. The fastest way to answer
  "is this a lighting problem or a texture problem".
- **Scene view draw modes.** Overdraw, shaded wireframe, and the lighting modes. Free,
  and underused.
- **Shader inspector.** Compiled variant count. A shader with thousands of variants is a
  build time and memory problem, and the count is visible here.

## 7. Look development checklist

Run this on any scene before calling the lighting done:

1. Is there a clear key light with a deliberate direction?
2. Is ambient non-black, and do shadowed faces still read?
3. Do dynamic objects sit in the scene, or float? (Light probes.)
4. Do glossy surfaces reflect their actual surroundings? (Reflection probes.)
5. Is there depth cueing - fog, aerial perspective - or does everything sit on one plane?
6. Is tonemapping on, and do bright areas roll off rather than clip?
7. Does the critical path read as the brightest, most contrasted thing in frame?
8. Does it hold up under every lighting condition the game ships with?
9. Is anything pure black or pure white that should not be?
10. At the target frame rate, on the target hardware, does it still hold?

Question 7 is the one that connects rendering to design: light is the strongest
guidance tool in the level designer's kit, and a lighting pass that ignores it will
quietly undo the readability the blockout established.
