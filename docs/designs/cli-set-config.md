# Design: CLI set-config command and interactive required-config prompt

Spec: [`docs/specs/cli-set-config.md`](../specs/cli-set-config.md). GitHub issue #2.

## Summary

The app now creates its config folder and `PodcastGenerator.env` itself, adds `PodcastGenerator --set-config <KEY> <VALUE>`,
and, before generating a podcast, checks that each required config value (today only `OPENROUTER_API_KEY`) is present, asking
for a missing one on the console. Nothing here calls OpenRouter, so no real API call was made for this feature and none of the $5
verification budget was spent.

## Sources and what was verified

| Fact | How it was established |
|---|---|
| `Environment.GetFolderPath(UserProfile)` on Windows ignores the `USERPROFILE` and `HOME` environment variables. | Run on this machine (Windows 11, .NET 10): a probe program printed `C:\Users\webhe` with `USERPROFILE=C:\fake1` and `HOME=C:\fake2` set. Consequence: a child process cannot be pointed at a temporary profile, which drives D-6. |
| `FileStreamOptions.UnixCreateMode` throws `PlatformNotSupportedException` on Windows. | Run on this machine: setting it threw `Unix file modes are not supported on this platform`. So it is set only when `!OperatingSystem.IsWindows()`. |
| `UnixCreateMode` gives the new file mode 0600 on Linux. | .NET API documentation for `FileStreamOptions.UnixCreateMode` (used when a file is created; the umask can only remove bits). **Not run on Linux**: this machine has no Linux .NET. The unit test `AssertOwnerOnlyOnUnix` checks it with `File.GetUnixFileMode` and runs in the Linux job of the pull request workflow. |
| `Console.ReadKey(intercept: true)` does not display the key; `Console.KeyAvailable` says whether a key is waiting; `Console.IsInputRedirected` says whether stdin is a pipe or file; `ReadKey` needs a real console. | .NET API documentation (`System.Console`). **Not run**: no test may need an interactive console and an agent has no terminal to type into. The keystroke path in `ConsoleConfigPrompter.ReadHiddenAsync` is therefore the one piece of this feature without an automated test. The redirected-input path is tested with a `StringReader`. |
| `File.Move(source, destination, overwrite: true)` replaces the destination. | .NET API documentation (`File.Move`, overload with `overwrite`); it is also exercised by `PhysicalFileSystemTests` on the CI platforms. |
| The existing key-file parser rules. | Read from `EnvFileParser` and `CLAUDE.md`; `EnvFileEditor` mirrors them and its tests read results back through `EnvFileParser`. |

The Microsoft Learn docs tool was not available in this run, so the documentation rows above rest on the API documentation as I
know it and are marked where I could not confirm them by running code.

## Decisions

- **D-1: five attempts in total.** Spec open question 1, stated assumption: 1 initial attempt plus up to 4 re-prompts. The fifth
  invalid entry ends the run with exit code 1. `ConfigService.MaxAttempts = 5`.
- **D-2: `--set-config` accepts any key name.** Spec open question 2, stated assumption. It does not check `<KEY>` against the
  required list. Two extra rules apply to keep the file readable, described in D-7.
- **D-3: no console input.** Spec open question 3 (unspecified). When the console gives no input at all (`IConfigPrompter.ReadValueAsync`
  returns `null`: stdin closed or at end), the run fails at once, exit code 1, without using up the remaining attempts, with
  `Error: <KEY> was not found and there is no console input to ask for it. Set it with 'PodcastGenerator --set-config <KEY> <value>'
  (it is saved as the line <KEY>=<value> in '<path>'), or set the <KEY> environment variable.` A closed stdin gives no chance to
  type, so waiting or retrying would only hang or loop; failing with the way to fix it is the useful behavior for a script or CI job.
- **D-4: where the check runs.** `CliRunner.RunAsync` calls `IConfigService.EnsureRequiredAsync` before
  `IPodcastGenerationService.GenerateAsync`, so it is the first thing done for any command that generates a podcast (AC-6). A usage
  error, `--help` and `--version` are handled in `Program` before any of this (AC-12). A usage error does not trigger the
  check: it is not a valid invocation of the main command, and asking for a key before saying the arguments are wrong would be
  odd. A consequence of AC-6 is that a bad script path is now reported only after the key is present.
- **D-5: what is not echoed (AC-13).** Nothing the person supplies is written to standard output or standard error by the app:
  `--set-config` prints `Saved <KEY>.`; the prompt writes only the label `<KEY>: `; error messages never contain a value or file
  contents; `SetConfigCommand.ToString()` hides the value. At a real terminal the typed characters are also not shown (no
  asterisks), by reading keys with `ReadKey(intercept: true)`; a value cannot be shoulder-surfed from the screen. This goes one step
  beyond "not echoed back in a message" in the spec, and matches its stated reason (the value can be a secret). Ctrl+C at the prompt is meant to end the wait
  because the read polls `Console.KeyAvailable` and observes the cancellation token instead of blocking in `ReadKey` (not run by me, see the last section).
