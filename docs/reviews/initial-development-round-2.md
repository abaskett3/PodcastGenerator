# Code review: initial-development, round 2

Branch `feature/initial-development`, CODE_HEAD `b37671912d44360922e1463d17ef3f2150be2500`. Reviewed the delta `git diff 5413ba3...b376719`
(the coding agent's fix commit `b376719` and the tester's `test:` commit `93d254b`) plus the surrounding code. Round 1 is in
`docs/reviews/initial-development-round-1.md`.

VERDICT: APPROVE (0 blocking, 5 non-blocking)

I did not run `dotnet` (read-only rule), so build and test results are not verified by me. No key file was opened; `git grep` over tracked
files was used for searches.

## Round 1 findings: status

| # | Round 1 finding | Status | Evidence |
|---|---|---|---|
| 1 | AC-24 message must contain `OPENROUTER_API_KEY=<key>` | Fixed | `PodcastGenerationService.cs:168-170` now builds `{KeyName}=<key> (where <key> is your OpenRouter API key)`, so the exact string is present. The unit test at `PodcastGenerationServiceTests.cs:276` now asserts `"OPENROUTER_API_KEY=<key>"` with `StringComparison.Ordinal`. README example aligned (`README.md:64-67`). |
| 2 | Scan filters were substring tests | Fixed | New `tests/.../Support/RepositoryScan.cs` judges the path relative to the repo root, split into segments (`bin`, `obj`, `.git`, `.claude` skipped by segment, case-insensitive). `RepositoryScanTests` covers `.github/...`, `.gitignore`, `.gitattributes`, `src/robin/`, `src/binder/`, `tests/objective/` (scanned) and `bin/`, `obj/`, `.git/` (skipped), and asserts the real scan reaches `.github/workflows/release.yml`, `compute-release.sh`, `.gitignore`, `README.md` and `Program.cs`. `ArchitectureTests.cs` now delegates to it. |
| 3 | Scan opened `.env.*` and `.claude/` | Fixed | `RepositoryScan.cs:10-14` lists only the root's own files and `src`, `tests`, `docs`, `postman`, `.github`; `IsKeyFile` (`:22-25`) rejects `.env`, `.env.*`, `*.env`, and `.claude` is a skipped segment. The walk never opens a key file: the filter runs before any read, and the tests check `.env.local`, `docs/.env.local`, `tests/.ENV`, `config.env`, `.claude/credentials/key.json`. |
| 4 | `HttpClient.Timeout` does not cover the body read | Fixed | `OpenRouterSpeechClient.cs` creates a linked `CancellationTokenSource` with `CancelAfter(_options.RequestTimeout)` per attempt and uses its token for the send, the error-body read and the audio-body read. When it fires and the caller's token has not, the existing `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` throws the transient `SpeechException("The request timed out.")`, so the D-12 retry path runs. `HttpClient.Timeout` is `InfiniteTimeSpan` in DI (one clock), `SpeechClientOptions` is registered with 5 minutes. New tests: a body that stalls after a 200, an error status whose body stalls, caller cancellation while reading (stays an `OperationCanceledException`), a body arriving in time, and the registered values; the stall tests use `WaitAsync(30 s)` so a missing limit fails instead of hanging. |
| 7 | Ignore list matched directories case-insensitively | Fixed | `classify-changes.sh` no longer sets `nocasematch`; the pattern is `docs/* | .docs/* | .github/* | .claude/* | agent-memory/* | .gitignore | *.[mM][dD]`. `WorkflowScriptTests` checks `Docs/Program.cs`, `DOCS/x.cs`, `.GitHub/...`, `.Docs/...`, `.Claude/...`, `Agent-Memory/...`, `.Gitignore` are code, and `README.MD`, `docs/Notes.Md`, `x/y.mD`, `Docs/Program.md` are ignored. |
| 8 | Malformed `v*` tag only checked when releasing | Changed as described; see finding 1 below | `compute-release.sh` validates all tags before the `bump=none` exit; `release.yml:45-55` runs it before classification. |

