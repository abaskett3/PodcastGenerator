# Design: initial development (first working CLI and CI/CD)

Spec: `docs/specs/initial-development.md` (revision 4, 76 acceptance criteria). Branch: `feature/initial-development`.
Mode: feature, no GitHub issue number (so no closing keyword).

This document says how each acceptance criterion (`AC-n`) is met, what changed, what the tests are, and what was verified
against the real OpenRouter API and the GitHub documentation, with the sources. Findings that need the user's decision are in
section 2; read them first.

## 1. Summary

- A .NET 10 solution with four Clean Architecture projects and one xUnit project (section 5).
- `PodcastGenerator <scriptPath> [outputPath]` turns a `.txt` script into one two-channel MP3, narrated by
  `google/gemini-3.1-flash-tts-preview` through `POST /api/v1/audio/speech`, voice `Umbriel`. The request asks for `pcm`
  (24 kHz, 16-bit, mono); the chunks are stored in temporary files and encoded once, in order, by a pure managed LAME port
  (GroovyMp3), with the mono voice written to both channels.
- Two GitHub Actions workflows for pull requests (build and test; PR title) and one release workflow (section 9).
- `README.md`, the Postman collection `postman/PodcastGenerator.postman_collection.json`, and the scaffolding notes in
  `CLAUDE.md` (AC-58) were updated.
- Verification: 242 unit tests (no network, no key), `dotnet build -warnaserror` with 0 warnings, a published `win-x64` and
  `linux-x64` single-file executable (the Linux one was started in WSL Ubuntu 26.04 with no .NET installed), 135 capped real
  API calls made by running the app (section 3, the last 10 being the final end-to-end run of the sample script).

## 2. Findings that need the user's decision

None of these contradicts an acceptance criterion, so nothing was substituted. Each is a limit or a risk the user should know.

