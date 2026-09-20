# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

PodcastGenerator is a C# console app that turns a podcast script (.txt) into a narrated audio file, in the style
of "Welcome to Night Vale", by calling Google's Gemini 3.1 Flash TTS model through the OpenRouter API. The end product
is a standalone executable that runs on Windows and Linux. macOS is not a target yet.

Out of scope for now: background music, sound effects, mixing and Lyria (music generation). SFX and music cues in a
script are direction only; they are not rendered as sound.

## Current state

The repo is not scaffolded yet: there is no solution, no projects, no tests, no README.md and no `/postman`
collection. The GitHub repo is <https://github.com/abaskett3/PodcastGenerator> (`origin`); `main` holds the project
instructions, agents, templates and docs, and no code yet. The initial-development spec is being written in
`docs/specs/initial-development.md`. Commands and
architecture below describe the intended design. Replace this section once the scaffolding lands, and resolve the
"confirm after scaffolding" notes.

## Usage

- Input: path to a script file, up to about 3000 lines, given whole. Only `.txt` is accepted, matched
  case-insensitively (`.TXT` works); `.md` and any other extension are rejected. Markdown is not supported, so its
  rules can't be confused with the script's own format. An empty script, or one that has only cues, is rejected with
  an error and no API call.
- Output: optional path for the audio file. MP3 is the preferred format, and an output path with any other extension is
  an error that names the required one. The file has two channels, with the mono voice centered in both ears, for
  stereo headphones.
- Default output directory: `PodcastGenerator/` under the user profile folder. Resolve it with
  `Environment.SpecialFolder.UserProfile` so it works on Windows and Linux; don't use a literal `%USERPROFILE%`.
- Default file name: `Podcast-MM-DD-YYYY` (the date as month-day-year) plus the format's extension. If that file
  already exists, save as `Podcast-MM-DD-YYYY-2.<ext>`, then `-3`, and so on. The suffix is hyphen-number, so the name
  works on Linux too.
- If the output path is an existing directory, write the file inside it. Create any missing parent directories. If the
  run fails, delete any partial output file.
- Runtime config and resources all live in `Environment.SpecialFolder.UserProfile`/.config/PodcastGenerator/ (build
  the path with `Path.Combine`). Resources such as the style file are created there on the first run and loaded from
  there on every run.
- The API key is in `PodcastGenerator.env` in that folder, as the line `OPENROUTER_API_KEY=<key>`. The
  `OPENROUTER_API_KEY` env var is also supported; if both are set, the key file wins. .NET user-secrets are not
  supported. If no key is found, exit with a helpful, descriptive error that says where the key file goes and what it
  must contain. The key file parser tolerates a BOM and either line ending, ignores blank lines and `#` lines, trims
  spaces and one pair of quotes, ignores other keys, lets the last duplicate win, and treats an empty value as missing.
- Narration: a single speaker with the `Umbriel` voice. Speaker names in the script are stripped. A delivery direction
  is converted to an inline tag placed before its line's text, and a pause is passed to the model as a pause tag. SFX
  and music cues are removed silently. Emphasis marked with `*asterisks*` is converted to an emphasis tag. The exact
  tag syntax for delivery, pause and emphasis tags is verified before it is hardcoded. The script is narrated as
  written; the tool does not rewrite or check it.
- Long scripts are split inside the tool, at segment and then paragraph boundaries, and never between a delivery tag
  and its text. An oversized paragraph is split at a sentence end, with a warning. Every chunk gets the same profile
  and director's notes, and the chunk size is set from measurement, not guessed.
- If a chunk of the script can't be narrated, retry it up to 5 times, then fail the whole run.
- Exit code 0 on success and 1 on any failure. The error message says what went wrong.
- Script format and spoken-copy rules: `docs/script-writing-guide.md`. Narration style: `docs/style-guide.md`. Sample
  scripts: `docs/sample-scripts/`.

## Commands

`build` and `test` need a `.sln` (or project) in the working directory.

- Build: `dotnet build -warnaserror`
- Test: `dotnet test`
- Single test: `dotnet test --filter "FullyQualifiedName~<TestName>"`
- Run: `dotnet run --project src/PodcastGenerator.Cli -- <scriptPath> [outputPath]` (confirm project path after scaffolding)
- Publish the executable (intended, confirm after scaffolding):
  `dotnet publish src/PodcastGenerator.Cli -c Release -r <rid> --self-contained -p:PublishSingleFile=true`
  with `<rid>` = `win-x64` and `linux-x64`

