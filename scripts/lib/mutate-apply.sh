#!/usr/bin/env bash
# `maran mutate`, step 4: the crash record, the mutation itself, the evidence keeper and the does-it-compile guard.
#
# SOURCED by scripts/lib/mutate.sh (split out of it, issue #355), in the order the run needs it. This
# file runs at the top level of that script and shares its variables, traps and `-euo pipefail`.

# The crash record, written BEFORE the mutation lands. The ordering is the whole point: a record
# written afterwards would be missing for exactly the runs that were killed in between. A SIGKILLed
# bash runs no EXIT trap, so the restore below does not happen for the case that happens most here —
# this file is what turns "a silently wrong tree with no undo" into a refusal with the undo in hand.
tree_lock_mark_pending "$root" "$file" "$backup"

# Apply, then prove the file changed. "The mutation landed" is not the same claim as "the pattern
# was found": an occurrence index off the end, or a replacement identical to the pattern, both
# leave the file untouched.
python3 - "$target" "$find_text" "$replace_text" "${occurrence:-1}" <<'PYAPPLY'
"""Replaces one single-line occurrence of a literal in a file, leaving every other line alone."""
import sys

path, needle, replacement, index = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
with open(path, encoding="utf-8") as handle:
    lines = handle.readlines()

hits = [number for number, line in enumerate(lines) if needle in line]
if index < 1 or index > len(hits):
    sys.exit(f"REFUSED: --occurrence {index} but the pattern occurs {len(hits)} time(s)")

line_number = hits[index - 1]
lines[line_number] = lines[line_number].replace(needle, replacement, 1)
with open(path, "w", encoding="utf-8") as handle:
    handle.writelines(lines)
print(f"mutated line {line_number + 1}: {lines[line_number].rstrip()}")
PYAPPLY
touch "$target"

if cmp -s "$backup" "$target"; then
  echo "REFUSED: the file is unchanged after the replacement — the mutation did not land." >&2
  echo "         A run scored on an unmutated tree reports SURVIVED for a protection nobody broke." >&2
  exit 2
fi

work="$run_dir/work"
mkdir -p "$work"
trap 'restore; tree_lock_release' EXIT INT TERM

# keep_evidence: copies this run's raw logs somewhere durable and names the path.
#
# Every ABORT below is a statement about a log the reader cannot see — "the run produced no
# test-result line", "a target vanished", "the totals and the named failures disagree" — and a
# verdict whose evidence is deleted on the way out cannot be checked by the person it is quoted to.
# The logs go to the backup directory, which is already required to live OUTSIDE the working tree,
# so nothing this leaves behind can become repository litter.
evidence_dir=""
keep_evidence() {
  [ -n "$evidence_dir" ] && return 0
  evidence_dir="$backup_dir/evidence-$(date +%Y%m%d-%H%M%S)-$$"
  mkdir -p "$evidence_dir"
  cp -f "$work"/*.log "$work"/*.tsv "$work"/*.txt "$evidence_dir/" 2>/dev/null || true
  echo "evidence kept: $evidence_dir (the raw logs this verdict was read from)"
}
# Deliberate runs can ask for the same thing when nothing went wrong — a harness whose output can
# only be believed is the shape rules/testing.md warns about.
if [ "${MARAN_MUTATE_KEEP_EVIDENCE:-0}" = 1 ]; then
  trap 'keep_evidence; restore; tree_lock_release' EXIT INT TERM
fi

# Guard 2: does the mutant compile? Asked separately from the test run so that a build error is
# reported as a build error. A harness that runs the suite and reads the resulting red as "the test
# died" is the purest form of the defect this command exists to prevent — and both obvious C#
# mutations produce exactly that: `if (false)` is CS0162 and dropping a guard orphans a `using`
# (IDE0005), and both are errors under warnings-as-errors.
echo "-- compiling the mutant"
compiled=0
case "$stack" in
  rust)    (cd "$work_root/agent" && cargo test --workspace --no-run --no-fail-fast) >"$work/build.log" 2>&1 && compiled=1 ;;
  backend) (cd "$work_root/backend" && dotnet build Maran.sln) >"$work/build.log" 2>&1 && compiled=1 ;;
esac

if [ "$compiled" = 0 ]; then
  echo
  echo "THE MUTANT DOES NOT COMPILE. This is NOT a kill and NOT a survivor. Compiler output:"
  grep -E '^(error|error\[|.*: error )' "$work/build.log" | head -20 || tail -20 "$work/build.log"
  echo
  echo "If the protection cannot be removed without a compiler error, that is a STRONGER"
  echo "arrangement than a test — but say so in those words. It is not a test result."
  echo
  # The DID NOT RUN status, deliberately, and not the failure status this used to return: a mutant
  # that does not compile asked no suite anything, and 1 is what `NOT A KILL` returns — a verdict
  # about a suite that ran.
  did_not_run "the mutant does not compile, so no suite was ever asked about it."
fi
echo "the mutant compiles."
echo

