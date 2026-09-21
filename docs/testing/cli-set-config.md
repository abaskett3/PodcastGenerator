# Testing: CLI set-config command and required-config prompt

Spec: `docs/specs/cli-set-config.md` (AC-1 to AC-14, with the four fix-round-1 user decisions). Design: `docs/designs/cli-set-config.md`.
Branch `feature/cli-set-config`. Mode: feature, issue #2. Production code was not edited by me in either round.

- Round 1: CODE_HEAD `8323860b6a9283479d177cac49f5de05eaae3d97`. Old behavior: update in place, case-sensitive reader.
- Round 2: CODE_HEAD `2b9e2900f72788e56b2f103c2cad3cf2e295901e`. The four user decisions of fix round 1 apply: (1) keys are read
  case-insensitively; (2) `--set-config` removes every line for the key (case-insensitive) and appends one `KEY=VALUE` line in the
  typed spelling, while the prompt stays append-only; (3) a usage error skips the config check and fails at once; (4) `""` and `''`
  are rejected as `Invalid input` in `--set-config` and in the prompt.

## Result summary (round 2)

| Check | Result |
|---|---|
| `dotnet build -warnaserror` | 0 warnings, 0 errors |
| `dotnet test`, whole suite, run with `OPENROUTER_API_KEY` unset and `HTTPS_PROXY`/`HTTP_PROXY` pointing at a closed local port (`127.0.0.1:9`) | **800 total, 800 passed, 0 failed, 0 skipped** |
| `Integration/ConfigIntegrationTests` | 117 tests, all pass (82 in round 1; the coding agent changed 2 and added 2 tests plus 6 data rows; I added 27 this round) |
| `Integration/CliProcessTests` | 21 tests, all pass (I added 5 this round: 2 quote rows and 3 usage-error rows) |
| Bug 1 (`docs/bugs/cli-set-config-1.md`) | fixed, verified (see below) |
| New bug reports | none |
| Would the new tests have caught the old behavior? | Yes: I ran the round 2 `ConfigIntegrationTests.cs` against the source of my round 1 commit `b32a999` (pre-fix production code, in a copy outside the repository). 23 of 117 failed, all in the areas the four decisions changed; all 117 pass at `2b9e290`. |

All tests use the fake HTTP handler (`CannedSpeechHandler`), a temporary directory as the "user profile", a scripted standard input
(`StringReader` behind the real `ConsoleConfigPrompter`) and fake key values. None reads the real key file or needs a console, and the
full suite ran with no key and no network.

## How the tests are built

`Integration/PipelineHarness.cs` (`Pipeline`) builds the real dependency injection wiring with the real file system in a temporary
profile and a canned HTTP handler; I added `RunSetConfigAsync` and `ConfigPrompter` to it in round 1 (unchanged in round 2).
`Integration/ConfigIntegrationTests.cs` uses the real `ConfigService`, `EnvFileEditor`, `EnvFileParser`, `ApiKeyProvider`,
`PhysicalFileSystem`, `CliRunner` and `ConsoleConfigPrompter`, and the real generation pipeline behind the canned HTTP handler.
`Integration/CliProcessTests.cs` starts the built `PodcastGenerator.dll`; it only runs command lines that end before the profile is
touched (usage errors, help, version, a rejected `--set-config`), because on Windows a child process cannot be pointed at a temporary
profile (design table, first row). Expected values come from the spec, the four user decisions and `CLAUDE.md`'s parser rules, not
from what the code produces.

The coding agent changed my round 1 tests `Set_config_updates_the_existing_line_in_place_and_leaves_every_other_line_unchanged` and
`Set_config_updates_a_line_in_the_middle_of_the_file_without_moving_it` because they encoded the superseded update-in-place rule. I
checked them against the updated spec: they now expect the line removed and one line appended at the end (AC-3 as changed), with
every other line and its order unchanged. No assertion was weakened: the checks are stricter (a second differently cased duplicate is
in the fixture and must be gone too, and there is exactly one line for the key). The coding agent also strengthened the no-console
message assertion (`{KeyName}=<value>` and `--set-config {KeyName}`). I read the diff of `Integration/` between `b32a999` and
`2b9e290` in full; nothing else changed.

