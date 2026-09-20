# Testing: initial development (first working CLI and CI/CD)

Spec: `docs/specs/initial-development.md` (revision 4, 76 acceptance criteria). Design: `docs/designs/initial-development.md`.
Branch `feature/initial-development`, CODE_HEAD `5413ba35974d90e3a11cd096ad98f5d65d6063b4`. Mode: feature, no issue number.
Round 1. Tester: testing agent. Production code was not edited.

## Result summary (round 1)

| Check | Result |
|---|---|
| `dotnet build -warnaserror` (working tree, and a clean clone of CODE_HEAD) | 0 warnings, 0 errors |
| `dotnet test` at CODE_HEAD in a clean clone (the coding agent's tests only) | 242 passed, 0 failed |
| `dotnet test` with my tests added | 425 total: **424 passed, 1 failed**, 0 skipped |
| My new tests | 183 (182 pass, 1 fails: bug 1) |
| Same suite with no `OPENROUTER_API_KEY` and `HTTPS_PROXY`/`HTTP_PROXY` pointing at a closed local port | 420 of 420 passed, three runs in a row (before the 5 edge-case tests were added), no flakiness seen |
| Real profile folders after the runs | `~/.config/PodcastGenerator` directory time unchanged (00:23, before my session), `~/PodcastGenerator` does not exist: no test touched them |
| Leftover temp folders from the tests | none |
| Published executables | `win-x64` and `linux-x64` single file, both started and behaved as expected (section 4) |
| Bugs | 1: `docs/bugs/initial-development-1.md` |

## 1. How the tests are built (AC-5)

New tests live in `tests/PodcastGenerator.UnitTests/Integration/` (same xUnit project, so `dotnet test` runs them with the rest).

- `PipelineHarness.cs`: `Pipeline` builds the **real** dependency injection wiring (`AddPodcastGeneratorApplication`,
  `AddPodcastGeneratorInfrastructure`, the real `CliRunner`, the real file system, the real `HttpClient` pipeline from
  `AddHttpClient<ISpeechClient, OpenRouterSpeechClient>` and the real MP3 encoder). Only the outside world is replaced: the primary
  `HttpMessageHandler` (`CannedSpeechHandler`), the user profile folder (a temporary directory, so the real
  `~/.config/PodcastGenerator` and `~/PodcastGenerator` are never used), the environment variables, the clock and the
  retry waiting. `CannedSpeechHandler` throws for any URL other than `https://openrouter.ai/api/v1/audio/speech`, so nothing can
  reach the network. The key is the fake `sk-test-SENTINEL-...`; no test reads the user's key file.
- Canned HTTP answers follow documented behavior only: raw 16-bit mono PCM with content type `audio/pcm; rate=24000; channels=1`
  (OpenRouter TTS guide, and the headers seen in the design doc's capped calls) and the error shape
  `{"error": {"code", "message"}}` (OpenRouter errors page). The audio bytes are a tone made by the test (ordering and joining are
  checked, not what the model would say); error message texts are arbitrary and marked `(test text)`.
- `ScriptOracle` reads a script from the rules in `docs/script-writing-guide.md`, written independently of the product's parser,
  to give the expected spoken text. Expected values come from the spec, the guide, the documented API, Conventional Commits and
  SemVer, not from what the code outputs.
- `CliProcessTests` starts the built `PodcastGenerator.dll` (real `Program`). It uses only inputs that fail before the tool touches
  the runtime folder or the key (no argument, unknown option, missing script, `.md`, bad output extension, nothing to narrate), and
  removes `OPENROUTER_API_KEY` and points the proxy variables at a closed port as a second guard. A valid script is never run this way,
  because that would read the real key file and call the real API.
- `WorkflowScriptTests` runs `.github/scripts/classify-changes.sh` and `compute-release.sh` under bash (Git Bash on Windows, `bash` on
  Linux) and the PR-title pattern read from `pull-request-title.yml`. `WorkflowFileTests` checks properties of the workflow files as
  text. Neither runs GitHub Actions (section 5).

## 2. Plan and results: acceptance criteria to tests

"Unit" means a test the coding agent already wrote (`tests/PodcastGenerator.UnitTests`, outside `Integration/`). "Manual" is a check
I ran by hand (section 4). All listed tests pass except where marked FAIL.

| AC | Tests and checks | Result |
|---|---|---|
| 1 | Manual: `dotnet build -warnaserror` in a fresh clone of CODE_HEAD (Windows): 0 warnings, 0 errors. Linux build: the PR workflow does it; not run here | pass (Windows) |
| 2, 3 | Unit `ArchitectureTests` (references, no `HttpClient` outside Infrastructure, interface in Application); every `Pipeline` run builds the provider with `ValidateOnBuild` | pass |
| 4 | Unit `ArchitectureTests.Every_async_method_ends_in_Async...`; real DI in every integration test | pass |
| 5 | Whole suite passes with no key and dead proxies; `CannedSpeechHandler` rejects other URLs; real profile folders untouched (summary table) | pass |
| 6 | Read design section 6 (options, choice GroovyMp3, LGPL-3.0, alternatives rejected). Decoding tests use NLayer (test only) | pass (document review) |
| 7 | E `The_sample_script_becomes_one_valid_two_channel_mp3_in_the_default_directory` (stdout is the path, one file, exit 0) | pass |
| 8 | K `The_runtime_and_default_output_folders_come_from_the_user_profile_folder`, K `The_registered_paths_are_built_from_the_UserProfile_special_folder`; unit `ArchitectureTests` (no literal separator) | pass |
| 9 | I `The_default_name_uses_the_local_date_of_the_run_as_month_day_year` (real clock), E (fixed clock `Podcast-09-19-2026.mp3`) | pass |
| 10 | I `An_existing_default_file_is_never_touched_and_the_next_runs_get_suffix_2_then_3`, I `An_existing_explicit_file_is_never_overwritten...` | pass |
| 11 | I `An_explicit_file_path_is_written_and_missing_parent_directories_are_created` | pass |
| 12 | I `An_existing_directory_receives_the_file_with_the_default_name` | pass |
| 13 | I `An_output_file_that_is_not_mp3_is_an_error_naming_mp3_...` (3 extensions; no request, nothing created), P `An_output_path_that_is_not_mp3...`; Manual on both executables | pass |
| 14 | F `A_failure_in_the_third_chunk_names_it_and_keeps_no_output_...` (existing file untouched), F cancellation tests and every failure test call `AssertNothingLeftBehind` (no `.mp3`, no `.tmp`, temp audio folder deleted); unit tests for a failing encoder | pass |
| 15 | F, I, P failure tests (exit 1, message on stderr, stdout empty), P `A_failure_message_is_one_line_of_text_and_no_stack_trace` | pass |
| 16 | P `No_arguments...`, `More_than_two_arguments...`, `An_unknown_option...` (real process); Manual: published exes | pass |
| 17 | P `Help_prints_the_usage...`, P `Version_prints_only_the_version...`, Manual: exe built with `-p:Version=1.2.3` prints exactly `1.2.3` on both systems; W `The_packaged_executable_is_run_and_its_version_compared...` | pass |
| 18 | I `A_missing_script_names_the_path_and_makes_no_request`, P `A_missing_script_names_the_path_and_exits_1` | pass |
| 19 | I `Any_extension_other_than_txt_is_rejected...` (`.md`, `.markdown`, `.docx`, none), I `An_upper_case_txt_extension_is_accepted`, P | pass |
| 20 | I `An_empty_script_or_one_with_only_cues_is_rejected_saying_there_is_nothing_to_narrate` (4 inputs), P | pass |
| 21 | E `A_script_of_three_thousand_lines_is_processed_to_completion` (exactly 3,000 lines, every line's marker sent once, in order) | pass |
| 22 | K `The_default_style_is_created_on_the_first_run_is_never_overwritten...`, K path tests | pass |
| 23 | K `The_key_file_wins_when_the_environment_variable_is_also_set`, K `The_environment_variable_is_used_when_there_is_no_key_file`, K `..._when_the_key_file_has_no_usable_key` (3 files) | pass |
| 24 | K `No_key_gives_the_full_key_file_path_and_the_line_format_makes_no_request_and_creates_the_runtime_folder` | pass |
| 25 | K `A_key_file_with_a_byte_order_mark_CRLF_comments_spaces_quotes_and_other_keys_is_read` (real file), K `The_last_duplicate_in_the_key_file_wins`; unit `EnvFileParserTests` | pass |
| 26 | K `The_key_is_in_no_output_and_no_written_file_after_a_successful_run` (scans every file under the temp root, including the MP3 and the style file), K `A_server_that_echoes_the_key_in_its_error_never_leaks_it...` (401 and 500), K `..._because_of_the_style_file`, E (key only in the Authorization header) | pass |
| 27 | K `A_config_env_in_the_profile_and_a_claude_credentials_folder_are_never_read`; unit `ArchitectureTests` (`*.env` ignored, no key-shaped text) | pass |
| 28 | E golden test (`#` headings absent) | pass |
| 29 | E golden test (`PROGRAMME`, `EPISODE`, `STYLE:`, `CAST:`, `END` absent) | pass |
| 30 | X `Every_speaker_is_narrated_with_the_name_stripped_and_one_voice`, E `The_requests_together_carry_every_spoken_paragraph_once_in_script_order_and_nothing_else` | pass |
| 31 | E golden test (`CONT'D` absent, no tag) | pass |
| 32 | E golden test (a tag directly before "The sky is turning that shade of purple again."), E `A_pause_a_direction_and_an_emphasis_in_a_row...` | pass |
| 33 | E golden test (pause tag present, `BEAT` not spoken), X `A_decimal_pause_is_one_tag_before_the_next_words` | pass |
| 34 | E `Cue_lines_are_never_printed_or_sent`, X `A_line_of_two_cues_is_removed`, X `A_bracket_inside_a_spoken_line_is_narrated_as_written` | pass |
| 34, 36 | X `Spoken_words_between_two_cues_on_one_line_are_not_removed` | **FAIL** (bug 1) |
| 35 | E golden test (tag before "Instance logs", no asterisks), E `A_pause_a_direction_and_an_emphasis...` | pass |
| 36 | E `The_same_script_gives_the_same_request_text_with_LF_or_CRLF_and_with_or_without_a_byte_order_mark`, I `A_script_with_no_speaker_labels_or_headings_is_narrated_as_written`, I `A_script_with_a_byte_order_mark_and_CRLF...`, E `Non_ASCII_characters_reach_the_request_body_unchanged` | pass |
| 37 | E golden test (`]\s*[` never appears), E `A_pause_a_direction_and_an_emphasis_in_a_row_become_one_tag_before_the_words`, E `No_chunk_is_larger_than_the_chunk_size...` (no tag alone at a chunk end) | pass |
| 38 | E `The_text_sent_for_the_sample_has_no_headings_headers_speaker_names_cues_END_or_asterisks` (reads the real request bodies) | pass |
| 39 | Design section 4.2 read: evidence recorded for each tag. I cannot re-verify the tag effects (no live calls allowed) | document review only |
| 40 | E `Every_request_is_a_documented_POST_with_the_fixed_model_and_voice_and_the_key_only_in_the_header` (URL, method, fields, no `input_references`) | pass |
| 41 | F `A_json_body_on_a_200_is_never_turned_into_an_mp3`, F `An_empty_audio_body_is_never_turned_into_an_mp3`, F transient status theory (raw audio success) | pass |
| 42 | F `Cancelling_during_the_second_request_...`, `An_already_cancelled_token_sends_no_request`, `Cancelling_while_waiting_to_retry_...` | pass |
| 43 | K `The_default_style_is_created_on_the_first_run_is_never_overwritten_and_an_edit_changes_the_next_request` | pass |
| 44 | K `An_unusable_style_file_names_the_file_says_deleting_restores_the_default_and_makes_no_request` (also runs again after deleting the file) | pass |
| 45 | E `The_request_has_profile_scene_notes_then_the_transcript_...` (order, `#### TRANSCRIPT`). Whether OpenRouter passes the structure to the model (U-2) is in the design's real-call evidence, not re-checked | pass |
| 46 | F `A_chunk_that_never_succeeds_fails_the_run_after_five_retries...` (6 requests, `chunk 1 of 1`, status, API message), F transient theory (6 statuses), F `Five_failures_are_retried_and_the_sixth_attempt_can_succeed_with_a_growing_wait`, F client-error theory (5 statuses, no retry), F `A_retry_after_header_sets_the_wait`, F network and timeout tests | pass |
| 47 | E `The_request_has_profile_scene_notes_...` (no Baldwin, Night Vale, Cecil, Lyria, music, mixing; contains "American") | pass |
| 48 | E `Segments_that_do_not_fit_together_are_split_at_the_segment_boundary`, E `A_segment_larger_than_a_chunk_is_split_at_paragraph_boundaries`, E `An_oversized_paragraph_is_split_at_sentence_ends_with_a_warning_and_the_tag_stays_with_its_text`, E `A_single_sentence_longer_than_a_chunk_is_sent_whole_with_a_warning...`, E `No_chunk_is_larger_than_the_chunk_size_and_no_segment_that_fits_is_split` | pass |
| 49 | Design section 4.3 read (measurements and the 1500-character choice); the constant is `NarrationSettings.MaxChunkCharacters` = 1500. Cannot re-measure | document review only |
| 50 | E `Every_chunk_carries_the_same_direction_voice_and_format_and_chunks_are_requested_one_at_a_time` (max 1 request in flight), F (a retry sends the identical body) | pass |
| 51 | E sample test (`chunk k of n` on stderr, `n` on the first line, stdout only the path), unit `ConsoleRunReporterTests` | pass |
| 52 | E sample test (tone of chunk k found at position k after decoding; duration within 100 ms of the sum), E `The_requests_together_carry_every_spoken_paragraph_once_in_script_order_and_nothing_else` | pass |
| 53 | E sample test (decoded with NLayer: 24000 Hz, 2 channels, left equals right sample for sample), E 3,000-line test (also decodes) | pass |
| 54 | Design section 3 read (135 calls, about $2.75 estimated of the $5 cap, key never printed). I cannot check the spend or the real charge | document review only |
| 55 | Unit `ArchitectureTests.The_postman_collection_uses_a_key_variable_and_no_real_key`; I parsed the collection: `POST {{baseUrl}}/audio/speech`, variable `OPENROUTER_API_KEY` empty, model and voice variables | pass |
| 56 | Manual: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` and `-r linux-x64`, both succeeded, one 75 MB executable each (section 4) | pass |
| 57 | README read: dev setup, build and test, use, key file path on Windows and Linux with contents, style file, script format pointer, GitHub settings | pass (document review) |
| 58 | `git diff main...5413ba3 -- CLAUDE.md` read: only "Current state", the confirm-after-scaffolding notes and the README line changed | pass (document review) |
| 59 | Unit `StyleProviderTests` (RuntimeTests.cs) compares the embedded default with section 9.1; E `The_request_has_profile_scene_notes_...`; `git diff main...5413ba3 --stat` shows `docs/style-guide.md`, `docs/script-writing-guide.md` and the sample unchanged | pass |
| 60 | W `The_pull_request_workflow_triggers_on_pull_request_with_no_path_or_branch_filter`; YAML parsed with PyYAML | pass (file check) |
| 61 | W `There_are_separate_windows_and_linux_jobs_and_the_readme_states_their_check_names` | pass (file check) |
| 62 | S `A_change_to_only_an_ignored_file_is_not_code` (11 paths), S `A_change_to_only_ignored_files_of_several_kinds...`; W (build and test steps conditional on the classification). Steps not run on GitHub | pass (script run, workflow read) |
| 63 | S `A_change_to_a_file_outside_the_ignore_list_is_code` (11 paths), S `One_code_file_among_ignored_files...`; W `Build_and_test_steps_run_only_when_code_changed_and_use_warnaserror_and_dotnet_test` | pass (script run, workflow read) |
| 64 | S `The_pull_request_title_pattern_accepts_conventional_commits_only` (16 titles, pattern read from the workflow file); W (`edited` type, title through an environment variable) | pass |
| 65 | W `No_workflow_uses_a_repository_secret_or_names_OpenRouter` (3 files) | pass (file check) |
| 66 | W `The_release_workflow_runs_only_on_a_push_to_main` | pass (file check) |
| 67 | S classify tests; Manual: real `git diff -z` of the two docs-only commits in this repo gave `code=false` | pass |
| 68 | S `The_bump_follows_the_conventional_commit_and_the_semantic_version_rules` (11 cases), S `A_commit_that_is_not_feat_fix_or_breaking_publishes_nothing` (9), S `A_sentence_that_only_mentions_a_breaking_change_is_not_a_breaking_footer` | pass |
| 69 | S `The_latest_tag_is_the_highest_by_semantic_version_precedence`, S `A_patch_bump_of_a_two_digit_patch...`, S `With_no_tag_the_first_release_is_1_0_0`, S `A_v_tag_that_is_not_MAJOR_MINOR_PATCH_fails_naming_the_tag` (5 tags) | pass |
| 70 | S `The_version_is_plain_semver_and_greater_than_the_previous_release`; W (`-p:Version="$VERSION"` reaches build and publish); Manual: `--version` of the published exes | pass |
| 71 | W `The_release_workflow_never_commits_pushes_a_branch_or_moves_a_tag`; `Directory.Build.props` holds only `0.0.0-dev` | pass (file check) |
| 72 | W `The_release_tests_on_windows_and_linux_before_packaging_and_only_the_publish_job_can_write` and the file read (tag and release only in the last job, tag deleted on failure). Not run on GitHub | file check only; see finding 3 |
| 73 | W tag tests, file read (exact SHA, existing tag fails naming it, no force). "Two merges in quick succession" rests on `concurrency: {group: release, queue: max}`, which I could not verify (finding 2) | file check only; see finding 2 |
| 74 | W `The_release_passes_the_version_to_the_build_and_publishes_the_two_named_assets_with_notes` (zip and tar.gz names, notes, latest, no macOS) | pass (file check) |
| 75 | W `No_workflow_uses_a_repository_secret_or_names_OpenRouter` | pass (file check) |
| 76 | W `The_readme_lists_the_github_settings_to_change`; design section 4.7 cites the docs pages. Whether the coding agent's final report lists them is not visible to me | pass (document review) |

## 3. Bug reports

| Report | Affects | Status |
|---|---|---|
| `docs/bugs/initial-development-1.md` | AC-34, AC-36: `[SFX: DOOR] and then he spoke [SFX: DOOR]` loses the words between the cues (`CueRegex` `^\[.*\]$`) | open (test fails) |

## 4. Manual checks (recorded output)

Scratch files were in the session scratchpad, outside the repo. The executables were run with no `OPENROUTER_API_KEY` and
`HTTPS_PROXY` set to a closed port, and only with inputs that fail before the key or runtime folder is touched.

- **Clean clone at CODE_HEAD**: `git clone` then checkout `5413ba3`; `dotnet build -warnaserror`: 0 warnings, 0 errors;
  `dotnet test --no-build` with dead proxies: 242 passed.
- **`win-x64`**: `dotnet publish src/PodcastGenerator.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:Version=1.2.3`
  gave one `PodcastGenerator.exe` (74.7 MB) plus `.pdb` files. `--version` printed `1.2.3` (exit 0, also with a PATH that has no dotnet);
  `--help` exit 0; no arguments exit 1 with `Error: No script file was given.` and the usage; `--bogus`, `a b c`, an output path
  `out.wav`, a missing script, a `.md` script and a cues-only script each exit 1 with a one-line message and empty stdout.
- **`linux-x64`**: the same publish gave one 74.8 MB `PodcastGenerator`. It was copied into WSL Ubuntu 26.04 (where `dotnet` is not
  installed) and run: `--version` printed `1.2.3` (exit 0), `--help` exit 0, no arguments, an output path `out.wav`, a missing script,
  a cues-only script and `--bogus` each exit 1 with the same messages as on Windows.
- **Workflow YAML**: `.github/workflows/*.yml` parsed with PyYAML 6.0.3: all three files are valid YAML (`concurrency: {group: release, queue: max}` is read as plain keys).
- **`classify-changes.sh` with real `git` output**: `git diff --name-only -z --no-renames 090b699 d9311b7` and
  `git show --pretty=format: --name-only -z --no-renames d9311b7` (the fallback the release workflow uses when the before-commit is
  unknown), both docs-only, gave `code=false`.
- **Real profile folders**: before and after the test runs the directory time of `~/.config/PodcastGenerator` is unchanged and
  `~/PodcastGenerator` does not exist (only directories were looked at; no key file was opened or listed).

## 5. What I could not run, and findings for the caller

Not run, and why:

- The three GitHub workflows (no push, no `gh`, no GitHub runner here). Their decisions were tested through the two helper scripts and
  the title pattern, and their structure was checked as text; the steps themselves (checkout, `setup-dotnet`, `Compress-Archive`, `git tag`
  and `git push` of the tag with the workflow token, `gh release create`, the tag cleanup) have never run.
- `dotnet test` on Linux. The Linux executable was started (WSL) but the suite and the encoder-to-decoder tests ran on Windows only;
  the PR workflow's Linux job is their first Linux run. `dotnet` is not installed in the WSL distro and I did not install it.
- A real Ctrl+C on a running process (the cancellation path is tested through the token, in process).
- Anything against the real OpenRouter API, by rule. Tag effects, chunk size and cost claims in the design doc are unverified by me.

Findings (each one is not a failed acceptance criterion unless a bug report says so):

1. **Bug 1** (above): words between two bracketed cues on one line are dropped.
2. **`concurrency: {group: release, queue: max}` in `release.yml` could not be verified.** The design cites the GitHub workflow
   syntax page for it and says actionlint 1.7.12 does not know the `queue` key. I have no way to read that page here. AC-73 ("two merges
   in quick succession never produce the same tag") depends on it. If GitHub rejects the key, the release workflow is invalid and no
   release is ever made. Question for the user: has the key been seen in the current GitHub docs? The first merge to `main` is the real test.
3. **Tag cleanup uses `if: failure() && ...`.** A cancelled run (not a failure) between the tag push and the release would leave a tag
   with no release, against AC-72 ("on any failure neither exists"). `always()` would cover it. Low severity; reading only.
4. **A short final chunk is possible.** Chunks are packed greedily to 1500 characters, so a script slightly larger than a chunk ends
   with a tiny chunk. Shown with a real run: a paragraph of 1483 characters followed by a paragraph of 44 characters gave chunks of
   1483 and 44 characters. The design (section 2, item 1) measured runaway extra speech on 80 to 120 character transcripts (2 of 55
   calls), so short tail chunks may hit that. This is not an AC violation.
5. **`(WHISPERED, SLOW)` becomes `[whispered, slow]`**, not the verified `[whispers]`; the design measured `[whispered]` as much less
   effective (1 of 5 runs), and only the exact direction `WHISPERED` is mapped. Consistent with D-6 and the design section 4.2; noted
   because the tag differs by how the direction is written.
6. **Deviation from D-12 (design section 2, item 7):** a 402 that carries `Retry-After` is retried; the proposed default says 402 fails at
   once. I did not add a test for it (it rests on an OpenRouter doc statement I cannot re-read).
7. Behaviors the coding agent listed, seen or not seen by my tests: the model runaway on very short or very long chunks and the emphasis
   tag having no measured effect cannot be tested without the real API. Approximate pause lengths, the 1500-character chunk size, no
   silence trimming between chunks, and the `[whispers]` mapping are as the design says: the tests show the tags `[whispers]`,
   `[emphasis]`, `[short pause]` and `[pause for N seconds]` in the request text, the chunks joined with no silence added (duration equals
   the sum of the chunk durations within 100 ms), and 1500 as the largest chunk.
