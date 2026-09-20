---
name: deliver
description: Run the full delivery pipeline for a PodcastGenerator feature or bug fix - product-manager writes the spec, coding-agent implements, testing-agent and code-review-agent verify in parallel, project-manager opens the GitHub PR. Invoke as /deliver <feature description> or /deliver bug #<issue number> or /deliver bug <description>.
argument-hint: <feature description> | bug #<issue> | bug <description>
disable-model-invocation: true
---

You are the orchestrator of the delivery pipeline. Subagents cannot launch other subagents, so you (the main session)
run each agent with the Agent tool, pass file paths between them, and decide what happens next. The agents are defined
in `.claude/agents/`: `product-manager`, `coding-agent`, `testing-agent`, `code-review-agent`, `project-manager`.
Do not do their work yourself.

Arguments: `$ARGUMENTS`

## 0. Preflight

Run these and stop with the exact failure if any fails. Do not try to fix repo setup yourself.

- `git rev-parse --is-inside-work-tree` succeeds, and `git rev-parse --verify main` succeeds.
- `git status --porcelain` is empty and the current branch is `main` (a pipeline run starts from clean main).
- `gh auth status` succeeds and `git remote get-url origin` succeeds.

## 1. Classify and name

- If the arguments start with `bug`, it is a **bug**; otherwise a **feature**.
- If the arguments reference a GitHub issue (`#<n>`), record `ISSUE=<n>` for either kind of run and pass it to
  `coding-agent`, `testing-agent` and `project-manager` so their commits and the PR carry GitHub closing keywords
  (`Closes #<n>`). For a feature with an issue, read it with `gh issue view <n> --json number,title,body,labels` and use
  it as the feature description. No issue number means no keyword; never make one up.
- If the arguments name a GitHub project, record it as `PROJECT` and pass it to `project-manager` only.
- Pick `<slug>`: kebab-case, at most 5 words. For a bug with an issue number, `issue-<n>-<short-title>`; a bug
  without one, `bug-<short-title>`.

## 2. Intake

- **Feature**: launch `product-manager` with the description and `<slug>`. When it returns, read
  `docs/specs/<slug>.md`. If it lists `BLOCKING` open questions, ask the user (AskUserQuestion) and give the answers
  to the product-manager again to update the spec before continuing.
- **Bug with issue number**: `gh issue view <n> --json number,title,body,labels` and write the result as markdown to
  `docs/bugs/<slug>.md` (title, labels, body, issue number). No product-manager step.
- **Bug from text**: write the text to `docs/bugs/<slug>.md`. No product-manager step.

Intake files are left uncommitted; the coding agent creates the branch and they come along with its first commit
(tell it to include them).

## 3. Build

Launch `coding-agent` with: mode (feature or bug), `<slug>`, the spec or bug path, and "include the intake file in
your first commit". Record `BRANCH` and `CODE_HEAD` (the commit SHA) from its final message. If it reports that it
could not build or test, stop and show the user.

## 4. Verify (parallel)

In **one message**, launch both agents so they run in parallel. Give each the `<slug>`, `BRANCH`, `CODE_HEAD`, the
spec or bug path, the design path `docs/designs/<slug>.md`, and the round number `<n>` (starts at 1):

- `testing-agent`
- `code-review-agent`

Read the last line of each final message. It must be exactly `VERDICT: APPROVE` or `VERDICT: CHANGES_REQUESTED ...`.
A missing or malformed verdict counts as `CHANGES_REQUESTED` and you tell the user why.

## 5. Fix loop

If either returned `CHANGES_REQUESTED`:

1. Launch `coding-agent` in fix-round mode with the spec or bug path and every report path the verdict lines named.
   Update `CODE_HEAD` from its final message. (The tester may have added its own `test:` commit; that is expected.)
2. Increment `<n>` and repeat step 4 for **both** agents, since a fix can break what the other approved.
3. Stop after **3** fix rounds. Report to the user which findings are still open and ask how to proceed. Do not open a PR.

## 6. Pull request

When both returned `VERDICT: APPROVE`, launch `project-manager` with the mode, `<slug>`, `BRANCH`, the issue number
(if any), and the paths of the spec or bug report, design, latest review and testing report.

## 7. Report

Tell the user the PR URL (or what stopped the run), the branch, the number of fix rounds, and the paths of the
artifacts in `docs/`. Keep it short.