## Plan and results: acceptance criteria to tests

"Unit" is a test the coding agent wrote. "Int" is `ConfigIntegrationTests`, "Proc" is `CliProcessTests`. All pass. **New** marks
what I added or the coding agent changed in round 2.

| AC | Integration and process tests | Also covered by unit tests |
|---|---|---|
| AC-1 create folder | Int `Set_config_on_a_fresh_profile_creates_the_folder_and_the_file_writes_the_line_and_makes_no_podcast` | yes |
| AC-2 create file, write `KEY=VALUE` | same test; `Set_config_creates_the_file_when_only_the_folder_exists` | yes |
| AC-3 (as changed) key unique: remove every line for the key, append one in the typed spelling, other lines unchanged | Int **New** `Set_config_removes_every_line_for_the_key_and_appends_one_and_leaves_every_other_line_unchanged` (CRLF, no final line ending, two differently cased duplicates), **New** `Set_config_moves_the_key_to_the_end_of_the_file_and_keeps_the_order_of_the_other_lines`, **New** `Set_config_removes_a_padded_key_line_but_keeps_comments_and_keys_that_only_look_similar` (a commented-out copy, `OPENROUTER_API_KEY_2`, `MY_OPENROUTER_API_KEY` stay), **New** `Set_config_leaves_one_line_in_the_spelling_typed_whatever_spellings_the_file_had` (3 typed spellings, then the next run sends the new value with no prompt), **New** `Set_config_with_a_lowercase_key_keeps_the_key_usable_and_the_file_unique` (bug 1), `Set_config_adds_a_new_line_when_the_key_is_not_in_the_file_and_leaves_the_existing_lines_alone`, `A_file_with_a_byte_order_mark_and_comments_is_still_parsed_after_set_config_updates_it`, `A_value_saved_with_set_config_is_used_by_the_next_run_without_a_prompt`, `A_second_set_config_replaces_the_first_value_and_the_next_run_sends_the_new_one` (one line after two runs), `A_saved_value_is_read_back_by_the_parser` (5 cases), `Set_config_accepts_a_key_the_app_does_not_require` | yes |
| AC-4 `Invalid input`, failure code, no change | Int `Set_config_with_an_unusable_key_or_value_prints_exactly_Invalid_input_and_creates_nothing` and `..._leaves_an_existing_file_byte_for_byte_unchanged` (19 cases each: 11 from the spec, 6 from the design's extra rules, **New** 2 from decision 4: `""` and `''`); **New** `A_value_is_either_refused_with_Invalid_input_or_saved_and_found_and_never_saved_but_missing` (12 values); **New** `A_value_that_is_not_an_empty_string_is_still_accepted_with_quote_characters_in_it` (5 values); Proc `Set_config_with_an_unusable_key_or_value_prints_Invalid_input_and_exits_1` (7 cases, **New** 2: `""`, `''`) | yes |
| AC-5 exits without a podcast, 0 on success | Int (fresh profile test: no HTTP request, no output folder, no workspace); Proc `Help_next_to_set_config_prints_the_usage_and_exits_0` | yes |
| AC-6 check first, create what is missing | Int `A_run_with_the_key_only_in_the_environment_creates_the_folder_and_an_empty_file_and_does_not_prompt`, `A_run_creates_the_key_file_when_only_the_folder_exists`, `The_config_check_and_prompt_come_before_the_script_is_looked_at`, `With_the_key_present_a_missing_script_is_reported_without_a_prompt` | yes |
| AC-6 and decision 3 (a usage error skips the check) | **New** Proc `A_usage_error_fails_right_away_without_the_config_check_or_a_prompt` (3 command lines: exit 1, `Error:` line and usage, no not-found message, no prompt, standard output empty); Proc `No_arguments_print_...`, `More_than_two_arguments_...`, `An_unknown_option_...`, `Set_config_without_a_value_is_a_usage_error_and_exits_1`, `Set_config_in_the_wrong_position_or_with_too_many_arguments_...`; Int `A_usage_error_is_never_parsed_as_something_that_runs_or_writes` (5), `No_arguments_at_all_is_a_usage_error`. Whether `Program` builds no service for a `UsageError` is read from `Program.cs:16-19` (it returns before `BuildProvider`); a process test cannot observe the real profile without reading it. | yes (`CliFailureOutputTests`) |
| AC-7 present in file, environment or both | Int `A_value_in_the_file_or_the_environment_or_both_does_not_trigger_a_prompt` (3 cases), `An_empty_value_in_the_file_counts_as_missing_and_prompts`, `An_empty_environment_variable_counts_as_missing_and_prompts`; **New** (decision 1) `A_lowercase_key_line_in_the_file_is_present_and_used_without_a_prompt`, `A_file_with_the_key_in_two_spellings_uses_the_last_one` (2 orders), and `KeyAndRuntimeFolderTests.A_lowercase_key_name_in_the_key_file_is_used` (coding agent) | yes |
| AC-8 message and prompt | Int `A_missing_key_prints_the_fixed_message_asks_for_it_saves_it_and_the_run_continues` (exact message, label, order), `Every_missing_required_value_is_asked_for_in_order_and_appended_on_disk` | yes |
| AC-9 reject, re-prompt, 5 attempts, fail with 1 | Int `Blank_entries_are_rejected_with_Invalid_input_and_asked_again_until_a_valid_one`, `The_fifth_attempt_can_still_succeed`, `Five_invalid_entries_fail_the_run_with_exit_code_1_and_a_sixth_line_is_never_read`; **New** `A_pair_of_quotes_at_the_prompt_is_rejected_with_Invalid_input_and_asked_again` (2 cases: `""`, `''`), **New** `Five_quote_pair_entries_fail_the_run_with_exit_code_1_and_save_nothing`, **New** `A_quoted_entry_of_only_blanks_at_the_prompt_is_rejected_and_the_typed_key_is_the_one_saved` (2 cases: `" "`, `'  '`, see the decision-4 note below) | yes |
| AC-10 append only | Int `A_typed_value_is_appended_and_no_existing_line_is_changed_or_removed`, `A_key_typed_once_is_not_asked_for_again_on_the_next_run`; **New** `The_prompt_stays_append_only_after_an_empty_line_in_another_spelling_of_the_key` (`openrouter_api_key=` stays, the typed value is appended after it, the run sends it) | yes |
| AC-11 run continues as before | Int `A_missing_key_prints_the_fixed_message_...` (podcast written, path on stdout, request authorized with the typed key), `A_run_with_a_good_key_file_does_not_rewrite_it`, the AC-7 tests | yes |
| AC-12 help and version skip the checks | Int `Help_and_version_win_over_every_other_argument` (5 cases); Proc `Help_next_to_set_config_...`, `Version_next_to_set_config_...`, and the existing `Help_*` and `Version_*` process tests. | yes |
| AC-13 no echo | Int `A_typed_secret_is_only_in_the_key_file`, `A_rejected_secret_value_is_not_printed_back`, the fresh-profile set-config test, `Set_config_that_cannot_write_the_file_exits_1_with_an_error_and_does_not_print_the_value`. The hidden typing at a real terminal (`ReadKey(intercept: true)`) needs a console and has no automated test (the design says so too). | yes |
| AC-14 README | Read, not a test. `README.md` "The API key and the config file" says "You do not create this folder or file yourself", gives both paths and the `OPENROUTER_API_KEY=<key>` line, documents `--set-config` as remove-and-append, the quote-pair rejection, case-insensitive key names, and the prompt. `docs/script-writing-guide.md` and `docs/style-guide.md` do not mention the file. `CLAUDE.md` still describes the key file as something the user makes (design "Not done"); it is not ours to edit. | n/a |

Beyond the acceptance criteria (unchanged from round 1 unless marked): the extra rejections (D-7: `A=B`, `=`, `#KEY`, a value with a
line break); no console input fails with exit 1 and a message naming the key file and `OPENROUTER_API_KEY=<value>`; a key file that
cannot be written (the path is a directory) exits 1 with an error and no value in it; a good key file is not rewritten by a run; the
required list is only `OPENROUTER_API_KEY`.

## Decision 4: `" "` and `'  '` are rejected too (the coding agent's reading)

The user's decision names two values, `""` and `''`. The coding agent rejects every value that the parser reads back as empty after
trimming and removing one pair of quotes (`ConfigInput.IsValidValue`, `ConfigInput.cs:25`, using `EnvFileParser.Unquote`), which also
refuses `" "`, `'  '` and `"<tab>"`.

Is that consistent with the intent (an empty string)? Yes, on the evidence: `EnvFileParser.GetValue` trims the text inside the quotes
and treats a whitespace-only result as missing (`EnvFileParser.cs:41,50`; `CLAUDE.md` says an empty value is missing), so `KEY=" "`
would be saved and then not found, the same "saved but missing" outcome as `""` that I reported in round 1 (finding 3). AC-4 already
rejects a whitespace-only value. `"a b"`, `"a"`, `'a'`, a lone `"` or `'`, `"'` are not empty and are accepted; I test that.

The exact list is the coding agent's, not the user's, so I did not write a test that pins `" "` as required behavior in the
`--set-config` path. `A_value_is_either_refused_with_Invalid_input_or_saved_and_found_and_never_saved_but_missing` pins the property
the decision is for: every value is either refused with exactly `Invalid input` and no file change, or saved and found by the parser.
For the prompt I do pin `" "` and `'  '` (`A_quoted_entry_of_only_blanks_...`); if the user wants only the literal `""` and `''`
refused, that test and the coding agent's `ConfigInputTests` rows for `" "` change together. This is listed as a question below, not
a bug.

## Findings

1. Bug 1 (`docs/bugs/cli-set-config-1.md`): **fixed**. The reproduction (a working `OPENROUTER_API_KEY=old-good` line and
   `--set-config openrouter_api_key new-lower`) now leaves one line, `openrouter_api_key=new-lower`, which the parser finds and the
   next run uses without a prompt. Verified by four tests that fail on the pre-fix code and pass now.
2. Round 1 finding 3 (`""` saved but read back as empty): **fixed** by decision 4 (both `--set-config` and the prompt), verified.
3. Observation, no bug: the new line that `--set-config` and the prompt append always ends with a line ending (the file's first one,
   else the platform's), so the file ends with a newline. The spec says nothing about the ending; the design (D-8) says so, and my
   tests assert it in two places (`...leaves_every_other_line_unchanged`, `...keeps_comments_and_keys_that_only_look_similar`). If
   the user wants no final line ending those two expectations change.
4. Observation, no bug: with redirected standard input the prompt label has no line ending after it, so the rejection text is on
   the same line (`OPENROUTER_API_KEY: Invalid input`). My tests count occurrences instead of lines.
5. Observation, no bug: an environment variable that is only whitespace counts as missing (spec Constraints now say so: environment
   handling unchanged).
6. Observation, minor documentation gap, not a bug: the README names the quote pair (`""`, `''`) as rejected but not the quoted-blank
   values (`" "`, `'  '`) that the code also refuses.
7. Observation: the CLI process tests for usage errors do not observe the real profile (they cannot without reading it); that the
   check is skipped is shown by the output (no prompt or not-found text, exit 1, finished) and by reading `Program.cs`.

## Not tested, and why

- The hidden key-by-key input at a real terminal, and Ctrl+C at the prompt: need a console (the design says the same).
- The Linux file mode 0600 of the key file: the coding agent's unit test checks it on non-Windows only; I could not run Linux.
- `dotnet publish` of the executables: not part of this change.

## Questions for the user

1. Decision 4: are `" "` and `'  '` (a pair of quotes around only blanks) also to be refused (the coding agent's choice, matches
   what the parser would read back as empty), or only the literal `""` and `''`? The README mentions only the literal pair.
2. Should the line that `--set-config` appends end with a line ending (now yes), or should the file end without one? The spec is
   silent.

Round 1 questions on bug 1, duplicates, usage errors, the whitespace-only environment variable and `""` were all answered by the four
decisions and are closed.
