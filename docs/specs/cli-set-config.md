# CLI set-config command

Source: GitHub issue #2 - Add a path for the user to add their openrouter api-key via the cli

## Summary

Today the user must manually create `PodcastGenerator.env` (and its parent folder) under
`Environment.SpecialFolder.UserProfile`/.config/PodcastGenerator/ before the app will run, and the README walks them
through doing it by hand. This feature removes that manual step entirely: the app creates the config folder and file
itself, adds a `--set-config <KEY> <VALUE>` CLI command so the user can set config values without opening the file,
and, when a required config value is still missing, interactively asks the user for it before continuing. The only
required value today is `OPENROUTER_API_KEY`, but the required-value list and the `--set-config` command are both
meant to support more config values later. All documentation telling the user to manually edit or create the file or
its folder is removed as part of this feature.

## User-facing behavior

- New command: `PodcastGenerator --set-config <KEY> <VALUE>`.
  - `<KEY>` must not be blank, an empty string, or contain any whitespace (including internal spaces); an invalid
    `<KEY>` is rejected with the error message `Invalid input` and the command exits with a failure code, making no
    change to the file.
  - `<VALUE>` must not be blank, an empty string, or whitespace-only; an invalid `<VALUE>` is rejected the same way,
    with the same `Invalid input` message and failure exit code.
  - Writes `KEY=VALUE` into `PodcastGenerator.env` in the runtime config folder
    (`Environment.SpecialFolder.UserProfile`/.config/PodcastGenerator/PodcastGenerator.env`, built with `Path.Combine`).
  - Creates the config folder and/or the file first if either does not exist yet.
  - If `PodcastGenerator.env` already has a line for that key (matched case-insensitively), that line is updated in
    place with the new value instead of adding a second line for the same key. If the key is not yet in the file, a
    new `KEY=VALUE` line is added.
  - Like `--help` and `--version`, this command does not go on to generate a podcast; it sets the value and exits.
- Main command (generating a podcast, i.e. any invocation that is not `--set-config`, `--help`, or `--version`):
  - Before doing anything else, checks whether the config folder and `PodcastGenerator.env` exist. If either is
    missing, creates them.
  - Then checks each required config value (currently just `OPENROUTER_API_KEY`). A value is considered present if
    it has a non-empty value in `PodcastGenerator.env`, or if it is set as a non-empty environment variable; either
    source satisfies the check, so a value supplied only via the environment variable does not trigger a prompt.
  - For each required value that is missing from both the file and the environment, the app prints
    `<CONFIG_KEY_NAME_HERE> not found. Please set this config value to continue.` (with the actual key name in place
    of the placeholder) and interactively prompts the user for it on the console.
    - Rejects a blank entry, an empty string, or a whitespace-only entry with the `Invalid input` message, and
      re-prompts.
    - The user gets up to 5 attempts total for that value. If the 5th attempt is also invalid, the run fails (exit
      code 1) without generating a podcast.
    - Once a valid value is entered, it is appended as a new `KEY=VALUE` line to `PodcastGenerator.env`. This path
      always appends; it never edits or removes an existing line in the file (unlike `--set-config`, which updates an
      existing line for the same key in place).
  - Once every required value is present (whether it was already there, satisfied by an environment variable, or
    just supplied through the prompt), the run continues as normal.
  - `--help` and `--version` never trigger this check, per the issue.
- Values entered through either path (`--set-config` or the interactive prompt) are not echoed back to the console,
  since a value can be a secret such as an API key (`CLAUDE.md` already bars logging the key; the same reasoning
  applies to a value the user just typed).
- All existing documentation (README and any other doc) that tells the user to manually create or edit the config
  folder, `PodcastGenerator.env`, or its contents is removed, since the app now does this for them. The README still
  documents where the file lives and what it contains, for anyone who wants to inspect it, and documents the new
  `--set-config` command as the supported way to set a value by hand.

## Acceptance criteria

1. **AC-1**: Running `PodcastGenerator --set-config <KEY> <VALUE>` creates the config folder
   (`Environment.SpecialFolder.UserProfile`/.config/PodcastGenerator/) if it does not exist.
2. **AC-2**: Running `PodcastGenerator --set-config <KEY> <VALUE>` creates `PodcastGenerator.env` in that folder if
   it does not exist, then writes `KEY=VALUE` into it.
3. **AC-3**: If `PodcastGenerator.env` already has a line for `<KEY>` (matched case-insensitively), `--set-config`
   updates that line in place with `<VALUE>` rather than adding a duplicate line; every other line in the file is
   left unchanged. If no line for `<KEY>` exists yet, a new `KEY=VALUE` line is added.
4. **AC-4**: `--set-config` rejects a `<KEY>` that is blank, an empty string, or contains any whitespace, and
   rejects a `<VALUE>` that is blank, an empty string, or whitespace-only. Either case prints the exact message
   `Invalid input`, exits with a failure code, and makes no change to `PodcastGenerator.env`.
5. **AC-5**: `PodcastGenerator --set-config <KEY> <VALUE>` exits without generating a podcast (exit code 0 on
   success), the same way `--help` and `--version` do today.
6. **AC-6**: Any invocation other than `--set-config`, `--help`, or `--version` checks, before doing any other work,
   whether the config folder and `PodcastGenerator.env` exist, and creates whichever is missing.
7. **AC-7**: On that same invocation, the app checks each required config value (currently just
   `OPENROUTER_API_KEY`). A value counts as present if it has a non-empty value in `PodcastGenerator.env`, or if a
   same-named environment variable is set to a non-empty value; a value present only in the file, only in the
   environment, or in both, does not trigger a prompt.
8. **AC-8**: For each required value missing from both the file and the environment, the app prints
   `<CONFIG_KEY_NAME_HERE> not found. Please set this config value to continue.` (with the real key name substituted
   for the placeholder) and prompts the user for it interactively on the console.
9. **AC-9**: The interactive prompt rejects a blank, empty-string, or whitespace-only entry with the exact message
   `Invalid input`, and re-prompts, up to 5 total attempts for that value. If the 5th attempt is also invalid, the
   run fails with exit code 1 and does not generate a podcast.
10. **AC-10**: A value accepted from the interactive prompt is appended as a new `KEY=VALUE` line to
    `PodcastGenerator.env` without changing or removing any line already in the file, even if a line for that key
    already exists (append-only, unlike `--set-config`'s update-in-place behavior in AC-3).
11. **AC-11**: Once all required values are present (pre-existing in the file, satisfied by an environment variable,
    or just supplied through the prompt), the invocation proceeds with the original request (script processing,
    etc.) exactly as it does today.
12. **AC-12**: `--help` and `--version` never perform the folder/file/required-value checks in AC-6 through AC-10.
13. **AC-13**: Neither `--set-config` nor the interactive prompt echoes the supplied value back to the console (for
    example in a confirmation message).
14. **AC-14**: The README, and any other project documentation, no longer instructs the user to manually create or
    edit the config folder or `PodcastGenerator.env`; it still documents the file's location and required contents,
    and now documents the `--set-config` command as the supported way to set a value directly.

## Constraints

- Config folder and file paths are built with `Path.Combine` from `Environment.SpecialFolder.UserProfile`, per
  `CLAUDE.md`, so this works on both Windows and Linux.
- The key-file format is defined by the existing parser behavior in `CLAUDE.md` (tolerates a BOM and either line
  ending, ignores blank lines and `#` lines, trims spaces and one pair of quotes, ignores other keys, last duplicate
  wins, empty value treated as missing). `--set-config`'s update-in-place behavior (AC-3) must not otherwise disturb
  that parser's handling of lines it does not touch.