1. **The model sometimes speaks text that is not in the transcript, or drops text.** Measured with the app (section 3):
   - Runaway extra speech: 2 of 55 calls with a very short transcript (one or two sentences, 80 to 120 characters, some with
     tags) under the default prompt returned 50 s and 150 s of audio where about 7 s was expected. A speech-to-text check showed made-up
     town announcements after the real sentences (not the director's notes read aloud). One further call had an added
     greeting before the transcript. A 150 s runaway costs about $0.08.
   - Dropped text: with chunks of about 3000 characters, one of four chunks (3048 characters, expected about 250 s) returned
     only 78 s of mostly unintelligible audio (39 characters per second; normal is 10 to 13). Chunks of at most 1800
     characters gave normal pace in 9 of 9 chunks, and chunks of at most 900 in 15 of 15.
   - The tool does not detect either failure: the request succeeds, so nothing is retried. Google's documentation lists
     "quality of longer outputs" as a known limit of this model
     (https://ai.google.dev/gemini-api/docs/speech-generation, "Limitations"). A plausibility check (audio length against
     character count, retrying a chunk outside a band) would catch the gross cases, but it adds behavior and thresholds that
     the spec does not contain, so it is **not implemented**. Decision needed: add such a check?
   - A prompt with a first line telling the model to "synthesize speech ... read only the words in the transcript" (as Google
     advises: "add a clear preamble instructing the model to synthesize speech") gave 0 runaways in 15 short calls, and the
     unchanged prompt 0 in 15 (2 in the 40 earlier calls). That is too few calls to say anything. The default prompt is
     section 9.1 of `docs/style-guide.md` unchanged (AC-59, D-2), so no preamble was added. The user can add one to the
     editable style file. Decision needed: change the default prompt?
2. **Emphasis has no verified effect.** `*emphasis*` becomes `[emphasis]` placed before the words (AC-35, D-7). Six tag
   wordings (`[emphasis]`, `[emphasized]`, `[stressed]`, `[with emphasis]`, `[emphatically]`, `[emphasis on every word]`)
   and ALL CAPS were tried three times each. The tag words were never spoken (checked with speech recognition on the
   `[emphasis]`, `[emphasized]` and `[emphatically]` runs), but no measurable change in loudness, pitch or duration of the
   emphasized phrase appeared, while a positive control (`[shouting]`) raised the phrase level by about 3 dB. So it is
   harmless but probably does nothing; where an emphasis effect would end could not be measured either. The acoustic
   measure is crude (it cannot hear "stress"), so this is "no effect measured", not "no effect exists". Decision needed:
   keep `[emphasis]` or drop the tag and simply remove the asterisks.
3. **Pause lengths are only approximate.** `(BEAT)` becomes `[short pause]` and `(PAUSE - N SECONDS)` becomes
   `[pause for N seconds]`. The tags were not spoken aloud in the checked runs and they lengthen the gap on average, but
   the length varies widely from run to run (a requested 3 s gave 2.3 to 4.7 s; a requested 5 s gave 1.5 to 7.1 s).
4. **Silence at chunk joins.** The model puts about 0.2 to 0.9 s (once 1.95 s) of near silence before speech and about 0.25 s
   after it. The tool adds none and trims none (AC-52), so the join between two chunks has roughly 0.5 to 1.2 s of quiet.
5. **License of the MP3 encoder.** GroovyMp3 is LGPL-3.0. It is the only option that meets AC-6 (section 6). The repo has no
   license file yet. Shipping an unmodified LGPL library inside a single-file executable is common but the LGPL asks that the
   user can replace it; the source of this app is public, so a rebuild is possible. The user should confirm this is acceptable.
6. **Cost.** The API returns no cost or usage header, so spend was estimated from the documented prices: 25 audio tokens per
   second of audio and $20 per million output tokens (Google pricing page), $1 per million input tokens (OpenRouter model
   list), plus a 10 percent margin. Estimated total for all real calls: about $2.75 of the $5 cap (section 3). The real
   charge should be checked on the OpenRouter activity page.
7. **Deviations from proposed defaults.** D-12 says a 402 fails at once; OpenRouter documents a 402 with a `Retry-After`
   header as a wait-and-retry case, so that one is retried (section 8). D-4 is applied only when the script has a scene
   heading (section 8).
8. **`CLAUDE.md` "Gotchas" still says the tag/voice/sample-rate facts are unverified.** They are now verified (section 4), but
   AC-58 allows only the scaffolding notes to change, so that sentence was left alone.

## 3. Real API verification calls (AC-54, D-20)

All calls were made by running the app (`PodcastGenerator.dll`), never by a test. The key was read by the app from its
runtime folder; it was never opened, printed or copied by the agent. To capture the raw response bytes and headers, a
temporary code block in the speech client (marked `TEMP-VERIFY`) wrote them to a scratch folder outside the repo; it was
removed before the commit (`grep TEMP-VERIFY` finds nothing). Audio was analysed with scratch tools kept outside the repo
(a level and pause analyser, and the Windows built-in speech recognizer through `System.Speech` to check what was said).

Spend is a running total from the documented prices (D-20), computed from the size of each response and request:

| Group | Calls | Audio | Estimated spend |
|---|---:|---:|---:|
| First call (one sentence) | 1 | 11 s | $0.007 |
| Pause tag trials | 23 | 260 s | $0.151 |
| Whisper tag trials | 16 | 296 s | $0.168 |
| Emphasis tag trials, with positive control | 27 | 399 s | $0.230 |
| Sample script, chunks of at most 1800 characters | 9 | 972 s | $0.542 |
| Sample script, chunks of at most 3600 characters | 4 | 749 s | $0.417 |
| Sample script, chunks of at most 900 characters | 15 | 1026 s | $0.574 |
| Prompt preamble comparison | 30 | 215 s | $0.130 |
| **Before the final run** | **125** | **3927 s** | **$2.22** |
| Final end-to-end run of the sample (chunks of at most 1500 characters, final build) | 10 | 957 s | about $0.53 |
| **Total** | **135** | **4884 s** | **about $2.75** (cap $5) |

No call returned an error status: 135 of 135 answered HTTP 200 (so the retry rule was never exercised for real).

## 4. Verified facts (U-1 to U-11)

### 4.1 Endpoint, request and response (U-2, U-3, U-4)

- Endpoint `POST https://openrouter.ai/api/v1/audio/speech`; body fields `model`, `input`, `voice`, `response_format`
  (`mp3` or `pcm`, default `pcm`), `speed`, `input_references`, `provider`; the response is a raw audio byte stream, not JSON;
  errors are JSON. Source: https://openrouter.ai/docs/guides/overview/multimodal/tts (read directly, as markdown).
- `Umbriel` is one of the 30 `supported_voices` of the model, from the public list
  `GET https://openrouter.ai/api/v1/models?output_modalities=speech`, and every real call with it returned 200 (U-3).
- Real response headers (call 1): `Content-Type: audio/pcm; rate=24000; channels=1`, `Transfer-Encoding: chunked`,
  `X-Generation-Id: gen-tts-...`. No cost or usage header. The body of 543,360 bytes for 122 characters is 271,680 16-bit
  samples, 11.32 s at 24 kHz: consistent with Google's documented layout (24 kHz, 16-bit, mono PCM,
  https://ai.google.dev/gemini-api/docs/speech-generation), and speech recognition read the audio correctly. So `pcm` is
  signed 16-bit little-endian, 24000 Hz, mono. The client reads `rate` and `channels` from the header and falls back to the
  documented layout when they are absent.
- The prompt structure of `docs/style-guide.md` section 9.1 (audio profile, scene, director's notes, `#### TRANSCRIPT`,
  then the text) is passed through `input`: the delivery matched the deadpan notes and the notes were not read aloud in any
  checked run. `mp3` as `response_format` was **not** tested: `pcm` was chosen because chunks join exactly as samples with
  no decode and re-encode, and one MP3 is encoded at the end (U-4: no byte-joined MP3 question arises).
- Provider limits (public `GET /api/v1/models/google/gemini-3.1-flash-tts-preview/endpoints`): `max_prompt_tokens` 8192,
  `max_completion_tokens` 16384, `context_length` 32768; Google's model page says the same (input 8,192, output 16,384
  tokens). At 25 audio tokens per second the output limit is about 10.9 minutes of audio per request.
- Latency: about 5 s for 11 s of audio, 30 to 80 s for 1 to 4 minutes. The client timeout is 5 minutes.

### 4.2 Tags (U-1, U-6; AC-39)