- **D-6: existing process-level tests.** `CliProcessTests` started the built program and used script paths that failed before the
  runtime folder was touched. With AC-6 every such command now checks the config folder and key file in the real user profile
  first, and the first row of the verification table shows a child process cannot be redirected to a temporary profile on Windows. Left as they were, those
  tests would read the developer's real key file, create files in the real profile, and, on a CI machine with no key, hit the prompt
  with whatever stdin the test host gave. So the five tests (six cases) that used a script path (missing script, `.md`/`.docx` script, output not
  `.mp3`, empty script, failure text with no stack trace) moved to `Cli/CliFailureOutputTests.cs`, which runs `CliRunner` and the real
  services against the in-memory file system. Their assertions are unchanged. `CliProcessTests` keeps the usage, help and version
  tests and gains tests for `--set-config`, all of which end before the profile is touched.
- **D-7: what a key and a value may contain.** AC-4 rejects a key that is empty, blank, or contains whitespace, and a value that is
  empty, blank, or whitespace-only. Two more rules are added because without them a write would corrupt the file, which the
  constraints forbid: a key must not contain `=` or start with `#` (the parser takes the key as the text before the first `=` and
  skips `#` lines, so such a key could never be read back), and a value must not contain a line break (it would write a second
  line and could inject another key). They give the same `Invalid input`. A value is trimmed before it is stored, because the
  parser trims it on reading. An internal space in a value is allowed.
