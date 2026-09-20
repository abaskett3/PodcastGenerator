# Code review: initial-development, round 1

Branch `feature/initial-development`, CODE_HEAD `5413ba35974d90e3a11cd096ad98f5d65d6063b4`, diff `git diff main...5413ba3`.
Spec: `docs/specs/initial-development.md` (revision 4, 76 ACs). Design: `docs/designs/initial-development.md`.

VERDICT: CHANGES_REQUESTED (1 blocking, 7 non-blocking)

## Summary

The implementation is close to complete and well built. Layering is correct (Domain references nothing, Application only
Domain, Infrastructure Application, Cli composes; the only `HttpClient` user is `OpenRouterSpeechClient` behind
`ISpeechClient`). Services come from DI, async methods end in `Async` and take a `CancellationToken`, the key is held in an
`ApiKey` type that redacts on `ToString`, is used only for the bearer header, and is redacted from API messages. No test makes a
live call or reads a key file (fake `HttpMessageHandler`, in-memory file system, fake key). The README covers setup, usage, key
path, check names and the GitHub settings. The three workflows and two scripts follow the CI ACs (always-start PR workflow,
ignore list, least-privilege permissions, version computed from the latest `v*` tag, no version stored in the repo, `.zip` and
`.tar.gz` asset names, tag created with `git push` on the built SHA). `CLAUDE.md` was changed only in the scaffolding notes
(AC-58). `git grep` over tracked files found no key-shaped text (`sk-or-`) and no `TEMP-VERIFY` code.

There is one blocking finding: the missing-key error does not contain the literal line format AC-24 requires, and its test was
loosened to hide that. The other findings are optional improvements, each with evidence.

I did not run `dotnet` (read-only rule). Claims about build and test results in the design doc are not verified by me. Findings
that rest on .NET or GitHub behavior say so.

## Findings

### 1. BLOCKING: the missing-key message does not contain `OPENROUTER_API_KEY=<key>` (AC-24)

- Location: `src/PodcastGenerator.Application/Generation/PodcastGenerationService.cs:168-170`; weak test at
  `tests/PodcastGenerator.UnitTests/Generation/PodcastGenerationServiceTests.cs:276`.
- Evidence: AC-24 says the tool "prints an error containing the full path of the key file and the line format
  `OPENROUTER_API_KEY=<key>`". The spec's error table says the message must contain "`OPENROUTER_API_KEY=<key>`", and
  `CLAUDE.md` "Usage" gives the same line. The code builds `"...{ApiKeyProvider.KeyName}=<your key>, or set..."`, so the text is
  `OPENROUTER_API_KEY=<your key>`, which does not contain the required string. The test asserts only
  `Assert.Contains("OPENROUTER_API_KEY=<", ...)`, so it passes and would not catch a wrong format.
- Change requested: make the message contain exactly `OPENROUTER_API_KEY=<key>` (for example "containing the line
  `OPENROUTER_API_KEY=<key>`, where `<key>` is your OpenRouter key"), and tighten the test to `Assert.Contains("OPENROUTER_API_KEY=<key>", ...)`.
  The README example at `README.md:64` (`<your key>`) may stay, or be aligned; it is not part of AC-24.

### 2. NON-BLOCKING: two "skip build output" filters in `ArchitectureTests` do not do what they say, so scans can silently skip files

- Location: `tests/PodcastGenerator.UnitTests/Architecture/ArchitectureTests.cs:283` and `:289` (used by `SourceFiles()` at
  `:270-272` and `ScannedFiles()` at `:275-286`).
- Evidence: `Path.Combine("", ".git", "")` and `Path.Combine("", "bin", "")` skip empty segments and (per the documented behavior
  of `Path.Combine`, an empty last argument adds no separator) return just `.git` and `bin`, not `\.git\` and `\bin\`. The checks are
  therefore substring tests on the whole path. Consequences: (a) `file.Contains(".git")` is true for everything under `.github/`
  and for `.gitignore` and `.gitattributes`, so the AC-27 key scan (test `No_source_document_or_collection_contains_something_shaped_like_a_real_key`)
  never scans the workflows or scripts, although its extension list (`.yml`, `.sh`, `.gitignore`, `.gitattributes`) says it should;
  (b) on a machine whose clone path contains `bin` or `obj` (for example `/home/robin/...`), `SourceFiles()` and `ScannedFiles()` return
  nothing and the AC-8 and AC-27 tests pass without checking anything. Today no tracked path contains `bin` or `obj`
  (`git ls-files | grep -E "bin|obj"` is empty), so the tests pass on this tree. I could not run the tests to confirm.
- Change requested: compare path segments, for example
  `path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj" or ".git")`, measured from
  `RepositoryFiles.Root` (relative path), so the repository's location does not matter.

### 3. NON-BLOCKING: the repo-wide key scan opens any dot-file and anything under `.claude/`, including `.env.*`

- Location: `tests/PodcastGenerator.UnitTests/Architecture/ArchitectureTests.cs:282-285`.
- Evidence: `CLAUDE.md` "Gotchas": "no test reads the user's key or key file"; AC-5 says the same. The scan skips only names ending in
  `.env`, but its last filter `Path.GetFileName(file).StartsWith('.')` admits `.env.local`, `.env.production` and similar
  (`.gitignore` lines 486-489 list `.env` and `.env.*` as secret files) and the walk enters `.claude/` (AC-27 names `.claude/credentials/`
  as a place a key may live). Any such file present on a developer machine is opened and read into memory by `dotnet test`. Nothing is
  printed from it, but the rule is that tests do not read key files.
- Change requested: scan only the repository's tracked content roots (for example `src`, `tests`, `docs`, `postman`, `.github`, and the
  root files) and skip `.claude` and every file whose name is `.env` or starts with `.env.` or ends with `.env`.

