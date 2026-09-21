# PodcastGenerator

PodcastGenerator turns a podcast script (a `.txt` file) into one narrated MP3 file. The narration is done by Google's
Gemini 3.1 Flash TTS model (`google/gemini-3.1-flash-tts-preview`) through the [OpenRouter](https://openrouter.ai/) API, with
the `Umbriel` voice, in a deadpan "Welcome to Night Vale"-style delivery. The result is a two-channel MP3 with the mono
voice centered in both ears, for stereo headphones.

It is a console app that ships as a standalone executable for Windows (`win-x64`) and Linux (`linux-x64`). macOS is not
supported yet. Background music, sound effects and mixing are out of scope: music and SFX cues in a script are direction
only and are not rendered as sound.

## Use it

```text
PodcastGenerator <scriptPath> [outputPath]
PodcastGenerator --set-config <KEY> <VALUE>
PodcastGenerator --help
PodcastGenerator --version
```

| Argument | Meaning |
|---|---|
| `<scriptPath>` | An existing `.txt` file (the extension is matched case-insensitively). `.md` and every other extension are rejected. Up to about 3000 lines; longer scripts are still processed. |
| `[outputPath]` | Optional. Either an `.mp3` file path (missing folders are created) or an existing directory (the file is written inside it). Any other extension is an error. |
| `--set-config <KEY> <VALUE>` | Saves a config value, such as the API key, and exits. See [The API key and the config file](#the-api-key-and-the-config-file). |

Exit code `0` means success; `1` means anything failed. The full path of the written file is printed on standard output;
progress (`chunk k of n`) and warnings go to standard error. Ctrl+C stops the run and removes any partial output.

- **Default output.** Without `[outputPath]` the file is `PodcastGenerator/Podcast-MM-DD-YYYY.mp3` in your user profile folder
  (`C:\Users\<you>\PodcastGenerator\` on Windows, `/home/<you>/PodcastGenerator/` on Linux), using the local date.
- **Existing files are never overwritten.** If the name is taken, the file is saved as `Podcast-MM-DD-YYYY-2.mp3`, then `-3`,
  and so on. The same happens for an explicit `[outputPath]`.
- **What is spoken.** The script's words are narrated as written; the tool does not rewrite or check them. Speaker names,
  headings (`#`, `##`, `###`), header lines (`PROGRAMME:`, `EPISODE:`, `STYLE:`, `CAST:`), `END` and `[SFX: ...]`,
  `[MUSIC: ...]` and `[FADE OUT]` lines are not spoken. A delivery direction such as `(WHISPERED)` becomes an inline tag
  placed before that line's text, `(BEAT)` and `(PAUSE - 3 SECONDS)` lines become pause tags, and `*emphasis*` becomes an
  emphasis tag. The script format is described in [`docs/script-writing-guide.md`](docs/script-writing-guide.md); a sample is
  in [`docs/sample-scripts/`](docs/sample-scripts/).
- **Long scripts** are split inside the tool, at segment (`### SEGMENT`) and then paragraph boundaries, into chunks of at most
  1500 characters, narrated one after another with the same voice and style, and joined into one file. A chunk that fails is
  retried up to 5 times (network errors, timeouts, HTTP 408, 429 and 5xx, and a 402 that carries `Retry-After`) before the
  whole run fails.
- **Cost.** Narration is billed by OpenRouter: about 3 US cents per minute of audio (the model's output price of $20 per million
  tokens at 25 tokens per second of audio). The tool asks nothing before spending; check your usage on OpenRouter.

### The style file

The narration style (audio profile, scene and director's notes, then the transcript) lives in an editable file that is read
on every run, so a change takes effect on the next run without rebuilding:

| Platform | Path |
|---|---|
| Windows | `C:\Users\<you>\.config\PodcastGenerator\style.md` |
| Linux | `/home/<you>/.config/PodcastGenerator/style.md` |

The file is created from the default embedded in the executable the first time the tool runs and is never overwritten. It
must contain the placeholder `{transcript}` exactly once, where the script text goes. If the file is unusable the tool says so
and names the file; deleting it restores the default.

### The API key and the config file

The tool needs an OpenRouter API key. It keeps it in a file named `PodcastGenerator.env` in the runtime folder, as one line:

```text
OPENROUTER_API_KEY=<key>
```

where `<key>` is your OpenRouter API key.

| Platform | Full path of the config file |
|---|---|
| Windows | `C:\Users\<you>\.config\PodcastGenerator\PodcastGenerator.env` |
| Linux | `/home/<you>/.config/PodcastGenerator/PodcastGenerator.env` |

You do not create this folder or file yourself. The tool creates them the first time it runs, and when a required value is
missing it asks for it:

```text
OPENROUTER_API_KEY not found. Please set this config value to continue.
OPENROUTER_API_KEY:
```

Type or paste the value and press Enter. What you type is not shown on screen and is never printed back. A blank entry is
rejected with `Invalid input` and you are asked again; after 5 attempts in total the run fails with exit code `1`. A
valid value is added as a new line at the end of the file. When there is no console input at all (standard input is closed or
has ended) the run fails at once with exit code `1` and a message that says how to set the value.

To set or change a value directly, use `--set-config`:

```text
PodcastGenerator --set-config <KEY> <VALUE>
PodcastGenerator --set-config OPENROUTER_API_KEY <key>
```

It writes `KEY=VALUE` to the config file, creating the folder and the file first if they are missing. When the file already
has a line for that key (matched without regard to case) the line is updated in place, and every other line is left as it is;
otherwise a new line is added. `<KEY>` must not be empty or contain any whitespace, `=`, or start with `#`, and `<VALUE>` must not
be empty or whitespace-only and must be on one line; anything else is rejected with `Invalid input` and the file is not changed.
Any other key name is accepted, not only the ones the tool needs. The command saves the value and exits; it does not generate a podcast, and it does not print the
value back. A value typed on the command line can stay in your shell history, so the prompt above is the better way to enter a
key on a shared machine.

The `OPENROUTER_API_KEY` environment variable also works and counts as present, so no prompt appears when it is set; if both
the file and the variable are set, the file wins. .NET user-secrets are not used. The file may have a UTF-8 byte order mark, `#`
comment lines and quotes around the value. On Linux the tool creates the file readable and writable by you only. The tool never
prints or logs the key.

## Develop

### Set up

- The [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.x), Git, and Windows or Linux.
- On Linux, .NET needs OpenSSL (`libssl`) for HTTPS. The executable runs in globalization-invariant mode, so it does not need
  ICU ([.NET on Ubuntu](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install),
  [globalization invariant mode](https://learn.microsoft.com/dotnet/core/runtime-config/globalization)).
- An OpenRouter key is needed only to run the app for real. Building and testing need no key and no network.

### Build and test

```text
dotnet build -warnaserror
dotnet test
dotnet test --filter "FullyQualifiedName~<TestName>"     # a single test
```

`dotnet test` passes with no network access and without `OPENROUTER_API_KEY`: no test makes a live OpenRouter call, and no test
reads your key file or your real profile folders. Tests fake the HTTP layer and use temporary directories and a fake key.

### Run from source

```text
dotnet run --project src/PodcastGenerator.Cli -- <scriptPath> [outputPath]
```

### Publish the executable

```text
dotnet publish src/PodcastGenerator.Cli -c Release -r win-x64   --self-contained -p:PublishSingleFile=true
dotnet publish src/PodcastGenerator.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

Each output is one executable that starts on a machine without the .NET runtime. `--version` prints `0.0.0-dev` for a local
build; the release workflow passes the real version with `-p:Version=<x.y.z>`, so the version is not stored in the repo.

### Layout

| Project | Role |
|---|---|
| `src/PodcastGenerator.Domain` | The script model and the API key type. References nothing. |
| `src/PodcastGenerator.Application` | Script parsing, tags, chunking, retries, the generation service and the interfaces Infrastructure implements. References Domain. |
| `src/PodcastGenerator.Infrastructure` | The OpenRouter HTTP client, the file system, the MP3 encoder and embedded resources. References Application. |
| `src/PodcastGenerator.Cli` | The command line and the composition root (dependency injection). |
| `tests/PodcastGenerator.UnitTests` | xUnit tests. |
| `postman/` | A Postman collection to try the speech endpoint by hand. Not part of tests or CI. |

Design notes, the verified API facts and the verification-call log are in
[`docs/designs/initial-development.md`](docs/designs/initial-development.md).

### The Postman collection

[`postman/PodcastGenerator.postman_collection.json`](postman/PodcastGenerator.postman_collection.json) holds the speech request
with the key as the variable `OPENROUTER_API_KEY` (empty in the file; set it in Postman, and never commit a real key). Calls
cost real money.

## Pull requests, releases and versions

Pull requests are squash-merged. The squash commit subject (the pull request title) must be a
[Conventional Commit](https://www.conventionalcommits.org/en/v1.0.0/); it decides the version
([Semantic Versioning 2.0.0](https://semver.org/)):

| Title | Release |
|---|---|
| `fix: ...` or `fix(scope): ...` | patch |
| `feat: ...` or `feat(scope): ...` | minor |
| `type!: ...`, or a `BREAKING CHANGE:` footer in the commit details | major (a changed or removed CLI argument or output format) |
| `refactor:`, `test:`, `chore:`, `ci:`, `docs:` and so on | no release |

The first release is `1.0.0`. Merges that change only `docs/`, `.docs/`, `.github/`, `.claude/`, `agent-memory/`, the root
`.gitignore` or `.md` files build, test and release nothing.

Two workflows run on pull requests, and one on merges to `main`:

| Workflow | Job (the name GitHub shows as the check) | What it does |
|---|---|---|
| `Pull request` | `Build and test (Windows)` and `Build and test (Linux)` | Always starts. When only ignored files changed the job succeeds without building; otherwise it runs `dotnet build -warnaserror` and `dotnet test` on the .NET 10 SDK. |
| `Pull request title` | `PR title (conventional commit)` | Fails when the title is not a conventional commit. Editing the title runs it again, with no new commit. |
| `Release` | `Plan release`, `Release test (...)`, `Package ...`, `Publish release` | On a push to `main`: reads the squash subject and body, computes the version from the latest `v*` tag, re-runs the tests on Windows and Linux, publishes the `win-x64` and `linux-x64` executables, then creates the tag `v<version>` and the GitHub Release with `PodcastGenerator-<version>-win-x64.zip` and `PodcastGenerator-<version>-linux-x64.tar.gz`. It never commits or pushes to a branch. |

The workflows use no repository secret and never call OpenRouter.

### GitHub settings you must change

The agents cannot change these; the repository owner does.

1. **Required status checks.** Settings, Branches (or Rules, Rulesets): protect `main`, turn on "Require status checks to pass
   before merging", and add these three checks: `Build and test (Windows)`, `Build and test (Linux)` and
   `PR title (conventional commit)`. GitHub lists a check for selection only after it has run in the repository during the
   past seven days, so open a pull request first. Successful, skipped and neutral statuses satisfy a required check
   ([about protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches#require-status-checks-before-merging),
   [troubleshooting required status checks](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks)).
   Do not require the `Release` workflow's jobs: it runs only on `main`.
2. **Squash merging.** Settings, General, Pull Requests: turn on "Allow squash merging" and set its default commit message to
   "Pull request title and commit details", so the squash subject is the pull request title and the body carries the commit
   details (a `BREAKING CHANGE:` footer in a commit is then seen by the release workflow). Turn off "Allow merge commits" and
   "Allow rebase merging" so every merge is one squash commit
   ([configuring commit squashing](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/configuring-commit-squashing-for-pull-requests)).
3. **Workflow permissions.** Settings, Actions, General, Workflow permissions: the default setting "Read repository contents and
   packages permissions" is enough. The `Publish release` job asks for `contents: write` itself, which is what allows it to push the tag and
   create the release ([workflow syntax: permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions)).
   An organization policy that limits the token to read-only would stop releases. Actions must be enabled, and the workflows
   use only `actions/*` actions from GitHub.

## Documentation

| File | Purpose |
|---|---|
| [`docs/script-writing-guide.md`](docs/script-writing-guide.md) | How a script is formatted and how the words are written for the ear. |
| [`docs/style-guide.md`](docs/style-guide.md) | The narration style. Section 9.1 is the default prompt. |
| [`docs/sample-scripts/`](docs/sample-scripts/) | Sample scripts. |
| [`docs/specs/`](docs/specs/), [`docs/designs/`](docs/designs/) | The spec and the design doc of each feature. |