## Stack

- .NET 10, C# (SDK 10.0.x is installed on the dev machine)
- Targets: Windows (`win-x64`) and Linux (`linux-x64`), shipped as an executable. No macOS support needed yet; add
  other runtime identifiers (arm64, `osx-*`) only when asked
- xUnit for unit tests
- A third-party audio library is acceptable, for joining audio, MP3 encoding and channel handling. It must work on
  `win-x64` and `linux-x64` inside a self-contained single-file executable. Research the options and record the choice
  and its license in the design doc.
- Postman collection in `/postman` for a person to try OpenRouter speech calls by hand (confirm after scaffolding).
  It is not part of `dotnet test` or CI, and agents don't run it.

## Source Control

- Git
- GitHub
- Semantic Versioning 2.0.0 (`MAJOR.MINOR.PATCH`). Releases are tags `vMAJOR.MINOR.PATCH` on `main` (for example
  `v1.0.0`) with a GitHub Release that carries the Windows and Linux executables. The first release is `1.0.0`. There
  is no separate build number; the version is the only identifier.
- Pull requests are squash-merged, so the squash commit subject (the PR title) decides the bump. It must be a
  conventional commit: `fix:` -> PATCH, `feat:` -> MINOR, a breaking change (`!` after the type or a
  `BREAKING CHANGE:` footer, such as a changed or removed CLI argument or output format) -> MAJOR. A merge with none of
  these (`refactor:`, `test:`, `chore:`, `ci:`, ...) publishes no release. With no `v*` tag yet, the first merge that
  does release publishes `1.0.0` with no bump applied. Pushes that only touch ignored paths, like the docs-only
  commits made before any code exists, create no version. The latest tag is the highest by SemVer, and a `v*` tag that
  isn't `vMAJOR.MINOR.PATCH` fails the workflow with a message naming it.
- The repo's default squash message is "Pull request title and commit details": the PR title is the subject and the
  commit details are the body. The release workflow reads both, so a `BREAKING CHANGE:` footer in a commit counts.
- CI computes the version: on a merge to `main` it reads the latest `v*` tag, bumps it, and passes the result to the
  build, then tags and releases. The version is not stored in a file in the repo and CI does not commit to `main`.
  This is to be revisited.
- Tagging, version bumps and releases are done by the CI/CD workflow. The agents don't do them.
- Main is the default branch name. If other docs, inputs, or anything else refers to master, please assume it means main.

## CI/CD

