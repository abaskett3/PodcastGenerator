# Bug: `--set-config` with a differently cased key renames a working line, so the app no longer finds the key

- Slug: `cli-set-config`, round 1, CODE_HEAD `8323860b6a9283479d177cac49f5de05eaae3d97`. Issue #2.
- Behavior affected: **AC-3** (update the line "in place with `<VALUE>`") together with the spec's Constraints (the file format is
  defined by the existing parser, which matches the key with an exact-case comparison) and **AC-7** (a value is present when the
  file has a non-empty value for it). Design D-8 chose this behavior on purpose, so it needs a decision, not only a code change.
- Severity: medium. The command reports success and exit code 0, and the user's previously working key stops working.

## Where

`src/PodcastGenerator.Application/Runtime/EnvFileEditor.cs:27`: the matched line is replaced by `$"{key}={value}"`, with `key`
spelled as given on the command line. The match (`IsLineFor`, line 75) is `OrdinalIgnoreCase`, but the reader
(`src/PodcastGenerator.Application/Runtime/EnvFileParser.cs:29`) compares with `StringComparison.Ordinal`.

## Reproduce

Start with a key file that works: one line, `OPENROUTER_API_KEY=old-good`, in a temporary profile.

1. Run `--set-config openrouter_api_key new-lower` (lowercase key).
2. Read the file.
3. Run a podcast (or ask `EnvFileParser.GetValue(contents, "OPENROUTER_API_KEY")`).

I ran this through the integration harness (`Pipeline` with a fake profile and a canned HTTP handler) in a temporary test, which I
removed afterwards. Output:

```text
A: exit=0 file=[openrouter_api_key=new-lower
A: parser reads OPENROUTER_API_KEY -> (missing)
A: next run exit=1 asked=1 err=[Error: OPENROUTER_API_KEY was not found and there is no console input to ask for it. ...
```

## Expected vs actual

- Expected (spec AC-3): the line for the key is updated with the new value, and "every other line in the file is left unchanged".
  After a successful command the app should be able to use the value it was just given for that key, and should not lose a value
  that worked before.
- Actual: exit code 0 and `Saved openrouter_api_key.`, but the working line `OPENROUTER_API_KEY=old-good` no longer exists. The file now
  holds `openrouter_api_key=new-lower`, which the app does not read. The next run treats the key as missing and prompts for it (or
  fails when there is no console input).

## Why a decision is needed

The spec matches the key case-insensitively for the update (AC-3) but says nothing about how the line is rewritten, and the reader
is case-sensitive. Two fixes are possible and the spec does not pick one:

- Keep the existing line's key text and replace only the value (the reverse case, a lowercase line that the user updates with the
  correct spelling, then stays lowercase and unread, which is what D-8 tried to avoid); or
- Make the reader match keys without regard to case, so either spelling works.

I have not added a failing test, because the expected result is not fixed by the spec. Once decided, the regression test belongs in
`ConfigIntegrationTests` next to `Set_config_updates_the_existing_line_in_place_and_leaves_every_other_line_unchanged`.
