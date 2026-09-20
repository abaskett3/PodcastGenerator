# Initial development: first working CLI (TTS narration) and CI/CD (GitHub Actions)

Status: DRAFT, revision 4. This revision writes the user's settled decisions (relayed by the caller and recorded in
`CLAUDE.md` [S1]) as testable acceptance criteria, reads the script format from `docs/script-writing-guide.md` [S12] and
the updated sample [S10], and moves everything not settled into "Proposed defaults awaiting confirmation". There are no
BLOCKING questions. An acceptance criterion that rests on a proposed default ends with `[D-n]`.

Evidence rules: I (the product-manager agent) have only Read, Glob, Grep and Write and could not open any web page.
External facts are cited as `[S<n>]` to "Sources", each marked read directly or "relayed, not read by the PM". Facts
nobody has verified are listed under "Unverified facts the coding agent verifies" and are not written as fact elsewhere.

## Summary

PodcastGenerator does not exist as code yet (no solution, projects, tests or `README.md`; `CLAUDE.md` "Current state"
[S1]). This feature delivers the first working version and its delivery pipeline. The product turns a `.txt` podcast
script, formatted as `docs/script-writing-guide.md` describes, into one MP3 file: the script's spoken words are
narrated by a single voice (`Umbriel`) with the model `google/gemini-3.1-flash-tts-preview` through OpenRouter's
`POST /api/v1/audio/speech` [S2], in a deadpan "Welcome to Night Vale" style taken from an editable style file. Speaker
names, headings, header lines and SFX/music/transition cues are not spoken; delivery directions, pauses and `*emphasis*`
become inline tags. Scripts of up to about 3000 lines are split into chunks, narrated one after another with the same
style, and joined into one two-channel MP3 with the mono voice centered. Music, SFX, mixing and Lyria are out of scope
for now. The API key and all runtime resources live in `<UserProfile>/.config/PodcastGenerator/`. The feature also
delivers the Clean Architecture solution and two GitHub Actions workflows: a pull-request workflow that always starts and
reports checks the user can require, and a release workflow that, on a squash-merge to `main`, computes a Semantic
Versioning version from the conventional-commit message, tags `v<version>` and publishes a GitHub Release with the Windows
and Linux executables. The first release is `1.0.0`.

## User-facing behavior

### Command line

```text
PodcastGenerator <scriptPath> [outputPath]
PodcastGenerator --help
PodcastGenerator --version
dotnet run --project src/PodcastGenerator.Cli -- <scriptPath> [outputPath]
```

- `<scriptPath>`: an existing `.txt` file (extension matched case-insensitively), up to about 3000 lines, given whole.
  `.md` and any other extension are rejected [S1].
- `[outputPath]`: an MP3 file path, or an existing directory. Any other extension is an error naming the required one.
- Default output: `PodcastGenerator/` under `Environment.SpecialFolder.UserProfile`, file `Podcast-MM-DD-YYYY.mp3` (local
  date). If the name exists the file is saved as `Podcast-MM-DD-YYYY-2.mp3`, then `-3`, and so on, also for an explicit
  output path. A directory given as output path means "write inside it". Missing parent directories are created. A
  partial output file is deleted when the run fails [S1].
- Key and resources: everything runtime lives in `<UserProfile>/.config/PodcastGenerator/`, built with `Path.Combine`.
  The key is the line `OPENROUTER_API_KEY=<key>` in `PodcastGenerator.env` there. The `OPENROUTER_API_KEY` environment
  variable is also supported; if both exist the file wins. .NET user-secrets are not supported. Resources such as the style
  file are created there on the first run from defaults embedded in the executable and loaded from there on every run [S1].
- Progress: `chunk k of n` on standard error. No cost prompt.
- Exit code 0 on success, 1 on any failure, with a message that says what went wrong.

### What the tool does with the script

The script follows `docs/script-writing-guide.md` [S12]; the sample is `docs/sample-scripts/…nightvale-podcast-script.txt`
[S10] (223 lines, 10 segments). The tool narrates the script as written: it does not rewrite it and does not check that it
conforms to the guide.

| Script element (guide) | Example | What the tool does |
|---|---|---|
| Title and other headings | `# WELCOME TO ...`, `## SCENE 1 ...`, `### SEGMENT 2 ...` | Not spoken (user) |
| Header lines | `PROGRAMME:`, `EPISODE:`, `STYLE:`, `CAST:` | Not spoken (user) |
| End marker | `END` | Not spoken (user) |
| Speech line | `CECIL: Good evening, Night Vale.` | Speaker name stripped; the text is narrated; the following unlabelled paragraphs of the same speech are narrated |
| Delivery direction | `CECIL: (WHISPERED) The sky ...` | Converted to an inline tag placed before the line's text (user) |
| Pause | `(BEAT)`, `(PAUSE - 3 SECONDS)` | Passed to the model as a pause tag (user) |
| Continued speech | `CECIL: (CONT'D) We have ...` | Not covered by the user; see D-3 |
| SFX, music, transition cue | `[SFX: ...]`, `[MUSIC: ...]`, `[FADE OUT]` | Removed silently: nothing narrated, nothing printed, nothing generated (user) |
| Emphasis | `*Instance logs*` | Converted to an emphasis tag (user) |

