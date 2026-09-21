# Code review: cli-set-config, round 1

Branch `feature/cli-set-config`, `CODE_HEAD` 8323860b6a9283479d177cac49f5de05eaae3d97. Spec `docs/specs/cli-set-config.md`,
design `docs/designs/cli-set-config.md`.

VERDICT: APPROVE

Blocking findings: 0. Non-blocking findings: 3. Questions for the user: 3. Notes: 2.

I did not run `dotnet` (read-only review while the testing agent builds). The review is from `git diff main...8323860` and the
surrounding code.

## Summary

All of AC-1 to AC-14 are implemented and each has tests that would fail if the behavior broke.

- AC-1 to AC-4: `ConfigService.SetAsync` validates first (ConfigService.cs:252), so a rejected key or value touches nothing. It then
  creates the folder and file and edits in place through `EnvFileEditor.SetValue`.
- AC-5 and AC-12: `Program` handles `--help` and `--version` before any service is built (Program.cs:10-15).
  `--set-config` uses `CliRunner.SetConfigAsync` only.
- AC-6 to AC-11: `CliRunner.RunAsync` calls `EnsureRequiredAsync` before `GenerateAsync` (CliRunner.cs:26-31).
  `ConfigService` prints the fixed message, allows 5 attempts, and appends only.
- AC-13: no path writes a value to stdout or stderr.
  - `Saved <KEY>.` carries the key name only.
  - Error text carries the file path and the exception type name only (ConfigService.cs:348-349).
  - `SetConfigCommand.ToString()` hides the value.
  - Sentinel-based tests cover the paths that can be automated.
- AC-14: the README section is rewritten, no longer tells the user to create the folder or file, and documents the prompt and
  `--set-config`. A repository search found no other user-facing instruction to create the file by hand outside historical
  `docs/designs`, `docs/specs`, `docs/reviews` and `docs/testing` artifacts and `CLAUDE.md` (see Notes).
- `CLAUDE.md` rules:
  - Layering: the new Application types are interfaces and pure logic, console I/O is in Cli, file I/O is in Infrastructure
    behind `IFileSystem`, and there is no HTTP.
  - DI: services are resolved from the container, and `RequiredConfig` is registered as an instance.
  - Naming and tokens: async methods end in `Async` and take a `CancellationToken`, and cancellation reaches
    `Task.Delay`, `ReadLineAsync`, `FlushAsync` and the file writes.
  - Portability: paths use `Path.Combine`, and `UnixCreateMode` is set only when `!OperatingSystem.IsWindows()` (verified by the
    coding agent on Windows, see the design).
  - Tests: none reads the real profile or key. `ConfigServiceOnDiskTests` uses a temporary folder. The remaining process tests
    (usage, help, version, rejected `--set-config`) end before the profile is touched.
- Existing tests: the five moved tests in `CliFailureOutputTests` keep their assertions verbatim, and one assertion is added
  (`Assert.Empty(fixture.Speech.Requests)`). `CliRunnerTests` and `CompositionTests` only gain the new constructor arguments and
  registration. One assertion was replaced in `KeyAndRuntimeFolderTests` (finding 3).

Points examined from the request:

- Extra `Invalid input` rejections (key with `=` or starting with `#`, value with a line break). These are grounded: without
  them the write would produce a line `EnvFileParser` cannot read back (`EnvFileParser.cs:19-30`), which the spec constraint on
  the parser format forbids. They do not contradict AC-4, which lists cases that must be rejected but does not say "only".
  Accepted.
- Trimming the value. `EnvFileParser` trims on read (`EnvFileParser.cs:35`), so it is consistent.
- Linux 0600 and temp-file-plus-move. Not required by the spec but consistent with `CLAUDE.md` treating the key as sensitive, and
  contained in Infrastructure behind the `IFileSystem` interface. Tests cover a failed replace leaving no temp file. The mode
  assertion runs on the Linux CI job only, which the design says openly.
- `--set-config` with the wrong argument count, or not first, is a usage error. The spec is silent. The behavior is reasonable
  and tested (question 2).
- `Saved <KEY>.` on stdout and `Invalid input` on stderr with no prefix. This meets AC-4 (exact message) and AC-13, because no
  value is printed.
- No console input fails with exit 1. The spec leaves it open (open question 3). The behavior is documented in D-3 and the
  README and is tested.

## Findings