### 4. NON-BLOCKING: `HttpClient.Timeout` does not cover reading the response body, so a stalled body can hang a run

- Location: `src/PodcastGenerator.Infrastructure/Speech/OpenRouterSpeechClient.cs:45-47` (`HttpCompletionOption.ResponseHeadersRead`) and `:54`
  (`ReadAsByteArrayAsync`); timeout set at `src/PodcastGenerator.Infrastructure/DependencyInjection.cs:11-13,29`.
- Evidence: the comment says the 5 minute timeout "only stops a hung connection". With `ResponseHeadersRead`, .NET's `HttpClient.Timeout`
  stops applying once the headers have arrived (my reading of the framework, not verified by running it). The design doc records that the
  real response is `Transfer-Encoding: chunked` with 30 to 80 s of latency for long chunks, so the body read is the long part. If the
  connection stalls after the headers, the run waits until Ctrl+C, never reaching the D-12 retry path ("network errors, timeouts"). The
  tests `A_timeout_is_a_transient_failure` only simulate a timeout during `SendAsync`.
- Change requested: apply a per-attempt linked `CancellationTokenSource` with the same limit around the whole send and body read, mapped
  to the existing transient "timed out" `SpeechException` when the caller's token is not the one cancelled, and add a test with a handler whose
  content stream never completes.

### 5. NON-BLOCKING (needs user confirmation): D-12 deviation, a 402 with `Retry-After` is retried

- Location: `OpenRouterSpeechClient.cs:122-124`; test `OpenRouterSpeechClientTests.cs:154-166`; README `README.md:40-41`.
- Evidence: D-12 (and AC-46, which defers to D-12) says other 4xx errors "(for example 401, 402) fail at once". The code retries a 402 only when
  it carries `Retry-After`. The design (section 4.4) cites OpenRouter's error documentation for this ("a 402 without the header is not a wait-and-retry
  case") and lists it as a deviation for the user (section 2, item 7). U-10 asked the coding agent to check OpenRouter's error behavior, so the
  change is grounded and narrow, a 402 without the header still fails at once, and it is tested and documented. This is not an AC violation.
  No code change requested unless the user rejects the deviation; the user should confirm D-12 as amended.

### 6. NON-BLOCKING (accepted deviation): D-4 header-line rule applies only when a `##`/`###` heading exists

- Location: `src/PodcastGenerator.Application/Scripts/ScriptParser.cs:59-61` and `:114-117`; test `ScriptParserTests.cs:60-67`.
- Evidence: D-4 says any `LABEL: value` line before the first heading is a header line. Applied literally to a script with no heading, every
  speech line is "before the first heading", the script would have nothing to narrate, and AC-36 ("a script that departs from the guide still runs")
  and AC-30 would break. The coding agent limited D-4 to scripts that have a scene or segment heading and recorded this in the design (section 8, D-4). The
  four named labels are always dropped (AC-29). I agree with the reading. No change requested; the user should confirm D-4 as amended.

