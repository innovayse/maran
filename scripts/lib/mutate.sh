#!/usr/bin/env bash
# `maran mutate` — the mutation harness rules/testing.md describes, as code instead of as a list to
# remember.
#
# A mutation run is how this repository checks that a protection is actually held up by a test:
# break the protection on purpose, confirm a NAMED test goes red, restore, move on. It is the
# strongest evidence we produce about security code, and it is worth exactly as much as the harness
# that produced it. Every harness defect measured here has failed in the same direction — reporting
# a protection as tested when it was not — so the nine rules in rules/testing.md live in this file
# and are not optional for the author of a run:
#
#   1. The mutation must LAND. A pattern that is absent, ambiguous, or spans lines is refused
#      before anything is written, because a silent no-op scores SURVIVED.
#   2. The mutation must COMPILE. A mutant that does not build has measured nothing; it is refused
#      and scored as nothing — never as a survivor, and never as "the test went red".
#   3. The score is taken over the WHOLE suite. There is no scope option here, and `suite_run`
#      has no scope parameter to pass one to.
#   4. A run that produces no test-result line is ABORTED, not scored. Nor is a run whose totals
#      and whose named failures disagree: those come from two parsers reading two different kinds
#      of line, and a verdict read off a silently-blind one is GREEN over real red. That is not a
#      hypothetical — the backend lane did exactly that, on every run it ever made, until the
#      bracket expression in `suite_failures` was fixed and `suite_selftest_parsers` was written to
#      plant a failure and prove the parser still finds it BEFORE the suite is run.
#   5. A run taken while another build holds the outputs is refused (see suite_refuse_concurrency).
#   6. The verdict is named failures plus a collected total, compared against the committed
#      baseline. Never an exit code.
#   7. The NAMED test must be the one that died. Anything else that went red is printed beside it,
#      and a red suite without the named test is not a kill.
#   8. A SURVIVED verdict owes a witness. Without `--witness` the verdict is WITHHELD: a mutant
#      that is a semantic no-op leaves the suite green and every other guard here sees nothing
#      wrong, so the author states what the mutation changed and why nothing died, or says nothing
#      at all.
#   9. Restore with a FRESH mtime, verify it with `cmp`, delete the backup — and keep the backup
#      OUT of the working tree. cargo and MSBuild key their caches on mtime, so a restore that puts
#      the original timestamp back leaves the MUTATED binary in the build directory and the next
#      run measures the previous experiment. Backups in the repository root are litter that outlives
#      the agent that made them; this command refuses a backup directory inside the tree.
#  11. ONE TREE, ONE MUTATION AT A TIME. The mutation goes into the shared working tree, so a
#      second run scoring at the same time is scoring somebody else's experiment — and it does not
#      fail, it prints a plausible verdict about the wrong code. Measured on 2026-09-08: a run
#      mutating only a doc COMMENT reported "KILLED — a_label_ending_with_a_hyphen_is_rejected"
#      because another agent's mutant was resident in a different file. `tree-lock.sh` holds an
#      flock for the length of the run, and a killed holder leaves a pending record rather than a
#      stale lock. Every run also gets a PRIVATE directory for its backup and logs, because the
#      shared scratchpad is what let one agent's write land inside another agent's run.
#  10. The local workspace is NOT the whole evidence. Some protections are invisible to it: moving
#      `setuid` before `setgroups` in the agent's privilege drop survives all 1396 local tests and
#      is killed only inside a polygon container, running as root on a real system. `--polygon`
#      adds those lanes; see the block below for why it ADDS them rather than replacing.
#
# `git checkout --` is not used for any of this, and never should be: it discards uncommitted work
# in the file it "restores", and this repository has lost work to it once already. The backup copy
# taken before the edit is the whole restore mechanism, and it is verified.
#
# WHY `--polygon` SUPPLEMENTS THE LOCAL SCORE AND DOES NOT REPLACE IT
#
# Three lanes are run and all three are always reported: the local workspace, and the `#[ignore]`d
# host suites as root on each family. Averaging them, or letting one stand in for the other, would
# throw away the only interesting result a two-lane run can produce — a disagreement:
#
#   killed locally, survives on the polygon  the local test asserts an argv, a rendered string or a
#                                            typed error, and nothing observes the EFFECT on a real
#                                            host. That is the shape docker/README.md records twice
#                                            (a grep of a unit file's text standing in for a runtime
#                                            directory; a fake ConfigHost standing in for nginx).
#   survives locally, killed on the polygon  the protection has no local test at all, and the only
#                                            thing holding it up is a suite that runs on two
#                                            machines a day. That is worth knowing precisely because
#                                            the local suite is what a developer runs before pushing.
#
# So the verdict is a per-lane table plus one overall line, and the overall line is KILLED if the
# named test died in ANY lane. A protection is tested if something, somewhere, catches it breaking;
# it is untested only when nothing does.
#
# In `--polygon` mode NO REPOSITORY FILE IS WRITTEN. The mutation is applied to an rsync snapshot
# outside the tree and every lane — the local one included — runs from that snapshot, with its own
# `CARGO_TARGET_DIR` outside the tree. The repository copy of the mutated file is backed up anyway
# and `cmp`ed at the end, so "the tree was not touched" is measured rather than asserted.
#
# Usage:
#   maran mutate --file <path> --find <text> --replace <text> --expect <test name> \
#                [--polygon] [--occurrence N] [--witness "what changed and why nothing died"]
#
# Example (a Rust protection):
#   maran mutate --file agent/crates/agent-core/src/validation/web/domain.rs \
#     --find 'if label.is_empty() {' --replace 'if false {' \
#     --expect domain_with_empty_label_is_rejected
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/polygon.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"