## Findings

### 1. NON-BLOCKING: AC-69 tag check is now unconditional; both readings are supported, no AC is violated

- Location: `.github/scripts/compute-release.sh` (tag loop now before the `bump=none` exit), `.github/workflows/release.yml:45-55` (script runs
  before `classify-changes.sh`, line 64); tests `WorkflowScriptTests.cs:259-273`, `WorkflowFileTests.cs:156-167`.
- Evaluation against the spec:
  - AC-69 says "A `v*` tag that is not `vMAJOR.MINOR.PATCH` fails the workflow with a message naming the tag." The sentence has no condition, so the
    literal text supports the new behavior.
  - AC-67 says that when every changed file is in the ignore list "no tag and no release are created". The new behavior still creates neither; it only
    makes the `Plan release` job fail. AC-67 is not violated. `CLAUDE.md` "CI/CD" says a change touching only ignored files "does not build, test or
    release"; failing at the plan step does none of those.
  - The spec's own summary of the release workflow reads "on a push to `main` (the squash-merge), unless only ignored files changed, reads the squash
    subject and body ... It reads the latest `v*` tag". That wording supports the previous, conditional reading for docs-only pushes.
  - So the spec does not settle this; neither reading breaks an acceptance criterion. The unconditional reading is stricter about AC-69's literal text and
    fails loudly. Its cost, stated in the design (section 13, item 7), is that while a malformed `v*` tag exists every push to `main`, including a
    docs-only one, shows a failed `Plan release` run; the workflow is not a required check on `main`, and no tag or release is produced.
- Change requested: none. This is a user decision (revert is described in design section 13, item 7 if the user prefers the conditional reading).

### 2. NON-BLOCKING: the stricter `CueRegex` narrates some malformed cue lines that used to be dropped

- Location: `src/PodcastGenerator.Application/Scripts/ScriptParser.cs:24-28`; oracle `tests/.../Integration/PipelineHarness.cs:345`.
- Evidence: the new pattern `^\[(?:[^\[\]]|\[[^\[\]]*\])*\](?:\s*\[...\])*$` accepts one or more bracket groups with at most one level of nesting. I checked
  that it is not vulnerable to catastrophic backtracking (the alternatives are exclusive on their first character), that it still removes every cue form in
  the guide and sample (`docs/script-writing-guide.md:65,71,79` and the sample's `[MUSIC: ...]`, `[SFX: ...]`, `[FADE OUT]` lines, which have no nested
  brackets), and that it fixes tester bug 1 (`[SFX: DOOR] and then he spoke [SFX: DOOR]` is narrated as written, AC-36, D-10). The change in behavior:
  a line with an unbalanced or doubly nested bracket, for example `[SFX: DOOR SLAM]]` (a stray bracket) or `[SFX: A [B [C]]]`, used to match `^\[.*\]$` and be
  removed silently; it no longer matches and is now narrated as written (as a spoken line with bracket text the model may treat as a tag). AC-34 says a line
  "consisting only of a square-bracket cue" is removed, and AC-36 says the tool performs no conformance check, so this is a defensible reading and not an AC
  violation. Separately, the test-side `ScriptOracle` (`PipelineHarness.cs:345`) still uses the old rule (`StartsWith('[') && EndsWith(']')`); it agrees with the
  product on the sample script and on the tests that use it, so nothing fails today.
- Change requested: none required. Optional: if the user wants stray-bracket typos in cue lines dropped, decide that explicitly; and align `ScriptOracle`
  with the parser's rule so a future test on such a line is not judged against the old rule.

### 3. NON-BLOCKING (disclosed gap): a run cancelled or failed during `gh release create` can leave a draft release

