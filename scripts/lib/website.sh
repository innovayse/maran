#!/usr/bin/env bash
# The public website's gate: lint, typecheck, build, tests — every one of them, every time.
#
# It is the same shape the other lanes use (rules/testing.md): every sub-check runs to completion,
# its verdict is printed, and the command's answer is a PRINTED verdict line plus a collected
# total. A run that stopped at the first failure looks exactly like a run that finished, and the
# engineer reading it cannot tell which checks never got to speak.
#
# Usage:
#   maran website            run the whole gate
#   maran website lint       run one step (lint|typecheck|build|test)
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
website="$root/website"

# The site is its own repository — gitlab.com/innovayse/maran-website — and this repository ignores
# `website/`. The lane stays because a developer who keeps the two checkouts side by side gets one
# command for both; it says where the site is rather than failing with an npm error when they do
# not.
if [ ! -d "$website" ]; then
  echo "website: not in this checkout. The site is its own repository:" >&2
  echo "  git clone git@gitlab.com:innovayse/maran-website.git website" >&2
  exit 1
fi

if [ ! -d "$website/node_modules" ]; then
  echo "website: dependencies are not installed — run 'npm ci' in website/ first" >&2
  exit 1
fi

# Each row: the step's name and the npm script it runs.
steps=(
  "lint:lint"
  "typecheck:typecheck"
  "build:build"
  "test:test"
)

requested="${1:-all}"
declared=0
ran=0
passed=0
failed_names=()

for row in "${steps[@]}"; do
  name="${row%%:*}"
  script="${row##*:}"

  if [ "$requested" != "all" ] && [ "$requested" != "$name" ]; then
    continue
  fi

  declared=$((declared + 1))
done

if [ "$declared" -eq 0 ]; then
  echo "website: unknown step '$requested' — expected one of lint, typecheck, build, test" >&2
  exit 1
fi

for row in "${steps[@]}"; do
  name="${row%%:*}"
  script="${row##*:}"

  if [ "$requested" != "all" ] && [ "$requested" != "$name" ]; then
    continue
  fi

  echo
  echo "=== website: $name ==="

  ran=$((ran + 1))

  if (cd "$website" && npm run --silent "$script"); then
    passed=$((passed + 1))
    echo "website: $name OK"
  else
    failed_names+=("$name")
    echo "website: $name FAILED"
  fi
done

failed=${#failed_names[@]}
verdict="OK"
[ "$failed" -eq 0 ] || verdict="FAIL"

echo
echo "WEBSITE VERDICT: $verdict — $declared steps declared, $ran run, $passed passed, $failed failed"

for name in "${failed_names[@]:-}"; do
  [ -n "$name" ] && echo "  FAILED: $name"
done

[ "$failed" -eq 0 ]