The exact tag strings for a delivery direction, a pause and emphasis, and whether the model honors them through OpenRouter,
are unverified (U-1, U-6); the coding agent verifies all three in the capped calls before hardcoding them [S1].

### Errors the user sees

| Condition | Message must contain | HTTP call? |
|---|---|---|
| No argument, more than two arguments, or an unknown option | Usage line with `<scriptPath> [outputPath]` | No |
| Script file missing | The path | No |
| Extension is not `.txt` | That only `.txt` is supported | No |
| Script empty, or nothing narratable after removals | That there is nothing to narrate | No |
| Output path extension is not `.mp3` | The required extension `.mp3` | No |
| No key found | Where the key file goes (full path) and what it must contain (`OPENROUTER_API_KEY=<key>`) | No |
| A chunk fails after all retries | That narration failed, `chunk k of n`, the HTTP status if any, the API message if any | Yes |

### CI/CD [S1]

- Pull request workflow: always starts on pull requests to `main`; each job skips build and tests when only ignored files
  changed, so the required check reports success; otherwise builds and runs the tests on Windows and on Linux. It also
  validates the PR title as a conventional commit [D-1]. The user marks the checks required (agents cannot).
- Release workflow: on a push to `main` (the squash-merge), unless only ignored files changed, reads the squash subject
  and body; `fix:` gives PATCH, `feat:` MINOR, a breaking marker MAJOR, anything else publishes nothing. It reads the latest
  `v*` tag, bumps it, passes the version to the build, re-runs the tests, publishes `win-x64` and `linux-x64`
  self-contained single-file executables, tags `v<version>` and creates a GitHub Release. First release `1.0.0`. No build
  number; the version is not stored in a repo file; CI does not commit to `main`.
- Ignored by both workflows: `docs/`, `.docs/`, `.github/`, `.claude/`, `agent-memory/` and `.gitignore` (root only for
  directories and `.gitignore`), and any `.md` file at any depth. The user accepts that edits to docs and `.md` files
  trigger no release.

## Acceptance criteria

### Solution and architecture

- **AC-1**: On a clean checkout, `dotnet build -warnaserror` from the repo root exits 0 with no warnings on Windows and on
  Linux.
- **AC-2**: The repo has a solution with projects Domain, Application, Infrastructure and Cli (Cli at
  `src/PodcastGenerator.Cli`) and at least one xUnit test project. References follow the Clean Architecture dependency
  rule: Domain references nothing; Application only Domain; Infrastructure Application (and Domain); Cli composes the rest.
- **AC-3**: All OpenRouter/HTTP code is in Infrastructure behind an interface declared in Application. No other project uses
  `HttpClient`.
- **AC-4**: Services come from dependency injection (no `new` for services). Every async method name ends in `Async` and
  accepts a `CancellationToken`.
- **AC-5**: `dotnet test` exits 0 with no network access and no `OPENROUTER_API_KEY`. No test of any kind (unit,
  integration, regression), locally or in CI, makes a live OpenRouter call. Tests fake the HTTP layer with canned responses
  that come from documented API behavior. No test reads the user's key file or the real
  `<UserProfile>/.config/PodcastGenerator/` or `<UserProfile>/PodcastGenerator/` folders; tests use temporary directories
  and a fake key.
- **AC-6**: A third-party audio library may be used for joining audio, MP3 encoding and channel handling. The chosen library
  works on `win-x64` and `linux-x64` inside a self-contained single-file executable, and the design doc records the options
  considered, the choice and its license. If no option can produce MP3 under these conditions, the coding agent stops and
  asks the user instead of choosing another format.

### Inputs, outputs and exit codes

- **AC-7**: Given an existing `.txt` script, a key available per AC-23, and stubbed narration responses, running
  `PodcastGenerator <scriptPath>` writes one MP3 file into the default output directory, prints its full path to standard
  output and exits 0.
