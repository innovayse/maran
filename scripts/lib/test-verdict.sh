#!/usr/bin/env bash
# `maran test` — runs a stack's whole test suite and reports a VERDICT, which rules/testing.md
# defines as named failures plus a collected total, compared against a committed baseline. Never an
# exit code.
#
# Why this exists rather than "just run cargo test": the exit code has been wrong here in every
# direction, on one day. `dotnet test` exited 0 with sixteen failures. It exited 0 while a project
# whose build another agent had broken never ran at all — 1508 collected against a 1683 baseline —
# and 1138 against 1725 in a second instance. It returned 143 on a run that had completed 13 of 15
# projects because the kernel killed it under load. A `cargo` invocation and a `dotnet` invocation
# each exited 0 having run NOTHING: cargo was absent from PATH, and the bare-PATH dotnet was an 8.0
# snap asked to run net9.0 projects.
#
# Every one of those is silent to `$?` and loud in the numbers, so this command reads the numbers:
#
#   - per target (a cargo test binary or doc-test) and per project (a test dll), one row each;
#   - summed, and compared against `scripts/test-baseline.txt`;
#   - a target present in the baseline and ABSENT from the run is named as VANISHED and fails the
#     run. A broken test project does not fail the run, it disappears from it, and the projects that
#     survive then print `Passed!` over a smaller total. That is the most believable false pass
#     there is;
#   - a run with no test-result line at all is ABORTED, never a pass. "No tests found" is a failure.
#
# The baseline is recorded by a separate, deliberate command — `maran test --accept` — that a
# developer types and CI never runs, and the result is committed. A check that refreshes its own
# baseline enforces nothing.
#
# Usage:
#   maran test              every stack this machine can run
#   maran test rust         the Rust workspace only
#   maran test backend      the backend solution only
#   maran test [stack] --accept   record the current totals as the baseline (then commit it)
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"

usage() {
  echo "usage: maran test [rust|backend|all] [--accept]" >&2
  exit 2
}

which_stacks="all"
accept=0
while [ $# -gt 0 ]; do
  case "$1" in
    rust|backend|all) which_stacks="$1"; shift ;;
    --accept) accept=1; shift ;;
    -h|--help) usage ;;
    --filter|-p|--package)
      echo "REFUSED: $1 narrows the run, and this command's job is to compare a WHOLE suite" >&2
      echo "         against a baseline. A filtered total is not comparable to one." >&2
      exit 2
      ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done

case "$which_stacks" in
  all) stacks="rust backend" ;;
  *)   stacks="$which_stacks" ;;
esac

# A count taken while somebody's MUTANT is in the source is not a measurement of this repository —
# it is a measurement of their experiment, and it looks exactly like a regression. `maran test`
# takes no lock (it writes nothing), so it only PROBES: the answer comes from the kernel, not from
# a pid file, and a killed mutation run's lock is already gone by the time this asks.
tree_lock_probe "$root" || exit 1
# The other half of the same question: a mutation run that was KILLED holds no lock any more, but
# its mutant is still in the file. The pending record is what says so.
if ! tree_lock_refuse_pending "$root" readonly; then
  echo "         Nothing was measured. Recover with: maran mutate --recover" >&2
  exit 1
fi

baseline="$root/scripts/test-baseline.txt"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Collected across every stack and reported together at the end. Exiting at the first failing stack
# would make the verdict the exit status of whichever ran first, which is the habit this command
# exists to break.
findings=""
note_finding() {
  findings="$findings
  $1"
}

: >"$work/accepted.tsv"
grand_passed=0
grand_failed=0
grand_targets=0

