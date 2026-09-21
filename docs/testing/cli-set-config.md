# Testing: CLI set-config command and required-config prompt

Spec: `docs/specs/cli-set-config.md` (AC-1 to AC-14). Design: `docs/designs/cli-set-config.md`. Branch `feature/cli-set-config`.
Mode: feature, issue #2. Production code was not edited.

- Round 1: CODE_HEAD `8323860b6a9283479d177cac49f5de05eaae3d97`.

## Result summary (round 1)

| Check | Result |
|---|---|
| `dotnet build -warnaserror` | 0 warnings, 0 errors |
| `dotnet test`, whole suite, run with `OPENROUTER_API_KEY` unset and `HTTPS_PROXY`/`HTTP_PROXY` pointing at a closed local port (`127.0.0.1:9`) | **735 total, 735 passed, 0 failed, 0 skipped** |
| Tests I added | 86: 82 in `Integration/ConfigIntegrationTests.cs` (new) and 4 in `Integration/CliProcessTests.cs`; all pass. The suite had 649 before them. |
| Bug reports | 1: `docs/bugs/cli-set-config-1.md` (user decision needed, see below) |
| Open questions for the user | see the end of this file |

All tests use the fake HTTP handler (`CannedSpeechHandler`), a temporary directory as the "user profile", a scripted standard input
(`StringReader` behind the real `ConsoleConfigPrompter`) and fake key values. None reads the real key file or needs a console.
(While checking that the existing folder is not written to I listed the names of the entries in the real `~/.config/PodcastGenerator`
folder once, without opening any file. No test does that.)

## How the tests are built

`Integration/PipelineHarness.cs` (`Pipeline`) already builds the real dependency injection wiring with the real file system in a
temporary profile and a canned HTTP handler. I added to it `RunSetConfigAsync` (runs `--set-config <KEY> <VALUE>` through a fresh
service provider the way `Program` does), and `ConfigPrompter`, so a test can use the real `ConsoleConfigPrompter` reading a
`StringReader` in place of the scripted fake. `RunAsync` now shares a private `BuildProvider` with it. Nothing else in the harness
changed.

`Integration/ConfigIntegrationTests.cs` is new: real `ConfigService`, `EnvFileEditor`, `EnvFileParser`, `ApiKeyProvider`,
`PhysicalFileSystem`, `CliRunner` and `ConsoleConfigPrompter`, and the real generation pipeline behind the canned HTTP handler.
Expected values come from the spec and `CLAUDE.md` (the exact messages, the append/in-place rules, the parser rules), not from what
the code produces.

`Integration/CliProcessTests.cs` starts the built `PodcastGenerator.dll`. I added four cases that end before the profile is touched
(usage errors, and `--help`/`--version` next to a `--set-config` whose key is unusable, so nothing could be written even if the
order were ever wrong). No process test runs a valid `--set-config` or a podcast: on Windows a child process cannot be pointed at a
temporary profile (design table, first row), so that would write to the real profile.

## Plan and results: acceptance criteria to tests

"Unit" is a test the coding agent wrote (`Runtime/ConfigTests.cs`, `Cli/SetConfigCliTests.cs`, `Cli/ConsoleConfigPrompterTests.cs`,
`Cli/CliFailureOutputTests.cs`, `Infrastructure/PhysicalFileSystemTests.cs`). "Int" is `ConfigIntegrationTests`, "Proc" is
`CliProcessTests`. All pass.