- **AC-8**: The default output directory equals
  `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PodcastGenerator")` and is created if
  missing. No literal `%USERPROFILE%`, `\` or `/` separator appears in path-building code.
- **AC-9**: The default file name is `Podcast-MM-DD-YYYY.mp3`, where the date is the local date of the run.
- **AC-10**: An existing file at the target is never overwritten or modified. The new file gets the suffix `-2`, then `-3`,
  and so on, before the extension (`Podcast-09-19-2026-2.mp3`), for the default name and for an explicit `[outputPath]`.
- **AC-11**: Given `[outputPath]` that is a file path, the file is written there, and missing parent directories are
  created.
- **AC-12**: Given `[outputPath]` that is an existing directory, the file is written inside it with the default file name.
- **AC-13**: An `[outputPath]` file path whose extension is not `.mp3` makes the tool print an error naming the required
  extension `.mp3`, exit 1 and make no HTTP call.
- **AC-14**: If the run fails or is cancelled after any output or temporary file was created, the partial output file and
  all temporary files are deleted, and any pre-existing file is left untouched.
- **AC-15**: The exit code is 0 on success and 1 on any failure, and every failure prints a message saying what went wrong
  to standard error.
- **AC-16**: With no argument, more than two arguments, or an unknown option, the tool prints a usage message containing
  `<scriptPath> [outputPath]` to standard error, exits 1 and makes no HTTP call.
- **AC-17**: `--help` prints the usage and exits 0. `--version` prints the version and exits 0. In an executable built by
  the release workflow, `--version` prints exactly the release version. [D-18]
- **AC-18**: If `<scriptPath>` does not exist, the tool prints an error containing the path, exits 1 and makes no HTTP call.
- **AC-19**: Only the extension `.txt`, matched case-insensitively, is accepted. `.md` and any other extension are
  rejected with an error saying only `.txt` is supported, exit 1, no HTTP call.
- **AC-20**: An empty script, or a script with no narratable text after the removals in AC-28 to AC-35, is rejected with an
  error saying there is nothing to narrate, exit 1, no HTTP call.
- **AC-21**: A script of 3,000 lines is processed to completion (stubbed responses), and the tool imposes no line-count
  limit. [D-21]

### Key and runtime folder

- **AC-22**: The runtime folder is `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
  ".config", "PodcastGenerator")` on Windows and on Linux. On a run where it or a default resource is missing, the tool
  creates it from the defaults embedded in the executable. An existing resource is never overwritten. [D-13]
- **AC-23**: The key is the value of `OPENROUTER_API_KEY` in the file `PodcastGenerator.env` in the runtime folder. The
  `OPENROUTER_API_KEY` environment variable is also supported. If both are set, the file wins; if the file has no key and the
  variable is set, the variable is used. .NET user-secrets are not read.
- **AC-24**: If no key is found, the tool prints an error containing the full path of the key file and the line format
  `OPENROUTER_API_KEY=<key>`, exits 1 and makes no HTTP call.
- **AC-25**: The key file parser tolerates a UTF-8 BOM and either line ending, ignores blank lines and lines starting `#`,
  trims spaces around the key and value and one pair of matching quotes, ignores other keys, lets the last duplicate win, and
  treats an empty value as missing.
- **AC-26**: The key value never appears in standard output, standard error, log output, exception messages or any written
  file, on success and on every failure path, and the tool never prints other contents of the key file. Verified with a
  sentinel key.
- **AC-27**: The product reads no key from `config.env` or `.claude/credentials/`. No committed file (source, tests, docs,
  Postman collection) contains a real key. `.gitignore` also ignores `*.env`. [D-19]

### Script parsing

- **AC-28**: The parser follows `docs/script-writing-guide.md` [S12]. Lines starting with `#` (title, `##` scene and `###`
  segment headings) are not spoken.
- **AC-29**: Header lines `PROGRAMME:`, `EPISODE:`, `STYLE:` and `CAST:`, and a line `END`, are not spoken. Other header
  lines: D-4. [D-4]
- **AC-30**: On a speech line the speaker name and colon (`CECIL:`) are stripped and the rest is narrated. Unlabelled
  paragraphs that follow it are narrated. Every speaker is narrated with the one configured voice. [D-5]
- **AC-31**: A `(CONT'D)` after the speaker name is not spoken and produces no tag. [D-3]
- **AC-32**: A delivery direction in parentheses directly after the speaker name (for example `(WHISPERED)`) is converted to
  an inline tag placed before that line's text; the direction words are not spoken. [D-6]
- **AC-33**: A pause on a line of its own (`(BEAT)`, `(PAUSE - 3 SECONDS)`) is passed to the model as a pause tag at that
  position, and its words are not spoken. [D-8]
- **AC-34**: A line consisting only of a square-bracket cue (`[SFX: ...]`, `[MUSIC: ...]`, `[FADE OUT]`) is removed. Nothing
  is narrated, nothing is generated, and nothing is printed about it on standard output or standard error. [D-10]
