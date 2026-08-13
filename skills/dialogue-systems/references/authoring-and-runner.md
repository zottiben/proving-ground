# Authoring formats and the runner - depth for `dialogue-systems`

## 1. Ink

Ink is a scripting language for branching narrative, authored in plain text files
compiled to JSON, with a Unity integration package. It suits writers: the syntax reads
like a screenplay with markup, and the state model is built in.

```ink
=== dockside ===
The gulls are the only ones working today.
{ player_has_key:
    * [Show him the key] -> he_recognises_it
}
* [Ask about the shipment] -> shipment
* [Leave] -> DONE

=== he_recognises_it ===
He goes very still. "Where did you get that."
~ suspicion = suspicion + 2
-> shipment
```

What it gives you: choices with conditions, variables and arithmetic, weighted and
sequenced alternatives, tunnels and threads for reusable content, and a save format for
its own state. External functions let a story ask the game a question, and tags on a
line carry metadata - speaker, emotion, camera - to the presenter.

The integration surface in Unity is small: load the compiled JSON into a `Story`, call
`Continue()` for the next line, read `currentChoices`, call `ChooseChoiceIndex`.
Everything else is your presenter.

## 2. Yarn Spinner

Yarn Spinner uses node-based `.yarn` files, has a Unity package with a built-in dialogue
view, and offers a visual editor. It suits conversation-heavy games where the structure
is nodes and jumps rather than deep state.

```yarn
title: dockside
---
Dockhand: The gulls are the only ones working today.
-> Ask about the shipment
    <<jump shipment>>
-> Show him the key
    <<if $player_has_key>>
    <<jump he_recognises_it>>
-> Leave
===
```

Commands (`<<command args>>`) are the seam into the game, and variables can be stored in
your own variable storage implementation, which is how dialogue state ends up in the
same save file as everything else rather than in its own.

## 3. A runner interface that survives a format change

Whichever you choose, put an interface in front of it. Formats get replaced; your
presenter should not care.

```csharp
public interface IDialogueRunner {
    bool IsRunning { get; }
    void Start(string nodeName);
    void Continue();                                   // advance to the next line
    void Choose(int index);
    event Action<DialogueLine> LinePresented;          // speaker, text, tags
    event Action<IReadOnlyList<string>> ChoicesPresented;
    event Action Finished;
    void RegisterCommand(string name, Action<string[]> handler);
    void RegisterVariable(string name, Func<object> getter);
}

public readonly struct DialogueLine {
    public readonly string Speaker, Text, AudioEvent;
    public readonly IReadOnlyList<string> Tags;
}
```

The presenter subscribes to the events and knows nothing about Ink or Yarn. The runner
implementation is the only file that changes if the format does, and swapping it becomes
an afternoon rather than a rewrite.

## 4. Conditions and variables

Two directions, and they should not be confused:

- **The game tells dialogue about itself** through variables the writer reads:
  `player_has_key`, `reputation_dockers`, `chapter`. Register these as getters so they
  are always current rather than snapshots.
- **Dialogue tells the game to do something** through commands: `give`, `setflag`,
  `camera`, `play`. Each is a capability a writer now has.

Keep the variable namespace flat and named for the fiction, not for the implementation:
`met_the_fixer`, not `npc_04_dialogue_state`. Writers use these constantly, and the
naming is the difference between a system they use fluently and one they ask about.

Variables that dialogue writes should live in the same store as the ones the game
writes, and both should be saved together. Two state systems that partially overlap is
a bug generator.

## 5. Localisation

- **One string per line, with placeholders.** `"You need {0} more."`, never `"You need "
  + n + " more."`.
- **Line IDs, stable and never reused.** Both Ink and Yarn support tagging lines with
  IDs; the ID is what the translation table is keyed on, so changing a line's text must
  not change its ID and reusing an ID silently mistranslates.
- **Extract, do not embed.** Translations live in their own tables, generated from the
  source, so a writer editing the English does not touch fifteen other languages.
- **Plurals and gender go through the localisation system**, not through string
  formatting. Languages have more plural forms than English does.
- **Font coverage** for every language claimed, with a fallback chain. A missing glyph
  renders as nothing, which `pg_check ui` reports as a zero-glyph label - one of the few
  ways to catch a broken translation without reading the language.
- **Test with the longest language you support**, and with a pseudo-localisation pass
  that doubles string length. Layout breaks are cheaper to find this way than after
  translation.

## 6. Voice-over synchronisation

- **Line ID is the filename.** `dockside_014.wav` for line `dockside_014`. Any other
  scheme drifts, and re-recording becomes a manual reconciliation.
- **Subtitle duration comes from the clip length** when there is voice, and from a
  characters-per-second estimate when there is not. Around 15-20 characters per second is
  a comfortable reading rate; give a minimum of about 1.5 s regardless of length.
- **The line advances when the audio finishes**, unless the player advances sooner. Never
  cut a line off without the player asking.
- **Missing audio must not block.** A line with no recorded clip falls back to the
  estimated duration, so a partially recorded build is still playable - which is most of
  a project's life.
- **Duck the mix** while dialogue plays. See `audio-design`.

## 7. Cutscene integration

Dialogue during a scripted sequence is where two systems both want control. The rules
that keep it working:

- The sequence owns the camera and the characters; dialogue owns the text and the
  timing. One of them has to be authoritative for each thing.
- Dialogue that must wait for an animation waits on a signal, not on a duration. Timings
  change every time an animation is re-exported.
- Everything is skippable, including the first playthrough, and skipping must land the
  world in the same state as playing through it. Test this specifically: the state after
  a skip diverging from the state after playing is a classic and painful bug.
- Pausing during a cutscene must pause both. Two independent timelines drift.

## 8. Reviewing dialogue in-game

Read it in the game, not in the file. Things only visible in the game:

1. Lines that overflow the box in another language.
2. Lines that reveal too slowly to be comfortable, or too fast to read.
3. A choice list longer than the box.
4. A conversation that cannot be advanced with a gamepad.
5. Subtitles unreadable over that particular background.
6. A speaker name that is wrong because the node was reached from an unexpected branch.

The last one is the one testing finds and reading never does, and it is why a recorded
scenario through a branching conversation is worth keeping.
