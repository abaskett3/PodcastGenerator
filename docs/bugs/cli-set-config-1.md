# Bug: `--set-config` with a differently cased key renames a working line, so the app no longer finds the key

- Slug: `cli-set-config`, found in round 1 at CODE_HEAD `8323860b6a9283479d177cac49f5de05eaae3d97`. Issue #2.
- **Status: FIXED, verified in round 2 at CODE_HEAD `2b9e2900f72788e56b2f103c2cad3cf2e295901e`.** The user decided (fix round 1)
  that config keys are read case-insensitively and that `--set-config` keeps a key unique (remove every line for the key, append
  one). The fix commit applies both. See "Verification (round 2)" at the end. The rest of this file is the original report, kept
  as it was written.
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

## Verification (round 2)

The spec now says (marked "user decision, fix round 1"): keys are read case-insensitively (Constraints), and after `--set-config`
the key is unique, with every line for it removed and one line appended in the typed spelling (AC-3). At `2b9e290`:

- `EnvFileParser.GetValue` compares with `OrdinalIgnoreCase` (`EnvFileParser.cs:33`); `EnvFileEditor.SetValue` removes every line
  for the key and appends one (`EnvFileEditor.cs:21-25`).
- Regression tests in `Integration/ConfigIntegrationTests.cs`, all passing:
  `Set_config_with_a_lowercase_key_keeps_the_key_usable_and_the_file_unique` (the exact reproduction above: the file holds one line,
  `openrouter_api_key=sk-test-NEW-LOWER`, the parser finds the value, the next run sends it with no prompt),
  `A_lowercase_key_line_in_the_file_is_present_and_used_without_a_prompt`,
  `Set_config_leaves_one_line_in_the_spelling_typed_whatever_spellings_the_file_had` (3 typed spellings) and
  `A_file_with_the_key_in_two_spellings_uses_the_last_one` (2 orders).
- These tests fail on the pre-fix code and pass on the fix. I ran the round 2 `ConfigIntegrationTests.cs` against the source of my
  round 1 commit `b32a999` (production code as it was before the fix), in a copy outside the repository: 23 of the 117 tests
  failed, including all four tests above and the `""` / `''` rows, with the same "saved but missing" outcome as reported here; on
  `2b9e290` all 117 pass.