- **AC-35**: `*emphasis*` is converted to an emphasis tag: the asterisks are removed, the emphasized words are narrated as
  written, and the tag is placed immediately before them. [D-7]
- **AC-36**: The tool adds, removes or rewords no word of the script beyond AC-28 to AC-35, performs no conformance check
  (a script that departs from the guide still runs), and the same script gives the same request text on every run.
- **AC-37**: Text sent to the model never has two tags adjacent; adjacent tags are merged into one bracket separated by a
  comma. [D-9]
- **AC-38**: Golden test on the sample [S10]: the text sent, across all chunks, contains none of its header lines,
  headings, speaker names, `(CONT'D)`, cue lines, `END` or asterisks; it contains "Good evening, Night Vale. I'm Cecil
  Palmer."; the tag for `(WHISPERED)` immediately precedes "The sky is turning that shade of purple again."; and an emphasis
  tag immediately precedes "Instance logs" (line 27).
- **AC-39**: The tag strings for a delivery direction, a pause and emphasis are hardcoded only after the coding agent has
  verified them in the capped calls (AC-53) or from documentation, and the design doc records the evidence [S1]. (U-1, U-6)

### Narration requests

- **AC-40**: Each narration request is `POST /api/v1/audio/speech` on OpenRouter with `model` =
  `google/gemini-3.1-flash-tts-preview` and `voice` = `Umbriel`, using only body fields documented in [S2] or confirmed by
  the capped calls (documented per the caller: `model`, `input`, `voice`, `response_format`, `speed`, `input_references`,
  `provider`). `voice` is fixed and not configurable in 1.0.0. [D-22] No request uses `input_references` or any field that
  clones or imitates a real person's voice.
- **AC-41**: The response is read as raw audio bytes and errors as JSON [S2]. A non-success status makes the request fail
  as in AC-46.
- **AC-42**: The `CancellationToken` from Ctrl+C reaches every HTTP call; after cancellation no further request is sent, and
  AC-14 applies. A unit test with an already-cancelled token shows this.

### Style

- **AC-43**: The narration style prompt is read from a file in the runtime folder on every run. Editing that file changes
  the request text of the next run without rebuilding. The default copy is embedded in the executable and written there
  when missing (AC-22). [D-2]
- **AC-44**: If the style file is present but unusable (for example it lacks the transcript placeholder), the tool prints an
  error naming the file and saying that deleting it restores the default, exits 1 and makes no HTTP call. [D-13]
- **AC-45**: Each request has the structure of `docs/style-guide.md` section 9.1: audio profile, scene, director's notes,
  then the transcript, with the direction kept visibly separate from the transcript [S11]. Whether OpenRouter passes this
  structure through `input` to the model is unverified (U-2). [D-2]
- **AC-46**: If a chunk still fails after its retries, the whole run fails: the tool prints that narration failed with
  `chunk k of n`, the HTTP status if any and the API's error message if present, exits 1 and deletes partial output
  (AC-14). A chunk is retried up to 5 times before that; which failures are retried is D-12. [D-12]
