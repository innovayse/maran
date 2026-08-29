#!/usr/bin/env bash
# Formats every language in the repository against its own rule set: `dotnet format` applies the
# .editorconfig style to the backend, oxlint and ESLint apply the frontend laws (rules/vue.md), and
# `cargo fmt` applies rustfmt to the agent. Run it before asking for a review; scripts/preflight.sh
# and CI verify the same rules without changing files.
#
# Usage:
#   scripts/format.sh           format everything in place
#   scripts/format.sh --check   report what is unformatted and exit non-zero, changing nothing
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev-env.sh"

check_only=0
if [ "${1:-}" = "--check" ]; then
  check_only=1
fi

echo "backend  (dotnet format)"
if [ "$check_only" -eq 1 ]; then
  dotnet format "$root/backend/Maran.sln" --verify-no-changes --verbosity minimal
else
  dotnet format "$root/backend/Maran.sln" --verbosity minimal
fi

echo "frontend (oxlint + eslint)"
if [ "$check_only" -eq 1 ]; then
  (cd "$root/frontend" && npm run lint)
else
  (cd "$root/frontend" && npm run lint:fix)
fi

echo "agent    (cargo fmt)"
if [ "$check_only" -eq 1 ]; then
  (cd "$root/agent" && cargo fmt --all -- --check)
else
  (cd "$root/agent" && cargo fmt --all)
fi

echo "formatting complete"
