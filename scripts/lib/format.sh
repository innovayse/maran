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
#
# THREE ANSWERS, NOT TWO. This is a gate, and a refusal is not a finding:
#
#   FORMAT VERDICT: OK            exit 0   every requested stack was checked and is formatted
#   FORMAT VERDICT: FAILED        exit 1   every requested stack was checked and one is not
#   FORMAT VERDICT: DID NOT RUN   exit 2   nothing was checked — a peer holds the tree lock, a
#                                          killed mutation run left a mutant behind, or the tool
#                                          this stack is formatted by is not installed
#
# Measured here before the third answer existed, with a real holder on the tree lock:
# `maran format --check agent` printed ZERO BYTES on stdout and returned 1 — the same status, and a
# less informative output, than a stack with a genuine formatting finding. `.github/workflows/
# backend.yml` scored that step by status alone, so a peer holding the lock was recorded as a
# formatting defect on the branch, and had that refusal ever exited 0 it would have been recorded as
# a pass over a tree nothing had looked at. The absent-toolchain path failed the other way round: no
# `dotnet` on PATH printed `FORMAT FAILED — backend`, a finding about source that was never read.
# The mechanism and the argument for the number 2 are in scripts/lib/suite.sh beside
# `suite_did_not_run`; `maran lock selftest` is what keeps this command honest about it.
#
# The OK verdict belongs to --check ALONE. Writing mode ends with `formatting complete`, which no
# gate matches on purpose: a CI step that lost its `--check` would then rewrite the tree and find
# nothing to gate on, rather than going green over a run that had silently edited the source.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"

# For `suite_did_not_run`, `$SUITE_STATUS_DID_NOT_RUN` and `suite_require_toolchain`. This command
# parses no test log and counts nothing, but it refuses for exactly the reasons `maran test` and
# `maran agent` refuse, and one vocabulary across the harness is worth more to a reader than a
# locally invented one.
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

# format_did_not_run: says which of the three answers this is, on STDOUT, and exits with the status
# that carries it. Stdout deliberately: the refusals it follows print to stderr, and a caller
# keeping only stdout was handed a zero-byte file — the shape that gets read as "no findings".
format_did_not_run() {
  suite_did_not_run "FORMAT VERDICT" "$*"
  exit "$SUITE_STATUS_DID_NOT_RUN"
}

check_only=0
if [ "${1:-}" = "--check" ]; then
  check_only=1
  shift
fi

target="${1:-all}"
case "$target" in
  all|backend|frontend|agent) ;;
  *)
    # 2, not 1: nothing was checked, and 1 is this harness's status for a run that produced a
    # verdict. No verdict LINE here, matching `maran test`'s own usage error — a command invoked
    # with a stack that does not exist has not refused to measure, it was never asked coherently.
    echo "usage: maran format [--check] [backend|frontend|agent]" >&2
    exit 2
    ;;
esac