1. NON-BLOCKING. The hidden-typing path has no automated test.
   - Location: `src/PodcastGenerator.Cli/ConsoleConfigPrompter.cs:687-716`. The test file's own header says the path is not
     exercised (`tests/PodcastGenerator.UnitTests/Cli/ConsoleConfigPrompterTests.cs:6-8`).
   - Evidence: AC-13 says the value is not echoed. This path is the only one where the value is typed at a terminal. Removing
     `intercept: true` from `Console.ReadKey` at line 697 would echo the secret and no test would fail. The polling that lets
     Ctrl+C end the wait (the `KeyAvailable` loop, lines 692-695) is likewise untested. The design records both as unverified
     ("Not done or not verifiable here").
   - Change requested: optional. Inject the key source, for example a `Func<bool>` for "key available" and a
     `Func<bool, ConsoleKeyInfo>` for read-key, with the console as the default. A test can then feed keys and assert that
     nothing typed reaches the error writer, that Backspace works, and that cancelling ends the wait.

2. NON-BLOCKING. An update with a differently cased key can turn a working key line into one the app cannot read.
   - Location: `src/PodcastGenerator.Application/Runtime/EnvFileEditor.cs:381-385` (the matched line is rewritten with the
     key as given). The exact-case read is at `EnvFileParser.cs:30`.
   - Evidence: the file has `OPENROUTER_API_KEY=old` and the user runs `--set-config openrouter_api_key new`. AC-3 matches the
     line case-insensitively, so it is rewritten as `openrouter_api_key=new`. `ApiKeyProvider` and `ConfigService.IsPresentAsync`
     read the key with an exact-case match, so the key now counts as missing and the user is prompted. The old value is gone.
     D-8 explains the opposite case (file lowercase, argument uppercase), where rewriting with the given spelling helps.
     The spec says only that the line is "updated in place with the new value", not which spelling wins.
   - Change requested: none required until the user answers question 1. The alternatives are to keep the spelling already in
     the file, or to keep the current behavior and say so in the README (it now says "matched without regard to case" and
     nothing about respelling).

3. NON-BLOCKING. An existing assertion was dropped when the no-key integration test was rewritten, and nothing replaces it.
   - Location: `tests/PodcastGenerator.UnitTests/Integration/KeyAndRuntimeFolderTests.cs:116-119`. The removed line was
     `Assert.Contains("OPENROUTER_API_KEY=<key>", error, ...)`.
   - Evidence: `CLAUDE.md` Usage says a missing key is reported with an error "that says where the key file goes and what it
     must contain". The message that CLI users now see comes from `ConfigService.NoConsoleInputMessage`
     (`ConfigService.cs:342-345`). It contains the line `<KEY>=<value>`, but no test asserts that (the tests at
     `ConfigTests.cs:519-521` check `--set-config <key>`, the path and the environment variable only). The only test that still
     asserts `OPENROUTER_API_KEY=<key>` is `PodcastGenerationServiceTests.cs:276`, which covers the
     `PodcastGenerationService` message that the CLI no longer reaches.
   - Change requested: add an assertion that the message contains `{KeyName}=<value>` (in `ConfigTests.cs` at 519-521 or in
     `KeyAndRuntimeFolderTests.cs`).

## Questions for the user

1. Case of the key on update (finding 2). When `--set-config` matches an existing line only by case, which spelling should the
   line have afterwards: the spelling already in the file, or the one typed on the command line (current behavior)?
2. `--set-config KEY` with no value, or with extra arguments. The code treats this as a usage error (exit 1, the usage text),
   not `Invalid input`. AC-4 covers an empty or blank value but not a missing argument. Is a usage error what you want?
3. Usage errors and AC-6. AC-6 says "any invocation other than `--set-config`, `--help`, or `--version`" runs the folder, file
   and required-value check first. The code deliberately skips the check for a malformed command line (design D-4), so it does
   not ask for a key and then say the arguments were wrong. Is that acceptable? If not, `Program` would have to run the check
   before printing a usage error.

## Notes

- `CLAUDE.md` is out of date and was correctly not edited by the coding agent. Its Usage section still says "If no key is
  found, exit with a helpful, descriptive error..." and describes the key file as something the user creates. It needs an update
  from the user for the prompt, `--set-config`, and the fail-fast behavior when there is no console input. It also lists no
  `--set-config` command in Usage. This is not a code change request.
- Two claims in the design rest on documentation only and were not run: `UnixCreateMode` giving mode 0600 on Linux (the
  `AssertOwnerOnlyOnUnix` test checks it on the Linux CI job), and Ctrl+C ending the hidden read (untested, finding 1). The
  design says so plainly, so I raise no defect for them.