- Never log or print the value of `OPENROUTER_API_KEY`, or any other config value entered by the user, per
  `CLAUDE.md`'s existing rule against logging the key.
- The `OPENROUTER_API_KEY` environment variable is still supported as an alternative source per `CLAUDE.md`; per the
  user's decision, a non-empty environment variable satisfies the required-value check in AC-7 the same as a
  non-empty value in the file.
- The exact error strings `Invalid input` and `<CONFIG_KEY_NAME_HERE> not found. Please set this config value to
  continue.` (with the real key name substituted) are fixed by the user and must be used verbatim, not paraphrased.
- No test may make a live OpenRouter call, read the real key file, or require an interactive console; per
  `CLAUDE.md`, tests fake this behavior.

## Out of scope

- Any config value other than `OPENROUTER_API_KEY`. The issue anticipates more required values in the future, but
  does not name any; the required-value list mechanism should support more, but no other value is being added now.
- Removing or changing the `OPENROUTER_API_KEY` environment-variable fallback.
- Editing or deleting existing values in `PodcastGenerator.env` through the interactive prompt path (only appending
  is asked for there; `--set-config` is the only path that updates an existing line).
- A `--get-config` / list-config command; the issue only asks for setting values.

## Open questions

- **NON-BLOCKING**: The user specified "5 retries" for the interactive prompt. This spec treats that as 5 total
  attempts (1 initial + up to 4 re-prompts) before the run fails; it could instead mean 5 retries *after* an initial
  attempt (6 total). If 6 total attempts is intended, this needs to be corrected before implementation.
- **NON-BLOCKING**: Does `--set-config` validate `<KEY>` against the known required-config list (currently only
  `OPENROUTER_API_KEY`), or accept any key name verbatim (future-proofing for values the app does not check yet)?
  The issue frames `--set-config` as general-purpose ("this ask could probably grow into checking and adding for
  multiple config values"), which suggests accepting any key, and this spec assumes that; it is not stated
  explicitly in the issue.
- **NON-BLOCKING**: Exact behavior and exit code when console input is not available at all (e.g. stdin redirected
  from an empty/closed source, or a non-interactive CI-like environment) is not specified by the issue.