- **AC-47**: The style block names no real person (including the show's narrator or actor), contains no text from the show,
  and describes tone only. The accent is neutral American English.

### Long scripts

- **AC-48**: A script larger than one chunk is split at segment boundaries (`### SEGMENT` headings) first, then at
  paragraph boundaries; never between a tag and its text; a paragraph larger than one chunk is split at a sentence end and
  a warning is printed to standard error. [D-11]
- **AC-49**: The chunk size is set from measurement in the capped calls, not guessed. The starting point is about 2 minutes
  of speech per chunk (the caller's unsourced guess); the design doc records the measured value and the evidence.
- **AC-50**: Every chunk of a run is sent with the same audio profile, scene and director's notes, the same voice and the same
  `response_format`. Chunks are requested one after another, in script order.
- **AC-51**: Standard error shows `chunk k of n` progress, with `n` known before the first request. There is no cost prompt.
- **AC-52**: The audio of the chunks is joined in script order into one file. With stubbed responses, a test shows no chunk
  is missing, duplicated or reordered, and the duration of the result equals the sum of the chunk durations within the
  tolerance the design doc states and justifies. No silence is added between chunks. [D-11]

### Audio output

- **AC-53**: The output is a valid MP3 that decodes without error. It has two channels, and the mono voice is centered: the
  two channels carry identical samples, with no panning (checked by decoding the output in a test).

### Real API verification

- **AC-54**: The coding agent may make real OpenRouter calls only by running the app (never in tests), with the key already
  in the runtime folder. Total spend across all such calls is at most $5. It never opens, prints, logs or pastes
  `PodcastGenerator.env` or `config.env`. What it learned, with the key removed, is recorded in the design doc or the
  Postman collection. [D-20]
- **AC-55**: A Postman collection under `/postman` contains the speech request with the key as a variable and no real key. It
  is for a person to try by hand and is not part of `dotnet test` or CI [S1].

### Packaging and documentation

- **AC-56**: `dotnet publish src/PodcastGenerator.Cli -c Release -r <rid> --self-contained -p:PublishSingleFile=true`
  succeeds for `win-x64` and `linux-x64`. Each output is a single executable that starts on a machine without the .NET
  runtime (with no arguments it prints the AC-16 usage). No `osx-*` or arm64 output.
- **AC-57**: `README.md` exists at the repo root and covers dev environment setup, build and test, how to run and use the
  app (arguments, default output and naming, the script format with a pointer to `docs/script-writing-guide.md`, the style
  file), where the key file goes (full path on Windows and Linux, contents), and the GitHub settings in AC-76.
- **AC-58**: In this delivery the coding agent updates `CLAUDE.md` only to match the scaffolding and the decisions recorded
  in it ("Current state", "confirm after scaffolding" notes). It makes no other edit to `CLAUDE.md`.
- **AC-59**: The default narration prompt embedded in the executable is taken from `docs/style-guide.md` section 9.1 and
  contains no music, Lyria or mixing content. `docs/style-guide.md`, `docs/script-writing-guide.md` and the sample are not
  edited in this delivery. [D-2]

### Pull request workflow

- **AC-60**: A workflow under `.github/workflows/` starts on every pull request targeting `main` and is never skipped by a
  workflow-level path or branch filter ([S6], relayed).
- **AC-61**: The tests run on Windows and on Linux as separate jobs. Their check names are stable and stated in `README.md`.
- **AC-62**: When every changed file is in the ignore list (`docs/`, `.docs/`, `.github/`, `.claude/`, `agent-memory/`,
  `.gitignore` at the repo root; any `.md` at any depth), each job succeeds without running build or test steps.
- **AC-63**: When at least one changed file is outside the ignore list, each job restores, builds
  (`dotnet build -warnaserror`) and runs `dotnet test` with the .NET 10 SDK, and fails if the build or any test fails, also
  when ignored files changed in the same PR.
- **AC-64**: The PR title is validated as a conventional commit (any conventional type; scopes such as `feat(cli):` are
  accepted). A non-conforming title fails the check with a message; editing the title makes the check pass without a new
  commit. [D-1]
- **AC-65**: The PR workflow uses no repository secret and never calls the real OpenRouter API.

### Release workflow

- **AC-66**: A workflow under `.github/workflows/` runs on pushes to `main` only.
- **AC-67**: When every file changed by the push is in the ignore list (this includes docs-only pushes such as those made
  before any code exists), no tag and no release are created.
- **AC-68**: The release type is read from the squash commit's subject (the PR title) and body: `fix` gives a PATCH bump,
  `feat` a MINOR bump, a breaking change (`!` after the type, or a `BREAKING CHANGE:` footer) a MAJOR bump. A commit with
  none of these (`refactor:`, `test:`, `chore:`, `ci:`, ...) creates no tag and no release. Scopes are accepted. [D-1]
- **AC-69**: The bump is applied to the latest `v*` tag, meaning the highest by Semantic Versioning precedence. With no `v*`
  tag, the first merge that releases publishes `1.0.0` with tag `v1.0.0` and no bump applied. A `v*` tag that is not
  `vMAJOR.MINOR.PATCH` fails the workflow with a message naming the tag.
- **AC-70**: Every version is valid Semantic Versioning 2.0.0 `MAJOR.MINOR.PATCH` [S8] with no build number. The GitHub
  Release version, the tag without its `v`, and the version passed to the build and embedded in the executables are the same
  string, and each version is strictly greater than the previous release.
- **AC-71**: The release workflow makes no commit or push to any branch, and no repo file stores the release version.
- **AC-72**: Before publishing, the workflow re-runs the tests on Windows and on Linux, builds, and publishes the `win-x64`
  and `linux-x64` executables. Only if all succeed does it create the tag and the release; on any failure neither exists.
  [D-17]
- **AC-73**: The tag `v<version>` points at the exact commit built. The workflow never moves, deletes or overwrites an existing
  tag or release; if the computed tag exists the run fails naming it. Two merges in quick succession never produce the same
  tag.
- **AC-74**: The GitHub Release has two assets: a `.zip` for `win-x64` and a `.tar.gz` for `linux-x64`, named
  `PodcastGenerator-<version>-<rid>.<ext>`, with GitHub-generated release notes, marked as the latest release. No macOS
  asset.
- **AC-75**: The release workflow uses no OpenRouter key and never calls the real API.
- **AC-76**: `README.md` and the coding agent's final report list the GitHub settings the user must change: required status
  checks (the Windows job, the Linux job and the PR title check), squash merging with the default squash message "Pull
  request title and commit details", and workflow permissions. The coding agent verifies the exact list against GitHub Docs
  and cites the pages in the design doc (U-8).

## Constraints

From `CLAUDE.md` [S1]:

- .NET 10, C#, xUnit; `win-x64` and `linux-x64` as a standalone executable; no macOS, arm64 or `osx-*` unless asked.
- Clean Architecture; dependency injection; all OpenRouter/HTTP code in Infrastructure behind an Application interface;
  `Async` suffix and `CancellationToken`.
- Never hardcode or log the key. Key files are off limits to agents; deny rules in `.claude/settings.json` back this up
  (read directly [S9]). No test makes a live OpenRouter call.
- Windows and Linux: `Path.Combine`, no hardcoded separators, Linux file names are case-sensitive, no Windows-only APIs or
  shell commands.
- `dotnet build -warnaserror`, `dotnet test`. Semantic Versioning 2.0.0 with CI-computed versions, squash merges, first release
  `1.0.0`, no build number. GitHub Actions with the always-start PR workflow and the ignore list.
- "Never fabricate, assume, or make-up anything"; agents do not edit `CLAUDE.md` without the user's permission, except the
  coding agent in this initial delivery (AC-58).

From the user's answers:

- Repository `https://github.com/abaskett3/PodcastGenerator`, `origin` `git@github.com:abaskett3/PodcastGenerator.git`, branch
  `main`. The user has pushed `main` (origin has commits `98af7c6` and `4498519`); a later local commit only changes
  `CLAUDE.md`. Prerequisite for the pipeline, not an acceptance criterion: `gh` is not installed yet, and `/deliver` needs it
  installed and authenticated [S1].
- TTS only through `google/gemini-3.1-flash-tts-preview` on `POST /api/v1/audio/speech`; voice `Umbriel`; one speaker.
- The style is a description of tone only; the user accepts the style guide's hard limits (no text from the show, no voice
  cloning or imitation of a real person, never name the actor in a prompt), a neutral American accent, and "Cecil Palmer" as
  the host name in scripts.
- The user has placed the key in the runtime folder.

## Out of scope

- Deferred (user: "for now"): Lyria and any music generation, background music, SFX rendering, mixing. Cues stay in scripts as
  direction only. Why: Google's docs say Lyria generates music exclusively [S13].
- Deferred to a later release (user: the style guide updates "can go on the next release"): revising `docs/style-guide.md` to
  remove its music, Lyria and mixing parts and reconcile it with TTS-only and the script format. The guide is left as it is in
  this delivery (AC-59). Edits to docs and `.md` files trigger no release, and the user accepts that.
- `.md` scripts and any Markdown support.
- More than one voice or speaker; a configurable voice; a configurable output format (MP3 only).
- Rewriting, correcting or checking the script (no conformance check); text-generation (LLM) steps.
- Voice cloning or imitation of any real person; reproducing text or music of the show.
- A build number; storing the version in a repo file; the workflow committing to `main`.
- Configuring GitHub settings (the user does it; AC-76 only lists them); installing `gh`.
- macOS, arm64, installers, Docker, NuGet packaging, code signing; changelog files.
- Running the Postman collection in tests or CI; live OpenRouter calls in any test.

## Open questions

BLOCKING: none. Everything the coding agent cannot yet know is either a proposed default (below) or a fact it verifies.

NON-BLOCKING, unverified facts the coding agent verifies (not questions for the user). If a result contradicts an
acceptance criterion, the coding agent stops and reports rather than substituting.

- **U-1**: The exact tag strings for a delivery direction (for example how `(WHISPERED)` becomes a whisper), a pause and
  emphasis through `/audio/speech`; for emphasis, also where the tag's effect ends, since a tag is placed before the text it
  affects. Sources: Google's tag guidance, relayed [S4]; not documented on OpenRouter's page [S2].
- **U-2**: Whether OpenRouter passes the profile, scene, director's notes and transcript structure through `input` to the
  Gemini model, and whether the notes are spoken aloud. Relayed only [S4, S11].
- **U-3**: Whether `Umbriel` is accepted as the `voice` value; `voice` is provider-dependent [S2].
- **U-4**: `response_format` to request (`mp3` or `pcm`; default `pcm` [S2]), and the sample rate, bit depth and channels of the
  audio; whether byte-joined MP3 is valid or the joined audio must be decoded and re-encoded (AC-6).
- **U-5**: Any input length limit (none documented [S2]) and the chunk size at which quality holds. `docs/style-guide.md`
  relays that Google says quality can drift beyond a few minutes [S11]; not verified for this model.
- **U-6**: How the model expresses a pause and whether a duration such as `PAUSE - 3 SECONDS` can be passed.
- **U-7**: Which audio libraries meet AC-6, their licenses, and how each behaves in a single-file publish on both runtime
  identifiers.
- **U-8**: The exact GitHub settings list (required checks, squash merging, workflow permissions). The caller could not confirm
  `contents: write` from the page it fetched; I recall, unverified, that a required check can be selected only after it has
  reported once. Source: [S6] and GitHub Docs on workflow permissions, not read by the PM.
- **U-9**: Real cost per call. Documented prices, relayed: $1 per million input tokens and $20 per million output tokens [S3];
  tokens per second of audio is not verified.
- **U-10**: OpenRouter's error and rate-limit behavior, for the retry rule (D-12).
- **U-11**: The Conventional Commits 1.0.0 rules [S7] and what GitHub puts in the squash body under the repo's default
  message setting.

## Proposed defaults awaiting confirmation

Each is applied in the acceptance criteria above (marked `[D-n]`) unless the user says otherwise.

1. **D-1** The release workflow reads the squash subject and body; scopes such as `feat(cli):` are accepted; the PR workflow
   validates the PR title as a conventional commit (any type). Evidence: `CLAUDE.md` says the workflow reads both and lists a
   `BREAKING CHANGE:` footer [S1]; a wrong title otherwise silently gives no release.
2. **D-2** The executable needs its own default narration prompt: embedded in the executable, written to the runtime folder on
   first run, and taken from `docs/style-guide.md` section 9.1 (audio profile, scene, director's notes, transcript
   placeholder) without any music content; the guide itself is not edited. Evidence: `CLAUDE.md` says resources are created
   from the first run and loaded from the runtime folder [S1]; only section 9.1 is meant to be sent [S11]; you accepted the
   guide's hard limits, accent and host name.
3. **D-3** `(CONT'D)` is a production marker: stripped, not spoken, no tag. Evidence: the guide defines it as marking a speech
   resumed after a cue [S12] but does not say explicitly that it is unspoken.
4. **D-4** Any `LABEL: value` line before the first `##`/`###` heading is a header line and is not spoken, in addition to the
   four named labels anywhere. Evidence: guide conformance item 1 (header block, then scene heading) [S12].
5. **D-5** A speech line starts with an all-caps speaker name (letters, spaces, apostrophes, hyphens, periods) followed by a
   colon; all speakers, if several, use the one voice; unlabelled text before any speaker is narrated as written. Evidence:
   guide "Speaker" row [S12]; single voice is your decision.
6. **D-6** A direction is the parenthetical directly after the speaker name (after an optional `(CONT'D)`); it applies to that
   paragraph only; the words inside are lower-cased into one bracket tag, and a comma list stays in one tag
   (`(WHISPERED, SLOW)` gives one tag). The mapping is adjusted if the verified tag syntax differs (U-1). Evidence: guide
   layout rule 3 [S12]; style guide section 4 [S11].
7. **D-7** The emphasis tag is placed immediately before the emphasized words, and for a multi-word emphasis the tag covers the
   words between the asterisks; how far the model's emphasis extends is verified (U-1). Evidence: Google's rule that a tag goes
   before the text it affects, relayed [S4].
8. **D-8** Only `(BEAT)` and `(PAUSE - N SECONDS)` alone on a line are pauses; other own-line parentheticals and parentheses
   inside spoken text are narrated as written. Known interaction: the guide's phonetic-spelling rule (a parenthetical after a
   hard word) would be read aloud. Evidence: guide "Pause" row [S12]; "narrated as written" is your rule.
