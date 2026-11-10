#!/usr/bin/env bash
# `maran mutate`, step 6: the verdict read from the lane table.
#
# SOURCED by scripts/lib/mutate.sh (split out of it, issue #355), in the order the run needs it. This
# file runs at the top level of that script and shares its variables, traps and `-euo pipefail`.

# --- the verdict --------------------------------------------------------------------------------
killed=0
red=0
aborted=0
index=0
if [ "${#lane_names[@]}" -gt 1 ]; then
  echo "lanes:"
  while [ "$index" -lt "${#lane_names[@]}" ]; do
    printf '  %-18s %-8s %s\n' "${lane_names[$index]}" "${lane_states[$index]}" "${lane_details[$index]}"
    index=$((index + 1))
  done
  echo
fi
for state in "${lane_states[@]}"; do
  case "$state" in
    KILLED)  killed=$((killed + 1)) ;;
    RED)     red=$((red + 1)) ;;
    ABORTED) aborted=$((aborted + 1)) ;;
  esac
done

# An aborted lane is fatal to the whole verdict, and deliberately so. A lane that measured nothing
# cannot be scored as anything, and a run that reports KILLED or SURVIVED while one of its lanes
# never executed is claiming to have looked somewhere it did not look.
if [ "$aborted" -gt 0 ]; then
  echo "VERDICT: ABORTED — $aborted lane(s) measured nothing. Nothing here is a kill and nothing"
  echo "here is a survivor. Fix the lane and run again; do not quote the lanes that did run as if"
  echo "they were the whole answer."
  exit 1
fi

if [ "$killed" -gt 0 ]; then
  echo "VERDICT: KILLED — $expect_test went red in $killed of ${#lane_names[@]} lane(s)."
  if [ "${#lane_names[@]}" -gt 1 ] && [ "$killed" -lt "${#lane_names[@]}" ]; then
    echo "Read the table: the lanes where it did NOT die are lanes that cannot see this defect."
    echo "If the only lane that killed it is a polygon lane, the protection is held up by a suite"
    echo "that runs on two images in CI and never on a developer's machine before a push."
  fi
  exit 0
fi

if [ "$red" -gt 0 ]; then
  echo "VERDICT: NOT A KILL — a suite went red, but $expect_test did NOT."
  echo "A red suite is compatible with the protection being untested and something else"
  echo "being brittle. What actually died is printed per lane above."
  exit 1
fi

if [ -z "$witness" ]; then
  echo "VERDICT: WITHHELD — every lane is green, but a SURVIVED verdict owes a witness."
  echo "Nothing here proves the mutation changed BEHAVIOUR: a semantic no-op leaves the suite"
  echo "green and every guard in this harness sees a file that changed and a build that worked."
  echo "Exhibit the difference — a probe, or a scoped witness test — and re-run with:"
  echo "  --witness \"<what the mutant now does differently, and why no test noticed>\""
  exit 1
fi

echo "VERDICT: SURVIVED — every lane above is green on the mutant."
echo "witness: $witness"
echo "Either that witness becomes the missing test, or this survivor is retracted."
exit 0
