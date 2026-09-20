#!/usr/bin/env bash
# Decides whether a change touches anything that needs a build, test or release.
#
# Input (stdin): changed file paths separated by NUL bytes, as printed by `git diff --name-only -z`.
# Output (stdout): one line, `code=true` or `code=false`.
#   code=false  every changed path is in the ignore list, so there is nothing to build, test or release.
#   code=true   at least one changed path is outside the ignore list, or no path was given (fail safe: build).
#
# The ignore list (CLAUDE.md, "CI/CD"): the directories docs/, .docs/, .github/, .claude/ and agent-memory/ and the file
# .gitignore, all at the repository root, and any .md file at any depth.
set -euo pipefail

shopt -s nocasematch

seen_any=false
code=false

while IFS= read -r -d '' path; do
  seen_any=true
  case "$path" in
    docs/* | .docs/* | .github/* | .claude/* | agent-memory/* | .gitignore | *.md)
      ;;
    *)
      code=true
      ;;
  esac
done

if [[ "$seen_any" == false ]]; then
  code=true
fi

echo "code=$code"
