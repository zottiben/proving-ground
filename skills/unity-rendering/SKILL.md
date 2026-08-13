---
name: unity-rendering
description: Get a Unity game looking right - URP setup and renderer features, lighting with realtime, mixed and baked modes, light and reflection probes, materials and Shader Graph, post-processing through the Volume system, colour space and tonemapping, and camera stacking. Use when a scene looks flat, black, blown out or wrong, when setting up lighting or post-processing, when writing or debugging shaders, or when materials look different in play mode than in the scene view.
---

# Unity rendering

Two facts explain most of what goes wrong here. **Lighting reacts to final geometry and
final material values**, so lighting a greybox is iteration you throw away. And **a scene
that looks wrong is usually lit wrong, not shaded wrong** - flat, black or blown out are
lighting problems, and people go looking in the material.

Everything below assumes the Universal Render Pipeline, which is the default for new
projects and the pipeline most Unity work targets. The concepts transfer; the component
names and the settings locations do not.

## When to use

- Use when a scene looks flat, black, washed out, blown out or plastic.
- Use when setting up lighting, probes, post-processing or quality levels.
- Use when writing or debugging a shader, or when a material renders wrong.
- Use when the scene view and the game view disagree.

**When *not* to use:** for GPU cost and budgets, `performance-optimization`. For
particle materials specifically, `unity-vfx`. For deciding *when* the lighting pass
happens in production, `game-production`.

## Core workflow

1. **Confirm the project is in linear colour space.** Gamma makes correct lighting
   impossible and every fix a compensation. It is a project setting, and switching it
   late changes every material's appearance.
2. **Set up the URP asset and renderer first.** One asset per quality level, a renderer
   with the features you need. A renderer feature that is not on the renderer does
   nothing, silently.
3. **Light with intent, in this order:** the key light and its direction, then ambient
   and the sky, then fill and bounce, then the local lights that shape the space.
4. **Choose the lighting mode deliberately.** Fully realtime, fully baked, or mixed -
   and mixed has sub-modes with very different costs and looks.
5. **Place probes.** Light probes for anything moving, reflection probes for anything
   glossy. Without them, dynamic objects float and metal looks like plastic.
6. **Post-process last, and lightly.** Tonemapping and a little bloom. Post is not a fix
   for bad lighting; it is a grade on good lighting.
7. **Judge under every condition the game has.** A material that only works at noon is
   not finished.

## Patterns

### 1. The lighting order that produces a lit scene

```
1  Key light        one directional light: angle, intensity, colour, shadows on.
                    The angle is the single most consequential decision - low and warm
                    reads as evening, high and neutral reads as flat noon.
2  Ambient          Lighting window > Environment. A skybox or a gradient, never black.
                    Black ambient is why shadowed faces are pure black.
3  Bounce           baked GI, or raise the environment contribution. Real shadows are
                    not black; they are lit by what is around them.
4  Local lights     shape the space, guide the eye, mark the path. Cheapest storytelling
                    in the engine.
5  Fog              depth cue, and it hides the far plane. Cheap, and instantly makes a
                    scene read as a place.
```

Every "the scene looks flat" report is step 2 or 3, and every "the shadows are black
holes" report is step 2.

### 2. Lighting modes, and what each costs

| Mode | Lighting | Shadows on static | Shadows on dynamic | Cost |
|---|---|---|---|---|
| Realtime | fully dynamic | realtime | realtime | highest runtime, zero bake |
| Baked | baked, static only | baked | none - dynamic objects are unlit by it | cheapest runtime, needs probes |
| Mixed / Baked Indirect | direct realtime, indirect baked | realtime | realtime | good quality, moderate cost |
| Mixed / Shadowmask | direct realtime, indirect and distant shadows baked | baked past the distance | realtime near | good for large static scenes |
| Mixed / Subtractive | fully baked, one realtime shadow | baked | approximated | cheapest mixed; looks it |

Baked lighting needs objects marked **Contribute GI** and correct lightmap UVs. The most
common baking failure is not a setting: it is a mesh with no second UV channel, where
the answer is Generate Lightmap UVs in the model import settings.

### 3. Probes, which are not optional

```
Light probes        a volume of them through any space a dynamic object moves.
                    Denser where lighting changes fast - a doorway, under an arch.
                    Without them, characters are lit by ambient alone and look pasted on.
Reflection probes   one per distinct lighting environment: each room, each area.
                    Without them, every glossy surface reflects the skybox, which is
                    why interiors look like they are outdoors.
```

A single reflection probe covering a whole level is barely better than none. The tell is
metal that looks like plastic and glass that reflects sky indoors.

### 4. Post-processing through the Volume system