- Location: `.github/workflows/release.yml:240-258` and design section 13, item 8.
- Evidence: the cleanup condition is `(failure() || cancelled()) && steps.tag.outputs.created == 'true' && steps.release.outcome != 'success'`. I checked
  it: `created` is written only after this run's `git push` of the tag succeeded (line 238), so a tag made by another run is never removed (the "tag exists"
  step fails earlier and leaves `created` unset); `cancelled()` covers the case the round 1 tester found, where `failure()` alone skips a cancelled run;
  `steps.release.outcome != 'success'` keeps the tag of a release that finished; a skipped `release` step counts as not success, so the tag is removed.
  The remaining gap, which the coding agent states: if the runner is cancelled while `gh` is uploading assets, `gh` may leave a draft release while the tag is
  deleted, and AC-72 says "on any failure neither exists". The `failure()`, `cancelled()` step-status functions are used as GitHub documents them (I could not
  open the page here). A person would delete a stray draft by hand.
- Change requested: none required (rare, disclosed, and a draft is not published). It could be listed in the README next to the settings if the user wants it known.

### 4. NON-BLOCKING (user decision, carried from round 1): D-12, a 402 with `Retry-After` is retried

- Location: `OpenRouterSpeechClient.cs` (`isTransient` for 402 with `Retry-After`); tests `OpenRouterSpeechClientTests.cs` `A_402_is_a_wait_and_retry_case_only_when_it_carries_Retry_After`.
- Evidence and status unchanged: the deviation from D-12 is grounded in OpenRouter's error documentation (design 4.4), narrow, tested and documented. The user should confirm.

### 5. NON-BLOCKING (user decision, carried from round 1): D-4, the header-line rule applies only when a `##`/`###` heading exists

- Location: `ScriptParser.cs:59-61,114-117`; test `ScriptParserTests.cs:60-67`.
- Evidence and status unchanged: applying D-4 literally to a script with no heading would drop every speech line and break AC-30 and AC-36. The user should confirm.

## Other checks on the new code

- Timeout code: the `using var attemptTimeout` is declared before the response and disposed at method exit; the caller's token and the attempt token are
  distinguished correctly in the catch; a non-success status whose body stalls is also retried as a timeout (tested), which is acceptable because a stalled
  connection is transient. `HttpClient.Timeout` is infinite, so connection setup is covered by the same attempt token. The typed client's new constructor is
  resolved through `AddHttpClient<ISpeechClient, OpenRouterSpeechClient>` with `SpeechClientOptions` from DI (`CompositionTests` and
  `The_registered_speech_timeout_is_five_minutes_and_the_client_owns_it` cover it).
- Tester's integration tests (`Integration/`): none reads the real key file or real profile folders (`git grep` for `GetFolderPath`, `ForCurrentUser`, `HOME`,
  `USERPROFILE`, `SetEnvironmentVariable`, `new HttpClient`, `HttpClientHandler` found only path arithmetic in `KeyAndRuntimeFolderTests.cs:39-40`, no I/O).
  `Pipeline` uses a temporary profile, a fake key, a fake `IEnvironmentVariables`, a canned `HttpMessageHandler` that throws for any URL other than the speech
  endpoint, a fake delayer and a fake clock. `CliProcessTests` starts the built CLI only on failure paths that end before the runtime folder or key is touched
  (I confirmed the order in `PodcastGenerationService.GenerateAsync`: output check, script read, parse, then the runtime folder), removes
  `OPENROUTER_API_KEY` from the child environment and sets every proxy variable to a closed local port as a second guard. `WorkflowScriptTests` runs the two
  scripts under bash with no network. AC-5 holds.
- Tests that could hide a defect: I found none. The new unit tests are written so they fail without their fix (the design records that each was checked by
  restoring the old source); the workflow file tests are text checks and say so.
- Everything else from round 1's "Checks made with no finding" section is unchanged by the delta.

## Counts

Blocking: 0. Non-blocking: 5.
