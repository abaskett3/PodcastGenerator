# Initial development: first working CLI (TTS narration) and CI/CD (GitHub Actions)

Status: DRAFT, revision 3. Revision 3 drops music, Lyria and SFX from 1.0.0 (user decision), folds in the user's answers
on repo, script format, style guide, API key and script length, and re-reads `CLAUDE.md`, the sample script and
`docs/style-guide.md`. BLOCKING open questions remain. Acceptance criteria that still depend on an unanswered question
say so with `(depends on OQ-n)`.

How to read the evidence: I (the product-manager agent) have only Read, Glob, Grep and Write and could not open any web
page. External facts are cited as `[S<n>]` to the "Sources" list, each marked as read directly or "relayed, not read by
the PM". The coding agent must read each relayed source itself before relying on it and stop and report if it differs
from this spec. Anything without a source is an open question or is marked as my estimate or recollection (unverified).

## Summary

PodcastGenerator does not exist as code yet (no solution, projects, tests or `README.md`; `CLAUDE.md` "Current state"
[S1]). This feature delivers the first working version and its delivery pipeline. The product turns a podcast script
(`.txt` or `.md`) into one audio file: the script's words are narrated, in a deadpan "Welcome to Night Vale" style [S5],
by the model `google/gemini-3.1-flash-tts-preview` through OpenRouter's speech endpoint `POST /api/v1/audio/speech` [S2].
Style comes from an editable style file derived from `docs/style-guide.md` [S11]. The script contains sound-effect and
music cues and delivery directions; in 1.0.0 the cues are direction only (nothing is rendered as sound). There is no
music generation, no SFX rendering and no mixing in 1.0.0. Long scripts (the user's range is 0 to 3000 lines) are
narrated in pieces and joined into one file. The API key is read by the executable from a config file in the user
profile. The feature also delivers the Clean Architecture solution (Domain, Application, Infrastructure, Cli) and two
GitHub Actions workflows: the pull-request workflow always starts and reports a check the user can require, and the
release workflow, on a squash-merge to `main`, computes a Semantic Versioning version from the conventional-commit
subject, tags `v<version>` and publishes a GitHub Release with the Windows and Linux executables. The first published
version is `1.0.0`.

## User-facing behavior

### CLI

Invocation (`CLAUDE.md` "Usage" and "Commands" [S1]):

```text
PodcastGenerator <scriptPath> [outputPath]
dotnet run --project src/PodcastGenerator.Cli -- <scriptPath> [outputPath]
```

- `<scriptPath>` (required): path to an existing `.txt` or `.md` script. Markdown is treated "like a regular Md file"
  (user). The script holds narrated words, speaker labels, and bracketed cues and directions (examples from the user:
  `[THEME MUSIC: Eerie music swells, fades to gentle static]`, `**CECIL:** [Whispered]`, `[SFX: Machine shutdown
  sequence]`). What is narrated, dropped or passed to the model as direction is decided partly (see "Script handling")
  and partly open (OQ-1, OQ-2).
- `[outputPath]` (optional): a file path or an existing directory.
- Default output directory: `PodcastGenerator/` under the user profile folder, resolved with
  `Environment.SpecialFolder.UserProfile` (never a literal `%USERPROFILE%`) and built with `Path.Combine` [S1].
- Default file name: `Podcast-<date as MM-DD-YYYY>` plus the audio extension, for example `Podcast-09-19-2026.<ext>`
  (user). Extension: OQ-7. Time zone of the date: OQ-14.
- Output path rules (user): an existing directory means write inside it with the default name; missing parent
  directories are created; if the file exists it is kept untouched and the new file gets a numeric suffix "like Windows
  does" (pattern: OQ-14); if the run fails after a file was created the partial file is deleted.
- API key: the executable reads `OPENROUTER_API_KEY` from the file
  `<Environment.SpecialFolder.UserProfile>/.config/PodcastGenerator/PodcastGenerator.env`, which holds the line
  `OPENROUTER_API_KEY=XXX` (user). The path uses `.config` under the profile on both Windows and Linux. The user keeps a
  separate copy of the key for Claude in a global `.claude/credentials/` location; that location is not used by the
  product. Whether the environment variable is still supported: OQ-3. The key is never printed or logged.
- Long scripts: split into chunks, each narrated with the same style and voice, and joined in order into one file
  (OQ-6, OQ-7).
- On success: one audio file is written, its full path is printed to standard output, the exit code is 0. On failure: a
  human-readable message on standard error, a non-zero exit code, no stack trace for an expected failure (wording:
  OQ-23).

Script handling, decided:

- Markdown formatting marks are not spoken (headings `#`, `---`, `*emphasis*`, `**bold**`, backticks, `- ` list
  markers). Sample: [S10] lines 1-5, 19, 27, 63-65.
- `[SFX: ...]` and `[THEME MUSIC: ...]` cues are direction only: not narrated, nothing generated (user; [S10] lines 7,
  15, 207).
- A delivery tag such as `[Whispered]` in `**CECIL:** [Whispered] The sky is turning ...` ([S10] line 209) is passed to
  the speech model, which reads it as an instruction (user). Google's rules, relayed [S4]: square brackets, placed before
  the text they affect, never two tags adjacent.

Script handling, open: how to tell a cue from a delivery tag when there is no prefix (`[Low, ominous music]`, `[Pause]`,
`[FADE OUT]`, [S10] lines 23, 89, 173, 211), whether dropped cues are also passed to the model as direction or listed
on standard error (OQ-1); what happens to speaker labels, headings, title lines and unlabelled text (OQ-2).

Errors the user can see:

| Condition | Message must contain | HTTP call made? |
|---|---|---|
| No argument, or more than two | Usage line showing `<scriptPath> [outputPath]` | No |
| `<scriptPath>` does not exist | The path | No |
| Extension not `.txt` or `.md` | The supported extensions | No |
| Script empty, or no narratable text left after cues are removed | That there is nothing to narrate | No |
| API key not found (per OQ-3) | The expected config file path and the name `OPENROUTER_API_KEY`; never a key value | No |
| Config file unreadable | That the file could not be read and its path | No |
| A narration request gets a non-success HTTP status | That narration failed, the chunk number and count if chunked, the status code, the API error message if present | Yes |
| Network failure or timeout | Which chunk failed and why (no key) | Yes |

### CI/CD

Pull request workflow (`CLAUDE.md` "CI/CD" [S1], user answers): always starts on pull requests to `main`. Inside the job
it checks whether every changed file is in the ignore list; if so it skips the build and tests and the job (the check)
reports success; otherwise it builds, runs the tests and reports pass or fail. The user marks the check required in
GitHub settings. Reason it always starts (relayed, [S6]): a workflow skipped by path or branch filtering, or a commit
message, leaves its checks "Pending" and blocks a PR that requires them, while a skipped job reports Success. The user
accepts that PRs touching only workflow files or `.md` files do not run tests.

Release workflow: runs on every push to `main` (the squash-merge). It publishes nothing when every changed file is
ignored, and nothing when the squash commit has no `fix:`, `feat:` or breaking marker. Otherwise it reads the latest
`v*` tag, bumps it (`fix:` PATCH, `feat:` MINOR, breaking MAJOR), passes the version to the build, builds, publishes
`win-x64` and `linux-x64` self-contained single-file executables, tags `v<version>` and creates a GitHub Release with
both attached. With no tag the version is `1.0.0` (OQ-15). No build number. The version is not stored in a repo file and
the workflow does not commit to `main` ("Lets not update any code yet, but we will revisit that").

Ignore list, both workflows (user): `docs/`, `.docs/`, `.github/`, `.claude/`, `agent-memory/`, `.gitignore`, any `.md`
file. Anchoring: OQ-17. Consequence to note: `docs/style-guide.md` and `docs/sample-scripts/` are ignored (OQ-9).

## Acceptance criteria

### Solution and architecture (`CLAUDE.md` [S1])

- **AC-1**: On a clean checkout, `dotnet build -warnaserror` from the repo root exits 0 with no warnings on Windows and
  on Linux.
- **AC-2**: The repo contains a solution with projects Domain, Application, Infrastructure and Cli (Cli at
  `src/PodcastGenerator.Cli`) and at least one xUnit test project. Project references follow the Clean Architecture
  dependency rule: Domain references nothing; Application only Domain; Infrastructure Application (and Domain); Cli the
  others for composition.
- **AC-3**: All OpenRouter/HTTP code (any `HttpClient` use) is in Infrastructure behind an interface declared in
  Application. No other project uses `HttpClient`.
- **AC-4**: Services come from dependency injection (no `new` for services). Every async method name ends in `Async` and
  accepts a `CancellationToken`.
- **AC-5**: `dotnet test` from the repo root exits 0 on a machine with only the .NET SDK. No test calls the real
  OpenRouter API, no test needs a real API key, and no test reads or writes the developer's real
  `<UserProfile>/.config/PodcastGenerator/` or `<UserProfile>/PodcastGenerator/` folders (tests use fake keys and
  temporary directories). HTTP is mocked or stubbed.

### Inputs, outputs and failures

- **AC-6**: Given an existing `.txt` script, a key available per AC-17, and a stubbed narration response, running
  `PodcastGenerator <scriptPath>` writes one audio file into the default output directory, prints its full path to
  standard output and exits 0. The same holds for a `.md` script. The format is decided in OQ-7.
- **AC-7**: The default output directory equals
  `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PodcastGenerator")` and is created if
  missing. No literal `%USERPROFILE%`, `\` or `/` separator appears in path-building code.
- **AC-8**: The default file name is `Podcast-` plus the run date formatted `MM-DD-YYYY` plus the audio extension, for
  example `Podcast-09-19-2026.<ext>`. (Extension: OQ-7. Date time zone: OQ-14.)
- **AC-9**: Given `[outputPath]` that is a file path, the file is written there and not in the default directory; missing
  parent directories are created. (Extension mismatch: OQ-7.)
- **AC-10**: Given `[outputPath]` that is an existing directory, the file is written inside it with the default file name.
- **AC-11**: An existing file at the target path is never overwritten or modified. The new file is written under the same
  name with a numeric increment in the style of Windows duplicate names. (Pattern, and whether it applies to an explicit
  `[outputPath]`: OQ-14.)
- **AC-12**: If the run fails or is cancelled after any output file was created, that partial file is deleted, and any
  pre-existing file is left untouched. No intermediate chunk data is left in the output directory.
- **AC-13**: With no argument, or more than two, the tool prints a usage message containing `<scriptPath> [outputPath]`
  to standard error, exits non-zero and makes no HTTP call.
- **AC-14**: If `<scriptPath>` does not exist, the tool prints an error containing the path, exits non-zero and makes no
  HTTP call.
- **AC-15**: If the extension is not `.txt` or `.md`, the tool prints an error naming the supported extensions, exits
  non-zero and makes no HTTP call. (Case: OQ-22.)
- **AC-16**: If the script is empty, or contains no narratable text after cues and markup are removed, the tool prints an
  error saying there is nothing to narrate, exits non-zero and makes no HTTP call. (PM addition: OQ-25.)

### API key and configuration

- **AC-17**: The key is read from the line `OPENROUTER_API_KEY=<value>` in the file at
  `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "PodcastGenerator",
  "PodcastGenerator.env")` (user). It works on Windows and Linux. (Environment variable, precedence, other sources:
  OQ-3. Parsing details: OQ-11.)
- **AC-18**: If no key is found, or the file cannot be read, the tool prints an error containing the expected file path
  and the name `OPENROUTER_API_KEY`, exits non-zero and makes no HTTP call. (depends on OQ-3 for the set of sources)
- **AC-19**: The key value never appears in standard output, standard error, log output, exception messages, or any
  written file, including on failure paths, and the tool never prints other contents of the config file. Verified with a
  sentinel key across the failure scenarios.
- **AC-20**: The product reads no key from any location other than those decided in OQ-3; in particular its code does not
  reference `.claude/credentials/`. No committed file (source, tests, docs, Postman collection, fixtures) contains a real
  key.

### HTTP failures and cancellation

- **AC-21**: If a narration request returns a non-success HTTP status, the tool exits non-zero and prints that narration
  failed, the chunk number and total when chunked, the HTTP status code and, if the response carries one, the API's error
  message. The speech endpoint returns JSON errors [S2].
- **AC-22**: If a request fails at the network level or times out, the tool exits non-zero with a message naming the
  failed chunk and no unhandled exception.
- **AC-23**: The `CancellationToken` from Ctrl+C reaches every HTTP call; after cancellation no further request is sent
  and no output file remains (AC-12). A unit test with an already-cancelled token shows this.

### Script handling

- **AC-24**: Markdown formatting marks (`#` heading markers, `---` lines, `*`, `**`, backticks and `- ` list markers) never
  appear in the text sent to the speech model. The words inside emphasis and code spans are kept as written. Sample:
  [S10] lines 1-5, 19, 27, 63-65.
