#!/usr/bin/env bash
# Reports the newest GitHub Actions run and, when it is red, prints the failing
# steps' log lines ready to paste somewhere.
#
#   ci_status.sh            the newest run on any branch
#   ci_status.sh main       the newest run on that branch
#
# Exits non-zero when the run failed, so it also works in a chain.
set -euo pipefail

if ! command -v gh >/dev/null 2>&1; then
  printf '%s\n' 'ERROR: gh is required: brew install gh && gh auth login' >&2
  exit 2
fi
if ! command -v jq >/dev/null 2>&1; then
  printf '%s\n' 'ERROR: jq is required: brew install jq' >&2
  exit 2
fi

branch="${1:-}"
selector=(--limit 1 --json databaseId,status,conclusion,displayTitle,headBranch,createdAt)
# Not `[[ ... ]] && selector+=(...)`: under `set -e` a false test would end the
# script rather than skip the flag.
if [[ -n "$branch" ]]; then
  selector+=(--branch "$branch")
fi

run="$(gh run list "${selector[@]}")"
run_id="$(jq -r '.[0].databaseId // empty' <<<"$run")"
if [[ -z "$run_id" ]]; then
  printf 'No workflow run found%s.\n' "${branch:+ on $branch}"
  exit 0
fi

jq -r '.[0] | "\(.conclusion // .status)  ·  \(.displayTitle)  ·  [\(.headBranch)]  ·  \(.createdAt)"' <<<"$run"
printf -- '\n'
gh run view "$run_id" --json jobs \
  --jq '.jobs[] | "\(.conclusion // .status)\t\(.name)"' \
  | column -t -s "$(printf '\t')"

conclusion="$(jq -r '.[0].conclusion // ""' <<<"$run")"
if [[ -z "$conclusion" ]]; then
  printf '\nStill running — run this again when it finishes.\n'
  exit 0
fi
if [[ "$conclusion" == "success" ]]; then
  exit 0
fi

printf -- '\n--- failing steps ---\n'
gh run view "$run_id" --log-failed
exit 1
