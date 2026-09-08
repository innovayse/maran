#!/usr/bin/env bash
# `maran polygon verify` — scores a polygon run that SOMETHING ELSE executed, from the logs it left.
#
# Why this command exists, and why it does not run the containers itself.
#
# `scripts/lib/polygon.sh` grew five axes that between them refuse the ways a container reports
# nothing as if it were something: a suite that never started, a suite that started and never
# printed a `test result:` line, a run that executed zero tests, a docker status that is not a test
# result, and — the fifth, added last — a suite that ran FEWER (or more) tests than
# `scripts/test-baseline.txt` records for it. All five were witnessed refusing and accepting on both
# families. And none of them applied in `.github/workflows/agent.yml`, which runs the polygon on a
# schedule, never sources that library, and reads the docker EXIT CODE as its verdict. Measured in
# this repository, all on one day: a `docker stop` gave **exit 0 for zero tests executed**; a
# container that executed nothing exited 0 printing "Finished" and naming a suite executable; a
# `cargo` invocation exited 0 having run nothing. The one lane whose totals nobody reads was the one
# lane with no check on its totals.
#
# The obvious repair — have the workflow call the library and let `polygon_run` do the running — is
# the wrong one, and this file is the shape that was chosen instead. `polygon_run` runs every suite
# in ONE `--privileged` container, deliberately: its subject is a mutant, and a capability split
# there would mean a hard-coded group membership, which is what once left four suites unrun. CI's
# subject is the runner, and CI splits the eleven suites across three steps by the capability each
# group needs, so that a suite started without its capabilities fails as a runner mistake rather
# than as `JailFailed` — which reads as a code defect. Both splits are right for their own lane.
# Routing CI through `polygon_run` would discard CI's, and would additionally make CI depend on
# `polygon_ensure_image`'s fingerprint tagging, which on a fresh runner never hits and would simply
# build each image a second time.
#
# So the RUNNING stays where it is, and only the SCORING moves. That works because scoring is
# portable in a way running is not: it needs the container's stdout, the committed baseline, and the
# tree — no docker, no root, no capabilities, no images. This command takes the logs the workflow
# tees out of its three steps, discovers the suites from the tree exactly as every other polygon
# consumer does, and hands the whole thing to the same `polygon_verify_ran` the mutation harness
# uses. One implementation, two callers, no second copy of the rule.
#
# A second thing falls out of it, unasked. `maran structure` check 19 requires every discovered
# polygon suite to be named in a `docker/README.md` run command — but nothing checks the WORKFLOW,
# so a `*_on_a_real_host.rs` added without an edit to `agent.yml` runs nowhere in CI and says
# nothing. This command derives its suite list from the tree and requires each of them to have
# spoken in the logs, so a suite the workflow forgot to name is now a red CI job that names it.
#
# Usage:
#   maran polygon verify [--statuses FILE] LOG [LOG ...]
#
#   LOG          a file holding the stdout+stderr of a polygon `docker run`. Pass one per step; they
#                are concatenated in the order given, which is how the eleven suites of a
#                capability-split run become one observation.
#   --statuses   a file of exit statuses, one per line, one per LOG. Optional, and reported rather
#                than believed: see aggregate_status below for what is done with it and why.
#
# Exit: 0 only when every axis holds AND no test failed. Anything else is 1. The OUTPUT is the
# verdict — named failures and totals — and the exit status exists only so that a CI job goes red.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
. "$root/scripts/lib/suite.sh"
. "$root/scripts/lib/polygon.sh"
. "$root/scripts/lib/tree-lock.sh"

# usage: prints the calling convention and refuses. A command invoked wrongly must not proceed to
# score something: a run with no logs would find no suites, and "found nothing" has to be a failure
# here for the same reason "no tests found" is a failure everywhere else in this repository.
usage() {
  cat >&2 <<'USAGE'
usage: maran polygon verify [--statuses FILE] LOG [LOG ...]

  Scores an already-executed polygon run from its container logs, against the suites discovered in
  the tree and the counts committed in scripts/test-baseline.txt. Runs nothing itself.

  --statuses FILE   exit statuses of the runs that produced the logs, one per line
USAGE
  exit 2
}

# aggregate_status: reduces the per-step exit statuses to the single one polygon_verify_ran reads.
#
# The rule, and it matters which way round it goes: a status that is NOT a test result outranks one
# that is. 0 and 101 are cargo's two honest answers (all green, some test failed); 124, 137 and 143
# are a timeout or a kill, and every other value is something that is not a score at all. So the
# worst status wins, because a run where one step was killed has measured less than it printed, and
# the axis that must fire is the one about the kill.
#
# It is reported, never believed. The observation is the result lines; this only decides which
# sentence polygon_verify_ran prints about the container. A missing or empty statuses file yields 0,
# which is the permissive answer — deliberately, because letting the absence of this file refuse a
# run would put the verdict back on an exit code, which is the whole defect.
aggregate_status() {
  local file="${1:-}" worst=0 line
  [ -n "$file" ] && [ -r "$file" ] || { echo 0; return 0; }
  while IFS= read -r line; do
    case "$line" in
      ''|*[!0-9]*) continue ;;
    esac
    case "$line" in
      0) ;;
      101) [ "$worst" = 0 ] && worst=101 ;;
      *) worst="$line" ;;
    esac
  done <"$file"
  echo "$worst"
}

# The subcommand is spelled out rather than implied, so that `maran polygon` can grow a sibling
# later without changing what an existing CI step means. There is exactly one today.
case "${1:-}" in
  verify) shift ;;
  -h|--help|'') usage ;;
  *) echo "REFUSED: '$1' is no polygon subcommand. The only one is 'verify'." >&2; usage ;;