9. **D-9** Adjacent tags are merged into one comma-separated bracket, and a pause tag at a chunk boundary stays with the text
   that follows. Evidence: style guide section 4 [S11]; the never-adjacent rule is relayed from Google [S4], unverified.
10. **D-10** Only whole-line `[...]` cues are removed; a bracket inside a spoken line is narrated as written. Evidence: guide
    layout rule 2 says cues never sit inside a spoken line [S12].
11. **D-11** Consecutive segments are packed into one chunk up to the chunk size; a segment larger than the chunk size is packed
    by paragraphs; a tag stays with its text; the chunk size unit (characters, words or tokens) is the coding agent's choice from
    the measurement; no silence is added between chunks. Evidence: none beyond your rules; fewer requests.
12. **D-12** Retries (up to 5, so up to 6 attempts) apply to network errors, timeouts and HTTP 408, 429 and 5xx, with a short
    increasing wait (respecting a `Retry-After` header if present); other 4xx errors (for example 401, 402) fail at once.
    Evidence: none sourced; retrying a bad key five times cannot help. The coding agent checks U-10.
13. **D-13** The runtime folder and default resources are created at startup, before the key check, so the folder exists when
    the missing-key error points to it; existing files are never overwritten (so a new release's default style does not replace
    an edited copy); an unusable style file is an error, not a silent reset. Evidence: `CLAUDE.md` "created there on the first
    run and loaded from there on every run" [S1].