for stack in $stacks; do
  echo "== $stack =="
  if ! suite_require_toolchain "$stack"; then
    note_finding "$stack: REFUSED — the toolchain cannot run this suite (see above)."
    echo
    continue
  fi
  # Proven able to see a planted failure before anything is measured — see suite_selftest_parsers.
  if ! suite_selftest_parsers "$stack"; then
    note_finding "$stack: REFUSED — the log parsers are blind (see the self-test above)."
    echo
    continue
  fi
  if ! suite_refuse_concurrency "$stack" "$root"; then
    note_finding "$stack: REFUSED — another build was running; this would not be a measurement."
    echo
    continue
  fi

  suite_run "$stack" "$root" "$work/$stack.log"
  if ! suite_recheck_concurrency "$stack" "$root"; then
    note_finding "$stack: CONTAMINATED — another build started mid-run; this is not a measurement."
  fi
  suite_parse "$stack" "$work/$stack.log" >"$work/$stack.tsv"
  suite_failures "$stack" "$work/$stack.log" >"$work/$stack.failures"

  targets="$(wc -l <"$work/$stack.tsv")"
  if [ "$targets" -eq 0 ]; then
    echo "ABORTED: the run produced no test-result line. Nothing was measured."
    tail -20 "$work/$stack.log"
    note_finding "$stack: ABORTED — no test-result line. 'No tests found' is a failure, not a pass."
    echo
    continue
  fi

  passed="$(awk -F'\t' '{p+=$2} END{print p+0}' "$work/$stack.tsv")"
  failed="$(awk -F'\t' '{f+=$3} END{print f+0}' "$work/$stack.tsv")"
  ignored="$(awk -F'\t' '{i+=$4} END{print i+0}' "$work/$stack.tsv")"
  grand_targets=$((grand_targets + targets))
  grand_passed=$((grand_passed + passed))
  grand_failed=$((grand_failed + failed))

  sed 's/^/  /' "$work/$stack.tsv"
  echo "  ----"
  echo "  $targets targets: $passed passed / $failed failed / $ignored ignored"

  # The totals and the named failures come from two parsers reading different lines; a run where
  # they disagree measured nothing, whichever of the two is wrong.
  if ! reconcile="$(suite_reconcile_failures "$failed" "$work/$stack.failures" "$stack" "$work/$stack.log")"; then
    echo "$reconcile" | sed 's/^/  /'
    note_finding "$stack: UNRECONCILED — the totals and the named failures disagree."
  fi
  if [ "$failed" -gt 0 ]; then
    echo "  failures:"
    sed 's/^/    /' "$work/$stack.failures"
    while IFS= read -r name; do
      [ -n "$name" ] && note_finding "$stack: FAILED $name"
    done <"$work/$stack.failures"
  fi

  { grep -E "^$stack	" "$baseline" 2>/dev/null || true; } | sed -E "s/^$stack	//" >"$work/$stack.baseline"
  # --accept does not compare: its whole job is to record what ran, and comparing first would make
  # the first recording impossible (every target is "new" against a baseline that does not exist yet)
  # while a later re-record would be refused by the very drift it is being run to accept. What still
  # gates --accept is everything that says the run was not a measurement: the toolchain, a concurrent
  # build, an empty run, and any failing test.
  if [ "$accept" = 1 ]; then
    :
  elif [ ! -s "$work/$stack.baseline" ]; then
    note_finding "$stack: no committed baseline — record one with 'maran test $stack --accept'."
  else
    while IFS= read -r finding; do
      [ -n "$finding" ] && note_finding "$stack: ${finding#FINDING: }"
    done < <(suite_compare "$work/$stack.baseline" "$work/$stack.tsv")
  fi

  sed "s/^/$stack	/" "$work/$stack.tsv" >>"$work/accepted.tsv"
  echo
done

if [ "$accept" = 1 ]; then
  if [ -n "$findings" ]; then
    echo "REFUSED to record a baseline from a run with findings:$findings" >&2
    exit 1
  fi
  # Only the stacks that ran are rewritten; a baseline for a stack this machine could not run is
  # kept, so recording the Rust totals on a workstation without dotnet does not silently delete
  # every backend project from the baseline and turn the next CI run green over half a suite.
  {
    echo "# Committed per-target test baseline. Recorded by 'maran test --accept'; commit the diff."
    echo "# stack<TAB>target<TAB>passed<TAB>failed<TAB>ignored"
    for stack in rust backend; do
      case " $stacks " in
        *" $stack "*) grep -E "^$stack	" "$work/accepted.tsv" || true ;;
        *)            grep -E "^$stack	" "$baseline" 2>/dev/null || true ;;
      esac
    done
  } >"$work/new-baseline.txt"
  mv "$work/new-baseline.txt" "$baseline"
  echo "recorded scripts/test-baseline.txt — review and commit it."
  exit 0
fi

echo "== verdict =="
echo "collected $grand_targets targets: $grand_passed passed / $grand_failed failed"
if [ -n "$findings" ]; then
  echo "TEST VERDICT: FAILED — findings:$findings"
  exit 1
fi
echo "TEST VERDICT: OK — every baselined target reported, no failures, no total dropped."
