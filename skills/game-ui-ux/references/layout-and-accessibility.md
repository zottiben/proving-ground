# Layout, flow and accessibility - depth for `game-ui-ux`

## 1. Anchoring, in one page

A `RectTransform` has anchors (a fraction of the parent, 0-1 on each axis) and offsets
(pixels from those anchors). The whole system follows from that.

| Intent | anchorMin / anchorMax | Then |
|---|---|---|
| Pin to a corner | both at that corner, e.g. (1,1) | set `anchoredPosition` and `sizeDelta` |
| Stretch across the top | (0,1) and (1,1) | `offsetMin.x`, `offsetMax.x` are left and right margins |
| Fill the parent | (0,0) and (1,1) | zero both offsets |
| Fixed height, full width | (0,1) and (1,1) | `sizeDelta.y` is the height |

Two facts that clear up most confusion: when anchorMin and anchorMax differ on an axis,
`sizeDelta` on that axis is a *margin*, not a size. And `anchoredPosition` is measured
from the pivot to the anchor, so moving the pivot moves the element without changing a
single number in the inspector.

**Layout groups** (Horizontal, Vertical, Grid) plus `ContentSizeFitter` handle dynamic
content, and they are worth using for anything list-shaped. They are also a common
performance and layout-thrash source: nested fitters inside fitters rebuild repeatedly,
and the usual fix is one fitter at the top of a subtree rather than one on every child.

## 2. UI Toolkit equivalents

If the screen is built in UI Toolkit, the concepts map:

| uGUI | UI Toolkit |
|---|---|
| Canvas | `UIDocument` plus a Panel Settings asset |
| RectTransform anchors | flexbox: `position`, `flex-direction`, `align-items` in USS |
| Canvas Scaler | Panel Settings scale mode and reference resolution |
| Layout groups | flexbox layout, which is the default |
| Prefab | UXML template, instantiated with `TemplateContainer` |
| Style on the component | USS selectors and classes |

The mental shift is that layout is declarative and cascading, which is faster to author
for dense screens and less direct for a HUD element that has to sit exactly over a
world position. For world-anchored UI, uGUI in world space is still the simpler answer.

## 3. HUD information hierarchy

Rank everything on screen into three tiers, and be ruthless:

- **Always** - the two or three things the player must know at every instant. Health,
  ammo, the objective marker. Nothing else qualifies.
- **On change** - appears when it changes, fades after a few seconds. Score, pickups,
  status effects.
- **On demand** - a map, an inventory, a quest log. Behind a button.

A HUD with eight always-visible elements has no hierarchy, which means the player reads
none of them. The test: cover the screen, ask what the player would miss, uncover, and
remove everything they did not name.

Placement conventions exist because players have learned them: health bottom-left or
bottom-centre, ammo bottom-right, minimap a corner, objectives top-left or centre-top,
crosshair centre. Deviating is allowed and costs a learning tax; do it deliberately.

## 4. Menu flow and state

Model the shell as a stack, not as a set of booleans. Every screen pushes; back pops.

```csharp
// A stack means "back" is always correct, and nesting three screens deep does not
// require anyone to remember what to re-enable.
readonly Stack<Screen> _stack = new();

public void Push(Screen screen) {
    if (_stack.Count > 0) _stack.Peek().SetInteractable(false);
    _stack.Push(screen);
    screen.Show();
    EventSystem.current.SetSelectedGameObject(screen.FirstSelected);   // gamepad focus
}

public void Back() {
    if (_stack.Count == 0) return;
    _stack.Pop().Hide();
    if (_stack.Count > 0) {
        var top = _stack.Peek();
        top.SetInteractable(true);
        EventSystem.current.SetSelectedGameObject(top.LastSelected);   // restore focus, not reset it
    }
}
```

The details people miss: restoring the previously selected element rather than jumping
to the first, disabling interaction on covered screens so a stray click reaches
something invisible, and pausing the game exactly once regardless of stack depth.

## 5. Controller navigation

- **Set the first selected object** for every screen. A menu that opens with nothing
  selected is dead to a gamepad.
- **Explicit navigation** where automatic gets it wrong, which is any non-grid layout.
- **A focus state that is unmistakable.** A subtle tint is not enough; use scale,
  outline, or a moving highlight.
- **Never lose focus.** If the focused element is disabled or destroyed, move focus
  somewhere valid in the same frame.
- **Consistent buttons.** Confirm and cancel on the platform's conventional face
  buttons, and the same everywhere in the game.

## 6. Localisation, planned for or paid for

- Strings live in data, never in code or in a serialized field on a prefab.
- Containers sized for roughly 1.5-2x the English string length. German and Finnish are
  the usual offenders; some languages are shorter and leave gaps instead.
- Auto-sizing with a **minimum** so it shrinks gracefully rather than to nothing.
- No sentence assembled from fragments. Word order differs by language and the result
  is nonsense. One string per sentence, with placeholders.
- Fonts with the glyph coverage for every language you claim to support, and a fallback
  chain for the rest. A missing glyph renders as nothing, which `pg_check ui` reports as
  a zero-glyph label.
- Number, date and currency formatting through the culture, not by hand.

## 7. The accessibility checklist

Structural, and checkable:

- Contrast at least 4.5:1 for body text, 3:1 for large text (24 px, or 19 px bold).
- Font size at or above 16 px at the reference resolution, and a UI scale option.
- Interactive targets at least 44 px on their smallest dimension.
- Nothing clipped, nothing rendering zero glyphs, nothing outside the safe area.
- No interactive elements overlapping.

Design-level, and expected:

- **Colour is never the only channel.** Team identity, status, danger - all need a shape,
  an icon or a label as well.
- **Subtitles on by default**, with a background plate, a size option, and speaker names.
- **Every timed input has an alternative** or an adjustable window.
- **Reduce motion** - covers camera shake, flashing, parallax and screen transitions.
- **Remappable everything**, including menus.
- **Text speed and auto-advance** are player settings, not designer constants.

Every item on the first list is a `pg_check ui` finding. The second list is
implementation the check cannot see, which is exactly why it is worth writing down.

## 8. Reviewing a screen

1. At 1920x1080, at 1280x720, and at a phone aspect - does anything move, clip or leave
   the safe area?
2. With a gamepad only - can every element be reached, and is the focus visible at a
   glance?
3. In greyscale - is anything now ambiguous?
4. With the longest localised string - does anything overflow?
5. On the busiest gameplay background - is the text still legible?
6. Does every button say what happened when pressed?

`pg_check ui` answers 1, 3 and 5 numerically. The others need a person, which is why
the list is short.
