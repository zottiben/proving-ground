---
name: game-ui-ux
description: Design and build game UI in Unity - uGUI and UI Toolkit, anchoring and scaling across resolutions, HUD layout and information hierarchy, menu flow and navigation, diegetic versus screen-space choices, feedback and state, plus the accessibility floors that are wrong at any aesthetic. Use when building a HUD, menus, inventory or settings screen, when UI breaks at another resolution or aspect, when text is unreadable, or when a design needs verifying against a spec.
---

# Game UI and UX

Game UI fails in two distinct ways, and they need different fixes. It fails
**structurally** - wrong anchors, unreadable at 1080p, clipped on a phone, buttons too
small to hit - and it fails **as design** - too much on screen, no hierarchy, unclear
what is interactive.

The structural half is entirely checkable, and Proving Ground checks it: contrast
ratios, hit target sizes, font sizes, clipped text, safe area, and every property in a
manifest you write. The design half needs judgement. Do not spend judgement on things
a check can answer.

## When to use

- Use when building a HUD, menu, inventory, settings screen or dialogue box.
- Use when UI breaks at a different resolution, aspect ratio or DPI.
- Use when text is unreadable, overlapping, clipped or too small.
- Use to write a UI manifest so the design is a spec rather than a memory.

**When *not* to use:** for the feel of a button press - the pop, the sound - use
`game-feel`. For menu navigation input and rebinding, `input-systems`. For text-heavy
narrative UI, `dialogue-systems`.

## uGUI or UI Toolkit

Both ship with Unity and both are supported. The honest split:

- **uGUI** (Canvas, RectTransform, TextMeshPro) - the mature option for in-world and
  runtime game UI. Everything integrates with it, artists know it, and world-space
  canvases work. Its weakness is that a large canvas rebuilds as a unit and gets
  expensive.
- **UI Toolkit** (UXML, USS, UIDocument) - retained-mode, styled with something close to
  CSS, and the right answer for editor tooling and for data-dense screens where the
  layout system earns its keep. Its weakness is that world-space rendering and some
  runtime integrations are less complete.

Pick one per screen family and do not mix them inside one screen. Proving Ground
collects facts from both, so the manifest and the checks work either way.

## Core workflow

1. **Decide the information hierarchy before the layout.** What must the player know
   always, what on demand, what only in an emergency. Everything else is decoration and
   should be cut.
2. **Set the reference resolution and scale mode first.** Canvas Scaler, Scale With
   Screen Size, a reference resolution you actually target, and a match value chosen
   deliberately. Everything anchors from there.
3. **Anchor to intent, not to pixels.** Anchor to the corner or edge the element belongs
   to. An element anchored to the centre that should hug a corner is a bug that only
   appears on someone else's monitor.
4. **Respect the safe area.** `Screen.safeArea` on notched and rounded displays, and a
   margin from the screen edge everywhere else.
5. **Write the manifest as you build.** `ProvingGround/Contracts/ui.json`: tokens for
   colours and sizes, one entry per element with what it must be.
6. **Check it, do not eyeball it.** `pg_check ui` reports every disagreement with the
   manifest in one pass, plus contrast, hit targets, clipped text and safe area.
7. **Navigate with a gamepad before shipping.** If the menu cannot be driven without a
   mouse, it is not finished.

## Patterns

### 1. Canvas setup that survives other people's screens

```csharp
// Set once, deliberately. The match value is the setting people leave at 0 and regret.
var scaler = canvas.GetComponent<CanvasScaler>();
scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
scaler.referenceResolution = new Vector2(1920, 1080);
scaler.screenMatchMode    = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
scaler.matchWidthOrHeight = 0.5f;   // 0 = width-driven, 1 = height-driven, 0.5 = balanced
```

Match 0 means an ultrawide monitor scales everything up until the HUD swallows the
screen. Match 1 means a tall phone does the same. 0.5 is the safe default; choose
something else only when you know which axis your layout depends on.

### 2. Safe area, applied once

```csharp
// Notches, rounded corners and system bars eat the edges. One component on a
// full-screen child of the canvas, and every screen inherits the fix.
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour {
    RectTransform _rect;
    Rect _applied;

    void Awake() => _rect = GetComponent<RectTransform>();

    void Update() {
        var safe = Screen.safeArea;
        if (safe == _applied) return;                       // only when it changes
        _applied = safe;

        var min = safe.position;
        var max = safe.position + safe.size;
        min.x /= Screen.width;  min.y /= Screen.height;
        max.x /= Screen.width;  max.y /= Screen.height;

        _rect.anchorMin = min;
        _rect.anchorMax = max;
        _rect.offsetMin = _rect.offsetMax = Vector2.zero;
    }
}
```

### 3. The manifest as the spec