usage() {
  cat >&2 <<'USAGE'
usage: maran mutate --file <path> --find <text> --replace <text> --expect <test name>
                    [--polygon] [--occurrence N] [--witness <text>]

  --file        the source file to mutate, relative to the repository root
  --find        the exact single-line text to replace (must occur exactly once,
                or --occurrence must name which one)
  --replace     what to put there. It must COMPILE: a mutant that does not build
                has measured nothing and is refused, not scored
  --expect      the test that must go red. A red suite without THIS test is not a kill
  --occurrence  which occurrence to replace, 1-based, when --find is ambiguous
  --polygon     score three lanes instead of one: the local workspace, and every #[ignore]d
                host suite as root inside a container on BOTH families. Agent code only.
                Some protections — the privilege drop above all — are invisible to the local
                workspace, which answers SURVIVED for them. The lanes are reported separately;
                a disagreement between them is the finding, not a detail to average away.
                No repository file is written in this mode: the mutation goes to a snapshot.
  --witness     required only to publish a SURVIVED verdict: what the mutation changed in
                behaviour, and why no test noticed
  --wait N      queue for up to N seconds behind another mutation run holding the tree lock.
                The default is 0 — refuse at once and name the holder — because a silent
                block is indistinguishable from a hung harness. While waiting, a line is
                printed every 15 seconds saying who is being waited for.
  --recover     put back a mutation that a KILLED run left applied to this tree, from the
                backup that run took, verified with cmp. Takes no other argument.

The stack is chosen by path: agent/ runs the Rust workspace, backend/ runs the solution.
The score is always the whole suite; there is no way to narrow it from the command line.
USAGE
  exit 2
}

file=""
find_text=""
replace_text=""
expect_test=""
occurrence=""
witness=""
polygon=0
wait_for=0
recover=0
while [ $# -gt 0 ]; do
  case "$1" in
    --polygon)    polygon=1; shift ;;
    --file)       file="${2:-}"; shift 2 ;;
    --find)       find_text="${2:-}"; shift 2 ;;
    --replace)    replace_text="${2:-}"; shift 2 ;;
    --expect)     expect_test="${2:-}"; shift 2 ;;
    --occurrence) occurrence="${2:-}"; shift 2 ;;
    --witness)    witness="${2:-}"; shift 2 ;;
    --wait)       wait_for="${2:-0}"; shift 2 ;;
    --recover)    recover=1; shift ;;
    -h|--help)    usage ;;
    # Named explicitly so the refusal explains itself. Someone reaching for these is trying to make
    # the run finish sooner, and the answer they would get back is the one that manufactures
    # confidence.
    --filter|-p|--package|--test|--lib)
      echo "REFUSED: $1 narrows the run. A mutant scored against part of the suite is scored" >&2
      echo "         blind to the rest, and the answer that produces is SURVIVED." >&2
      echo "         (rules/testing.md: score against the WHOLE suite, never a subset.)" >&2
      exit 2
      ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done

# --recover is its own command: it takes the same tree lock as a run, so it cannot fight a live one,
# and everything it finds afterwards therefore belongs to a run that is no longer alive.
if [ "$recover" = 1 ]; then
  tree_lock_acquire "$root" "$wait_for" "recovery" || exit 1
  trap tree_lock_release EXIT INT TERM
  tree_lock_recover "$root"
  exit $?
fi

[ -n "$file" ] && [ -n "$find_text" ] && [ -n "$replace_text" ] && [ -n "$expect_test" ] || usage

target="$root/$file"
if [ ! -f "$target" ]; then
  echo "REFUSED: no such file: $file" >&2
  exit 2
fi