- GitHub Actions
- Pull request workflow: builds and runs the tests. The user marks it as a required status check in the GitHub
  repository settings (the agents can't). It always starts, and skips the build and tests inside the job when only
  ignored files changed, so the required check still reports success.
- Release workflow: on a merge to `main`, builds, tags `v<version>` and publishes a GitHub Release with the Windows
  (`win-x64`) and Linux (`linux-x64`) executables attached.
- Ignored by both workflows (a change touching only these does not build, test or release): `docs/`, `.docs/`,
  `.github/`, `.claude/`, `agent-memory/`, `.gitignore`, and any `.md` file.

## Architecture

- Clean Architecture: Domain, Application, Infrastructure, Cli (confirm project names after scaffolding)
- Dependency injection everywhere; no `new` for services
- All OpenRouter/HTTP code lives in Infrastructure behind an interface in Application
- Async methods end in `Async` and accept a `CancellationToken`

## Gotchas

- The OpenRouter API key comes from `PodcastGenerator.env` in the runtime folder (see Usage). Never hardcode it or log
  it.
- The key files are off limits to agents: never open, print, log or paste the contents of `PodcastGenerator.env` or the
  repo's git-ignored `config.env`. Agents may only run the app, which reads the key itself. Deny rules in
  `.claude/settings.json` back this up. `config.env` is not used by the app; it is kept in case it is needed later.
- The coding agent may make a small number of real OpenRouter calls to verify API behavior, outside tests, by running
  the app with the key in the runtime folder. The total spend across all such calls is capped at $5. Record what was
  learned, with the key removed, in the design doc or the Postman collection.
- Speech model: `google/gemini-3.1-flash-tts-preview` through OpenRouter's `POST /api/v1/audio/speech`. OpenRouter's
  docs say the response is raw audio bytes, `mp3` or `pcm` (default `pcm`). Unverified: how style prompts, inline tags
  and voices are passed through, the sample rate, and any input length limit. Confirm from documentation or the capped
  verification calls above, or ask the user, before hardcoding anything.
- No test ever makes a live OpenRouter call. This covers unit, integration and regression tests, locally and in CI.
  Tests fake the HTTP layer (for example a fake `HttpMessageHandler`) and use canned responses that come from
  documented API behavior, never invented ones. `dotnet test` must pass with no network access and without an
  `OPENROUTER_API_KEY`, and no test reads the user's key or key file.
- Code must run on both Windows and Linux: build paths with `Path.Combine`, never hardcode `\` or `/`, remember Linux
  file names are case-sensitive, and don't depend on Windows-only APIs or shell commands.

## Agent pipeline

Features and bug fixes go through `/deliver` (`.claude/skills/deliver/SKILL.md`), which runs the agents in
`.claude/agents/` in this order. Subagents cannot launch other subagents, so the main session orchestrates and
passes file paths between them.

`/deliver <feature description>` or `/deliver bug #<issue>` / `/deliver bug <text>` (bugs skip the product manager).

1. `product-manager` (sonnet): feature description -> `docs/specs/<slug>.md` with numbered acceptance criteria (`AC-n`).
2. `coding-agent` (sonnet): branches from `main` (`feature/<slug>` or `hotfix/<slug>`), writes
   `docs/designs/<slug>.md`, the code and unit tests, and authors the code commits. Also does fix rounds.
3. `testing-agent` (sonnet) and `code-review-agent` (sonnet) run in parallel on the same commit.
   - Tester: integration and regression tests (HTTP stubbed, no live OpenRouter calls), `docs/testing/<slug>.md`, bug reports in
     `docs/bugs/`, and its own `test:` commit. Never edits production code.
   - Reviewer: read-only, `docs/reviews/<slug>-round-<n>.md`. Every finding needs `file:line` plus spec or
     `CLAUDE.md` evidence; no preference-only findings.
   - Both end with `VERDICT: APPROVE` or `VERDICT: CHANGES_REQUESTED <paths>`. Changes go back to the coding agent,
     max 3 fix rounds, then ask the user.
4. `project-manager` (haiku): after both approve, checks the branch is clean and green, pushes, and opens the PR
   against `main` with `gh`, filling in `.github/pull_request_template.md` (`gh` skips the template when a body is
   passed, so the agent fills it in itself).

Issue linking: when a run has an issue number, the coding agent's and testing agent's commits and the PR body all
carry a GitHub closing keyword line (`Closes #<n>`, one keyword per issue), and the project manager verifies the link
with `gh pr view --json closingIssuesReferences`. No issue number means no keyword.

GitHub templates: `.github/pull_request_template.md` and the bug form `.github/ISSUE_TEMPLATE/bug_report.yml`
(Summary, Steps to Reproduce, Expected Result, Actual Result, optional Evidence; blank issues are disabled in
`config.yml`). GitHub only offers them once they are on `main`, so commit them in the first commit.

Prerequisites for a real run: a git repo with a `main` branch and an `origin` remote, the `gh` CLI installed and
authenticated (`gh auth login`), and a buildable solution (`/deliver` checks the first two and stops with the
failure). The GitHub MCP server is not used.

## Documentation

- All docs are Markdown.
- Pipeline artifacts live in `docs/` (`specs/`, `designs/`, `testing/`, `bugs/`, `reviews/`) and are committed. The
  script and style guides (`docs/script-writing-guide.md`, `docs/style-guide.md`) and `docs/sample-scripts/` live there
  too.
- README.md does not exist yet. Create it during scaffolding.
- README.md must cover: dev environment setup, build and test, how to run and use the app, and where the key file goes.
- Update the README whenever CLI arguments or setup steps change.

## Workflow

- Run build and tests before calling a task done.
- Keep changes focused; don't refactor unrelated code.

## Rules

Never fabricate, assume, or make-up anything. All logic must be researched and grounded.

- Ask clarifying questions instead of guessing.
- Never make up data, API responses or behavior to get a test to pass.
- Agents don't edit `CLAUDE.md` without the user's permission. The one exception is the initial MVP delivery, where
  the coding agent may update it to match the scaffolding and the decisions recorded here.
