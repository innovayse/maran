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

# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"

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

# The tree lock (scripts/lib/tree-lock.sh), on both sides of this command's two behaviours.
#
# Writing mode is the only unlocked WRITER of source this repository had. `dotnet format`,
# `eslint --fix` and `cargo fmt --all` rewrite files in place across the whole tree, and a mutation
# run's subject is a line in one of those files. Two concrete wrong answers follow from running the
# two at once, and neither announces itself: the formatter rewrites the mutant line while the suite
# that is scoring it is mid-build, so the verdict describes source that existed for a moment; and
# the mutation harness then restores its byte-exact backup over the file, silently discarding
# whatever the formatter wrote to the REST of that file. So this takes the same exclusive lock
# `maran mutate` takes — one tree, one writer.
#
# --check writes nothing, so it takes no lock and only PROBES, exactly as `maran test` does: its
# output is a verdict about this tree's source, and "these files are unformatted" is a claim nobody
# owns when the offending line is somebody's in-flight mutant.
if [ "$check_only" -eq 1 ]; then
  tree_lock_probe "$root" || exit 1
  if ! tree_lock_refuse_pending "$root" readonly; then
    echo "         Nothing was checked. Recover with: maran mutate --recover" >&2
    exit 1
  fi
else
  # Wait 0 — refuse at once and name the holder. A silent block is indistinguishable from a hung
  # tool, and this repository has already paid 855 seconds for that mistake once.
  tree_lock_acquire "$root" 0 "format $target" || exit 1
  trap tree_lock_release EXIT INT TERM
  # `readonly`: this command is a writer, but it is not the mutation harness and must never tidy
  # away another run's only backup.
  if ! tree_lock_refuse_pending "$root" readonly; then
    echo "         Nothing was formatted. Recover with: maran mutate --recover" >&2
    exit 1
  fi
fi

# Every requested stack runs, and the failures are named together at the end. Under `set -e` these
# three blocks were a chain: `maran format --check` reported the first stack that had something to
# say and never asked the other two, so a developer fixed one nit per round trip and never learnt
# the size of the job. rules/testing.md: a verdict is named failures plus a collected total, never
# an exit code.
failed_stacks=""
note_failure() {
  failed_stacks="$failed_stacks $1"
}

if [ "$target" = "all" ] || [ "$target" = "backend" ]; then
echo "backend  (dotnet format)"
# IDE0005 is excluded here and enforced by the BUILD instead (backend/.editorconfig sets it to
# error, with EnforceCodeStyleInBuild). `dotnet format` evaluates projects without running the
# resx source generator, so it cannot see the `ErrorMessages` class each module generates from
# Resources/ErrorMessages.resx — and reports the `using` that reaches it as unnecessary, on every
# file that uses a localized error message and would not compile without it. Excluding it loses no
# coverage: the build sees the generated code, and a genuinely unused directive still fails there.
#
# No file count is written here on purpose. The number is one per file that reaches an
# ErrorMessages class, so it moves with every slice added anywhere in the backend, and a figure
# frozen in a comment is a claim that quietly stops being true (rules/architecture.md). Measure it
# instead — drop the exclusion below and read the findings off the run:
#   dotnet format backend/Maran.sln --verify-no-changes --verbosity normal \
#     | grep -oP '^\S+\.cs(?=\(.*IDE0005)' | sort -u | wc -l
# It stood at 91 files on 2026-09-07 (the comment had said 32), out of 92 files that run wanted to
# touch at all; the 92nd is a generated migration it wants to change for an unrelated reason, and
# the build — the gate that counts — passes it.
format_args=(--exclude-diagnostics IDE0005 --verbosity minimal)
if [ "$check_only" -eq 1 ]; then
  dotnet format "$root/backend/Maran.sln" --verify-no-changes "${format_args[@]}" || note_failure backend
else
  dotnet format "$root/backend/Maran.sln" "${format_args[@]}" || note_failure backend
fi
fi

if [ "$target" = "all" ] || [ "$target" = "frontend" ]; then
echo "frontend (oxlint + eslint)"
if [ "$check_only" -eq 1 ]; then
  (cd "$root/frontend" && npm run lint) || note_failure frontend
else
  (cd "$root/frontend" && npm run lint:fix) || note_failure frontend
fi
fi

if [ "$target" = "all" ] || [ "$target" = "agent" ]; then
echo "agent    (cargo fmt)"
if [ "$check_only" -eq 1 ]; then
  (cd "$root/agent" && cargo fmt --all -- --check) || note_failure agent
else
  (cd "$root/agent" && cargo fmt --all) || note_failure agent
fi
fi

if [ -n "$failed_stacks" ]; then
  echo "FORMAT FAILED —$failed_stacks"
  exit 1
fi

# The message says which of the two things happened. "formatting complete" was printed by
# --check too, where nothing had been formatted (rules/architecture.md: a message that describes
# what the code did not do is a defect of the same severity as the behaviour).
if [ "$check_only" -eq 1 ]; then
  echo "FORMAT-OK — every requested stack is already formatted; nothing was written"
else
  echo "formatting complete"
fi