case "$file" in
  agent/*)   stack=rust ;;
  backend/*) stack=backend ;;
  *)
    echo "REFUSED: $file is in no stack this harness can score (expected agent/ or backend/)." >&2
    exit 2
    ;;
esac

if [ "$polygon" = 1 ] && [ "$stack" != rust ]; then
  echo "REFUSED: --polygon scores the agent's #[ignore]d host suites, which are Rust." >&2
  echo "         There is no polygon for $file." >&2
  exit 2
fi

# Guard 1: the mutation must be expressible and unambiguous, BEFORE anything is written.
if [ "$find_text" != "${find_text%$'\n'*}" ] || [ "$replace_text" != "${replace_text%$'\n'*}" ]; then
  echo "REFUSED: --find/--replace must be single-line. A multi-line pattern matches nothing and" >&2
  echo "         replaces nothing, silently, and the run then scores SURVIVED." >&2
  exit 2
fi

occurrences="$(grep -cF -- "$find_text" "$target" || true)"
if [ "${occurrences:-0}" -eq 0 ]; then
  echo "REFUSED: --find is not present in $file — the mutation would not land." >&2
  exit 2
fi
if [ "${occurrences:-0}" -gt 1 ] && [ -z "$occurrence" ]; then
  echo "REFUSED: --find matches $occurrences lines in $file. Name which one with --occurrence N," >&2
  echo "         because replacing all of them is a different experiment than the one you named." >&2
  grep -nF -- "$find_text" "$target" >&2
  exit 2
fi

# Guard 11 (new): the TREE LOCK. Taken here — after the refusals above, which only READ, and before
# the first thing that writes — so a malformed invocation is rejected instantly instead of queueing
# behind somebody's ten-minute suite.
#
# It is not the concurrency guard below and does not replace it: that one observes BUILDS, and it
# was measured blind to a second mutation run twice on 2026-09-08 (report:
# .superpowers/sdd/mutate-tree-lock-report.md). Two `maran mutate` runs started in the same second
# both passed it, both applied a mutant to this tree, and one of them published a KILLED verdict
# naming a test the OTHER agent's mutant had killed. See scripts/lib/tree-lock.sh for why the lock
# is per-tree, why it is an flock, and why a killed holder leaves no stale lock.
tree_lock_acquire "$root" "$wait_for" "mutate $file" || exit 1
trap tree_lock_release EXIT INT TERM

# Under the lock, no other mutation run can be alive — so anything still recorded as pending was
# left by a run that was KILLED, and its mutation is still in these files. Refuse rather than score.
tree_lock_refuse_pending "$root" || exit 1

# The backup lives outside the working tree. A `.orig` beside the source is litter that outlives the
# run that made it — this repository has been cleaned of exactly that — and worse, an interrupted
# run leaves a file the next `maran structure` has to have an opinion about.
backup_dir="${MARAN_MUTATE_BACKUP_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/maran/mutate}"
mkdir -p "$backup_dir"
backup_dir="$(cd "$backup_dir" && pwd)"
case "$backup_dir/" in
  "$root"/*)
    echo "REFUSED: backup directory $backup_dir is inside the working tree." >&2
    echo "         Backups must not be able to become repository litter." >&2
    exit 2
    ;;
esac
# THIS RUN'S PRIVATE DIRECTORY. Everything the run owns — the backup, the raw logs, the scratch
# files — lives inside it, and nothing else writes there.
#
# What it replaces: `mktemp -d`, which honours $TMPDIR, so an agent that pointed TMPDIR at a shared
# scratchpad had another session's file of the same name land inside its run. That is not a
# hypothetical: a `drive.sh` being EXECUTED by a live harness was truncated by another agent writing
# the same path, and the harness's child was a third agent's mutation applied to this tree.
# `mktemp -d` under the backup directory keeps the name unguessable (no pid collisions) and keeps
# the whole thing OUTSIDE the working tree, which the check above has already proven.
#
# WHAT A KILL LEAVES: this directory, containing `<file>.orig` — the original bytes — plus a pending
# record in the tree-lock directory naming it. Nothing is cleaned up by a killed run, deliberately:
# the next run reads that record, refuses to start, and prints `maran mutate --recover`.
run_dir="$(mktemp -d "$backup_dir/run-$(date +%Y%m%d-%H%M%S)-XXXXXX")"
backup="$run_dir/$(basename "$file").orig"
repo_file="$target"
cp "$repo_file" "$backup"

# In `--polygon` mode the mutation never reaches the repository. The tree is rsynced to a snapshot
# outside it and every lane runs from the snapshot, which is how the defect that motivated this mode
# was measured in the first place — without touching a repository file. It is strictly better than
# mutating the tree: nothing an interrupted run can leave behind is inside the repository, two other
# agents can keep working in `agent/` while a run is in flight, and the "was the tree restored"
# question becomes "was the tree ever written", which `cmp` can answer for certain.
snapshot=""
if [ "$polygon" = 1 ]; then
  snapshot="$run_dir/snapshot"
  rm -rf "$snapshot"
  mkdir -p "$snapshot"
  rsync -a --delete \
    --exclude '.git/' --exclude 'target/' --exclude 'node_modules/' \
    --exclude 'bin/' --exclude 'obj/' --exclude 'dist/' \
    "$root/" "$snapshot/"
  target="$snapshot/$file"
  if ! cmp -s "$repo_file" "$target"; then
    echo "REFUSED: the snapshot copy of $file differs from the repository copy before any" >&2
    echo "         mutation — the snapshot is not a copy of this tree and would score something else." >&2
    exit 2
  fi
fi

# Guard 9, armed before the first write and disarmed only on the way out: whatever happens next —
# a failed build, a killed process, a Ctrl-C — the original comes back, with a FRESH mtime, verified
# by `cmp`, and the backup is deleted. In polygon mode there is nothing to put back, so the same
# `cmp` is used for the stronger claim: the repository copy is byte-identical to the backup taken
# before anything ran, i.e. it was never written at all.
restored=0
restore() {
  [ "$restored" = 1 ] && return 0
  restored=1
  if [ "$polygon" = 1 ]; then
    if cmp -s "$backup" "$repo_file"; then
      echo "UNTOUCHED: $file in the working tree is byte-identical to the copy taken before this"
      echo "           run started (verified with cmp) — the mutation only ever existed in the snapshot."
      tree_lock_clear_pending "$root"
      rm -rf "$run_dir"
    else
      echo "TREE WAS MODIFIED: $file differs from $backup. The backup and the snapshot are KEPT." >&2
      echo "                   Compare them by hand. Do NOT run 'git checkout --' on it: that" >&2
      echo "                   discards uncommitted work." >&2
      return 1
    fi
    return 0
  fi
  cp "$backup" "$target"
  touch "$target"
  if cmp -s "$backup" "$target"; then
    echo "RESTORED: $file is byte-identical to the pre-mutation original (verified with cmp)"
    # The pending record is cleared ONLY here, on the far side of the cmp. A record deleted before
    # the comparison would say the tree was clean on exactly the runs where it was not.
    tree_lock_clear_pending "$root"
    rm -rf "$run_dir"
  else
    echo "RESTORE FAILED: $file differs from $backup — the backup is KEPT. Compare them by hand." >&2
    echo "                Do NOT run 'git checkout --' on it: that discards uncommitted work." >&2
    return 1
  fi
}
trap 'restore; tree_lock_release' EXIT INT TERM

# Every lane keeps its own build directory, outside the tree. Three separate ones because they are
# not interchangeable: the two families ship different glibc versions (Ubuntu 24.04 has 2.39,
# AlmaLinux 9 has 2.34), so a test binary built by one starts on that family and refuses to start on
# the other, and neither is the host's. Sharing one directory between them would produce a run that
# either rebuilds everything every time or, worse, runs a binary built elsewhere.
cache_root="$backup_dir/build"
host_target="$cache_root/host"

# Where the run happens. In polygon mode this is the snapshot; otherwise the tree itself.
work_root="$root"
[ "$polygon" = 1 ] && work_root="$snapshot"

echo "== maran mutate =="
echo "file      $file  ($stack)"
echo "mutation  $find_text  ->  $replace_text"
echo "expecting $expect_test to go red"
echo "backup    $backup"
if [ "$polygon" = 1 ]; then
  echo "mode      POLYGON: three lanes — local workspace, ubuntu24, alma9"
  echo "snapshot  $snapshot  (the repository is not written)"
fi
echo

suite_require_toolchain "$stack" || exit 1
# The parsers that will produce this run's verdict are proven able to see a failure BEFORE the
# suite runs, not after: a blind parser answers GREEN, and GREEN is the verdict an author quotes
# when arguing a check can be deleted.
suite_selftest_parsers "$stack" || exit 1
if [ "$polygon" = 1 ]; then
  polygon_require_docker || exit 1
  export CARGO_TARGET_DIR="$host_target"
  mkdir -p "$host_target"
fi
suite_refuse_concurrency "$stack" "$work_root" || exit 1
if [ "$polygon" = 1 ]; then
  polygon_note "in polygon mode this concurrency check is asked about the SNAPSHOT, not the tree:"
  polygon_note "no lane reads or writes agent/target, so another agent's cargo build cannot corrupt"
  polygon_note "this run's outputs. It can still compete for CPU, and is reported above as LOAD."
fi
echo

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
  echo "VERDICT: REFUSED — THE MUTANT DOES NOT COMPILE. Nothing was measured."
  echo "This is NOT a kill and NOT a survivor. Compiler output:"
  grep -E '^(error|error\[|.*: error )' "$work/build.log" | head -20 || tail -20 "$work/build.log"
  echo
  echo "If the protection cannot be removed without a compiler error, that is a STRONGER"
  echo "arrangement than a test — but say so in those words. It is not a test result."
  exit 1
fi
echo "the mutant compiles."
echo

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
