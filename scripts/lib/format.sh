#!/usr/bin/env bash
# Formats every language in the repository against its own rule set: `dotnet format` applies the
# .editorconfig style to the backend, oxlint and ESLint apply the frontend laws (rules/vue.md), and
# `cargo fmt` applies rustfmt to the agent. Run it before asking for a review; scripts/maran check
# and CI verify the same rules without changing files.
#
# Usage:
#   scripts/maran format                   format everything in place
#   scripts/maran format --check           report what is unformatted, exit non-zero, change nothing
#   scripts/maran format --check backend   just one language
#
# The target argument exists for CI, where the jobs are split by language: the backend job has the
# .NET SDK and neither npm nor cargo, so a format step that always ran all three would fail on a
# missing tool rather than on unformatted code.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

check_only=0
if [ "${1:-}" = "--check" ]; then
  check_only=1
  shift
fi

target="${1:-all}"
case "$target" in
  all|backend|frontend|agent) ;;
  *)
    echo "usage: maran format [--check] [backend|frontend|agent]" >&2
    exit 1
    ;;
esac

if [ "$target" = "all" ] || [ "$target" = "backend" ]; then
echo "backend  (dotnet format)"
# IDE0005 is excluded here and enforced by the BUILD instead (backend/.editorconfig sets it to
# error, with EnforceCodeStyleInBuild). `dotnet format` evaluates projects without running the
# resx source generator, so it cannot see the `ErrorMessages` class each module generates from
# Resources/ErrorMessages.resx — and reports the `using` that reaches it as unnecessary, on 32
# files that do not compile without it. Excluding it loses no coverage: the build sees the
# generated code, and a genuinely unused directive still fails there.
format_args=(--exclude-diagnostics IDE0005 --verbosity minimal)
if [ "$check_only" -eq 1 ]; then
  dotnet format "$root/backend/Maran.sln" --verify-no-changes "${format_args[@]}"
else
  dotnet format "$root/backend/Maran.sln" "${format_args[@]}"
fi
fi

if [ "$target" = "all" ] || [ "$target" = "frontend" ]; then
echo "frontend (oxlint + eslint)"
if [ "$check_only" -eq 1 ]; then
  (cd "$root/frontend" && npm run lint)
else
  (cd "$root/frontend" && npm run lint:fix)
fi
fi

if [ "$target" = "all" ] || [ "$target" = "agent" ]; then
echo "agent    (cargo fmt)"
if [ "$check_only" -eq 1 ]; then
  (cd "$root/agent" && cargo fmt --all -- --check)
else
  (cd "$root/agent" && cargo fmt --all)
fi
fi

echo "formatting complete"