# THE TOOLCHAIN, before anything is looked at. rules/testing.md: "a gate you cannot run is not a
# gate that passed" — and this command failed that in the direction that manufactures findings
# rather than confidence. Measured with `dotnet` off PATH:
#
#   $ maran format --check backend
#   backend  (dotnet format)
#   FORMAT FAILED — backend            <- exit 1, a claim about source nothing had read
#
# `dotnet: command not found` is not a formatting defect on this branch; it is this run not
# happening. Every requested stack is proved BEFORE the first one is checked, and a missing tool
# refuses the whole invocation rather than quietly covering two stacks out of three: partial
# coverage reported under a whole-tree verdict is the same defect wearing a different hat.
require_stack_toolchain() {
  case "$1" in
    backend)
      # `dotnet format` is a first-party SDK command, and the .editorconfig it applies is read by
      # the same SDK the baseline was recorded on, so the version check in suite.sh applies here too.
      suite_require_toolchain backend ||
        format_did_not_run "the .NET SDK this stack is formatted by is absent or too old (named above)."
      ;;
    frontend)
      # Not `suite_require_toolchain spa`: that one also demands Playwright and a chromium, which
      # linting needs none of, and a refusal that names a browser for a lint run would teach the
      # reader to ignore refusals. What oxlint and ESLint actually need is node and an installed
      # frontend/node_modules; without the second, `npm run lint` exits non-zero having linted
      # nothing, which today reads as `FORMAT FAILED — frontend`.
      command -v node >/dev/null 2>&1 || {
        echo "REFUSED: node is not on PATH — oxlint and ESLint cannot run, so nothing would be" >&2
        echo "         linted. source scripts/dev first, and see: maran check" >&2
        format_did_not_run "node, which this stack is linted by, is not on PATH."
      }
      [ -d "$root/frontend/node_modules" ] || {
        echo "REFUSED: frontend/node_modules is absent — the linters are not installed, so no file" >&2
        echo "         would be read. Fix: (cd frontend && npm ci)" >&2
        format_did_not_run "the frontend linters are not installed (named above)."
      }
      ;;
    agent)
      # cargo AND rustfmt. `cargo fmt` is a separate rustup component: on a toolchain without it
      # cargo prints `no such command: fmt` and exits non-zero, which is indistinguishable, at this
      # script's old exit paths, from a file that needed reformatting. The C linker `suite.sh`
      # demands for `cargo test` is deliberately NOT required here — rustfmt parses source and links
      # nothing, so refusing on `cc` would be a refusal this command could have run past.
      command -v cargo >/dev/null 2>&1 || {
        echo "REFUSED: cargo is not on PATH — this run would format nothing." >&2
        echo "         source scripts/dev first, and see: maran check" >&2
        format_did_not_run "cargo, which this stack is formatted by, is not on PATH."
      }
      (cd "$root/agent" && cargo fmt --version) >/dev/null 2>&1 || {
        echo "REFUSED: 'cargo fmt' is unavailable — rustfmt is a rustup component and this" >&2
        echo "         toolchain does not carry it. Fix: rustup component add rustfmt" >&2
        format_did_not_run "rustfmt, which this stack is formatted by, is not installed."
      }
      ;;
  esac
}

if [ "$target" = "all" ]; then
  require_stack_toolchain backend
  require_stack_toolchain frontend
  require_stack_toolchain agent
else
  require_stack_toolchain "$target"
fi

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
  tree_lock_probe "$root" ||
    format_did_not_run "a mutation run holds this tree's write lock (named above)."
  if ! tree_lock_refuse_pending "$root" readonly; then
    echo "         Nothing was checked. Recover with: maran mutate --recover" >&2
    format_did_not_run "a killed mutation run left its mutant in this tree (named above)."
  fi
else
  # Wait 0 — refuse at once and name the holder. A silent block is indistinguishable from a hung
  # tool, and this repository has already paid 855 seconds for that mistake once.
  tree_lock_acquire "$root" 0 "format $target" ||
    format_did_not_run "the tree lock is held, so nothing was formatted (holder named above)."
  trap tree_lock_release EXIT INT TERM
  # `readonly`: this command is a writer, but it is not the mutation harness and must never tidy
  # away another run's only backup.
  if ! tree_lock_refuse_pending "$root" readonly; then
    echo "         Nothing was formatted. Recover with: maran mutate --recover" >&2
    format_did_not_run "a killed mutation run left its mutant in this tree (named above)."
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

# THE VERDICT. Named stacks plus the whole of what was asked, never an exit code
# (rules/testing.md). The prefix is the one every other gate in this harness prints, so the three
# answers here are the same three words a reader already knows from `TEST VERDICT`,
# `POLYGON VERDICT` and `AGENT VERDICT`.
if [ -n "$failed_stacks" ]; then
  echo "FORMAT VERDICT: FAILED — unformatted:$failed_stacks (of: $target)"
  exit 1
fi

# The message says which of the two things happened. "formatting complete" was printed by
# --check too, where nothing had been formatted (rules/architecture.md: a message that describes
# what the code did not do is a defect of the same severity as the behaviour).
#
# And writing mode deliberately does NOT print the OK verdict: a CI step that lost its `--check`
# would then have rewritten the tree, and the gate that scores it must find no line to pass on
# rather than report a clean check it never performed.
if [ "$check_only" -eq 1 ]; then
  echo "FORMAT VERDICT: OK — every requested stack ($target) is already formatted; nothing was written"
else
  echo "formatting complete — $target rewritten in place; this was a WRITE, not a check"
fi