```csharp
// URP post-processing is Volume components with overrides. A global volume for the
// base grade, local volumes for area-specific looks, blended by weight and priority.
//   Global volume:  Tonemapping (ACES or Neutral), a little Bloom, Vignette
//   Local volumes:  colour grading per area, fog density, depth of field for a cutscene
//
// The camera needs Post Processing enabled on it, and the URP asset needs HDR on for
// bloom to behave. Both are silent when wrong.
```

Tonemapping is the one that matters. Without it, bright values clip hard and the image
looks harsh and video-gamey; with ACES or Neutral it rolls off and the whole scene reads
as photographed. Turn it on before adjusting anything else.

Restraint is the rule: heavy bloom, chromatic aberration and vignette read as an
attempt to hide something. If the scene needs post to look acceptable, the lighting is
not done.

### 5. Materials, batching and property blocks

```csharp
// renderer.material INSTANTIATES a copy - per object, every call. It breaks GPU
// instancing, leaks the copy, and is the most common source of "why do I have 400
// materials at runtime".
_renderer.sharedMaterial.color = c;               // shared: changes every user

// Per-instance variation without breaking batching:
var block = new MaterialPropertyBlock();
_renderer.GetPropertyBlock(block);
block.SetColor("_BaseColor", c);                  // URP Lit uses _BaseColor, not _Color
_renderer.SetPropertyBlock(block);
```

The property name matters: URP shaders use `_BaseColor` and `_BaseMap`, where the
built-in pipeline used `_Color` and `_MainTex`. Setting the wrong one fails silently.

### 6. Shader Graph, and when to write HLSL

Shader Graph covers the large majority of game shaders and is SRP Batcher compatible by
construction, which is a real performance property and not just convenience. Reach for
handwritten HLSL when you need something the graph cannot express - a custom lighting
model, a compute-driven effect, tight control over variants - and know that a
handwritten shader must declare its material properties in a `UnityPerMaterial` constant
buffer to stay SRP Batcher compatible.

Debugging a shader graph: use the preview on each node, then the Frame Debugger to see
what was actually submitted, then the Rendering Debugger to isolate channels. Guessing
at a graph is slower than looking at the previews, every time.

## Pitfalls

- **Gamma colour space.** Correct lighting is impossible; everything becomes
  compensation. Linear, from the start.
- **Black ambient.** Shadowed faces render pure black and the scene reads as broken.
- **No light probes.** Dynamic objects are lit differently from their surroundings and
  appear pasted on.
- **One reflection probe for the whole level.** Interiors reflect the sky.
- **Lighting a greybox.** Final albedo changes everything; you will tune it twice.
- **Post-processing as a fix.** It grades good lighting; it does not create it.
- **`renderer.material` in a loop.** Material copies per object, instancing defeated.
- **Built-in pipeline property names on URP shaders.** `_Color` does nothing on URP Lit.
- **A renderer feature added to the wrong renderer**, or to none. Decals and custom
  passes silently do not render.
- **Shadow distance left at the default.** Either shadows vanish a few metres away, or
  cascades are spread so thin that everything is blurry.
- **Emissive materials expected to light the scene.** They only do so through baked GI.
  In a realtime setup, an emissive surface glows and lights nothing.
- **Real-time lights everywhere.** Each one costs, and URP has per-object light limits;
  past them, lights silently stop affecting objects.
- **Judging in the scene view.** It has its own lighting settings and its own camera.
  Judge in the game view, in play mode.
- **Never checking the night setup.** Materials tuned only at noon fall apart under
  every other condition.

## Prove it with Proving Ground

Rendering is the one area where the aesthetic question is genuinely aesthetic - but most
of what goes wrong is not aesthetic at all.

- `pg_capture` writes a screenshot with labelled boxes and a legend. Read them together:
  the legend tells you what each thing in the frame is, which is exactly what a vision
  model gets wrong on a dark or busy scene.
- `pg_visual_check` compares a capture with its stored baseline and writes a diff image
  on failure. This is how a lighting change that broke a scene you were not looking at
  gets caught.
- `pg_view` answers what is on screen, at what distance, and what is occluded, without
  interpretation. "Is the landmark visible from the spawn" is a question with an exact
  answer.
- `pg_check ui` covers the contrast half of legibility, which is a rendering question as
  much as a UI one: text over a bright scene fails the same check as text over a bright
  panel.
- `pg_console` catches shader compilation errors, which otherwise show up only as
  magenta.

## References

- `references/urp-and-lighting.md` - URP asset and renderer settings, renderer features
  and custom passes, lightmapping in depth, probe placement, camera stacking, the
  Rendering Debugger and Frame Debugger, and a look-development checklist.

## Related skills

- `performance-optimization` - GPU cost, overdraw, batching and shadow budgets.
- `unity-vfx` - particle materials, blend modes and transparent sorting.
- `game-production` - where the lighting pass belongs, and why it is late.
- `level-design` - light is the strongest guidance tool, and the lighting pass can undo
  the guidance the blockout established.