| AC | Integration and process tests | Also covered by unit tests |
|---|---|---|
| AC-1 create folder | Int `Set_config_on_a_fresh_profile_creates_the_folder_and_the_file_writes_the_line_and_makes_no_podcast` | yes |
| AC-2 create file, write `KEY=VALUE` | same test; `Set_config_creates_the_file_when_only_the_folder_exists` | yes |
| AC-3 update in place, add when absent, other lines unchanged | Int `Set_config_updates_the_existing_line_in_place_and_leaves_every_other_line_unchanged` (case-insensitive match, CRLF, no final line ending), `Set_config_updates_a_line_in_the_middle_of_the_file_without_moving_it`, `Set_config_adds_a_new_line_when_the_key_is_not_in_the_file_and_leaves_the_existing_lines_alone`, `A_file_with_a_byte_order_mark_and_comments_is_still_parsed_after_set_config_updates_it`, `A_value_saved_with_set_config_is_used_by_the_next_run_without_a_prompt`, `A_second_set_config_replaces_the_first_value_and_the_next_run_sends_the_new_one`, `A_saved_value_is_read_back_by_the_parser` (5 cases), `Set_config_accepts_a_key_the_app_does_not_require` | yes |
| AC-4 `Invalid input`, failure code, no change | Int `Set_config_with_an_unusable_key_or_value_prints_exactly_Invalid_input_and_creates_nothing` (17 cases: 11 from the spec, 6 from the design's extra rules) and `..._leaves_an_existing_file_byte_for_byte_unchanged` (17 cases); Proc `Set_config_with_an_unusable_key_or_value_prints_Invalid_input_and_exits_1` (5 cases, coding agent) | yes |
| AC-5 exits without a podcast, 0 on success | Int (fresh profile test: no HTTP request, no output folder, no workspace); Proc `Help_next_to_set_config_prints_the_usage_and_exits_0` | yes |
| AC-6 check first, create what is missing | Int `A_run_with_the_key_only_in_the_environment_creates_the_folder_and_an_empty_file_and_does_not_prompt`, `A_run_creates_the_key_file_when_only_the_folder_exists`, `The_config_check_and_prompt_come_before_the_script_is_looked_at`, `With_the_key_present_a_missing_script_is_reported_without_a_prompt` | yes |
| AC-7 present in file, environment or both | Int `A_value_in_the_file_or_the_environment_or_both_does_not_trigger_a_prompt` (3 cases), `An_empty_value_in_the_file_counts_as_missing_and_prompts`, `An_empty_environment_variable_counts_as_missing_and_prompts` | yes |
| AC-8 message and prompt | Int `A_missing_key_prints_the_fixed_message_asks_for_it_saves_it_and_the_run_continues` (exact message, label, order), `Every_missing_required_value_is_asked_for_in_order_and_appended_on_disk` | yes |
| AC-9 reject, re-prompt, 5 attempts, fail with 1 | Int `Blank_entries_are_rejected_with_Invalid_input_and_asked_again_until_a_valid_one`, `The_fifth_attempt_can_still_succeed`, `Five_invalid_entries_fail_the_run_with_exit_code_1_and_a_sixth_line_is_never_read` | yes |
| AC-10 append only | Int `A_typed_value_is_appended_and_no_existing_line_is_changed_or_removed`, `A_key_typed_once_is_not_asked_for_again_on_the_next_run` | yes |
| AC-11 run continues as before | Int `A_missing_key_prints_the_fixed_message_...` (podcast written, path on stdout, request authorized with the typed key), `A_run_with_a_good_key_file_does_not_rewrite_it`, the AC-7 tests | yes |
| AC-12 help and version skip the checks | Int `Help_and_version_win_over_every_other_argument` (5 cases); Proc `Help_next_to_set_config_...`, `Version_next_to_set_config_...`, and the existing `Help_*` and `Version_*` process tests. Whether `Program` builds no service for them is read from `Program.cs:10-15`; a process test cannot observe the real profile without reading it. | yes |
| AC-13 no echo | Int `A_typed_secret_is_only_in_the_key_file`, `A_rejected_secret_value_is_not_printed_back`, the fresh-profile set-config test, `Set_config_that_cannot_write_the_file_exits_1_with_an_error_and_does_not_print_the_value`. The hidden typing at a real terminal (`ReadKey(intercept: true)`) cannot be run without a console and has no automated test (the design says so too). | yes |
| AC-14 README | Read, not a test. `README.md` "The API key and the config file" no longer tells the user to create the folder or file ("You do not create this folder or file yourself"), still gives the two paths and the `OPENROUTER_API_KEY=<key>` line, and documents `--set-config`, the prompt and the usage line. `docs/script-writing-guide.md`, `docs/style-guide.md` and `docs/sample-scripts/` do not mention the file. `CLAUDE.md` still describes the key file as something the user makes (design "Not done" section); it is not ours to edit. | n/a |

Beyond the acceptance criteria:

- Extra rejections (D-7): the 6 design cases in the AC-4 theories (`A=B`, `=`, `#KEY`, and a value with `\n`, `\r\n`, `\r`).
- No console input: Int `A_missing_key_with_no_console_input_fails_with_exit_code_1_and_a_message` (exit 1, `Error:` line, no request,
  message names the key file and the `OPENROUTER_API_KEY=` line, so CLAUDE.md's "says where the key file goes and what it must
  contain" still holds).
- File failure (key file path is a directory): Int `Set_config_that_cannot_write_the_file_...`, `A_run_that_cannot_prepare_the_key_file_...`.
- Regression: `A_run_with_a_good_key_file_does_not_rewrite_it` (BOM, CRLF, quotes, comments: same bytes after a run); the existing
  `KeyAndRuntimeFolderTests` and `EndToEndPipelineTests` still pass; `A_usage_error_is_never_parsed_as_something_that_runs_or_writes`
  (5 cases) and `No_arguments_at_all_is_a_usage_error`; Proc `Set_config_in_the_wrong_position_or_with_too_many_arguments_...` (2 cases).
- The registered required list is only `OPENROUTER_API_KEY`: Int `The_registered_required_values_are_only_the_api_key`.

## Items the coding agent asked about

| Item | Contradicts the spec or CLAUDE.md? |
|---|---|
| Extra `Invalid input` rejections (key with `=` or starting with `#`, value with a line break) | No. AC-4 says what must be rejected, not that nothing else may be. Without them the file could not be read back (the parser takes the key before the first `=` and skips `#` lines), which the spec's parser constraint forbids. Tested, 6 cases x 2. |
| In-place update rewrites every duplicate line for the key | Not a contradiction: AC-3 says "every other line" is left unchanged, and a duplicate is a line for the key. The parser lets the last duplicate win, so updating only the first would hide the new value. The wording of AC-3 does not say which. Listed as a question. |
| The rewritten line takes the key spelling given on the command line | **Problem, see bug 1.** |
| Value trimming | No. The parser trims on reading, so the value read back is identical (tested with `A_saved_value_is_read_back_by_the_parser`). |
| `--set-config` with the wrong argument count, or not the first argument, is a usage error (exit 1) | No. The spec is silent. Tested at process level. |
| Fail fast with exit 1 when there is no console input | No. Spec open question 3 leaves it open; CLAUDE.md says every failure is exit 1 with a message that says what went wrong. |
| `CliProcessTests` moved in-process (`CliFailureOutputTests`) | Consistent with CLAUDE.md ("no test reads the user's key or key file"): with AC-6 every such process run would read and create files in the real profile. The assertions are the same, so no coverage was lost. The four cases I added to `CliProcessTests` stay on paths that end before the profile is touched. |
| One assertion changed in `KeyAndRuntimeFolderTests` (`OPENROUTER_API_KEY=<key>` replaced by `--set-config OPENROUTER_API_KEY`, plus the prompt and file-exists checks) | Not a weakening of the intent: CLAUDE.md wants the message to say where the file goes and what it holds. The new message names the path and the line `OPENROUTER_API_KEY=<value>`, and I added an assertion for both in `A_missing_key_with_no_console_input_...`. |

## Findings

1. **Bug 1** (`docs/bugs/cli-set-config-1.md`): `--set-config openrouter_api_key <value>` on a file that has a working
   `OPENROUTER_API_KEY=...` line renames the line to the lowercase spelling. The command prints success, but the app reads keys
   with an exact-case match, so the key is then missing. Demonstrated with a temporary probe (removed after the run):
   `--set-config` exit 0, file `openrouter_api_key=new-lower`, `EnvFileParser.GetValue(..., "OPENROUTER_API_KEY")` returns nothing,
   and the next run asks for the key again.
2. Observation, no bug: with redirected standard input the prompt label has no line ending after it, so the rejection text is
   printed on the same line (`OPENROUTER_API_KEY: Invalid input`). In an interactive terminal a line ending is written after
   Enter, so it does not happen there. The message text is exact in both cases. My tests count occurrences instead of lines.
3. Observation, no bug: a value that is literally two quote characters (`""`, typed as `'""'` in a shell) is accepted by
   `--set-config` and by the prompt (it is not blank), is stored as `KEY=""`, and reads back as empty, so the key is still missing.
   Run with the temporary probe: `--set-config` exit 0, next run asks again. The spec's "empty string" is not clear on whether
   this counts.
4. Observation, no bug: an environment variable set to whitespace only (`"   "`) counts as missing, so the prompt appears. The spec
   text says "non-empty"; `ApiKeyProvider` already ignores a whitespace-only variable, and the design says it uses the same rule.

## Not tested, and why

- The hidden key-by-key input at a real terminal, and Ctrl+C at the prompt: need a console (the design says the same).
- The Linux file mode 0600 of the key file: the coding agent's unit test checks it on non-Windows only and I could not run Linux.
- `dotnet publish` of the executables: not part of this change.

## Questions for the user

1. Bug 1: when `--set-config` finds an existing line for the key with a different spelling, which should win: keep the existing
   spelling and replace only the value (the working line keeps working, but a wrongly cased line stays wrongly cased), or rewrite it
   in the spelling given (what the code does now)? Or should the app read keys without regard to case, so both spellings work?
2. AC-3 and duplicates: when the file has several lines for the key, should all of them be rewritten (now), or only the last one
   (the one the parser uses)?
3. AC-6 says "any invocation other than `--set-config`, `--help`, or `--version`" checks the config. The design applies the check
   only to a valid run command, so a usage error (no script, too many arguments) asks for nothing. Is that the intent?
4. AC-7: does a whitespace-only environment variable count as present ("non-empty") or missing (now)? And should a value of `""`
   (two quote characters) be rejected as an empty string?
