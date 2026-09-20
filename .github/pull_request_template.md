## Summary
<!-- What changed and why, in 1-3 sentences. -->

## Linked work
<!-- Closes #123 (one keyword per issue) · Features: docs/specs/<slug>.md -->

## What changed
<!-- Bullets: layers/projects touched and the key design choice. -->

## How it was verified
<!-- Map each acceptance criterion (AC-n) or bug step to the test covering it. -->
- `dotnet build -warnaserror`:
- `dotnet test`:

## Risks and review focus
<!-- Where should the reviewer look hardest? Known limitations, follow-ups. "None" if none. -->

## Checklist
- [ ] `dotnet build -warnaserror` and `dotnet test` pass
- [ ] No test makes a live OpenRouter call (HTTP is faked; tests pass without network or an API key)
- [ ] No API keys or secrets in code, tests, logs or docs
- [ ] README updated if CLI args or setup steps changed
- [ ] Spec, design, test and review docs are committed under `docs/`
