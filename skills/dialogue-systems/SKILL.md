---
name: dialogue-systems
description: Build dialogue and narrative delivery in Unity - authoring formats like Ink and Yarn Spinner, a runner that separates content from presentation, typewriter text that does not break rich text, choices and conditions, barks, subtitles and localisation, and saving conversation state. Use when adding dialogue, conversations, choices, barks or subtitles, when text displays incorrectly or advances by itself, or when narrative content needs to be authored by someone who is not a programmer.
---

# Dialogue systems

Dialogue is content, and content changes constantly. That single fact decides the
architecture: the writer must be able to change a line, add a branch or fix a typo
without touching code or opening the Editor, or the writing stops improving.

So the shape is always the same. **Content in a format writers can edit. A runner that
walks it. A presenter that displays it.** Three pieces, and the value is in the seams
between them - swap the presenter for a different UI, swap the content for another
language, and nothing else changes.

## When to use

- Use when adding conversations, choices, barks, subtitles or narrative text.
- Use when text renders wrong, advances by itself, or breaks with rich text tags.
- Use when writers need to author or edit content without a programmer.
- Use when dialogue needs to react to game state, or the game needs to react to dialogue.

**When *not* to use:** for the UI layout and legibility of the dialogue box,
`game-ui-ux`. For voice-over mixing and ducking, `audio-design`. For persisting
conversation state, `save-systems` covers the mechanics.

## Choosing a format

| Option | Suits | Cost |
|---|---|---|
| **Ink** (inkle) | branching, state-heavy narrative; writers who want a text file | learning its syntax; integration is a package |
| **Yarn Spinner** | node-based conversations with a visual editor | slightly less expressive than Ink for deep state |
| **ScriptableObject graphs** | small conversation counts, tight engine integration | you build and maintain the tooling |
| **CSV or JSON** | barks, one-liners, simple linear lines | no branching; becomes unmanageable past a point |

Both Ink and Yarn Spinner are mature, both are used in shipped games, and both solve the
same core problem: a writer-editable file with branching, variables and conditions.
Rolling your own is only right when the requirements are genuinely small - barks and a
handful of linear conversations - and it stops being right the moment somebody asks for
a condition.

## Core workflow

1. **Separate content, runner and presenter** from the first line of code. Everything
   else follows from this.
2. **Author in a text format under version control.** Dialogue in a serialized asset is
   dialogue that cannot be diffed, merged or reviewed.
3. **One string per line, with placeholders.** Never assemble a sentence from fragments -
   word order differs by language and the result is nonsense.
4. **Drive the game from dialogue through commands**, and dialogue from the game through
   variables. Two narrow interfaces rather than direct references.
5. **Make the presenter dumb.** It shows a line and reports when the player advanced.
   Every decision lives in the runner.
6. **Subtitles on by default**, with a speaker name and a background plate.
7. **Save what has been said**, not just where the player is. Repeated first-meeting
   dialogue is the classic bug.

## Patterns

### 1. Typewriter text that does not break rich text

```csharp
// The obvious implementation - assigning a growing substring - breaks the moment a line
// contains <b> or <color>, because it will slice the tag in half and render it as text.
// TextMeshPro's maxVisibleCharacters reveals glyphs without touching the string.
IEnumerator Reveal(TMP_Text label, string line, float charactersPerSecond) {
    label.text = line;                       // set once, tags parsed once
    label.maxVisibleCharacters = 0;
    label.ForceMeshUpdate();                 // so characterCount is correct now

    int total = label.textInfo.characterCount;
    float interval = 1f / charactersPerSecond;

    for (int visible = 0; visible <= total; visible++) {
        label.maxVisibleCharacters = visible;
        if (_skipRequested) { label.maxVisibleCharacters = total; break; }
        yield return new WaitForSecondsRealtime(interval);   // unscaled: dialogue during a pause
    }
    _revealComplete = true;
}
```

Two details that matter as much as the technique: `ForceMeshUpdate` before reading
`characterCount`, or the count is from the previous line; and unscaled time, because
dialogue frequently plays while the game is paused.

### 2. Advance that cannot double-fire

```csharp
// The single most common dialogue bug: one press reveals the line AND advances past it,
// so the player never reads it. Require a release between the two.
void Update() {
    if (!_advance.WasPressedThisFrame()) return;

    if (!_revealComplete) { _skipRequested = true; return; }   // first press: finish the line
    if (Time.unscaledTime - _lineShownAt < _minimumLineSeconds) return;  // guard against a held press
    _runner.Continue();                                        // second press: next line
}
```

A minimum display time of 0.2-0.4 s per line stops a held button from skipping an entire
conversation, which players do by accident far more often than deliberately.

