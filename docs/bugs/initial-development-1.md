# Bug: spoken words between two square-bracket cues on one line are dropped

- **Status: fixed at `b376719` (round 2).** The test below passes there and fails on `5413ba3`. `CueRegex` now matches only lines made
  of bracket groups and spaces; the tests stay as regression tests.
- Slug: `initial-development`, round 1, CODE_HEAD `5413ba3`
- Behavior affected: **AC-34** ("A line consisting only of a square-bracket cue ... is removed") and **AC-36** ("The tool
  adds, removes or rewords no word of the script beyond AC-28 to AC-35 ... a script that departs from the guide still runs").
- Severity: low. The line breaks the guide's layout rule 2 ("a cue is never part of a spoken line"), so it needs a malformed
  script, but AC-36 says a script that departs from the guide still runs and is narrated as written.

## Where

`src/PodcastGenerator.Application/Scripts/ScriptParser.cs`, line 26 (`CueRegex`, pattern `^\[.*\]$`) used at line 93.
The greedy `.*` makes any line that starts with `[` and ends with `]` a "cue", including one with spoken words between two cues.

## Reproduce

```text
dotnet test --filter "FullyQualifiedName~Spoken_words_between_two_cues_on_one_line_are_not_removed"
```

Test: `tests/PodcastGenerator.UnitTests/Integration/ScriptEdgeCaseTests.cs`,
`Spoken_words_between_two_cues_on_one_line_are_not_removed`. It runs the whole pipeline (real wiring, HTTP faked) on:

```text
CECIL: Before.

[SFX: DOOR] and then he spoke [SFX: DOOR]

CECIL: After.
```

## Expected

The line is not "only a cue", so its spoken words are narrated: the request transcript contains `and then he spoke`. A line of
several cues and nothing else (`[SFX: DOOR SLAM] [MUSIC: STING]`) is still removed; the passing test
`A_line_of_two_cues_is_removed` covers that.

## Actual

The whole line is removed. Real output of the test run:

```text
Assert.Contains() Failure: Sub-string not found
String:    "Before.\n\nAfter."
Not found: "and then he spoke"
```

## Suggested direction (not applied; production code is not mine to edit)

Match only lines made of one or more bracketed cues and whitespace, for example `^\[[^\]]*\](\s*\[[^\]]*\])*$`.
