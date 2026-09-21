# Code review: cli-set-config, round 2

Branch `feature/cli-set-config`, `CODE_HEAD` 2b9e2900f72788e56b2f103c2cad3cf2e295901e. Spec `docs/specs/cli-set-config.md`
(updated with the four fix round 1 user decisions), design `docs/designs/cli-set-config.md`. Reviewed
`git diff 8323860..2b9e290`, with the tester's commit b32a999 as context (`git diff b32a999..2b9e290` for the tester's test file).

VERDICT: APPROVE

Blocking findings: 0. Non-blocking findings: 2. Questions for the user: 1. Notes: 1.

I did not run `dotnet` (read-only review while the testing agent builds). The review is from the diff and the surrounding code.

## Summary

The four user decisions are implemented as the updated spec words them, and every test that encoded the old behavior moved to
the new behavior without a weaker assertion.

- Decision 1, keys read case-insensitively. `EnvFileParser.GetValue` now compares with `StringComparison.OrdinalIgnoreCase`
  (`EnvFileParser.cs:33`). This is culture-independent, so it is safe on Linux. Every other parser rule is untouched: the BOM
  trim, the split on `\r`/`\n`, the `#` skip, the trim and one-pair unquote, and `value = ...` on every match, which keeps
  "last duplicate wins" (`EnvFileParser.cs:17-41`). `ApiKeyProvider.cs:39` and `ConfigService.cs:106` are the only readers, and
  both go through `GetValue`, so both get the new rule. The environment variable lookup is unchanged, as the spec's Constraints
  section requires.
- Decision 2, remove every line and append one. `EnvFileEditor.SetValue` (`EnvFileEditor.cs:15-26`) takes the line ending
  first, removes every line for which `IsLineFor` is true, then appends. `IsLineFor` (lines 55-65) uses the same rules as the
  parser: trimmed, blank and `#` lines never match, the key is the text before the first `=`, and the compare is
  `OrdinalIgnoreCase`. I traced these cases by hand:
  - CRLF file, the key in the middle: the other lines keep `\r\n`, and the new line gets `\r\n`.
  - The key line is the only line, with or without an ending: a file with an ending keeps it because the ending is read before
    the removal; a file with none gets `Environment.NewLine`, the documented fallback.
  - The key line is first and the last remaining line has no ending (`KEY=x\r\nA=1`): the last line gets the first-found ending
    `\r\n`, then the new line. That is why taking the ending before the removal matters.
  - A removed last line with no ending: the other lines stay intact (`EnvFileEditorTests`, `A_removed_last_line_without_a_line_ending_...`).
  - A BOM: dropped when the file is rewritten, as in round 1 and as the design says (D-8).
  - A lone `\r` file: covered by `A_lone_carriage_return_...`.
  - A mixed-ending file: each surviving line keeps its own ending.
  - The temp-file-plus-move and mode 0600 are in `PhysicalFileSystem.ReplacePrivateTextAsync`, which this diff does not touch,
    and `ConfigService.SetAsync` still goes through it (`ConfigService.cs:62`). Validation still runs before any file access
    (`ConfigService.cs:52-55`), so a rejected input changes nothing (AC-4).
- The prompt path (AC-10, D-17). `ConfigService.AskForAsync` is unchanged and still uses `AppendValue` (`ConfigService.cs:131`).
  `IsPresentAsync` goes through the case-insensitive `GetValue`, so a lowercase key line with a value counts as present
  and one with an empty value counts as missing. The prompt then appends a line after it, and the last duplicate wins. Covered by
  `ConfigServiceTests.An_empty_lowercase_key_line_is_left_alone_and_the_typed_value_is_appended_after_it` and
  `A_lowercase_key_line_with_a_value_counts_as_present`.
- "Last duplicate wins" across spellings is a consequence of decision 1 and matches the pre-existing rule. For example
  `OPENROUTER_API_KEY=good` followed by `openrouter_api_key=` now reads as missing, as a same-case empty duplicate always did.
  `RuntimeTests.Every_other_parser_rule_still_applies_to_a_key_of_any_case` pins the equivalent case, and
  `The_last_duplicate_wins_across_different_spellings_of_the_key` covers it in both orders. This does no harm on either path: `--set-config` leaves
  one line, and the prompt appends.
- Decision 3, a usage error skips the config check. There is no code change in this diff; D-4 stands and is unchanged. No finding.
- Decision 4, quotes. `ConfigInput.IsValidValue` (`ConfigInput.cs:22-25`) rejects a value when what remains after `Trim()` and the
  parser's own `Unquote` is blank. It is used by both `SetAsync` (`ConfigService.cs:52`) and the prompt
  (`ConfigService.cs:126`), so AC-4 and AC-9 both hold. `Unquote` became `internal` (`EnvFileParser.cs:46`) and is reached from
  `ConfigInput` in the same assembly. There is no copy of the rule, so it cannot drift. I traced the tested inputs:
  - `""`, `''`, `  ""  `, `" "` and `' '` are rejected.
  - `"`, `'`, `"a"`, `"""` and `"'` are accepted, and `A_value_with_a_quote_that_is_not_an_empty_pair_is_valid` also asserts that
    the parser reads each back as non-null.