14. **D-14** MP3 bitrate and sample rate: the coding agent picks a constant bitrate suited to speech, the sample rate follows the
    source audio, and both are recorded in the design doc. Evidence: you set no value.
15. **D-15** Chunk audio is never all held in memory; temporary files or streaming, deleted afterwards. Evidence: a 3000-line
    script is on the order of hours of audio (my estimate: about 13 times the sample's 223 lines; unmeasured).
16. **D-16** Script encoding is UTF-8 (a BOM tolerated) with either line ending. Evidence: the sample contains dashes such as
    "—" [S10].
17. **D-17** The release workflow's tests run on both Windows and Linux. Evidence: same as the PR workflow; you said only
    "re-runs the tests".
18. **D-18** `--help` prints usage and exits 0; `--version` prints only the version and exits 0; an unknown option is a usage
    error, exit 1. Evidence: your "`--help` and `--version` only".
19. **D-19** `.gitignore` gets `*.env`. Evidence: it has `.env`, `.env.*` and `config.env` but not `PodcastGenerator.env` (lines
    487 to 490, read directly).
20. **D-20** The coding agent tracks verification spend as a running total from the response's cost or usage data if present,
    otherwise from the documented prices [S3], writes it in the design doc, and stops before $5. Evidence: your $5 cap.
21. **D-21** No line-count limit is enforced; "about 3000 lines" is guidance, and longer scripts are processed. Evidence: your
    "up to about 3000 lines".
22. **D-22** `voice` is the fixed constant `Umbriel`. If OpenRouter rejects it (U-3) the coding agent stops and reports rather
    than substituting another voice. Evidence: your choice of `Umbriel`.

### Sources

- [S1] `CLAUDE.md` (read directly, current revision).
- [S2] OpenRouter TTS guide, `https://openrouter.ai/docs/guides/overview/multimodal/tts`. Relayed, not read by the PM:
  `POST /api/v1/audio/speech`; fields `model`, `input`, `voice` (provider-dependent), `response_format` (`mp3` or `pcm`, default
  `pcm`), `speed`, `input_references`, `provider`; raw audio bytes; JSON errors; no maximum input length documented.
- [S3] OpenRouter text-to-speech models collection, `https://openrouter.ai/collections/text-to-speech-models`. Relayed, not read
  by the PM: Gemini model "200+ inline audio tags", two speakers, 70+ languages, $1 per million input tokens, $20 per million
  output tokens.
- [S4] Google Gemini TTS guides, `https://aistudio.google.com/learn/gemini-tts-prompt-guide-with-tags` and
  `https://ai.google.dev/gemini-api/docs/models/gemini-3-1-flash-tts-preview`. Relayed as search summaries, not read by the PM:
  square-bracket tags placed before the text, never two adjacent, style prompts and director's notes.
- [S5] Wikipedia, Welcome to Night Vale, `https://en.wikipedia.org/wiki/Welcome_to_Night_Vale`. Relayed, not read by the PM.
- [S6] GitHub Docs, Troubleshooting required status checks,
  `https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks`.
  Relayed as a search summary (a workflow skipped by path or branch filtering or a commit message leaves its checks Pending
  and blocks a PR that requires them; a skipped job reports Success). Not read by the PM; the coding agent reads it.
- [S7] Conventional Commits 1.0.0, `https://www.conventionalcommits.org/en/v1.0.0/`. Named for the coding agent; not read.
- [S8] Semantic Versioning 2.0.0, `https://semver.org/`. Named for the coding agent; not read.
- [S9] Files read directly by the PM in this revision: `.claude/settings.json` (deny rules for `config.env` and
  `PodcastGenerator.env`) and `.gitignore`. The repo state (`main` pushed, commits `98af7c6` and `4498519` on origin, a later
  local commit changing only `CLAUDE.md`, `gh` not installed) is relayed by the caller.
- [S10] `docs/sample-scripts/QE-3395-RC-09-17-26-nightvale-podcast-script.txt` (223 lines, 112 non-blank, 10 segments; read
  directly).
- [S11] `docs/style-guide.md`, DRAFT 1 (read directly). Its external claims were read by its author through a summarizing tool.
- [S12] `docs/script-writing-guide.md`, DRAFT 1 (read directly). Its own sources were fetched through a summarizing tool.
- [S13] Deferral reason, relayed, not read by the PM: Google's Lyria 3 docs, "Both models generate music exclusively"
  (`https://ai.google.dev/gemini-api/docs/interactions/music-generation`).