### 3. The seam between dialogue and game

```csharp
// Dialogue asks the game questions through variables, and tells it to do things
// through commands. Two narrow interfaces, and no direct references either way.
_runner.RegisterVariable("player_has_key",  () => Inventory.Has(ItemId.Key));
_runner.RegisterVariable("reputation",      () => Faction.Reputation("dockers"));

_runner.RegisterCommand("give",    args => Inventory.Add(ItemId.Parse(args[0])));
_runner.RegisterCommand("setflag", args => World.SetFlag(args[0], true));
_runner.RegisterCommand("camera",  args => Cameras.Focus(args[0]));
```

Every command is a thing a writer can now do without asking a programmer, which is the
whole point. Keep the list short and orthogonal; a hundred bespoke commands is a
scripting language you did not design.

### 4. Barks, which are not conversations

```csharp
// A bark is one line, unblocking, positional, and it must not repeat.
public void Bark(string category, Transform speaker) {
    if (Time.time - _lastBarkAt < _globalCooldown) return;      // never two at once
    var line = _barks.Next(category);                           // shuffle bag, not random
    if (line == null) return;

    Subtitles.ShowFloating(line.Text, speaker, line.DurationSeconds);
    Audio.Post(line.AudioEvent, speaker.position);
    _lastBarkAt = Time.time;
}
```

A shuffle bag - draw without replacement, reshuffle when empty - is the difference
between an NPC that seems to have things to say and one that says the same thing twice
in a row. Plain random will repeat, and players notice immediately.

Fifty to a hundred lines per archetype is what makes a crowd feel populated, and barks
are the cheapest narrative content there is.

### 5. What gets saved

```csharp
// Not just the current node: what has been said, and every variable the writer set.
[Serializable] public class DialogueState {
    public string CurrentNode;                       // null outside a conversation
    public List<string> VisitedNodes = new();        // so "first meeting" happens once
    public Dictionary<string, string> Variables = new();
}
```

Saving the position without the visited set produces the bug where every character
introduces themselves again after a reload, which reads as the game having forgotten the
player.

## Pitfalls

- **Substring typewriters.** Break rich text tags, and allocate a string per frame. Use
  `maxVisibleCharacters`.
- **One press revealing and advancing.** The line is never read. Require a release.
- **Scaled time in dialogue.** Freezes the text when the game pauses.
- **Sentences assembled from fragments.** Untranslatable, and the fix is expensive once
  the pattern is established.
- **Dialogue in serialized assets.** Cannot be diffed, merged or reviewed, and two
  writers cannot work at once.
- **Direct references from dialogue into gameplay types.** Every refactor breaks the
  content. Go through commands and variables.
- **No skip.** Players who have heard a line will hear it again on every retry, and they
  will resent it. Skip everything, including cutscenes, including the first time.
- **Text sized for English.** Other languages are up to twice as long. Size the box for
  the longest plausible string.
- **Subtitles off by default**, or without speaker names, or over a background that makes
  them unreadable. All three are accessibility failures.
- **Random barks.** Immediate repetition. Shuffle bag.
- **Barks that interrupt each other.** A global cooldown and a priority, or a crowd
  becomes noise.
- **Not saving visited nodes.** Everyone reintroduces themselves after a reload.
- **Choices with no visible consequence.** If a branch changes nothing, the player learns
  their choices do not matter - and that lesson generalises to the ones that do.

## Prove it with Proving Ground

Dialogue is UI plus audio plus state, and all three are checkable.

- `pg_check ui` against a `ui.json` manifest: contrast for subtitles, font size against
  the legibility floor, clipped text, and labels that rendered zero glyphs - which is
  what a missing font glyph looks like in a language you do not read.
- `pg_check audio` after a run: a voice line event that never fired, or one firing far
  more often than a conversation should produce.
- `pg_record` while a writer or tester plays through the conversation that misbehaves.
  The result is a deterministic scenario that reproduces it, which for a branching
  conversation is worth considerably more than a description.
- `pg_run_scenario` with `submit` and `cancel` presses to walk a conversation and assert
  the resulting world state.
- `pg_events` to confirm the line fired once, and in the frame you expected.

## References

- `references/authoring-and-runner.md` - Ink and Yarn Spinner integration in Unity,
  a runner interface that survives swapping formats, conditions and variables,
  localisation pipeline, voice-over synchronisation, and cutscene integration.

## Related skills

- `game-ui-ux` - the dialogue box itself: layout, legibility and safe area.
- `audio-design` - voice-over, ducking, and the mix that keeps dialogue intelligible.
- `save-systems` - persisting conversation state and variables.
- `game-production` - narrative delivery cheap to expensive, and cast size.