- **AC-25**: A bracketed cue beginning `SFX:` or `THEME MUSIC:` is not narrated, and no sound effect or music is
  generated ([S10] lines 7, 15, 207).
- **AC-26**: Every other bracketed item in the sample (`[Low, ominous music]`, `[Pause]`, `[FADE OUT]` and the inline
  `[Whispered]`) is handled by the rule decided in OQ-1, applied identically to all 20 bracket-only lines and the one
  inline tag of the sample. (depends on OQ-1)
- **AC-27**: A delivery tag is sent to the speech model in square brackets immediately before the text it applies to
  (`**CECIL:** [Whispered] The sky ...` yields `[Whispered]` before `The sky ...`, user example; placement rule per [S4]).
  Two tags are never sent adjacent. (Adjacent-tag handling: OQ-1; whether the model honors the tag through OpenRouter is
  unverified until OQ-5.)
- **AC-28**: Speaker labels (`**CECIL:**`), headings, title lines, `---` and unlabelled paragraphs are handled as decided
  in OQ-2. (depends on OQ-2)
- **AC-29**: Beyond the decided rules, no word of the script is added, removed or reworded, and the same script produces
  the same request text on every run.
- **AC-30**: Whether removed direction is passed to the model or reported to the user follows OQ-1. (depends on OQ-1)
- **AC-31**: A script of 3,000 lines (the top of the user's stated "0-3000 lines" range) is processed to completion,
  with stubbed responses, without any line-count limit imposed by the tool. (The lower end, 0 lines, is AC-16.)

### Narration request and voice

- **AC-32**: The narration request is `POST /api/v1/audio/speech` on OpenRouter with `model` =
  `google/gemini-3.1-flash-tts-preview`, using only body fields documented in [S2] or confirmed by the OQ-5 calls
  (documented, per the caller: `model`, `input`, `voice`, `response_format` (`mp3` or `pcm`, default `pcm`), `speed`,
  `input_references`, `provider`). The response is a raw audio byte stream (`audio/mpeg` or `audio/pcm`), errors are JSON
  [S2]. The coding agent reads [S2] first and stops and reports if it differs.
- **AC-33**: The `response_format` and the properties of the returned audio (sample rate, bit depth, channels) used by the
  tool are those confirmed in OQ-5 and decided in OQ-7. (depends on OQ-5, OQ-7)
- **AC-34**: The `voice` value is the one decided in OQ-8, and it is the same on every request of a run. (depends on OQ-8)
- **AC-35**: No request uses `input_references` or any field that clones or imitates a real person's voice (user decision).

### Style

- **AC-36**: The style prompt is loaded at run time from an editable file, not hardcoded. Editing the file changes the
  request text of the next run without rebuilding. (File location and which part is sent: OQ-9.)
- **AC-37**: Each narration request has the structure of `docs/style-guide.md` section 9.1: audio profile, scene,
  director's notes, then the transcript, with the direction kept visibly separate from the transcript so it is not read
  aloud [S11]. Whether OpenRouter passes this structure to the Gemini model through the `input` field is unverified
  until OQ-5.
- **AC-38**: The profile, scene and director's notes are identical on every request of a run.
- **AC-39**: The style block names no real person (including the show's narrator or actor) and contains no text from the
  show; it describes tone only (user decision; [S11] hard limits).
- **AC-40**: The coding agent revises `docs/style-guide.md` for TTS-only: the Lyria music prompt (section 9.2), section 7
  and every other music, Lyria and mixing reference are removed or marked deferred, and references to `OQ-n` numbers of
  earlier spec revisions are updated. The PM did not edit it. Its content decisions stay the user's (OQ-13).

### Long scripts

- **AC-41**: A script whose text exceeds the chunk size decided in OQ-6 is split into several narration requests at the
  boundaries decided in OQ-6, never inside a delivery tag and its text. (depends on OQ-6)
- **AC-42**: Every chunk of a run is sent with the same style block (AC-38), voice and `response_format`.
- **AC-43**: The audio of the chunks is joined in script order into one file. With stubbed responses, a test shows no chunk
  is missing, duplicated or reordered. (Joining method and format: OQ-7.)
- **AC-44**: If any chunk fails after retries decided in OQ-10, the run fails as in AC-21 and the output file is deleted
  (AC-12). (depends on OQ-10)
- **AC-45**: Progress reporting on standard error is as decided in OQ-10. (depends on OQ-10)

### Format

- **AC-46**: The final audio format and file extension are those decided in OQ-7, and the file is valid and playable in
  that format: for example for a WAV file, the header sample rate, bit depth, channel count and data length match the
  audio data. (depends on OQ-7)

### Packaging, documentation

- **AC-47**: `dotnet publish src/PodcastGenerator.Cli -c Release -r <rid> --self-contained -p:PublishSingleFile=true`
  succeeds for `win-x64` and `linux-x64`. Each output is a single executable that starts on a machine without the .NET
  runtime (with no arguments it prints the AC-13 usage message). No `osx-*` or arm64 output.
- **AC-48**: `README.md` exists at the repo root and covers dev environment setup, build and test, and how to run and use
  the app: arguments, default output directory and file naming, the API key file and its path on Windows and Linux, the
  script syntax decided in OQ-1 and OQ-2, and where the style file lives. It also states the exact name of the PR check to
  mark required, how versions and releases are produced, and which repository settings the workflows need (OQ-20).
- **AC-49**: After scaffolding, `CLAUDE.md` is updated as the user approves in OQ-26.

### Pull request workflow

- **AC-50**: A workflow under `.github/workflows/` starts on every pull request targeting `main` and is never skipped by a
  workflow-level path or branch filter ([S6]).
- **AC-51**: When every file changed by the PR is in the ignore list (`docs/`, `.docs/`, `.github/`, `.claude/`,
  `agent-memory/`, `.gitignore`, any `.md`), the required check finishes with success and no build or test step runs.
  (Anchoring: OQ-17.)
- **AC-52**: When at least one changed file is outside the ignore list, the workflow restores, builds
  (`dotnet build -warnaserror`) and runs `dotnet test` with the .NET 10 SDK, and the check fails if the build or any test
  fails, even when the PR also changes ignored files. (Operating systems: OQ-18.)
- **AC-53**: The check name is stable and stated in `README.md` so the user can select it as a required status check.
- **AC-54**: The PR workflow uses no repository secret and never calls the real OpenRouter API.

### Release workflow

- **AC-55**: A workflow under `.github/workflows/` runs on pushes to `main` only, not on pull requests or other branches.
- **AC-56**: No tag and no release are created when every file changed by the push is in the ignore list (AC-51).
- **AC-57**: The release type is read from the squash commit created by the merge (the PR title is its subject, per
  `CLAUDE.md`). `fix` gives a PATCH bump, `feat` a MINOR bump, a breaking change (`!` after the type, or a
  `BREAKING CHANGE:` footer) a MAJOR bump. A commit with none of these creates no tag and no release. (Footer in the
  body, scopes, title validation: OQ-16.)
- **AC-58**: The bump is applied to the latest `v*` tag. With no `v*` tag the first release is `1.0.0`, tag `v1.0.0`.
  (Definition of "latest", the no-tag rule: OQ-15.)
- **AC-59**: Every version is valid Semantic Versioning 2.0.0 `MAJOR.MINOR.PATCH` [S8] with no build number. The GitHub
  Release version, the tag without its `v`, and the version passed to the build and embedded in the executables are the
  same string. Each new version is strictly greater than the previous release.
- **AC-60**: The release workflow makes no commit and no push to any branch; it only creates a tag and a release. No repo
  file stores the release version.
- **AC-61**: The workflow builds, then publishes `win-x64` and `linux-x64` executables (AC-47). Only if all succeed does
  it create the tag and release; on any failure no tag and no release exist for that run.
- **AC-62**: The tag `v<version>` points at the exact commit built. The workflow never moves, deletes or overwrites an
  existing tag or release; if the computed tag exists the run fails naming it. Two merges in quick succession never
  produce the same tag.
- **AC-63**: The GitHub Release has exactly two executable assets, `win-x64` and `linux-x64`, named so OS and architecture
  are identifiable (format and naming: OQ-19). No macOS asset.
- **AC-64**: The release workflow uses no OpenRouter API key and never calls the real API.

## Constraints

From `CLAUDE.md` [S1]:

- .NET 10, C#; xUnit; `win-x64` and `linux-x64` shipped as a standalone executable; no macOS, arm64 or `osx-*` unless
  asked.
- Clean Architecture (Domain, Application, Infrastructure, Cli); dependency injection; all OpenRouter/HTTP code in
  Infrastructure behind an Application interface; async methods end in `Async` and take a `CancellationToken`.
- Never hardcode or log the API key. Unit tests mock HTTP and never call the real API.
- Windows and Linux: `Path.Combine`, no hardcoded separators, Linux file names are case-sensitive, no Windows-only APIs or
  shell commands.
- `dotnet build -warnaserror` and `dotnet test`.
- Semantic Versioning 2.0.0; tags `vMAJOR.MINOR.PATCH` on `main` with a GitHub Release carrying the Windows and Linux
  executables; first release `1.0.0`; no build number; squash merges, the squash subject decides the bump; CI computes the
  version, does not store it in a repo file and does not commit to `main`.
- GitHub Actions; the PR workflow always starts and skips inside the job when only ignored files changed; ignore list as
  above.
- README covers dev environment setup, build and test, and run and use, and is updated when CLI arguments or setup steps
  change. Pipeline artifacts under `docs/` are committed. "Never fabricate, assume, or make-up anything."

From the request and the user's answers:

- Repository: `https://github.com/abaskett3/PodcastGenerator`, `origin` `git@github.com:abaskett3/PodcastGenerator.git`,
  branch `main` (verified from `.git/config` [S9]).
- Narration model `google/gemini-3.1-flash-tts-preview` through `POST /api/v1/audio/speech`; TTS only; no music, no SFX
  rendering, no mixing in 1.0.0. Cues are direction only.
- The result is a spoken-word podcast in the style of "Welcome to Night Vale", a description of tone only; no voice
  cloning of any real person.
- The user adopts option A for style: a researched style guide stored as an editable file.
- API key in `<UserProfile>/.config/PodcastGenerator/PodcastGenerator.env` as `OPENROUTER_API_KEY=XXX` for the executable.
- Script length "0-3000 lines"; Markdown treated "like a regular Md file".
- Output naming, collision, missing-directory and partial-file rules as above. On PR the tests run and the user makes them
  required (the user changes that setting, not the agents).
- Windows and Linux only.

## Out of scope

- Deferred, not dropped forever (user: "for now"): Lyria and any music generation, background music, mixing music under
  narration, rendering SFX as sound. Cues stay in scripts as direction only. Reason for deferring recorded in [S12].
- Configuring GitHub repository settings (branch protection, required checks, Actions permissions). The user does this.
- Voice cloning or imitation of any real person (including the show's narrator); reproducing text or music of the show.
- Writing or rewriting the script's words. The tool narrates the script as given (style guide section 6 is guidance for
  script authors only).
- Text-generation (LLM) steps: the user chose option A, so there is no extra text-model call.
- macOS, arm64, other runtime identifiers; installers, Docker, NuGet packaging, code signing.
- A build number; storing the version in a repo file; the workflow committing to `main` (to be revisited).
- Changelog generation, release-notes design beyond OQ-19, automatic updates.
- The `.claude/` agent pipeline files and the GitHub PR and issue templates (unchanged). Running the real OpenRouter API in
  `dotnet test` or in CI.
- Anything not chosen in an open question below (for example more than one speaker, OQ-8, or extra CLI options, OQ-24).

## Open questions

Each item has a recommendation and its evidence. A recommendation is advice; it is not written into an acceptance
criterion until the user chooses.

### Resolved (user answers relayed by the caller)

| Topic | Resolution | Where |
|---|---|---|
| Scope | TTS only. Lyria, music, SFX rendering and mixing dropped for 1.0.0 (deferred). Cues are direction only. Old OQ-3, 5 (mixing), 6, 8, 15 (music part), 16, 27 are closed. | Out of scope |
| Repo | Repository created by the user; `origin`, `main`, one commit `init commit` containing only `.gitignore` (caller verified; `.git/config` re-read by me [S9]). `.claude/`, `.github/`, `CLAUDE.md`, `docs/` are still untracked; the caller raises that with the user. | Constraints |
| Style | Option A: researched style guide as an editable file; `docs/style-guide.md` (DRAFT 1) is the user's adopted starting point. | AC-36 to AC-40 |
| Markdown | "Like a regular Md file". | AC-24 |
| Versioning, ignore list, output naming, required-check design | As in revision 2 (unchanged). | AC-50 to AC-64, AC-8 to AC-12 |
| Config | Key file path as given. | AC-17 |
| Script length | "0-3000 lines". | OQ-6, AC-31 |

### BLOCKING

**OQ-1 (BLOCKING): How does the tool tell a delivery tag from a cue, and what does "direction only" mean?**
The user's three examples: `[THEME MUSIC: ...]`, `**CECIL:** [Whispered]`, `[SFX: ...]`. The sample [S10] has 21 lines with brackets:
14 `[SFX: ...]`, 2 `[THEME MUSIC: ...]` (all on their own line), 3 unprefixed on their own line (`[Low, ominous music]` line 23,
`[Pause]` lines 89 and 173), `[FADE OUT]` (line 211), and one inline tag `[Whispered]` (line 209, after the speaker
label). A regular Markdown parser leaves `[text]` without a following `(url)` as literal text (recalled, unverified), so
Markdown alone does not separate them. Google's tags (relayed [S4]): square brackets, before the text, never adjacent.
Questions: (a) the classification rule; (b) does "direction only" mean the cue is removed from the spoken text only, or
also passed to the speech model as direction (for example in the director's notes); (c) are removed cues listed on
standard error, and how; (d) what happens to `[Pause]` and `[FADE OUT]`; (e) if two delivery tags end up adjacent, pass
as written, merge into one bracket with a comma (the style guide's suggestion), or warn?
Options for (a): (A) position: a bracket on a line of its own is direction and is dropped, a bracket inside a spoken line
(such as after the label) is a delivery tag; (B) prefix allowlist: `SFX:` and `THEME MUSIC:` are dropped, every other
bracket is sent as a tag, so `[Low, ominous music]`, `[Pause]` and `[FADE OUT]` would reach the model; (C) a new marker
syntax you define for delivery tags.
Recommendation: (A); for (b) remove only, do not pass to the model; (c) one summary line with the count on standard error,
not the full list; (d) dropped like other bracket-only lines; (e) warn and pass as written. Evidence: (A) classifies all
20 bracket-only lines and the one inline tag of the sample correctly with no list of prefixes to maintain, while (B)
would send `[Pause]`, `[FADE OUT]` and `[Low, ominous music]` to a model that treats brackets as instructions [S4], where
the effect is unknown; passing music or SFX descriptions to a speech model that cannot render them risks them being spoken
or vocalized (unverified until OQ-5). The count: 20 dropped lines per 215 sample lines is about 280 lines of output for a
3000-line script if the full list were printed (my arithmetic). Risk of (A): a delivery tag written alone on its own line
would be dropped; tell me if you write tags that way.

**OQ-2 (BLOCKING): What happens to speaker labels, headings, title lines, separators and unlabelled text?**
In the sample [S10]: lines 1-3 are `#`, `##`, `###` title lines ("Welcome to Vast Night Vale", a subtitle, "RC 09-17-26
Feature Release Broadcast"); `---` separators between segments; 11 paragraphs start with `**CECIL:**` followed by text
on the same line, and the paragraphs after each are unlabelled; line 215 is `*End of broadcast*`. Questions: (a) is
`**CECIL:**` spoken, stripped, or used to choose a voice; (b) are the title lines narrated; (c) is `*End of broadcast*` narrated;
(d) are `---` lines dropped or turned into a pause; (e) how is a paragraph with no label handled, both after a labelled one
(as in the sample) and in a script with no labels at all; (f) bullets ([S10] lines 63-65): each item read as its own
sentence?
Recommendation: (a) stripped, with the single configured voice used for everything; (b) not narrated (headings are
document structure); (c) narrated unless you say otherwise, because it is an ordinary paragraph under "regular Md"; (d)
dropped; (e) unlabelled text is narrated with the same voice as the paragraph before it, and a script with no labels is
narrated in the single voice; (f) yes, each item a separate sentence, read evenly (the style guide's rule: lists are read
evenly [S11]). Evidence: multi-speaker support through OpenRouter is not documented [S2]; the style guide says prompts
never name the character [S11], and sending `CECIL:` would name it; all 11 labels in the sample are one speaker. Tell me
if you want (b) or (c) different: I could not infer whether a title is meant to be spoken.

**OQ-3 (BLOCKING): Is the `OPENROUTER_API_KEY` environment variable still supported, and what if no key is found?**
`CLAUDE.md` currently says the key comes from the environment variable [S1]; the user said the executable should expect
the config file. Questions: (a) is the environment variable still read; (b) if both exist, which wins; (c) are .NET
user-secrets supported (`CLAUDE.md` asks to confirm); (d) confirm that a missing file, an unreadable file, or a file
without the key each end in the AC-18 error.
Recommendation: (a) yes; (b) the environment variable wins over the file; (c) no; (d) yes. Evidence: a process-level
override is a common convention (recalled, unverified) and lets tests set a fake key without touching the profile; the
risk is a stale variable silently overriding the file, which the README would state. If you prefer the file only, say so
and AC-17 stands as written.

**OQ-4 (BLOCKING): What may an agent read when it needs the API key?**
You keep a copy of the key in a global `.claude/credentials/` location, outside this repo. `.claude/settings.json` has no
permission rules about it (read [S9]), so nothing technical restricts an agent from reading it; only your approval does.
Options: (a) no agent reads any key file; you run the verification calls yourself (OQ-5); (b) you put the key in the
product's own config file, and the coding agent may run the app or calls that read it internally, but never opens, prints
or copies the file or `.claude/credentials/`; (c) the coding agent may read the credentials location directly.
Recommendation: (b), plus tests never read either location (AC-5). Evidence: `CLAUDE.md` says never hardcode or log the key
[S1]; reading a key file into an agent's context can place it in transcripts (my reasoning, no source). The user's
"credentials" answer is not treated as approval for OQ-5.

**OQ-5 (BLOCKING): Do you approve real API calls to verify the OpenRouter contract, and with what cap?**
Not verified by anyone, and needed before the coding is final: how the Gemini model takes style prompts, director's notes and
inline tags through `/audio/speech`; whether `[Whispered]` is honored; the `voice` values; the sample rate, bit depth and
channel count of `pcm`; whether an input limit exists; latency and real cost per call [S2, S3]. `dotnet test` must never call
the real API [S1]. Proposed cap (my proposal, change it freely): one short narration call with the style template and
`response_format` `pcm`; one short call with `mp3`; one call at the largest chunk size under consideration (OQ-6); up to
three short calls to try candidate voices (OQ-8); at most six calls in total; results saved with the key removed and, if you
approve, recorded in a Postman collection under `/postman` (`CLAUDE.md` plans one [S1]). Cost depends on the audio output
tokens produced (the price is $20 per million output tokens and $1 per million input tokens for this model [S3]; tokens per
second of audio is not verified) and on any OpenRouter fee; the first call will show the real figure, and I cannot state a
number without it. Options: (a) yes with this cap and a maximum spend you name; (b) you run the calls and hand over the
responses; (c) implement from documentation only.
Recommendation: (a). Evidence: (c) contradicts "never assume" [S1]; the calls are few and short. I need an explicit yes,
the cap, and the spend ceiling from you.

**OQ-6 (BLOCKING): How are long scripts chunked?**
Your range is 0 to 3000 lines. OpenRouter documents no maximum input length [S2]. `docs/style-guide.md` (section 9.1 notes),
relaying Google's speech-generation docs, says quality and consistency can drift on outputs longer than a few minutes and
recommends smaller chunks [S11, unverified for this model]. Scale, my estimate and not measured: the sample has 110
non-blank lines; I estimate about 2,000 words, which at a typical 150 words per minute is on the order of 13 minutes of
speech, so even the sample may exceed "a few minutes"; a 3000-line script at the same density is about 14 times the sample,
on the order of 3 hours and, with a chunk of about 3 minutes, on the order of 60 requests. Questions: (a) the chunk size
unit and value (characters, tokens or seconds), which needs the OQ-5 measurement; (b) the split boundary; (c) what happens
to a single paragraph larger than one chunk; (d) may a chunk end between a delivery tag and its text.
Options for (b): paragraph packing up to the chunk size; split at `---` segments ([S10] has 11); split at speaker-label
boundaries (one speaker in the sample, so this would rarely split).
Recommendation: paragraph packing up to a chunk size set from the measured limit and a listening test; an oversize
paragraph is split at a sentence end and a warning is printed; never split between a tag and its text. Evidence: paragraphs
are the sample's natural unit (the largest is about 100 words, line 21, my reading), `---` segments are uneven and the
guidance is about output length, not about segments.

**OQ-7 (BLOCKING): What is the final audio format, and how are chunks joined?**
The speech endpoint returns `mp3` or `pcm` (default `pcm`) [S2]. `pcm` is raw samples with no header, so a playable file
needs a header with a sample rate, bit depth and channel count that the OpenRouter page does not give in the summary I
have (OQ-5). Whether byte-concatenated MP3 chunks play without gaps or clicks is unverified. Options: (a) request `pcm` for
every chunk, join the raw samples, write a WAV header, output `.wav`; (b) request `mp3` per chunk and concatenate, output
`.mp3`, only if listening tests confirm it is valid; (c) decode and re-encode with an audio library (adds a dependency and
raises the standalone-executable question). Size illustration, unverified inputs: 44.1 kHz, 16-bit, 2 channels is 176,400
bytes per second, about 10.6 MB per minute, about 1.9 GB for 3 hours (my arithmetic); a 24 kHz mono 16-bit stream, which
I recall (unverified) is what Gemini TTS produces, is about 2.9 MB per minute, about 520 MB for 3 hours. I recall (unverified)
that a WAV file's 32-bit size field caps it at about 4 GiB.
Also: if `[outputPath]` has an extension that does not match (for example `episode.mp3` when the format is WAV), error,
replace the extension, or keep it?
Recommendation: (a) `.wav`, as the only format in 1.0.0; on an extension mismatch, exit with an error naming the required
extension. Evidence: lossless joining and no decoder or encoder, so no external tool and no conflict with the
standalone-executable rule [S1]; MP3 output can be a later feature. Cost: large files, which you must accept.

**OQ-8 (BLOCKING): Which voice, and one speaker or two?**
The `voice` field is provider-dependent [S2]. Google's guidance (relayed [S4]) mentions up to 2 speakers and 30 named voice
presets; multi-speaker support through OpenRouter is not documented [S2]. The sample has one speaker [S10]. The style guide
asks for a calm, warm, mid-to-low preset picked by listening and never by resemblance to a real person [S11].
Recommendation: one narrator in 1.0.0, a voice you pick after listening to candidates during the OQ-5 calls, and the voice
name held as a configurable default. Evidence: one speaker in the sample; multi-speaker unverified.

**OQ-9 (BLOCKING): Where does the style file live at run time, and which part of it is sent?**
The executable is standalone [S1], so a file next to it cannot be assumed to exist. `docs/style-guide.md` mixes reference
text (sections 1 to 8) with the prompt template (section 9.1); only the template is meant to be sent [S11]. Also: `.md`
files and `docs/` are on the ignore list, so an edit to a style file that is `.md` or under `docs/` builds and releases
nothing, and an embedded copy would not update until some other change releases.
Options for location: (a) a default embedded in the executable plus an override file in
`<UserProfile>/.config/PodcastGenerator/` (the folder you chose for the key); (b) next to the executable; (c) in
`<UserProfile>/PodcastGenerator/` (the output folder). Options for the sent part: one file that the tool parses, or a
small separate runtime template file with a placeholder for the script text.
Recommendation: (a), with a separate small runtime template (the guide stays documentation), and the embedded default kept
in a file that is neither `.md` nor under `docs/`, so a change to it triggers a release; you confirm the override file name.
Evidence: standalone rule [S1]; `.config/PodcastGenerator/` is already your config location; ignore list [S1]. Please confirm you
accept, or reject, the release-trigger consequence.

### NON-BLOCKING

**OQ-10 (NON-BLOCKING): Failures, retries, order, progress and cost during a long run.** A 3000-line script may need dozens
of paid requests. (a) If one chunk fails, fail the whole run, or retry that chunk first; (b) sequential or parallel
requests; (c) progress output; (d) a cost warning; (e) must a multi-hour episode fit in memory, or may chunk audio be
written to temporary files (deleted afterwards) or appended to the output file as it goes.
Recommendation: (a) a small fixed number of retries per chunk on transient errors (value chosen by the coding agent from
OpenRouter's error and rate-limit documentation, not read by me), then fail the whole run and delete the output; (b)
sequential; (c) one line per chunk on standard error ("chunk k of n") and a starting line with the chunk count; (d) no
prompt; (e) temporary files or streaming, never the whole episode in memory. Evidence: `CLAUDE.md` gives no fallback [S1] and
your rule deletes partial output; sequential order keeps order and avoids unknown rate limits (unverified); memory reasoning
is from the size arithmetic in OQ-7.

**OQ-11 (NON-BLOCKING): Config file parsing.** You specified a single `KEY=value` line. Blank lines, `#` comments, quotes,
spaces around `=`, a UTF-8 BOM, other keys, a repeated key, and Windows or Linux line endings are not covered.
Recommendation: tolerate a BOM and either line ending; ignore blank lines and lines beginning `#`; trim spaces around the
key and the value; remove one pair of matching surrounding quotes; ignore other keys; if `OPENROUTER_API_KEY` appears twice,
the last wins; an empty value counts as missing. Also add `*.env` to `.gitignore`. Evidence: dotenv-style conventions
(recalled, unverified); `.gitignore` ignores `.env` and `.env.*` but not `PodcastGenerator.env` (lines 6-7 and 487-489,
read), so a copy of your file placed in the repo would not be ignored.

**OQ-12 (NON-BLOCKING): Markdown constructs not in the sample, and `.txt` scripts.** The sample has headings, `---`,
emphasis, code spans, bullets, bracket lines and labels. It has no links, numbered or nested lists, block quotes, fenced
code, tables, images or HTML comments. Are `.txt` scripts subject to the same label and bracket rules but no Markdown
stripping?
Recommendation: `.txt` gets the bracket and label rules without Markdown stripping; for a construct outside the sample in
a `.md` file, remove its markup, narrate its text and print one warning naming the construct; links narrate the link text
only. Evidence: "like a regular Md file" (user); I cannot decide constructs you have not shown.

**OQ-13 (NON-BLOCKING): Review of `docs/style-guide.md` DRAFT 1.** Its open items: (1) the sample script uses the show's
character name as the label, while the guide's prompts say "the host"; (2) the accent defaults to neutral American
English; (3) script rewriting is not a feature. Also, the guide's claims marked "verify" are unverified until OQ-5.
Recommendation: you review it before merge; the coding agent only revises it for TTS-only (AC-40). Evidence: the guide states
these are your call [S11]; the author of the guide is not known to me.

**OQ-14 (NON-BLOCKING): Output naming details.** (a) The numbering pattern when the file exists; (b) does numbering also
apply to an explicit `[outputPath]` file; (c) is the date local or UTC.
Recommendation: (a) `Podcast-09-19-2026 (2).<ext>`, counting from 2; (b) yes, never overwrite; (c) local date. Evidence: (a)
is my recollection, unverified, of Windows Explorer naming; (c) it is your file for your day.

**OQ-15 (NON-BLOCKING): First release and tag selection.** (a) With no `v*` tag, is `1.0.0` published on the first merge
whose squash subject has `fix:`, `feat:` or a breaking marker, with no bump applied? (b) "Latest `v*` tag": highest by
SemVer precedence or most recently created? (c) A `v*` tag that is not `vMAJOR.MINOR.PATCH`: ignore or fail?
Recommendation: (a) yes; (b) highest by SemVer precedence; (c) fail naming the tag. Evidence: AC-59 needs strictly greater
versions [S8, not read]; your "first version posted should be 1.0.0".

**OQ-16 (NON-BLOCKING): Reading the squash message.** `CLAUDE.md` says the subject decides but also lists a
`BREAKING CHANGE:` footer, which is in the body [S1]. (a) Read the subject and the body? (b) Accept scopes like
`feat(cli):`? (c) Validate the PR title in the PR workflow, since a wrong title silently means no release? (d) The default
squash body depends on a repository setting I could not see.
Recommendation: (a) both; (b) yes; (c) yes, if you accept an extra check; (d) you tell the coding agent the setting.
Evidence: Conventional Commits 1.0.0 defines `!`, the footer and scopes [S7, not read; the coding agent reads it].

**OQ-17 (NON-BLOCKING): Ignore-list anchoring.** Do the directory entries and `.gitignore` match only at the repo root, or
at any depth?
Recommendation: root only for directories and `.gitignore`; any depth for `.md`. Evidence: your entries were written with a
leading slash (`/.docs/`, `/.github/`, `/.claude/`, `/agent-memory/`) and "any .md file" separately.

**OQ-18 (NON-BLOCKING): CI scope.** (a) PR tests on Linux, Windows or both (a matrix gives several check names, all to be
marked required)? (b) Re-run `dotnet test` in the release workflow? (c) The coding agent verifies the .NET 10 runner setup
from the `actions/setup-dotnet` docs.
Recommendation: (a) both; (b) yes. Evidence: `CLAUDE.md` requires Windows and Linux support [S1]; a squash commit is a new
commit the PR check did not test exactly (my reasoning, no source); your request said "run the build then publish".

**OQ-19 (NON-BLOCKING): Release assets and page.** (a) Raw executable or `.zip` and `.tar.gz`; (b) asset names; (c) release
title and notes; (d) marked latest, never draft or pre-release.
Recommendation: `.zip` for Windows and `.tar.gz` for Linux, names `PodcastGenerator-<version>-<rid>.<ext>`, generated
notes, marked latest. Evidence: I recall (unverified) that a raw Linux file loses its executable bit after download while a
`.tar.gz` keeps it; the coding agent verifies.

**OQ-20 (NON-BLOCKING): Repository settings the workflows need.** I recall, unverified, that creating tags and releases
with `GITHUB_TOKEN` needs `contents: write`, that default workflow permissions may be read-only, and that a required check
can be selected only after it has reported once. The coding agent verifies with GitHub Docs and lists exact steps in the
README. Recommendation: list them in the README and the final report. Evidence: none read by me.

**OQ-21 (NON-BLOCKING): Where is `agent-memory/`?** It does not exist in the repo [S9]. Recommendation: keep the root
`agent-memory/` entry as written. Evidence: your request and `CLAUDE.md` list it at the root [S1].

**OQ-22 (NON-BLOCKING): Extension case.** Are `.TXT` and `.Md` accepted? Recommendation: yes, case-insensitive. Evidence:
Linux file names are case-sensitive [S1], but the extension check is a separate rule and upper-case extensions are common
on Windows (my reasoning).

**OQ-23 (NON-BLOCKING): Message wording and exit codes.** Distinct codes per error or any non-zero code? Recommendation: any
non-zero code, wording left to the coding agent. Evidence: none is specified anywhere.

**OQ-24 (NON-BLOCKING): Extra CLI options.** `--help` and `--version`, or options for the voice, style file or chunk size?
Recommendation: `--help` and `--version` only. Evidence: the request requires none; every option changes the README and
counts as a CLI change for versioning [S1].

**OQ-25 (NON-BLOCKING): Empty-script rule.** AC-16 is my addition; your range starts at 0 lines, and a script with 0 lines
has nothing to narrate. Recommendation: keep AC-16 (error, no API call), also for a script that has only cues.
Evidence: any call would cost money and return nothing useful.

**OQ-26 (NON-BLOCKING): Approve edits to `CLAUDE.md`.** You approved the versioning edits (made). These statements are now
wrong or incomplete, and agents may not edit them without approval: (1) lines 5 to 6, "by calling Google Lyria through the
OpenRouter API" (now: Gemini TTS narration through OpenRouter's speech endpoint, Night Vale style); (2) "Usage": add the
default file name and numbering, directory output, script syntax and the style file; (3) "Gotchas" line 79 and the
"Never hardcode or log" line: the key now comes from `PodcastGenerator.env` (and the environment variable per OQ-3), and
the user-secrets note is resolved; (4) "Gotchas" lines 80 to 81, "Model ID: google/lyria-3-pro-preview. Unverified ...":
now `google/gemini-3.1-flash-tts-preview` with the OQ-5 verification result; (5) "Stack" line 40, "Postman collection ... for
testing OpenRouter/Lyria calls" (now the speech endpoint); (6) "Current state" and the "confirm after scaffolding" notes;
(7) "Documentation": README must also cover the key file and add `docs/style-guide.md` and `docs/sample-scripts/` to the list
of docs; (8) a rule for what agents may read (OQ-4), if you want one recorded.
Recommendation: approve all, applied by the coding agent in the same PR (AC-49). Evidence: each statement contradicts the
decisions in this spec.

### Sources

- [S1] `CLAUDE.md` (read directly, current revision: `main`, squash merges, CI-computed versions).
- [S2] OpenRouter TTS guide, `https://openrouter.ai/docs/guides/overview/multimodal/tts`. Relayed, not read by the PM:
  `POST /api/v1/audio/speech`; fields `model`, `input`, `voice` (provider-dependent), `response_format` (`mp3` or `pcm`,
  default `pcm`), `speed`, `input_references`, `provider`; raw audio byte stream; JSON errors; no maximum input length
  documented; multi-speaker not documented.
- [S3] OpenRouter text-to-speech models collection, `https://openrouter.ai/collections/text-to-speech-models`. Relayed, not
  read by the PM: Gemini model has "200+ inline audio tags", two speakers, 70+ languages, $1 per million input tokens and
  $20 per million output tokens.
- [S4] Google Gemini TTS guides, `https://aistudio.google.com/learn/gemini-tts-prompt-guide-with-tags` and
  `https://ai.google.dev/gemini-api/docs/models/gemini-3-1-flash-tts-preview`. Relayed as search summaries, not read by the
  PM: square-bracket tags such as `[whispers]` placed before the text, never two adjacent, style prompts and director's
  notes, up to 2 speakers, 30 voice presets.
- [S5] Wikipedia, Welcome to Night Vale, `https://en.wikipedia.org/wiki/Welcome_to_Night_Vale`. Relayed as a search summary,
  not read by the PM: community radio broadcast from a fictional desert town, deadpan, absurdist.
- [S6] GitHub Docs, Troubleshooting required status checks,
  `https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks`.
  Relayed as a search summary, not read by the PM. The coding agent reads it directly.
- [S7] Conventional Commits 1.0.0, `https://www.conventionalcommits.org/en/v1.0.0/`. Named for the coding agent; not read.
- [S8] Semantic Versioning 2.0.0, `https://semver.org/`. Named for the coding agent; not read.
- [S9] Repo files read directly by the PM in this revision: `.git/config` (remote `origin` =
  `git@github.com:abaskett3/PodcastGenerator.git`, branch `main`), `.claude/settings.json` (only `enabledPlugins`),
  `.gitignore`, the listing of `docs/`.
- [S10] `docs/sample-scripts/QE-3395-RC-09-17-26-nightvale-podcast-script.md` (215 lines, read directly by the PM; counts
  from regular-expression searches: 20 bracket-only lines, 1 inline bracket, 11 speaker-labelled paragraphs, 110 non-blank
  lines; word count is my estimate).
- [S11] `docs/style-guide.md`, DRAFT 1 (read directly by the PM; author unknown to me). Its own external claims (Wikipedia,
  TV Tropes, Google guides) were read by its author through a summarizing tool and are not verified by me.
- [S12] Reason for deferring music and SFX, relayed, not read by the PM: Google's Lyria 3 docs say "Both models generate
  music exclusively" (`https://ai.google.dev/gemini-api/docs/interactions/music-generation`); OpenRouter describes Lyria 3 as
  music-generation models (`https://openrouter.ai/google/lyria-3-pro-preview/api`); no dedicated SFX model was found on
  OpenRouter's audio-models collection (`https://openrouter.ai/collections/audio-models`, summary not definitive).
