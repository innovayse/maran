#!/usr/bin/env bash
# `maran mutate`, step 5: lane scoring, the local lane and the polygon lanes.
#
# SOURCED by scripts/lib/mutate.sh (split out of it, issue #355), in the order the run needs it. This
# file runs at the top level of that script and shares its variables, traps and `-euo pipefail`.

# One row per lane, filled in below and read once at the end. A lane is one place the mutant was
# scored: the local workspace, and — with `--polygon` — each family's host suites. They are kept
# apart on purpose. A run that averages them, or lets the first answer stand for all of them, throws
# away the only thing a multi-lane run can tell you that a single-lane run cannot: WHERE the
# protection is held up, and where it is not.
lane_names=()
lane_states=()
lane_details=()

# score_lane: turns one lane's failure list into KILLED / RED / GREEN, and prints it.
#
# KILLED requires the NAMED test. "The suite went red" is compatible with the protection being
# untested and something unrelated being brittle, so everything else that died is printed beside it
# rather than folded into the verdict.
score_lane() {
  local name="$1" failures="$2"
  local state detail others
  # A lane whose failure list does not exist has not been scored, and must never fall through to
  # the `-s` test below, which cannot tell "no file" from "an empty file" and answers GREEN for
  # both. Missing is ABORTED; empty is green only once suite_reconcile_failures has agreed that
  # the totals also say nothing failed.
  if [ ! -e "$failures" ]; then
    echo "   $name: ABORTED — no failure list was produced for this lane ($failures does not"
    echo "   exist). A lane that produced NO list is not a lane that produced an EMPTY one."
    record_aborted_lane "$name" "no failure list was produced — nothing was scored"
    return 0
  fi
  if grep -qF -- "$expect_test" "$failures"; then
    state=KILLED
    detail="$expect_test went red"
    echo "   $name: KILLED — $expect_test went red on the mutant."
    others="$(grep -vF -- "$expect_test" "$failures" || true)"
    if [ -n "$others" ]; then
      echo "   Also red in this lane (these are NOT the kill; say so when you quote this run):"
      echo "$others" | sed 's/^/     /'
    fi
  elif [ -s "$failures" ]; then
    state=RED
    detail="red, but NOT $expect_test"
    echo "   $name: NOT A KILL — the suite went red, but $expect_test did NOT. What died:"
    sed 's/^/     /' "$failures"
  else
    state=GREEN
    detail="green"
    echo "   $name: GREEN — nothing died in this lane."
  fi
  lane_names+=("$name")
  lane_states+=("$state")
  lane_details+=("$detail")
}

# record_aborted_lane: a lane that measured nothing is neither a kill nor a survivor, and it must
# never be silently dropped — a run that quietly scores two lanes out of three and reports a total
# is the same defect as a suite that loses a project and still prints `Passed!`.
record_aborted_lane() {
  lane_names+=("$1")
  lane_states+=("ABORTED")
  lane_details+=("$2")
}

# --- lane: the local workspace -----------------------------------------------------------------
echo "-- lane: LOCAL — the WHOLE $stack suite, unfiltered"
suite_run "$stack" "$work_root" "$work/test.log"
if ! suite_recheck_concurrency "$stack" "$work_root"; then
  echo
  echo "VERDICT: ABORTED — another build joined this run. A contaminated suite is not a score."
  exit 1
fi
suite_parse "$stack" "$work/test.log" >"$work/observed.tsv"

rows="$(wc -l <"$work/observed.tsv")"
if [ "$rows" -eq 0 ]; then
  echo
  echo "VERDICT: ABORTED — the run produced NO test-result line. Nothing was measured."
  tail -20 "$work/test.log"
  keep_evidence
  exit 1
fi

passed="$(awk -F'\t' '{p+=$2} END{print p+0}' "$work/observed.tsv")"
failed="$(awk -F'\t' '{f+=$3} END{print f+0}' "$work/observed.tsv")"
ignored="$(awk -F'\t' '{i+=$4} END{print i+0}' "$work/observed.tsv")"
suite_failures "$stack" "$work/test.log" >"$work/failures.txt"

echo "collected  $rows targets: $passed passed / $failed failed / $ignored ignored"

# The totals above and the named failures below come from two different parsers reading two
# different kinds of line in the same log. Neither can vouch for the other, so they are made to
# agree before either is scored — see suite_reconcile_failures for the run that made this necessary.
if ! reconcile="$(suite_reconcile_failures "$failed" "$work/failures.txt" "$stack" "$work/test.log")"; then
  echo
  echo "$reconcile"
  echo
  echo "VERDICT: ABORTED — the totals and the named failures disagree. Nothing here is a kill and"
  echo "nothing here is a survivor."
  keep_evidence
  exit 1