esac

statuses=""
logs=()
while [ $# -gt 0 ]; do
  case "$1" in
    --statuses) statuses="${2:-}"; shift 2 || usage ;;
    -h|--help) usage ;;
    -*) echo "REFUSED: unknown option '$1'." >&2; usage ;;
    *) logs+=("$1"); shift ;;
  esac
done

[ "${#logs[@]}" -gt 0 ] || usage

for log in "${logs[@]}"; do
  if [ ! -r "$log" ]; then
    echo "REFUSED: log '$log' is not readable. A polygon step that left no log measured nothing" >&2
    echo "         that this command can see, and an unscored lane is not a green lane." >&2
    exit 1
  fi
done

# This command runs no container and writes no file, so it takes no lock — an exclusive lock here
# would block a ten-second scoring run behind an hour-long one for no gain. It PROBES, like
# `maran test`, because its verdict is derived from the TREE and not only from the logs: the suite
# list comes from `polygon_suites`, which greps agent/crates/*/tests for `#[ignore]`, and the
# per-suite counts come from the committed baseline. A mutant live in one of those test files can
# therefore move the list this run demands the logs account for, and the answer would be a named
# suite failure belonging to nobody.
#
# Note what this probe does NOT claim: the logs were produced somewhere else, usually on a CI runner
# against a clean checkout. UNOBSERVED HERE: whether the tree that PRODUCED these logs was clean.
# This only refuses to score them against a tree that is dirty right now.
tree_lock_probe "$root" || exit 1
if ! tree_lock_refuse_pending "$root" readonly; then
  echo "         Nothing was scored. Recover with: maran mutate --recover" >&2
  exit 1
fi

baseline_file="$root/scripts/test-baseline.txt"
if [ ! -r "$baseline_file" ]; then
  echo "REFUSED: scripts/test-baseline.txt is not readable, so no suite's count can be checked." >&2
  exit 1
fi

# The suite list comes from the TREE, never from a list written down here or in the workflow. That
# is the whole reason the workflow's own `--test` enumeration is now checkable: the workflow says
# what it ran, this says what exists, and a difference is a suite nobody runs.
mapfile -t suites < <(polygon_suites "$root")
if [ "${#suites[@]}" -eq 0 ]; then
  echo "ABORTED: no polygon suite could be identified in agent/crates/*/tests (a suite is a"
  echo "crate-level test file carrying #[ignore]). There was nothing to score, which is a failure,"
  echo "not a pass."
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The parsers are proven able to see a PLANTED failure and a planted total before they are pointed
# at the real logs. This lane scores logs it did not produce, so a parser that has gone blind here
# reports the reassuring answer — no failures named, nothing to score against — over a container
# that went red. rules/testing.md: a check must be able to observe what it reports on.
suite_selftest_parsers rust || exit 1

cat "${logs[@]}" >"$work/combined.log"
suite_parse rust "$work/combined.log" >"$work/observed.tsv"
# The committed baseline, stack-stripped to the rust rows — the same view mutate.sh builds and the
# same file `maran test rust --accept` maintains. One source of truth: a second expectations file
# would be a second copy of numbers only a docker run keeps honest, and the copy nobody runs drifts.
{ grep -E '^rust	' "$baseline_file" || true; } | sed -E 's/^rust	//' >"$work/baseline.tsv"

status="$(aggregate_status "$statuses")"

echo "-- polygon suites discovered from the tree (${#suites[@]}): ${suites[*]}"
echo "   Derived, never written down here or in the workflow. A suite added to agent/crates/*/tests"
echo "   is required to have spoken in these logs by the next run."
echo "-- logs scored (${#logs[@]}): ${logs[*]}"
echo

failed=1
if ! ran="$(polygon_verify_ran "$work/combined.log" "$status" "$work/observed.tsv" \
  "$work/baseline.tsv" "${suites[@]}")"; then
  echo "$ran"
  echo
  echo "Last lines of the container output:"
  tail -20 "$work/combined.log" | sed 's/^/  /'
  echo
  echo "POLYGON VERDICT: ABORTED — this lane measured nothing it can be scored on."
  exit 1
fi
echo "$ran"
echo "(docker status $status — reported, not believed: the result lines above are the observation,"
echo " because a stopped container was measured here exiting 0 having run nothing.)"
echo

# Named failures, and the count of them reconciled against the totals. A verdict is named failures
# plus a collected total; a run that reports one without the other is half a verdict, and the half
# that is missing is always the half that would have been inconvenient.
suite_failures rust "$work/combined.log" >"$work/failures.txt"
lane_failed="$(awk -F'\t' '{f += $3} END {print f + 0}' "$work/observed.tsv")"
if ! reconcile="$(suite_reconcile_failures "$lane_failed" "$work/failures.txt" rust "$work/combined.log")"; then
  echo "$reconcile"
  echo
  echo "POLYGON VERDICT: ABORTED — the totals and the named failures disagree, so nothing here is"
  echo "a score. Whichever number is wrong, the run cannot be reported."
  exit 1
fi

if [ "$lane_failed" -gt 0 ]; then
  echo "Failing tests ($lane_failed):"
  sed 's/^/  /' "$work/failures.txt"
  echo
  echo "POLYGON VERDICT: FAILED — $lane_failed test(s) went red on a real host."
  exit 1
fi

failed=0
echo "Per-suite rows observed:"
sed 's/^/  /' "$work/observed.tsv"
echo
echo "POLYGON VERDICT: OK — every discovered suite started, finished, and executed exactly the"
echo "number of tests the committed baseline declares for it, with no failures."
exit "$failed"