```jsonc
// ProvingGround/Contracts/ui.json - the design system as data, so "the health bar is
// the wrong red" becomes a failing check instead of an argument.
{
  "tokens": {
    "danger": "#C8452B", "surface": "#14171C", "text": "#F2F4F7",
    "titleSize": "48", "bodySize": "18"
  },
  "elements": {
    "health.bar":   { "match": "HUD/Health/Fill",  "expect": { "color": "$danger" } },
    "health.label": { "match": "HUD/Health/Label",
                      "expect": { "fontSize": "$bodySize", "color": "$text" },
                      "contrastAgainst": "hud.background" },
    "pause.resume": { "match": "PauseMenu/Resume",
                      "expect": { "minHeight": "48", "text": "Resume" } }
  },
  "global": {
    "minHitTargetPx": 44, "minFontSizePx": 16,
    "minContrastRatio": 4.5, "minContrastRatioLargeText": 3.0,
    "forbidClippedText": true, "enforceSafeArea": true
  }
}
```

Matching is by path suffix, so re-parenting an element does not invalidate the whole
manifest. Values may reference a token with `$name`, which means changing the palette
is one edit rather than forty.

### 4. Keeping a canvas cheap

```csharp
// A canvas rebuilds as a unit. Split by update frequency, not by screen area.
//   Canvas: Static     background, frame, labels that never change
//   Canvas: Dynamic    health, ammo, timer - anything that changes per frame
//   Canvas: Popups     enabled rarely, disabled the rest of the time
//
// And on every non-interactive Image and Text:
_image.raycastTarget = false;   // the default is true, and raycasting costs per element
```

Disabling a canvas component is much cheaper than deactivating the GameObject
hierarchy: the objects stay alive and the canvas simply stops drawing and rebuilding.

## Pitfalls

- **Anchors left at centre.** Works at your resolution, wrong at every other. Anchor to
  the edge or corner the element belongs to.
- **Canvas Scaler at Constant Pixel Size.** UI shrinks to nothing on a 4K display.
- **Match value at 0 or 1 by accident.** One axis drives everything; the other aspect
  ratio breaks.
- **Ignoring the safe area.** Text under a notch, buttons under a home indicator.
- **Text that fits your string.** Localisation is longer, sometimes twice as long. Size
  containers for the longest plausible string, and use auto-sizing with a floor.
- **Font size below the legibility floor.** 16 px at the reference resolution is the
  floor here, and it is a floor rather than a target - 12 px on a screen someone plays
  from a sofa is invisible.
- **Hit targets under 44 px.** Especially with a gamepad or on touch. Grow the target,
  not just the visual.
- **Low contrast because it looked stylish.** WCAG AA is 4.5:1 for body text and 3:1 for
  large text. A dark grey label on a dark grey panel is a defect, not a mood.
- **Interactive elements overlapping.** Hit testing becomes ambiguous, and which one
  wins depends on sibling order nobody set deliberately.
- **Raycast Target on every image.** Every one of them is tested on every pointer event.
- **One canvas for the whole game.** One change rebuilds everything.
- **No gamepad navigation.** Explicit navigation between elements, a sensible first
  selected object, and a visible focus state that is not just a subtle tint.
- **UI that never says what happened.** Every action needs a response - a state change,
  a sound, a message. Silence reads as a broken button.
- **No pause, no confirm on destructive actions, no way back.** The shell around the
  game is part of the game.

## Prove it with Proving Ground

`pg_check ui` is a single pass that reports every disagreement with the manifest plus
the accessibility floors, against the **live hierarchy** and what elements actually
resolved to at runtime rather than what the code intended. That distinction is the
whole point: a colour set from a theme at runtime is checked as the colour that
appeared, not the one in the prefab.

What it catches without you writing anything: contrast below 4.5:1 (3:1 for large
text), hit targets under 44 px, fonts under 16 px, clipped text, labels that rendered
zero glyphs, interactive elements overlapping, anything outside the safe area.

```
pg_check ui        the manifest, plus accessibility, in one pass
pg_capture         when the question is genuinely aesthetic - read the legend with it
pg_visual_check    compare a screen against its stored baseline after a change
```

Accessibility findings are the ones worth trusting without a human: text below the
legibility floor and targets under 44 px are wrong at any aesthetic. For everything
genuinely subjective, gather it into one batch and ask the user once rather than asking
forty times whether each element looks right.

## References

- `references/layout-and-accessibility.md` - anchoring and layout groups in depth, UI
  Toolkit equivalents, HUD information hierarchy, menu flow and state, controller
  navigation, localisation, and the full accessibility checklist.

## Related skills

- `game-feel` - button pops, transitions and the feedback layer over the UI.
- `input-systems` - menu navigation, prompts and the rebinding screen.
- `performance-optimization` - canvas rebuilds are a top CPU cost in most projects.
- `dialogue-systems` - text-heavy narrative UI and its own timing rules.
- `unity-rendering` - world-space UI, sorting and post-processing interactions.