fi

# The baseline comparison belongs in the mutation run too, not only in `maran test`: a mutant can
# make a whole target disappear while every surviving target still prints a plausible total.
baseline="$root/scripts/test-baseline.txt"
{ grep -E "^$stack	" "$baseline" 2>/dev/null || true; } | sed -E "s/^$stack	//" >"$work/baseline.tsv"
findings="$(suite_compare "$work/baseline.tsv" "$work/observed.tsv")"
vanished="$(echo "$findings" | grep -c VANISHED || true)"
if [ -n "$findings" ]; then
  echo
  echo "$findings"
fi
if [ "${vanished:-0}" -gt 0 ]; then
  echo
  echo "VERDICT: ABORTED — a target vanished from the run. A smaller suite is not a score."
  keep_evidence
  exit 1
fi
score_lane "local" "$work/failures.txt"
echo

# --- lanes: the polygon, one per family --------------------------------------------------------
#
# These lanes exist because the local one cannot see some protections at all. The privilege drop is
# the measured case: moving `setuid` before `setgroups` leaves all 1396 local tests green, and kills
# eleven tests here. A harness with only the lane above would have answered SURVIVED — and SURVIVED
# is the verdict an author quotes when deleting a check.
if [ "$polygon" = 1 ]; then
  mapfile -t suites < <(polygon_suites "$work_root")
  if [ "${#suites[@]}" -eq 0 ]; then
    echo "VERDICT: ABORTED — no polygon suite could be identified in the tree, so there was"
    echo "nothing for these lanes to run. (A suite is identified by carrying #[ignore], the same"
    echo "way maran structure identifies one.)"
    exit 1
  fi
  echo "-- polygon suites discovered from the tree (${#suites[@]}): ${suites[*]}"
  echo "   The list is derived, never written down here: a suite added to agent/crates/*/tests is"
  echo "   scored by the next run. Nothing in this command can narrow it."
  echo

  for family in ubuntu24 alma9; do
    echo "-- lane: POLYGON $family — every suite above, as root, --ignored, --no-fail-fast"
    fingerprint="$(polygon_fingerprint "$work_root" "$family")"
    # The image name is all this returns on stdout; everything it says about currency goes to
    # stderr, so a build failure cannot be captured into a variable and used as an image name.
    if ! image="$(polygon_ensure_image "$work_root" "$family" "$fingerprint" "$work/build-$family.log")"; then
      record_aborted_lane "polygon/$family" "no current image could be built — nothing was measured"
      echo
      continue
    fi
    container="maran-mutate-$family-$$"
    status="$(polygon_run "$image" "$work_root" "$cache_root/$family" "$work/$family.log" "$container" "${suites[@]}")"
    suite_parse rust "$work/$family.log" >"$work/observed-$family.tsv"
    # The same committed baseline the local lane is compared against, stack-stripped. It is built
    # once above (work/baseline.tsv) from scripts/test-baseline.txt in the REPOSITORY, not in the
    # snapshot: the question this lane asks is "did the container run the suite this tree has
    # recorded", so the recorded answer must come from the tree, not from the copy under test.
    if ! ran="$(polygon_verify_ran "$work/$family.log" "$status" "$work/observed-$family.tsv" \
      "$work/baseline.tsv" "${suites[@]}")"; then
      echo "   $ran"
      echo "   Last lines of the container's output:"
      tail -15 "$work/$family.log" | sed 's/^/     /'
      record_aborted_lane "polygon/$family" "container measured nothing (docker status $status)"
      echo
      continue
    fi
    echo "   $ran (docker status $status — reported, not believed: the result lines above are the"
    echo "   observation, because a stopped container was measured exiting 0 having run nothing)"
    suite_failures rust "$work/$family.log" >"$work/failures-$family.txt"
    lane_failed="$(awk -F'\t' '{f+=$3} END{print f+0}' "$work/observed-$family.tsv")"
    if ! reconcile="$(suite_reconcile_failures "$lane_failed" "$work/failures-$family.txt" rust "$work/$family.log")"; then
      echo "$reconcile" | sed 's/^/   /'
      record_aborted_lane "polygon/$family" "totals and named failures disagree — nothing scored"
      keep_evidence
      echo
      continue
    fi
    score_lane "polygon/$family" "$work/failures-$family.txt"
    echo
  done
fi