- AC-13 and key printing. No file under `src/PodcastGenerator.Cli` or `Infrastructure` changed in this diff. `Saved <KEY>.`
  still carries the key name only, and the error messages still carry the path and exception type name only. The new code
  (`EnvFileEditor`, `ConfigInput`) is pure and does no I/O or logging.
- CLAUDE.md standards. Layering is unchanged (pure logic in Application, no HTTP, no new `new` for services). Async and
  `CancellationToken` signatures are unchanged. There is no Windows-only or Linux-only dependency, and
  `OrdinalIgnoreCase` and `Environment.NewLine` are portable. README is updated (AC-14): it now describes remove-and-append,
  the unique key, case-insensitive key names, and the quote rejection at both the prompt and `--set-config`, and it no
  longer says "updated in place". No test reads a real key or profile: the changed tests use `FakeFileSystem`, the temp-folder
  fixture or the pipeline harness, as in round 1.
- Tests moved to the new behavior. Comparing old and new expectations, each one has the same inputs with the expected text
  changed only by the removed line and the new last line, and the content assertions stay exact `Assert.Equal` on the whole
  file text. New coverage was added on top: the typed-spelling case in both directions, multiple duplicates in any case, the
  only-line file with `\r\n` and `\n`, the last line without an ending, the reported bug end to end (set with a lowercase key,
  then run without a prompt), the quotes at both entry points, and the parser rules across cases. The old parser test
  `The_key_name_is_case_sensitive` was replaced on purpose, and the design table says so. The tester's tests were
  changed only where they encoded the superseded behavior, and each carries a comment naming the test it replaces. In
  addition, the two assertions from round 1 finding 3 were added (`OPENROUTER_API_KEY=<value>` in `ConfigTests.cs`,
  `KeyAndRuntimeFolderTests.cs` and `ConfigIntegrationTests.cs`), so round 1 finding 3 is closed.
- Round 1 findings. Finding 2 (spelling on update) is resolved by decisions 1 and 2. Finding 3 is fixed. Finding 1 (no
  automated test for the hidden-typing path) is left as it was by the coding agent, and it is still non-blocking.

## Findings

1. NON-BLOCKING. The implemented quote rule is a superset of the spec's wording; the spec and the acceptance criteria were not
   widened to match.
   - Location: `src/PodcastGenerator.Application/Runtime/ConfigInput.cs:25` against `docs/specs/cli-set-config.md:23-24`
     and AC-4 and AC-9 (spec lines 72-75 and 87-90).
   - Evidence: the spec says a value "that is literally two quote characters (`""` or `''`)" is rejected. The code rejects
     that and also a value whose quoted content is only whitespace, such as `" "` or `'  '`, which the spec does not list
     (and which is not "whitespace-only", because it contains quote characters). The design records this openly in D-16, and
     `ConfigInputTests.A_value_that_is_only_a_pair_of_quotes_is_invalid_...` pins it.
   - Assessment of the user's intent: the user's stated goal is to reject an empty string, and the reason in the spec is that
     the parser "would read it back as empty". `" "` is read back as empty in the same way (`EnvFileParser.Unquote` trims
     inside the quotes, `EnvFileParser.cs:50`). Storing it would recreate the "saved but reported missing" problem this decision
     exists to remove, so the superset serves that intent and I do not treat it as a defect. It is still a difference between the spec
     text and the behavior.
   - Change requested: none in code. Once the user confirms (question 1), the spec wording for AC-4 and AC-9 (and the README
     sentence, which lists only `""` and `''`) should say "a value that is empty once the surrounding quotes are removed".
     If the user wants only the literal `""` and `''`, `ConfigInput.IsValidValue` would need a narrower check, and that would
     leave `" "` saved as a value the app reads as missing.

2. NON-BLOCKING. A stale comment still describes the old behavior.
   - Location: `tests/PodcastGenerator.UnitTests/Runtime/ConfigTests.cs:683`: "... and file creation and the in-place update are
     checked on a real disk".
   - Evidence: AC-3 as changed by the user decision says the line is removed and one is appended, and the test below the
     comment was renamed to `Set_removes_the_old_line_and_appends_the_new_one_on_disk_...`.
   - Change requested: reword to "the remove-and-append update". Comment only; no behavior effect.

## Questions for the user

1. Rejecting `" "` and `'  '` (finding 1). The literal ask was to reject `""` and `''`. The code also rejects a quoted
   string that holds only spaces, because the key file parser reads it back as empty, which matches your reason for the
   decision. Is that what you want? If yes, the spec and README wording should be widened to match. If you want only the
   literal two-quote strings rejected, say so and the coding agent narrows the check.

## Notes

- `CLAUDE.md` is intentionally not edited by agents and is now out of date for the user. Its Usage section still says the
  key file is something the user creates, still says "If no key is found, exit with a helpful, descriptive error ...", says
  nothing of `--set-config` or the prompt, and its parser rule list ("last duplicate wins" and so on) lacks the new
  case-insensitive key match and the rule that `--set-config` replaces (removes and re-adds) a key's lines, which are now
  facts about the app. This is not a code change request.
