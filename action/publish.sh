#!/usr/bin/env bash
#
# Publishes a QueryGuard Markdown report to the job summary and, on a pull request, to a sticky
# comment.
#
# "Sticky" means one comment that gets edited, found by a hidden marker. A new comment per push turns
# a ten-commit branch into ten identical comments, which is how a bot earns being muted.
#
# Deliberately never fails the build for its own reasons. A diagnostics tool that breaks CI because it
# could not post a comment has cost more than it delivered: a missing token or a fork's read-only
# token warns and exits 0. The one exception is fail-on-missing, which the caller opts into.

set -uo pipefail

SUMMARY_PATH="${QG_SUMMARY_PATH:-artifacts/queryguard/summary.md}"
TITLE="${QG_TITLE:-QueryGuard}"
SHOULD_COMMENT="${QG_COMMENT:-true}"
FAIL_ON_MISSING="${QG_FAIL_ON_MISSING:-false}"

# The marker is an HTML comment, so it is invisible in the rendered comment but findable in its body.
# Keyed on the title so two QueryGuard runs in one workflow, say unit and integration, can each own
# a comment instead of overwriting each other.
MARKER="<!-- queryguard:${TITLE} -->"

# The glob is expanded here rather than by the caller, so `summary-path` accepts a pattern without the
# workflow needing a shell of its own.
#
# Each candidate is tested with -f rather than trusted. `printf '%s\n' <unmatched-glob>` under nullglob
# receives no arguments and still prints one empty line, so the obvious one-liner produced an array of
# length 1 containing "", which meant a missing file took the "empty report" branch and fail-on-missing
# never fired. Found by testing the flag rather than by reading the code.
#
# Unquoted on purpose, to let the shell expand the pattern. A path containing spaces therefore has to
# be passed without a glob.
shopt -s nullglob
candidates=(${SUMMARY_PATH})
shopt -u nullglob

files=()
for candidate in "${candidates[@]}"; do
  if [ -n "$candidate" ] && [ -f "$candidate" ]; then
    files+=("$candidate")
  fi
done

if [ "${#files[@]}" -gt 1 ]; then
  mapfile -t files < <(printf '%s\n' "${files[@]}" | sort)
fi

if [ "${#files[@]}" -eq 0 ]; then
  message="QueryGuard found no report at '${SUMMARY_PATH}'."

  if [ "$FAIL_ON_MISSING" = "true" ]; then
    echo "::error::${message} Set fail-on-missing: false if an empty run is acceptable."
    exit 1
  fi

  # A notice rather than a warning: a run with nothing to measure is a normal outcome, and a yellow
  # annotation on every such run trains people to ignore the annotations that matter.
  echo "::notice::${message} Nothing to publish."
  exit 0
fi

body="$(cat "${files[@]}")"

if [ -z "${body//[[:space:]]/}" ]; then
  echo "::notice::QueryGuard report at '${SUMMARY_PATH}' is empty. Nothing to publish."
  exit 0
fi

# The job summary always gets it, comment or not: it needs no token and works on a fork.
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  printf '%s\n' "$body" >> "$GITHUB_STEP_SUMMARY"
  echo "Published to the job summary."
fi

if [ "$SHOULD_COMMENT" != "true" ]; then
  exit 0
fi

if [ "${QG_EVENT:-}" != "pull_request" ] && [ "${QG_EVENT:-}" != "pull_request_target" ]; then
  echo "::notice::Not a pull request event, so no comment was posted."
  exit 0
fi

if [ -z "${QG_PR:-}" ]; then
  echo "::notice::No pull request number available, so no comment was posted."
  exit 0
fi

if [ -z "${GH_TOKEN:-}" ]; then
  echo "::warning::No token available, so no comment was posted. Grant 'pull-requests: write'."
  exit 0
fi

comment_body="${MARKER}
${body}"

# Look for the existing comment before deciding to create one. Paginated, because a long-lived pull
# request can easily exceed one page and a missed marker means a duplicate comment.
existing_id="$(
  gh api --paginate "repos/${QG_REPO}/issues/${QG_PR}/comments" \
    --jq "map(select(.body | contains(\"${MARKER}\"))) | last | .id" 2>/dev/null
)"

if [ -n "$existing_id" ] && [ "$existing_id" != "null" ]; then
  if gh api -X PATCH "repos/${QG_REPO}/issues/comments/${existing_id}" \
      -f body="$comment_body" >/dev/null 2>&1; then
    echo "Updated existing comment ${existing_id}."
    exit 0
  fi

  echo "::warning::Could not update comment ${existing_id}. Leaving it as it was."
  exit 0
fi

if gh api -X POST "repos/${QG_REPO}/issues/${QG_PR}/comments" \
    -f body="$comment_body" >/dev/null 2>&1; then
  echo "Posted a new comment."
  exit 0
fi

# The usual cause is a pull request from a fork, where GITHUB_TOKEN is read-only by design. The job
# summary above still carries the report, so the information is not lost.
echo "::warning::Could not post a comment. On a fork the token is read-only; the job summary still has the report."
exit 0
