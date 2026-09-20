#!/usr/bin/env bash
# Works out whether a merge to main publishes a release, and which version.
#
# Input (environment):
#   COMMIT_SUBJECT  the subject of the squash commit (the pull request title, e.g. "feat(cli): add a flag (#12)")
#   COMMIT_BODY     the body of the squash commit
#   EXISTING_TAGS   the repository's tags that start with "v", one per line (may be empty)
# Output (stdout): `bump=<major|minor|patch|none>` and, unless the bump is none, `version=<MAJOR.MINOR.PATCH>`.
# Exit code 1, with a message naming the tag, when a "v*" tag is not vMAJOR.MINOR.PATCH.
#
# Rules (CLAUDE.md "Source Control"; Conventional Commits 1.0.0 https://www.conventionalcommits.org/en/v1.0.0/;
# Semantic Versioning 2.0.0 https://semver.org/):
#   "type(scope)!: description" or a "BREAKING CHANGE:" footer  -> major
#   "feat: ..."                                                 -> minor
#   "fix: ..."                                                  -> patch
#   anything else (refactor, test, chore, ci, docs, ...)         -> none
# The type is matched case-insensitively, as Conventional Commits requires. The first release is 1.0.0 and applies no bump.
set -euo pipefail

subject="${COMMIT_SUBJECT:-}"
body="${COMMIT_BODY:-}"
tags="${EXISTING_TAGS:-}"

bump=none
subject_pattern='^([A-Za-z]+)(\([^()]+\))?(!)?: .+'
if [[ "$subject" =~ $subject_pattern ]]; then
  type="$(printf '%s' "${BASH_REMATCH[1]}" | tr '[:upper:]' '[:lower:]')"
  breaking_mark="${BASH_REMATCH[3]}"
  # A footer line such as "BREAKING CHANGE: ..." (or its synonym "BREAKING-CHANGE:"). GitHub's default squash message
  # lists the pull request's commits, so leading spaces or a "* " bullet are tolerated.
  if [[ -n "$breaking_mark" ]] || printf '%s\n' "$body" | grep -Eq '^[[:space:]*-]*BREAKING[ -]CHANGE:'; then
    bump=major
  elif [[ "$type" == "feat" ]]; then
    bump=minor
  elif [[ "$type" == "fix" ]]; then
    bump=patch
  fi
fi

# The latest tag is the highest one by Semantic Versioning precedence. For plain MAJOR.MINOR.PATCH tags that is a
# numeric comparison of the three parts. Every v* tag is checked first, whether or not this merge releases: a v* tag that is
# not vMAJOR.MINOR.PATCH fails the run with a message naming the tag (AC-69, "fails the workflow").
tag_pattern='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
have_latest=false
latest_major=0
latest_minor=0
latest_patch=0
while IFS= read -r tag; do
  [[ -z "$tag" ]] && continue
  if ! [[ "$tag" =~ $tag_pattern ]]; then
    echo "The tag '$tag' starts with 'v' but is not vMAJOR.MINOR.PATCH. Fix or delete it, then run the release again." >&2
    exit 1
  fi
  major="${BASH_REMATCH[1]}"
  minor="${BASH_REMATCH[2]}"
  patch="${BASH_REMATCH[3]}"
  if [[ "$have_latest" == false ]] \
    || (( major > latest_major )) \
    || (( major == latest_major && minor > latest_minor )) \
    || (( major == latest_major && minor == latest_minor && patch > latest_patch )); then
    have_latest=true
    latest_major=$major
    latest_minor=$minor
    latest_patch=$patch
  fi
done <<< "$tags"

echo "bump=$bump"
if [[ "$bump" == none ]]; then
  exit 0
fi

if [[ "$have_latest" == false ]]; then
  version="1.0.0"
else
  case "$bump" in
    major) version="$((latest_major + 1)).0.0" ;;
    minor) version="${latest_major}.$((latest_minor + 1)).0" ;;
    patch) version="${latest_major}.${latest_minor}.$((latest_patch + 1))" ;;
  esac
fi

echo "version=$version"