### 7. NON-BLOCKING: `classify-changes.sh` matches the ignore list case-insensitively for directories and `.gitignore`

- Location: `.github/scripts/classify-changes.sh:13` (`shopt -s nocasematch`) and `:21`.
- Evidence: AC-62 and `CLAUDE.md` "CI/CD" list `docs/`, `.docs/`, `.github/`, `.claude/`, `agent-memory/` and the root `.gitignore`; only "any `.md` file" is meant to
  match at any case (design section 9 says `.md` is case-insensitive). `nocasematch` also applies to the directory names, so a changed
  `Docs/Program.cs` or `DOCS/x.cs` is classified as ignored and a PR or merge touching only such files would skip build, test and release.
  `CLAUDE.md` "Gotchas" says Linux file names are case-sensitive. Practical risk is low.
- Change requested: keep case-insensitivity for the `.md` extension only (for example `*.[mM][dD]`) and match the directory names and `.gitignore` case-sensitively.

### 8. NON-BLOCKING: an invalid `v*` tag is reported only when a release is being computed

- Location: `.github/scripts/compute-release.sh:41-43` (early exit on `bump=none`) versus `:52-56` (tag validation).
- Evidence: AC-69: "A `v*` tag that is not `vMAJOR.MINOR.PATCH` fails the workflow with a message naming the tag." With a bad tag present and a `chore:` merge, the
  script exits 0 at line 43 before validating tags, so the workflow reports success. A `feat:` or `fix:` merge does fail with the tag named, which is the
  case that matters for correctness of the version. The AC can be read either way.
- Change requested (optional): validate the tags before the `bump=none` exit.

## Checks made with no finding

- Completeness: AC-1 to AC-76 are each covered by code, tests or workflow files, as mapped in design section 11. AC-39 and AC-49 rest on recorded real
  measurements (design sections 3 and 4). Directions other than `WHISPERED` use an unverified lower-cased tag, disclosed in the design (section 4.2).
- Layering and DI: `Program.cs` builds the container with `ValidateOnBuild` and `ValidateScopes`; the only `new` of a service in `src` is the factory
  `Mp3AudioWorkspaceFactory.Create` (a factory is a legitimate place, and the workspace has per-run state).
- Retries and cancellation: up to 5 retries (6 attempts) with 2, 4, 8, 16, 30 s waits, `Retry-After` capped at 120 s; the token reaches the HTTP call, the
  delay and the encoder loop; the `finally` in `GenerateAsync` deletes the temporary file and the workspace deletes its folder. Tests cover these with fakes.
- Paths: everything is built with `Path.Combine`, `Environment.SpecialFolder.UserProfile`; `File.Move(overwrite: false)` and `FileMode.CreateNew` protect existing files (AC-10, AC-14).
- CI: PR workflow has no path or branch filter, every step after classification is conditional, permissions are `contents: read`; the title workflow has `permissions: {}`
  and passes the title through an environment variable; the release workflow gives `contents: write` only to the publish job, checks the tag does not exist,
  creates it on the built SHA with `git push` (no branch push, AC-71), uses `gh release create --verify-tag --generate-notes --latest`, and removes only the tag it created
  if publishing fails. `Directory.Build.props` holds only the `0.0.0-dev` placeholder; the release passes `-p:Version`.
- No workflow uses a secret or calls OpenRouter (AC-65, AC-75).

## Questions for the user or coordinator

1. `release.yml:17-19` uses `concurrency: {group: release, queue: max}`. The design (section 4.7) says the GitHub docs describe `queue: max` but that
   actionlint 1.7.12 does not know the key. I could not check GitHub's documentation from this environment. If the key is not valid, GitHub would reject the workflow file
   and no release would ever run. Please confirm against the current workflow-syntax page before merging. If it is not supported, the fallback (default
   concurrency, where a pending run can be replaced by a newer one) would need the AC-73 "two merges in quick succession" behavior reconsidered.
2. Items 5 and 6 (D-12 for 402 with `Retry-After`, D-4 limited to scripts that have a heading) are deviations from proposed defaults; the user should confirm them.
3. The open decisions the coding agent listed (plausibility check, emphasis tag, pause accuracy, 1500-character chunks, silence between chunks, LGPL-3.0 encoder without a license
   file) were not raised as findings, because no AC is violated by them.

## Counts

Blocking: 1. Non-blocking: 7.