Google documents inline tags placed before the text they affect, with no exhaustive list (`[whispers]`, `[sighs]`,
`[shouting]`, `[very slow]`, `[sarcastically, one painfully slow word at a time]`, and tags can be combined in one bracket),
but nothing about pause or emphasis tags (https://ai.google.dev/gemini-api/docs/speech-generation, "Audio tags"; searched, no
"pause" or "emphasis" tag appears). So these were measured. Each row is a real call through the app; "spoken" was checked with
speech recognition on a sample of runs.

| Script element | Tag sent | Evidence |
|---|---|---|
| `(WHISPERED)` | `[whispers]` | 5 runs: first-phrase level fell from about -20 dB to -31, -36, -42, -37 dB in 4 of 5 (one had no effect). `[whispered]`: 1 of 5 (-38 dB); `[whispering]`: no clear effect. So the verified tag replaces the words: `WHISPERED` maps to `whispers`. Any other direction becomes its own lower-cased words in one bracket (D-6), which is **not verified** for those words. |
| `(BEAT)` | `[short pause]` | 3 runs: gap between two sentences 1.5, 1.9 and 2.2 s, against a natural gap of about 1.0 to 1.3 s (4 untagged runs). Not spoken in the checked runs. `[beat]` broke a sentence in two (1 run); `[pause]` alone changed little (1.0, 1.0, 2.4 s). |
| `(PAUSE - N SECONDS)` | `[pause for N seconds]` (`second` for 1) | Requested 3 s: 4.7, 2.6, 2.3 s. Requested 5 s: 2.9, 7.1, 1.5 s. Requested 1 s: 3.1, 0.4, 1.5 s. Duration is followed only roughly. In one `[pause for 1 second]` run speech recognition heard extra words after the first sentence, so the words may occasionally be partly spoken. `[long pause]`: 2.4, 8.6, 3.7 s. |
| `*emphasis*` | `[emphasis]` | No measurable effect for any tried wording (section 2, item 2). Never spoken in the checked runs. |
| Adjacent tags | one bracket, comma separated | Google combines qualities in one bracket. Merging is applied as D-9 says; not separately measured. |

### 4.3 Chunk size (U-5, AC-49)

Measured on the sample script (12,824 characters, 10 segments) with the real app, varying the largest chunk:

| Largest chunk | Chunks | Audio | Characters per second (min to max) | Result |
|---|---:|---:|---|---|
| 900 characters | 15 | 1026 s | 8.8 to 13.1 | all normal |
| 1800 characters | 9 | 972 s | 9.9 to 13.5 | all normal (longest chunk 155 s) |
| 3600 characters | 4 | 749 s | 12.1 to 39.0 | one chunk of 3048 characters returned 78 s instead of about 250 s: text lost |

The caller's starting point was about 2 minutes per chunk. At the measured mean of 11.3 characters per second (10,000 to
11,300 characters in 972 to 1026 s) 1500 characters is about 133 s (2.2 minutes). **`MaxChunkCharacters` is 1500**: inside
the range that was clean in 24 of 24 chunks (up to 1777 characters), well under the size that failed (3048), and about the
2 minutes the caller proposed. One failed chunk in one run is thin evidence; the unit is characters of transcript (tags
included), not tokens, because the prompt is about 900 characters and the token limit (8192) is far away. The chunk size is
one constant (`NarrationSettings.MaxChunkCharacters`).

The MP3 written from the 1800-character run decoded to 971.59 s against 971.64 s of PCM (0.05 s difference); from the
900-character run 1026.14 s against 1026.2 s.

### 4.4 Errors and retries (U-10, D-12)

From https://openrouter.ai/docs/api-reference/errors (read directly): errors are `{"error": {"code", "message", "metadata"}}`
with the same HTTP status; 400, 401, 402, 403, 408, 429, 502, 503 are documented; `Retry-After` may be sent on 429, 503 and
on a 402 whose `metadata.limit_source` is `openrouter_in_flight_budget`, and "a 402 without the header is not a wait-and-retry
case". Google's speech page says the model "occasionally returns text tokens instead of audio tokens, causing the server to
fail the request with a 500 error ... implement automated retry logic". None of this was seen in the 135 calls. Retried:
network errors, timeouts, 408, 429, 5xx, and a 402 with `Retry-After`; not retried: other 4xx and a 402 without the header.
Waits: the server's `Retry-After` (capped at 120 s), else 2, 4, 8, 16, then 30 s.

### 4.5 Cost (U-9)

OpenRouter model list: `pricing.prompt` 0.000001 and `pricing.completion` 0.00002 per token. Google pricing page: "Audio tokens
correspond to 25 tokens per second of audio" (https://ai.google.dev/gemini-api/docs/pricing). So one minute of audio costs
about $0.03. OpenRouter's speech page says TTS models are priced per character; the model's own list gives per-token prices.
The real charge is not confirmed (section 2, item 6).

### 4.6 Final end-to-end run

