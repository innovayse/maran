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
# subject is the runner, and CI splits the discovered suites across three steps by the capability
# each group needs, so that a suite started without its capabilities fails as a runner mistake
# rather than as `JailFailed` — which reads as a code defect. Both splits are right for their own lane.
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
#   maran polygon stamp  FAMILY [IMAGE]
#
#   `stamp` is the run-time half of the image currency check, and it belongs to this command because
#   the line it prints is what `verify` later reads. A caller emits it into the log it is about to
#   tee a `docker run` into; it refuses, before the container starts, when the image was built from
#   sources this tree no longer has. See polygon_stamp / polygon_currency_from_log in polygon.sh.
#
#   LOG          a file holding the stdout+stderr of a polygon `docker run`. Pass one per step; they
#                are concatenated in the order given, which is how the suites of a capability-split
#                run become one observation.
#   --statuses   a file of exit statuses, one per line, one per LOG. Optional, and reported rather
#                than believed: see aggregate_status below for what is done with it and why.
#
# Exit: three answers, because both kinds of caller exist and each is blind where the other is not.
#   0  every axis held and no test failed          (POLYGON VERDICT: OK)
#   1  a test went red, or the logs are not a score (POLYGON VERDICT: FAILED / ABORTED) — including
#      a run whose own log records an image built from sources this tree no longer has
#   2  this command REFUSED before scoring anything (POLYGON VERDICT: DID NOT RUN) — the tree's
#      write lock is held, a killed mutation is still applied, or the parsers are blind. Nothing
#      was measured, so it is not a pass; nothing was found, so it is not a red branch.
# The OUTPUT is the verdict — one `POLYGON VERDICT:` line, named failures and totals — and the
# status exists beside it so a caller that keeps no output can still tell the three apart.
# .github/workflows/agent.yml gates on the LINE and reports the status; `maran lock selftest`
# asserts both halves of that contract against this file.
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
       maran polygon stamp  FAMILY [IMAGE]

  Scores an already-executed polygon run from its container logs, against the suites discovered in
  the tree and the counts committed in scripts/test-baseline.txt. Runs nothing itself.

  --statuses FILE   exit statuses of the runs that produced the logs, one per line

  stamp prints the one line that records which image a run used, and refuses when that image was
  built from sources this tree no longer has. Emit it into the log, before the docker run:

      scripts/maran polygon stamp ubuntu24 >>"$LOGDIR/step-1.log"
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
  stamp)
    shift
    # A family is required and never defaulted: the fingerprint is per family, and a stamp naming
    # the wrong one would be a sentence in a log that compares cleanly against nothing.
    [ $# -ge 1 ] || usage
    stamp_family="$1"
    stamp_image="${2:-maran-polygon-$stamp_family:latest}"
    if [ ! -r "$root/docker/polygon/$stamp_family.Dockerfile" ]; then
      echo "REFUSED: '$stamp_family' is no polygon family — docker/polygon/$stamp_family.Dockerfile" >&2
      echo "         does not exist, so there are no sources to fingerprint." >&2
      exit 2
    fi
    if ! polygon_require_docker; then
      exit 2
    fi
    polygon_stamp "$root" "$stamp_family" "$stamp_image" || exit 1
    exit 0
    ;;
  -h|--help|'') usage ;;
  *) echo "REFUSED: '$1' is no polygon subcommand. They are 'verify' and 'stamp'." >&2; usage ;;
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
    suite_did_not_run "POLYGON VERDICT" "there is no log to score, so nothing was scored."
    exit "$SUITE_STATUS_DID_NOT_RUN"
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
if ! tree_lock_probe "$root"; then
  suite_did_not_run "POLYGON VERDICT" "a mutation run holds this tree's write lock (named above)."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi
if ! tree_lock_refuse_pending "$root" readonly; then
  echo "         Nothing was scored. Recover with: maran mutate --recover" >&2
  suite_did_not_run "POLYGON VERDICT" "a killed mutation run left its mutant in this tree."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi

baseline_file="$root/scripts/test-baseline.txt"
if [ ! -r "$baseline_file" ]; then
  echo "REFUSED: scripts/test-baseline.txt is not readable, so no suite's count can be checked." >&2
  suite_did_not_run "POLYGON VERDICT" "there is no baseline to score against."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi

# The suite list comes from the TREE, never from a list written down here or in the workflow. That
# is the whole reason the workflow's own `--test` enumeration is now checkable: the workflow says
# what it ran, this says what exists, and a difference is a suite nobody runs.
mapfile -t suites < <(polygon_suites "$root")
if [ "${#suites[@]}" -eq 0 ]; then
  echo "No polygon suite could be identified in agent/crates/*/tests (a suite is a crate-level"
  echo "test file carrying #[ignore]), so there was nothing here to score."
  echo
  # The verdict LINE, which this one path used to omit while every other terminal path printed one.
  # That mattered because the caller scores this command by GREPPING for the line
  # (.github/workflows/agent.yml), so a path that exits without one is scored by its status alone —
  # the exact defect this command exists to close — and because the header above promises one of
  # four verdict lines, which rules/testing.md forbids a comment claiming when the code does not.
  #
  # ABORTED and not DID NOT RUN, deliberately, and the status stays 1. Nothing REFUSED here: the
  # tree was read, the discovery ran, and its answer was "no suite exists". That is a statement
  # about this branch, not about a peer holding a lock, and "no tests found" is a failure
  # everywhere else in this repository rather than a run that did not happen.
  echo "POLYGON VERDICT: ABORTED — no suite exists to score, which is a failure, not a pass."
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The parsers are proven able to see a PLANTED failure and a planted total before they are pointed
# at the real logs. This lane scores logs it did not produce, so a parser that has gone blind here
# reports the reassuring answer — no failures named, nothing to score against — over a container
# that went red. rules/testing.md: a check must be able to observe what it reports on.
if ! suite_selftest_parsers rust; then
  suite_did_not_run "POLYGON VERDICT" "the log parsers are blind, so these logs cannot be scored."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi

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

# THE IMAGE CURRENCY AXIS, and it is asked FIRST, before any count is believed.
#
# Every axis below this line asks whether the container said enough. None of them can ask whether the
# container was the right container. Measured here on 2026-09-11: a shared `maran-polygon-alma9:latest`
# built on the 9th, against a Dockerfile step added on the 10th, with no build in between — so
# `/etc/shadow` inside it carried the mode of an older build and TWELVE tests went red over correct
# code. The five axes below all held: every suite started, every suite finished, every count
# reconciled. The run was a perfect measurement of a tree that no longer existed.
#
# The lesson that decides where this goes: AN ASSERTION INSIDE A BUILD IS BLIND TO THE ABSENCE OF THE
# BUILD. The Dockerfile's own `stat -c %a /etc/shadow` assertion was present and correct; nothing ran
# it. So the check cannot live in the image, and it cannot live only in the builder either — it has to
# be asked by whatever consumes the result.
#
# ABORTED, not DID NOT RUN, and the distinction is the one this harness already draws: DID NOT RUN is
# a statement about the LANE — a peer holds the lock, a toolchain is missing, nothing was read, the
# branch is neither cleared nor accused. Here the tree WAS read, the logs WERE parsed, and the answer
# is a statement about the artefacts scored: these logs are not a score of this tree. That is the same
# reasoning the "no suite exists" path below was given, and it is the same verdict. What a caller does
# with it: treat it exactly as a red lane in urgency and not at all as a red lane in diagnosis —
# rebuild the image and run again, and do not go hunting the named failures, because they were
# measured against sources this tree does not have. A stale image must never be able to look like a
# pass, and it cannot look like one here: the only verdict this file prints with status 0 is OK.
#
# IT IS ASKED FIRST AND DECIDED LAST, and that is deliberate. A finding here does not `exit` on the
# spot, because one axis that ends the script takes every other axis down with it: a bash error in
# this very family of functions was measured today reporting ABORTED with an empty body while also
# masking a red test. So the currency finding is PRINTED here, remembered, and every other axis still
# runs and still says what it saw — the reader gets the whole picture — and the VERDICT below is
# ABORTED regardless of what those axes concluded, because a result measured against other sources is
# not a pass and is not a red branch either.
currency_stale=0
if ! currency="$(polygon_currency_from_log "$root" "$work/combined.log")"; then
  currency_stale=1
fi
echo "$currency"
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
fi

# The currency verdict, decided after every other axis has spoken. It outranks both remaining answers:
# a red test measured against sources this tree does not have is not a finding about this branch, and
# a clean run measured against them is certainly not a pass.
if [ "$currency_stale" -ne 0 ]; then
  echo "POLYGON VERDICT: ABORTED — these logs were not produced by the image this tree describes, so"
  echo "nothing in them scores this branch (the mismatch is named above). This is not a red polygon:"
  echo "no result here, red or green, was measured against these sources. Rebuild the image and run"
  echo "it again."
  exit 1
fi

if [ "$lane_failed" -gt 0 ]; then
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