- **D-8: in-place update details (AC-3).** Every line for the key (matched case-insensitively, using the parser's line rules) is
  rewritten as `KEY=VALUE`, with the key spelled as given. Reasons: the app reads the key with an exact-case match, so
  leaving `openrouter_api_key=` in place would leave the new value unused; and the parser lets the last duplicate win, so updating
  only the first duplicate would hide the new value. Every other line is left exactly as it was, and each line keeps its own
  ending (`\r\n`, `\n` or `\r`). A new line uses the file's first line ending, or the platform's for a file with none. A leading
  UTF-8 byte order mark is dropped when the file is rewritten (it is not a line, and the reader had already removed it).
- **D-9: the key file is private and replaced as one step.** The file may hold an API key, so on Linux it is created with mode 0600
  (`FileStreamOptions.UnixCreateMode`, applied at creation so the content is never world readable). Updates write a temporary
  file next to it (also 0600) and `File.Move(..., overwrite: true)` it over the target, so a crash or full disk cannot leave a
  half-written key file. On Windows the file inherits the folder's permissions (the user profile).
- **D-10: the "no key" message in `PodcastGenerationService`.** It stays, as a guard for a caller that skips the config step, but it
  no longer tells the user to create the file by hand: it names `--set-config`, the file path and the environment variable. It still
  contains the file path and the `OPENROUTER_API_KEY=<key>` line, so the existing service-level tests are unchanged.
- **D-11: `--set-config` position.** It must be the first argument and take exactly two more (`--set-config <KEY> <VALUE>`); the two
  are taken as given, so a value that starts with `-` is a value. Anywhere else, or with the wrong count, is a usage error (exit 1).
  `--help` and `--version` anywhere still win, as before. An empty `<KEY>`/`<VALUE>` is a valid parse and is rejected as `Invalid input`.
- **D-12: output streams.** `Invalid input` on a failed `--set-config` goes to standard error, exactly and with no `Error:` prefix
  (AC-4 says the exact message). Other failures keep the `Error: ...` form. The prompt text and the retry message go to standard
  error, so standard output still holds only a result: the podcast path, or `Saved <KEY>.`.
- **D-13: the required list.** `RequiredConfig(IReadOnlyList<string> Keys)` is registered in DI with `[ApiKeyProvider.KeyName]`;
  adding a later required value is one more entry. The presence rule (non-empty in the file, else a non-blank environment
  variable) is the same rule `ApiKeyProvider` already uses to find the key.

## Projects and types

| Project | Change |
|---|---|
| Application | New `Abstractions/IConfigPrompter` (`Tell`, `ReadValueAsync`), `Abstractions/InvalidInputException`. `IFileSystem` gains `TryWriteNewPrivateTextAsync` and `ReplacePrivateTextAsync`. New `Runtime/ConfigInput` (validation and the fixed message), `Runtime/EnvFileEditor` (pure set-in-place and append over the text), `Runtime/ConfigService` with `IConfigService` and `RequiredConfig`. `DependencyInjection` registers `RequiredConfig` and `IConfigService`. `PodcastGenerationService.MissingKeyMessage` reworded (D-10). |
| Infrastructure | `PhysicalFileSystem` implements the two new members (D-9). |
| Cli | `CommandLine` parses `--set-config` into `SetConfigCommand` and lists it in the usage text. `CliRunner` takes `IConfigService`, runs the check before generating, and has `SetConfigAsync`. New `ConsoleConfigPrompter`. `Program` builds the provider in one helper and handles `SetConfigCommand`. |
| Docs | `README.md` (AC-14): the "Where the API key goes" section is now "The API key and the config file", no longer tells the user to create the folder or file, and documents the prompt and `--set-config`. |

## How each acceptance criterion is met

| AC | Where | Tests |
|---|---|---|
| AC-1 create folder | `ConfigService.SetAsync` calls `EnsureFileAsync` (`CreateDirectory`) | `Set_creates_the_config_folder_when_it_is_missing`, `ConfigServiceOnDiskTests.Set_creates_the_folder_and_the_file_on_disk_and_writes_the_line` |
| AC-2 create file, write `KEY=VALUE` | `EnsureFileAsync` + `EnvFileEditor.SetValue` + `ReplacePrivateTextAsync` | `Set_creates_the_file_when_it_is_missing_and_writes_the_line`, `The_key_file_is_written_as_a_private_file` |
| AC-3 update in place / add | `EnvFileEditor.SetValue` (D-8) | `EnvFileEditorTests.*`, `Set_updates_the_existing_line_in_place_...`, `Set_adds_a_new_line_...`, `ConfigServiceOnDiskTests.Set_updates_the_line_in_place_on_disk_...` |
| AC-4 reject with `Invalid input`, no change | `ConfigInput` + `InvalidInputException`, validated before any file access; `CliRunner` prints the bare message | `ConfigInputTests.*`, `Set_rejects_an_unusable_key_or_value_...`, `Set_with_an_unusable_input_does_not_even_create_...`, `SetConfigCliTests.Set_config_with_an_unusable_key_or_value_prints_exactly_...`, process test `Set_config_with_an_unusable_key_or_value_prints_Invalid_input_and_exits_1` |
| AC-5 no podcast, exit 0 | `Program` handles `SetConfigCommand` with `CliRunner.SetConfigAsync` only | `Set_config_saves_the_value_exits_0_makes_no_podcast_and_does_not_print_the_value` |
| AC-6 check first, create what is missing | `CliRunner.RunAsync` calls `EnsureRequiredAsync` first (D-4) | `Ensure_creates_the_folder_and_an_empty_file_when_they_are_missing`, `The_config_check_runs_before_the_script_is_looked_at`, `ConfigServiceOnDiskTests.A_typed_value_is_appended_on_disk_...` |
| AC-7 present in file or environment | `ConfigService.IsPresentAsync` | `A_value_only_in_the_file_...`, `..._only_in_the_environment_...`, `..._in_both_places_...`, `An_empty_environment_variable_and_an_empty_file_value_count_as_missing` |
| AC-8 message and prompt | `ConfigService.AskForAsync`, `ConsoleConfigPrompter` | `A_missing_value_prints_the_fixed_message_with_the_key_name_and_asks_for_it`, `ConsoleConfigPrompterTests.*` |
| AC-9 re-prompt, 5 attempts, fail | `AskForAsync` loop | `An_invalid_entry_prints_the_fixed_message_and_asks_again`, `The_fifth_attempt_can_still_succeed`, `Five_invalid_entries_fail_the_run_...`, `Five_invalid_entries_exit_1_and_make_no_request` |
| AC-10 append only | `EnvFileEditor.AppendValue` | `An_accepted_value_is_appended_and_no_existing_line_is_changed`, `EnvFileEditorTests.Append_*` |
| AC-11 continue as before | `CliRunner` goes on to `GenerateAsync` | `With_the_key_in_the_file_the_run_goes_ahead_without_a_prompt`, `A_key_typed_at_the_prompt_is_saved_used_for_the_run_and_never_printed` |
| AC-12 help/version skip the check | `Program` returns before building services | `Help_and_version_still_win_over_set_config`, process tests `Help_*`, `Version_*` (exit 0, nothing on standard error) |
| AC-13 no echo | D-5 | `The_command_text_does_not_contain_the_value`, `A_value_is_never_in_a_message_...`, `A_rejected_value_is_not_printed_back`, `What_is_read_is_never_written_to_the_error_writer`, `..._never_printed` |
| AC-14 README | see Projects and types | not testable; reviewed by reading |

## Not done or not verifiable here

- The hidden-typing path at a real terminal (`ReadKey`, `KeyAvailable`) has no automated test and was not run by me. Ctrl+C
  behavior at the prompt likewise.
- The Linux file mode of the key file is asserted by a unit test that only does its check on non-Windows platforms; I could not run
  it on Linux.
- `CLAUDE.md` still says "If no key is found, exit with a helpful, descriptive error that says where the key file goes and what it
  must contain", and its Usage section describes the key file as something the user creates. Agents may not edit `CLAUDE.md`
  without permission, so it is left for the user to update.