After the temporary capture code was removed, the final build (chunks of at most 1500 characters) narrated the whole sample
script through the real API: 10 chunks, exit code 0, the path printed on standard output, `chunk k of 10` progress on standard
error, no warning and no retry. The MP3 is 11,480,256 bytes (96 kbps), decodes to 24000 Hz, 2 channels, 956.57 s (15 min 57 s),
22,957,632 frames, and the left and right channels are identical in every frame (0 differing). The earlier runs of the same
script gave 972 s (chunks of at most 1800) and 1026 s (at most 900), so the pace matched and no text was dropped in this run
(duration check only; the audio was not listened to).

### 4.7 GitHub and other documentation (U-8, U-11), read directly

- Required checks: "Successful check statuses are `success`, `skipped`, and `neutral`"; a job skipped by a conditional
  "reports Success"; a workflow skipped by path or branch filtering "stays Pending and blocks merging"; "A required status check
  must have completed successfully in the chosen repository during the past seven days"; a check is evaluated for a PR only
  when the run is triggered by `push`, `pull_request` and a few others. https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/troubleshooting-required-status-checks
- Job names must be unique across workflows for required checks
  (https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches).
- Permissions: "`contents: write` allows the action to create a release"; a job-level `permissions` key sets the token's access
  for that job; an organization can restrict write access. https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions
  (this resolves the caller's doubt about `contents: write`).
- Creating a release through the API with the workflow token is refused (404) when the target commit changes files under
  `.github/workflows/` relative to the default branch; "The GITHUB_TOKEN ... cannot be authorized for this"
  (https://docs.github.com/en/rest/releases/releases#create-a-release). The release workflow therefore creates the tag with
  `git push` and lets `gh release create --verify-tag` use it (section 9).
- `gh release create` (source: https://github.com/cli/cli/blob/trunk/pkg/cmd/release/create/create.go): with assets it creates
  the release as a draft, uploads them, and publishes only if every upload succeeded, cleaning up the draft on failure;
  `--verify-tag` aborts when the tag does not exist; `--generate-notes`; `--latest`.
- Concurrency: by default a new pending run replaces the pending one; `queue: max` keeps up to 100 pending runs in order
  (https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency). actionlint 1.7.12
  (released 2026-03-30) does not know the `queue` key yet (open issues 654 and 657 on rhysd/actionlint); it is the only
  finding when the workflows are linted, everything else passes.
- Squash message: the default squash message can be the "pull request title and commit details"
  (https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/configuring-commit-squashing-for-pull-requests).
  The exact body layout is not documented on that page, so the breaking-change footer search tolerates leading spaces and a
  `* ` bullet.
- Latest action versions (GitHub API, 2026-09-20): `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.0.0,
  `actions/upload-artifact` v7.0.1, `actions/download-artifact` v8.0.1. The workflows use the major tags.
- .NET on Linux needs OpenSSL (`libssl`); invariant globalization removes the ICU dependency
  (https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install,
  https://learn.microsoft.com/dotnet/core/runtime-config/globalization).
- Conventional Commits 1.0.0 (type case-insensitive except `BREAKING CHANGE`; `!` before the colon; `BREAKING CHANGE:` /
  `BREAKING-CHANGE:` footer) and Semantic Versioning 2.0.0 are followed by `compute-release.sh`.

## 5. Architecture (AC-2, AC-3, AC-4)

```text
PodcastGenerator.sln
Directory.Build.props                 net10.0, nullable, Version 0.0.0-dev (release passes the real one), no +commit suffix
src/PodcastGenerator.Domain           references nothing
src/PodcastGenerator.Application      references Domain
src/PodcastGenerator.Infrastructure   references Application (Domain through it)
src/PodcastGenerator.Cli              references Application and Infrastructure; assembly name PodcastGenerator
tests/PodcastGenerator.UnitTests      xUnit 2.9.3; references all four
```

**Domain** (`Scripts`, `Security`, `Narration`): `Script`, `ScriptSegment`, `ScriptBlock` (`SpeechBlock` with `Direction` and
`TextSpan`s marked emphasized or not, `PauseBlock` with an optional duration), `NarrationChunk`, and `ApiKey`, whose
`ToString` is a redaction placeholder so the key cannot leak by accident.

**Application**:

- `Abstractions`: `ISpeechClient` (+ `SpeechRequest`, `SpeechAudio`, `SpeechException`), `IApiKeyProvider`, `IFileSystem`,
  `IEnvironmentVariables`, `IRuntimePaths`, `IDefaultResources`, `IAudioWorkspace` and its factory, `IDelayer`, `IRunReporter`,
  `UserFacingException`.
- `Scripts.ScriptParser`, `Narration.TagVocabulary`, `TranscriptRenderer`, `SentenceSplitter`, `ChunkPlanner`,
  `NarrationSettings` (model, voice, retries, chunk size), `StyleProvider` and `NarrationStyle`.
- `Runtime.EnvFileParser`, `ApiKeyProvider`, `RuntimeInitializer`.
- `Generation.OutputPathResolver` and `PodcastGenerationService` (the orchestration and the retry loop).
- `DependencyInjection.AddPodcastGeneratorApplication`. Time comes from `TimeProvider`, waiting from `IDelayer`.

**Infrastructure**: `Speech.OpenRouterSpeechClient` (the only `HttpClient` user), `TaskDelayer`;
`FileSystem.PhysicalFileSystem`, `RuntimePaths`, `SystemEnvironmentVariables`, `EmbeddedDefaultResources` (the embedded
`Resources/default-style.md`); `Audio.Mp3AudioWorkspace` and its factory; `DependencyInjection.AddPodcastGeneratorInfrastructure`.

**Cli**: `Program` (parse, cancellation on Ctrl+C, build the service provider with `ValidateOnBuild` and `ValidateScopes`),
`CommandLine`, `CliRunner` (exit codes and messages), `ConsoleRunReporter` (standard error), `VersionInfo`.

**Flow** of `PodcastGenerationService.GenerateAsync`: resolve the output target (extension check, AC-13) -> read the script
(extension `.txt` case-insensitive, existence, AC-18 and AC-19) -> parse; nothing narratable is an error (AC-20) -> create the
runtime folder and default style if missing (D-13) -> load the style (AC-43, AC-44) -> find the key (AC-24) -> render and plan
chunks and print warnings -> create the output directory -> for each chunk, report `chunk k of n`, request with retries, store
the audio -> encode one MP3 to a temporary file next to the target -> move it to the first free name without overwriting
(AC-10) -> in `finally` delete the temporary file; the audio workspace deletes its folder on dispose (AC-14). Everything that
needs no network is checked before the first request, so a bad input never costs a call.

## 6. Audio library (U-7, AC-6)

Requirement: MP3 encoding (and channel handling) that works on `win-x64` and `linux-x64` inside a self-contained single-file
executable, with no separate install on Linux. Options found on NuGet (search of "mp3 encoder", "lame", "libmp3lame",
"mp3 encode managed", "shine mp3"), with the packages downloaded and their contents listed:

| Option | What it is | License | Meets AC-6? |
|---|---|---|---|
| `NAudio.Lame` 2.1.0 | Wrapper over native `libmp3lame` | LAME LGPL-2.1, wrapper MIT | No: Windows DLLs only. |
| `NAudio.Lame.CrossPlatform` 2.2.1 | Same wrapper; "Add Linux support" | MIT wrapper, LGPL-2.1 LAME | No: the package ships only `libmp3lame.32.dll` and `libmp3lame.64.dll`; its README says "On Linux, you need to install libmp3lame.so via the package manager", so the executable would not be standalone. |
| `EggEncoder` 4.3.0 | LAME bindings plus other codecs | MIT, LAME LGPL-2.1 | No: native binary only for `win-x64`. |
| `SoundFlow` (+ `SoundFlow.Codecs.FFMpeg`) | Audio engine | not evaluated | No: MP3 encoding needs FFmpeg binaries. |
| Bundle a Linux `libmp3lame.so` myself | Native | LGPL-2.1 | Not chosen: no trustworthy prebuilt binary source found, and building and committing one is out of proportion. |
| **`GroovyMp3` 0.1.3** (repo jongoochgithub/GroovyCodecs) | Pure C# encoder and decoder ported from LAME 3.98.4 via Jump3r; `netstandard2.0`; "no external OS dependencies, cross platform" | **LGPL-3.0** (GitHub license API) | **Yes**, chosen. |

Concerns with GroovyMp3, each checked: it is alpha, last pushed 2020, 29 stars, and "not the most polished or optimised" (its
README). Measured with a scratch harness: 60 s of 24 kHz stereo encodes in 0.9 s (about 65 times real time); the output
decodes (NLayer, MIT) to 24000 Hz, 2 channels, 60.048 s, and the two channels are identical in every frame (0 differing frames
of 1,441,152); a 971 s encode of real API audio decoded to 971.59 s, 23,318,208 frames, 0 differing. The published
single-file executables contain it and start (section 7). If it ever proves inadequate, the encoder sits behind
`IAudioWorkspace`, so replacing it changes one class.

Channel handling and format: the encoder is fed 16-bit stereo where each mono sample is written to both channels, in LAME's
stereo mode (not joint stereo), so the decoded channels are bit-identical (AC-53). Constant bitrate **96 kbps** for both
channels (about 48 kbps per identical channel) and the sample rate of the source, **24 kHz** (MPEG-2 Layer III, which LAME
selects for 24 kHz input); D-14. The decoder for tests is NLayer 2.0.1 (MIT and LGPL dual license), test project only.

**Duration tolerance (AC-52).** The decoded MP3 is longer than the sum of the chunks by the encoder and decoder delay plus padding:
one MPEG-2 Layer III frame at 24 kHz is 576 samples (24 ms); LAME's encoder delay (576 samples) plus the decoder delay (529)
plus padding to a whole frame is at most about 70 ms. The test allows 100 ms (3.0 to 3.1 s for 3 s of chunks); measured: +48
ms on a 60 s tone, +0.05 s on 971 s of real audio.

## 7. Packaging (AC-56)

`dotnet publish src/PodcastGenerator.Cli -c Release -r <rid> --self-contained -p:PublishSingleFile=true` was run for both
runtime identifiers. The outputs are one 74.7 MB executable each (plus separate `.pdb` files that are not shipped).
`InvariantGlobalization` is on in the Cli project, so a bare Linux machine needs no ICU. Checked:

- `win-x64` executable on Windows: `--version` printed `1.2.3` for `-p:Version=1.2.3`; no arguments printed the usage and exited
  1; `--help` exit 0; an unknown option, a `.md` script, a missing script, an empty script and an output path ending in `.wav`
  each exited 1 with a message and no HTTP call.
- `linux-x64` executable in WSL Ubuntu 26.04 where `dotnet` is not installed: `--version` printed `1.2.3` (exit 0), no
  arguments exited 1 with the usage, a `.wav` output path and an all-cues `EMPTY.TXT` script exited 1 with the right message.
  It was **not** run against the network on Linux (the key lives in the Windows profile), and the unit tests were run on Windows
  only; the CI workflows run them on Linux.

## 8. Decisions, interpretations and how proposed defaults were applied

- **D-1**: conventional commits, scopes accepted, any type; case-insensitive type. PR title validated by its own workflow.
- **D-2**: the embedded default is section 9.1 of `docs/style-guide.md` with the placeholder line replaced by `{transcript}`
  (a test compares the embedded text with the guide). The file is `style.md` in the runtime folder, created on first run,
  never overwritten. It must contain `{transcript}` exactly once (AC-44).
- **D-3**: `(CONT'D)` (also with a curly apostrophe or without one, any case) is dropped with no tag.
- **D-4**: a `LABEL: value` line before the first `##` or `###` heading is a header line, **only when the script has such a
  heading**. A script with no heading has no header block, so its speaker lines are kept; otherwise AC-36 (a script that departs
  from the guide still runs) would be broken by D-4, which would drop every speech line of such a script.
- **D-5**: a speaker is an all-capitals name of letters, spaces, apostrophes (straight or curly), hyphens and periods, then a
  colon. **Each non-blank line is one element** (guide layout rule 1), so a wrapped paragraph becomes several paragraphs; the
  guide's own scripts do not wrap.
- **D-6**: the direction is the parenthetical directly after the name (and an optional `(CONT'D)`), for that paragraph only.
  Its tag comes from `TagVocabulary` (section 4.2). A direction with no text after it is dropped with the empty paragraph.
- **D-7**: an emphasis tag goes immediately before the emphasized words. An unpaired asterisk is left as written.
- **D-8**: only `(BEAT)` and `(PAUSE - N SECONDS)` (hyphen, en or em dash, `SECOND` or `SECONDS`, any case) alone on a line are
  pauses. Other parentheticals are narrated as written, including the guide's phonetic-spelling parentheses.
- **D-9**: a pause is carried to the front of the next paragraph that has text, so it never ends a chunk; a pause with nothing
  after it is dropped; adjacent tags are merged into one bracket, comma separated.
- **D-10**: only whole-line `[...]` cues are removed.
- **D-11**: segments (`###` headings) are the first unit, paragraphs the second, sentences the last; a segment that fits is one
  unit; units are packed greedily up to the chunk size; the tail of a split segment is packed with the next segment; a
  paragraph over the chunk size is split at sentence ends (never inside a bracket) with a warning, and a single sentence over the
  size is sent whole and says so. The unit is characters.
- **D-12**: as section 4.4, one deviation: a 402 with `Retry-After` is retried, because OpenRouter documents it as a wait-and-retry
  case; a 402 without it fails at once.
- **D-13**: the runtime folder and the default style are created before the key check.
- **D-14**: 96 kbps CBR, source sample rate (24 kHz).
- **D-15**: each chunk's PCM is written to a temporary folder and encoded from disk; only one chunk is in memory at a time.
- **D-16**: UTF-8 with or without a BOM, any line ending (`\r\n`, `\n`, `\r`).
- **D-17**: the release workflow tests on Windows and Linux.
- **D-18**: `--help` exit 0; `--version` prints only the version, exit 0; an unknown option is a usage error, exit 1. A lone `-`
  is taken as a path.
- **D-19**: `*.env` added to `.gitignore`.
- **D-20**: section 3.
- **D-21**: no line limit; a 3,000-line script is a test.
- **D-22**: the voice is the constant `Umbriel`.
- **Cancellation** (AC-42): Ctrl+C cancels the token, which reaches the HTTP call, the delay and the encoder; the run exits 1
  with "cancelled" and no output kept.
- **Output writing**: the MP3 is encoded to a hidden temporary file in the target directory and then moved with
  `File.Move(overwrite: false)`. The final name is chosen only at that moment, so a file created during the run is never
  overwritten, and a killed process leaves at most a `.PodcastGenerator-*.tmp` file.
- **Key handling** (AC-26): the key exists as an `ApiKey` object whose text form is a placeholder; it is revealed only for the
  authorization header; the service replaces any occurrence of it in an API message with `[redacted]`; no logging framework is
  configured (the `HttpClient` factory's logging has no providers, so nothing is written).

## 9. CI/CD design (AC-60 to AC-76)

Files: `.github/workflows/pull-request.yml`, `pull-request-title.yml`, `release.yml`; `.github/scripts/classify-changes.sh`,
`compute-release.sh`; `.gitattributes` (`*.sh text eol=lf`, so Windows checkouts keep LF in the scripts).

**Ignore list and classification.** `classify-changes.sh` reads NUL-separated paths from `git diff --name-only -z --no-renames`
and prints `code=false` only when every path is under `docs/`, `.docs/`, `.github/`, `.claude/`, `agent-memory/`, is the root
`.gitignore`, or is a `.md` file at any depth (case-insensitive); zero paths give `code=true` (fail safe). `--no-renames` lists
both sides of a rename, so moving code into an ignored folder still counts as code. It was run locally on 8 inputs (docs only,
mixed, nested `.gitignore`, a `src/docs/` folder, empty input, a path with spaces, a look-alike folder, a `postman` file) with the
expected result each time.

**Pull request workflow** (AC-60 to AC-65). `on: pull_request` with the default types (opened, synchronize, reopened), no path
or branch filter, so it always starts. Job `Build and test (Windows)` and `Build and test (Linux)` (a two-entry matrix; the
names are stable and are in the README). Each job checks out with full history, classifies the PR's changed files, and every
later step is conditional on `code == 'true'`; so an ignored-only PR runs the job and reports success without building. Otherwise:
`actions/setup-dotnet@v6` (10.0.x), `dotnet restore`, `dotnet build -warnaserror --no-restore`, `dotnet test --no-build`, so a
failing build or test fails the job, also when ignored files changed in the same PR. No secret is used; permissions are
`contents: read`.
`pull-request-title.yml` (job `PR title (conventional commit)`) is separate on purpose: it also runs on `edited`, so editing the
title re-runs only it and the build jobs are not re-run (a re-run that skipped them would replace their results with "skipped").
The title is passed through an environment variable, never interpolated into the script, so a hostile title cannot run commands.
Pattern: `^[A-Za-z]+(\([^()]+\))?!?: [^[:space:]].*$`.

**Release workflow** (AC-66 to AC-75). `on: push: branches: [main]`; `concurrency: {group: release, queue: max}` so releases run
one at a time in order. Jobs:

1. `Plan release`: check out with all tags; list the push's changed files (`before..sha`, or the pushed commit alone when the
   before-commit is unknown) and classify; if code changed, `compute-release.sh` reads the squash subject and body and the
   `v*` tags. It prints `bump=` and `version=`. It was run locally on 14 inputs: `feat` and `fix` with and without scope, `!`,
   a `BREAKING CHANGE:` footer on a `refactor`, `BREAKING-CHANGE:` with a bullet, uppercase `FEAT`, a non-conforming title with a
   footer (no release), `docs` and `chore` (no release), the first release (`1.0.0`, no bump), version ordering (`v1.9.9` <
   `v1.10.0`), a `v0.4.2` major bump (`1.0.0`), and invalid tags `v1.1.0-rc.1` and `vNext` (exit 1, message names the tag).
2. `Release test (Windows|Linux)`: `dotnet build -warnaserror -p:Version=<version>` then `dotnet test --no-build`, on the exact
   commit (`ref: sha`).
3. `Package win-x64|linux-x64` (each on its own OS): `dotnet publish ... -p:Version=<version>`; then the job **runs the
   executable**: `--version` must print exactly the version, and running with no argument must exit 1 with the usage; then a `.zip`
   (Windows, `Compress-Archive`) or `.tar.gz` (Linux, keeps the executable bit) named `PodcastGenerator-<version>-<rid>.<ext>`.
4. `Publish release` (`permissions: contents: write`, only here): refuse if `v<version>` already exists on the remote (the run
   fails naming the tag; nothing is moved, deleted or overwritten); `git tag v<version> <sha>` and push it; `gh release create
   v<version> assets/* --verify-tag --title v<version> --generate-notes --latest`. If the release cannot be published the tag
   this run created is deleted (`if: failure()`), so neither exists. The version is never stored in the repo and no branch is
   pushed to (AC-71).

Limits, honestly: the workflows could not be run here (no push, no `gh`). They were checked with a YAML parser, with actionlint
1.7.12 (clean except its lag on `queue`, section 4.7) and the two scripts were run locally. The first pull request and the first
merge are their real test. The final `git push` of a tag by the workflow token depends on the repository allowing write access
for that job (README, settings list).

**Settings the user must change (AC-76, U-8)**, with sources in section 4.7 and the README: required checks `Build and test
(Windows)`, `Build and test (Linux)`, `PR title (conventional commit)` (selectable only after each has run in the past seven
days); squash merging on with the default message "Pull request title and commit details", and merge commits and rebase
merging off; Actions workflow permissions: the default read setting is enough because the release job asks for `contents: write`
itself, unless an organization policy limits the token.

## 10. Tests (AC-5)

242 xUnit tests in `tests/PodcastGenerator.UnitTests`; all fake the outside world: an in-memory `IFileSystem`, a fake speech
client, a fake `HttpMessageHandler` for the HTTP client, a fixed `TimeProvider`, a recording reporter, a fake delayer, and a
fake key (`sk-test-SENTINEL-...`). No test reads the real key file or the real profile folders; the only real disk use is a
temporary directory (MP3 files) that each test deletes, and repository files under `docs/` and `src/` read for the golden and
structure tests. The canned HTTP responses follow the documented behavior: raw audio bytes with `audio/pcm` and, from the real
calls, `rate=24000; channels=1`; and the documented JSON error shape.

| Area | Tests (what they show) |
|---|---|
| `ScriptParserTests` | headings, header lines, D-4, speakers, CONT'D, directions, pauses, cues, emphasis, segments, empty scripts, BOM and line endings, 3,000 lines, determinism, golden test on the sample (AC-20, AC-21, AC-28 to AC-36, AC-38) |
| `TranscriptRendererTests` | tag placement and merging, pause carrying, sample golden (AC-32, AC-33, AC-35, AC-37, AC-38, AC-39) |
| `ChunkPlannerTests` | packing, segment then paragraph then sentence splits, warnings, tags never separated, nothing lost or reordered, sample (AC-48, AC-52) |
| `EnvFileParserTests`, `ApiKeyProviderTests`, `RuntimePathsTests`, `RuntimeInitializerTests`, `StyleProviderTests` | key file rules and precedence, no `config.env`, runtime folder, default style equals section 9.1 and has no music content, style edits (AC-8, AC-22 to AC-27, AC-43, AC-44, AC-59) |
| `OutputPathResolverTests` | default name and date, suffixes, file path, directory, extension check (AC-8 to AC-13) |
| `PodcastGenerationServiceTests` | end to end with fakes: outputs, errors before any request, retries and back-off, `Retry-After`, non-retryable failures, redaction, cancellation, cleanup, progress, same style per chunk, 3,000 lines (AC-7 to AC-24, AC-40 to AC-52) |
| `OpenRouterSpeechClientTests` | request shape, bearer header only, audio parsing, error classification, timeouts, cancellation, key never in messages (AC-3, AC-26, AC-40 to AC-42, AC-46) |
| `PhysicalFileSystemTests` | the real file system adapter in a temp folder: new file not overwritten, no-overwrite move, BOM read, environment variables, delayer |
| `Mp3AudioWorkspaceTests` | decode the MP3: 2 channels, identical samples, order, duration within 100 ms, no silence added, bitrate, temp files deleted (AC-52, AC-53, D-14, D-15) |
| `CommandLineTests`, `VersionInfoTests`, `CliRunnerTests`, `ConsoleRunReporterTests`, `CompositionTests` | arguments, `--help`, `--version`, exit codes, stdout and stderr use, key never printed, DI resolves (AC-4, AC-15 to AC-17, AC-26, AC-51) |
| `ArchitectureTests` | project references, no `HttpClient` outside Infrastructure, `Async` and `CancellationToken` on every async method, no literal `%USERPROFILE%` or separator in `Path.Combine`, no key-shaped text in the repo, `.gitignore` has `*.env`, the Postman collection uses a variable (AC-2, AC-3, AC-4, AC-8, AC-27, AC-55) |

## 11. Acceptance criteria index

| AC | Met by |
|---|---|
| 1 | `dotnet build -warnaserror`, 0 warnings, on Windows here; the PR workflow builds on Linux |
| 2, 3 | section 5; `ArchitectureTests` |
| 4 | DI in `Program` and `AddPodcastGenerator*`; `ArchitectureTests`; `CompositionTests` |
| 5 | section 10; `dotnet test` was run with no key set |
| 6 | section 6 |
| 7 to 12 | `PodcastGenerationService`, `OutputPathResolver`; their tests |
| 13 | `OutputPathResolver.Resolve` before any request; tests |
| 14 | `finally` in `GenerateAsync`, workspace `DisposeAsync`; tests with a failing encoder and cancellation |
| 15, 16, 17 | `CliRunner`, `CommandLine`, `VersionInfo`; tests; the release workflow checks `--version` on both executables |
| 18 to 21 | `ReadScriptAsync`, `ScriptParser`; tests |
| 22 to 27 | `RuntimePaths`, `RuntimeInitializer`, `ApiKeyProvider`, `EnvFileParser`, `ApiKey`; tests |
| 28 to 38 | `ScriptParser`, `TranscriptRenderer`; golden tests on the sample |
| 39 | section 4.2 (evidence) |
| 40 to 42 | `OpenRouterSpeechClient`, `PodcastGenerationService`; tests |
| 43 to 47 | `StyleProvider`, embedded default; tests. AC-47: the default names no person, no show text, tone only, neutral American accent |
| 48 to 52 | `ChunkPlanner`, `PodcastGenerationService`, `Mp3AudioWorkspace`; tests; measurement in section 4.3 |
| 53 | `Mp3AudioWorkspace`; decoded in tests and on real audio |
| 54 | section 3 |
| 55 | `postman/PodcastGenerator.postman_collection.json` |
| 56 | section 7 |
| 57, 58, 59 | `README.md`; `CLAUDE.md` scaffolding notes only; the embedded default equals section 9.1; `docs/style-guide.md`, `docs/script-writing-guide.md` and the sample are unchanged |
| 60 to 76 | section 9; README |

## 12. Not done, and open items

- The workflows have not run on GitHub (section 9). The first pull request is their test.
- The Linux executable was started but not used against the network; the unit tests were run on Windows only.
- `mp3` as the API's `response_format` was not tested.
- Directions other than `WHISPERED` use unverified tags (section 4.2).
- The findings in section 2 need decisions.
- `CLAUDE.md` "Gotchas" is now out of date (section 2, item 8).
