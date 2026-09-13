#!/usr/bin/env bash
# `maran lock selftest` — proves the tree lock can still refuse, still let work through, and still
# be released by the kernel when its holder is KILLED.
#
# WHY THIS FILE EXISTS
#
# The tree lock (scripts/lib/tree-lock.sh) is a REFUSING GATE, and rules/testing.md is explicit
# about what those owe: "A gate mutated to refuse everything passes every test that only ever hands
# it broken input." A test that starts a holder and watches a command refuse says nothing at all —
# it is satisfied by a command that refuses unconditionally, by a typo in a subcommand name, by a
# missing python3. So every refusal below is paired with an INVERSE CONTROL: the same command, the
# same arguments, the lock free, and the assertion is that the lock's own refusal is ABSENT and the
# command got past it into its real work.
#
# The third case is the one that has actually cost this repository time. The lock's first
# implementation held the flock on a descriptor bash had opened, which is inherited across fork and
# exec: eight MSBuild `nodeReuse` daemons kept it alive after the run that opened it had exited, and
# a later run waited 855 SECONDS for a holder that did not exist. A lock that outlives its holder is
# worse than no lock, because it converts every subsequent run into a hang. The current design
# answers that with O_CLOEXEC plus PR_SET_PDEATHSIG(SIGKILL), and case 3 below is the only thing in
# this repository that observes it: hold the lock, SIGKILL the holder, and require the lock to be
# free immediately afterwards. If the descriptor ever leaks again, or the pdeathsig arming is lost,
# this case fails and names the wedge.
#
# WHAT THIS DOES NOT OBSERVE, stated rather than implied:
#   UNOBSERVED HERE: the commands' expensive halves. The inverse controls prove the run got PAST the
#   lock; they do not run `dotnet format`, cargo, or a container. A 40-minute polygon is not how a
#   lock is tested.
#   UNOBSERVED HERE: a leak into a build tool specifically. Case 3 kills a plain shell. A descriptor
#   leaking into an MSBuild daemon is the same defect seen through a different child, and O_CLOEXEC
#   is the property being checked.
#
# Usage:  maran lock selftest
# Exit:   0 only when every case passed. The OUTPUT is the verdict; the exit status exists so a CI
#         job can go red.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"
# For $SUITE_STATUS_DID_NOT_RUN: the status a refusing command owes is asserted below, and asserting
# it against a number written down here would let the two drift apart silently.
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

case "${1:-selftest}" in
  selftest) ;;
  -h|--help) echo "usage: maran lock selftest" >&2; exit 2 ;;
  *) echo "REFUSED: '$1' is no lock subcommand. The only one is 'selftest'." >&2; exit 2 ;;
esac

# The refusal message every locked command prints. Asserting on THIS string, and not on an exit
# code, is the rule from rules/testing.md: two harness defects found here in one day were
# exit-code-invisible.
refusal='holds the tree lock on this working tree'
# The third verdict, which a refusing command owes ON STDOUT. Asserting the refusal message alone
# was not enough and the gap was measured: with the lock held, `maran test rust` printed its refusal
# on stderr, wrote NOTHING to stdout, and returned 1 — indistinguishable, to a caller keeping stdout
# or reading `$?`, from a clean run and from a red one respectively.
did_not_run_line='VERDICT: DID NOT RUN'

passed=0
failed=0
findings=""

# note_pass / note_fail: one line per case, and the failures are collected and named at the end
# rather than ending the run at the first one.
note_pass() {
  passed=$(( passed + 1 ))
  echo "  PASS  $1"
}
note_fail() {
  failed=$(( failed + 1 ))
  findings="$findings
  $1"
  echo "  FAIL  $1"
  [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/          /'
  return 0
}

work="$(mktemp -d)"
holder_pid=""
# cleanup: measured defect — written as `[ -n "$holder_pid" ] && kill ...`, the test `[ -n "" ]`
# failed under `set -e` INSIDE THE EXIT TRAP, and bash then exited the script with status 1 after
# every case had passed. A green report with a red exit status is the shape that makes a CI job
# untrustworthy in both directions, so the branches are spelled out and the trap ends on a success.
cleanup() {
  if [ -n "$holder_pid" ]; then
    kill -9 "$holder_pid" 2>/dev/null || true
  fi
  rm -rf "$work"
  return 0
}
trap cleanup EXIT INT TERM

# start_holder: a real, separate process holding the real lock on this tree, exactly as a mutation
# run holds it. It is a separate process on purpose — a lock taken by this shell would be released
# by this shell's own exit and could not be SIGKILLed independently, which is the whole of case 3.
start_holder() {
  ( set +e
    # shellcheck disable=SC1091
    . "$root/scripts/lib/tree-lock.sh"
    if tree_lock_acquire "$root" 0 "lock selftest holder" >/dev/null 2>&1; then
      echo held >"$work/holder.state"
    else
      echo busy >"$work/holder.state"
      exit 1
    fi
    # No EXIT trap: the point of case 3 is that nothing this process does is what releases the lock.
    #
    # `exec` and not `while :; do sleep; done`, measured: a background `sleep` is a CHILD, it
    # inherits this script's stdout, and SIGKILLing its parent orphans it still holding the pipe —
    # so `maran lock selftest | tail` hung for two minutes with every case already finished. `exec`
    # makes the waiting process BE this shell's pid, so there is no descendant to orphan, and the
    # python lock helper's PR_SET_PDEATHSIG still names that same pid as the parent it dies with.
    exec sleep 3600
  ) >/dev/null 2>&1 &
  holder_pid=$!
  # `disown` so that bash does not announce the kill ("Killed  ( set +e; . ...")  in the middle of
  # the report. The announcement is emitted by the shell at a command boundary outside any function,
  # so no redirection around the kill or the wait suppresses it; taking the job out of the table is
  # what does. Nothing here waits on the job afterwards — the kernel releasing the lock is what is
  # being measured, and that is observed with tree_lock_probe, not with `wait`.
  disown "$holder_pid" 2>/dev/null || true
  local waited=0
  while [ ! -s "$work/holder.state" ]; do
    if ! kill -0 "$holder_pid" 2>/dev/null; then
      echo "the holder process died before it could take the lock" >&2
      return 1
    fi
    sleep 0.2
    waited=$(( waited + 1 ))
    if [ "$waited" -gt 100 ]; then
      echo "the holder process never reported a state within 20s" >&2
      return 1
    fi
  done
  [ "$(cat "$work/holder.state")" = held ]
}

stop_holder() {
  [ -n "$holder_pid" ] || return 0
  kill -9 "$holder_pid" 2>/dev/null || true
  holder_pid=""
  # The helper is killed by the kernel via PR_SET_PDEATHSIG, which is not synchronous with the
  # parent's death. Poll for the lock to become free rather than sleeping a guessed interval — but
  # bound it, because "it will be free eventually" is the claim under test.
  local waited=0
  while [ "$waited" -lt 50 ]; do
    if tree_lock_probe "$root" >/dev/null 2>&1; then
      echo "$waited"
      return 0
    fi
    sleep 0.2
    waited=$(( waited + 1 ))
  done
  echo "$waited"
  return 1
}

# run_capturing: runs a command with its output captured, and reports its exit status separately.
# A command's output is the observation here; the status is recorded beside it, never instead of it.
run_capturing() {
  local out="$1"
  shift
  local status=0
  "$@" >"$out" 2>&1 || status=$?
  echo "$status"
}

echo "== tree lock selftest =="
echo "tree: $(cd "$root" && pwd -P)"
echo

# A precondition, not a case: if a real mutation run is in flight, this selftest cannot take the
# lock and every result below would be a measurement of that run instead. Refuse rather than report.
if ! tree_lock_probe "$root" >/dev/null 2>&1; then
  echo "REFUSED: something already holds this tree's lock, so the selftest cannot control the" >&2
  echo "         conditions it is supposed to be creating. Re-run when it finishes." >&2
  tree_lock_probe "$root" >/dev/null || true
  exit 1
fi

# A log that is valid input to `maran polygon verify` — readable, and carrying nothing that could
# make it pass. The scoring verdict is irrelevant here; what matters is whether the run reached the
# scoring at all, which is what the inverse control observes.
printf 'this log is deliberately empty of test results\n' >"$work/polygon.log"

# ---------------------------------------------------------------------------------------------
# Case 1 — the PROCEED path (the inverse control). Lock free, and `maran polygon verify` must get
# past the lock into its own work. Without this case, every refusal below is also passed by a
# command that refuses everything.
# ---------------------------------------------------------------------------------------------
echo '-- case 1: lock free, maran polygon verify proceeds (inverse control)'
status="$(run_capturing "$work/free.out" bash "$root/scripts/lib/polygon-ci.sh" verify "$work/polygon.log")"
if grep -qF "$refusal" "$work/free.out"; then
  note_fail "polygon verify refused on the LOCK while the lock was free" "$(cat "$work/free.out")"
elif ! grep -q 'polygon suites discovered from the tree' "$work/free.out"; then
  note_fail "polygon verify did not reach its own work with the lock free (status $status)" \
    "$(cat "$work/free.out")"
else
  note_pass "polygon verify reached suite discovery with the lock free (status $status)"
fi
if grep -qF "$did_not_run_line" "$work/free.out"; then
  note_fail "polygon verify printed a DID NOT RUN verdict with the lock FREE — a command that
refuses unconditionally passes every refusal case above" "$(cat "$work/free.out")"
fi

# ---------------------------------------------------------------------------------------------
# Case 2 — the REFUSAL path. The same command, the same argument, one difference: a real holder.
# ---------------------------------------------------------------------------------------------
echo "-- case 2: a holder is alive, the same command must refuse"
if ! start_holder; then
  note_fail "could not start a lock holder, so the refusal path was never observed"
else
  status="$(run_capturing "$work/busy.out" bash "$root/scripts/lib/polygon-ci.sh" verify "$work/polygon.log")"
  if ! grep -qF "$refusal" "$work/busy.out"; then
    note_fail "polygon verify did NOT refuse while a holder was alive (status $status)" \
      "$(cat "$work/busy.out")"
  elif grep -q 'polygon suites discovered from the tree' "$work/busy.out"; then
    note_fail "polygon verify printed the refusal and then scored anyway (status $status)" \
      "$(cat "$work/busy.out")"
  elif [ "$status" = 0 ]; then
    note_fail "polygon verify refused in its output but exited 0, so no CI job would go red"
  elif ! grep -qF "$did_not_run_line" "$work/busy.out"; then
    note_fail "polygon verify refused without printing a DID NOT RUN verdict, so a caller cannot
tell 'a peer holds the lock' from 'this branch is red'" "$(cat "$work/busy.out")"
  elif [ "$status" != "$SUITE_STATUS_DID_NOT_RUN" ]; then
    note_fail "polygon verify refused but returned $status, and $SUITE_STATUS_DID_NOT_RUN is this
harness's status for a run that did not happen — 1 is what a SCORED red run returns"
  else
    note_pass "polygon verify refused, named the holder, printed DID NOT RUN and returned \
$status (not 0, and not the 1 a red run returns)"
  fi

  # The writer side, on the same holder: an exclusive acquire must not be granted twice. This is the
  # property `maran format` and `maran mutate` both rest on.
  if ( tree_lock_acquire "$root" 0 "selftest second writer" >"$work/second.out" 2>&1 ); then
    note_fail "a SECOND exclusive acquire succeeded while the lock was held — the lock grants twice"
  elif ! grep -qF "$refusal" "$work/second.out"; then
    note_fail "the second acquire failed without naming the lock as the reason" "$(cat "$work/second.out")"
  else
    note_pass "a second exclusive acquire was refused while the first holder was alive"
  fi
fi

# ---------------------------------------------------------------------------------------------
# Case 3 — the KILL. The 855-second failure, in a test. SIGKILL runs no trap and no EXIT handler, so
# nothing in userspace releases anything here; if the lock is free afterwards it is because the
# kernel dropped it, which is the only guarantee worth having.
# ---------------------------------------------------------------------------------------------
echo "-- case 3: the holder is SIGKILLed, the lock must be free without anyone clearing it"
if [ -z "$holder_pid" ]; then
  note_fail "no holder was alive to kill, so the kill path was never observed"
else
  freed_after=""
  if freed_after="$(stop_holder)"; then
    note_pass "the lock was free $(awk -v w="$freed_after" 'BEGIN{printf "%.1fs", w*0.2}') after SIGKILL, with nothing cleared by hand"
  else
    note_fail "the lock was STILL HELD 10s after its holder was SIGKILLed — a leaked descriptor or a lost PR_SET_PDEATHSIG; this is the 855-second wedge returning"
  fi

  # And the command must actually work again — a probe that says "free" is a claim about a file
  # descriptor; this is the claim a person cares about.
  status="$(run_capturing "$work/after.out" bash "$root/scripts/lib/polygon-ci.sh" verify "$work/polygon.log")"
  if grep -qF "$refusal" "$work/after.out"; then
    note_fail "polygon verify still refused after the holder was killed" "$(cat "$work/after.out")"
  elif ! grep -q 'polygon suites discovered from the tree' "$work/after.out"; then
    note_fail "polygon verify did not reach its own work after the holder was killed (status $status)" \
      "$(cat "$work/after.out")"
  else
    note_pass "polygon verify proceeded again after the killed holder (status $status)"
  fi
fi

# ---------------------------------------------------------------------------------------------
# Case 4 — the pending record: a mutation that a killed run left applied must stop a reader even
# though no lock is held any more. The lock and the record answer two different questions and the
# selftest would be half a test with only the first.
# ---------------------------------------------------------------------------------------------
echo "-- case 4: a killed run's PENDING record refuses a reader that holds no lock"
pending_dir="$(tree_lock_home)/pending"
mkdir -p "$pending_dir"
planted="$pending_dir/$(tree_lock_key "$root")-selftest$$.pending"
# A backup path that exists and DIFFERS from the file it names, which is what "a mutant is still
# applied" looks like on disk. Naming a file that is byte-identical would be cleared as a tidy-up
# and would test nothing.
printf 'not the current content of that file\n' >"$work/backup"
printf 'pid 999999\nfile scripts/maran\nbackup %s\nstarted %s\n' "$work/backup" "$(date -Is)" >"$planted"
status="$(run_capturing "$work/pending.out" bash "$root/scripts/lib/polygon-ci.sh" verify "$work/polygon.log")"
rm -f "$planted"
if ! grep -q 'was KILLED and its mutation is still in this tree' "$work/pending.out"; then
  note_fail "a planted pending record did not stop the reader (status $status)" "$(cat "$work/pending.out")"
elif [ "$status" = 0 ]; then
  note_fail "the reader named the pending record and still exited 0"
elif ! grep -qF "$did_not_run_line" "$work/pending.out"; then
  note_fail "the reader stopped on the pending record without printing a DID NOT RUN verdict" \
    "$(cat "$work/pending.out")"
elif [ "$status" != "$SUITE_STATUS_DID_NOT_RUN" ]; then
  note_fail "the reader stopped on the pending record but returned $status rather than \
$SUITE_STATUS_DID_NOT_RUN, so it is indistinguishable from a scored red run"
else
  note_pass "a planted pending record stopped the reader, which printed DID NOT RUN and returned \
$status"
fi
# The inverse control for case 4: with the record removed, the same command proceeds again.
status="$(run_capturing "$work/pending-gone.out" bash "$root/scripts/lib/polygon-ci.sh" verify "$work/polygon.log")"
if grep -q 'was KILLED and its mutation is still in this tree' "$work/pending-gone.out"; then
  note_fail "the reader still reported a pending record after it was removed" "$(cat "$work/pending-gone.out")"
elif ! grep -q 'polygon suites discovered from the tree' "$work/pending-gone.out"; then
  # The absence of the pending message is not enough: a run refused for ANY other reason would show
  # the same absence, and this case would then report a pass while nothing had proceeded. Measured —
  # against a deliberately broken lock it passed on absence alone while the command was refusing.
  note_fail "with the record removed the command still did not reach its own work (status $status)" \
    "$(cat "$work/pending-gone.out")"
else
  note_pass "with the record removed the same command proceeded (status $status)"
fi

echo
echo "cases passed: $passed   failed: $failed"
if [ "$failed" -gt 0 ]; then
  echo "LOCK SELFTEST FAILED — cases that failed:$findings"
  exit 1
fi
echo "LOCK SELFTEST OK — the lock refuses, lets clean runs through, and is released by the kernel"
echo "when its holder is killed. Every refusal above was paired with the same command succeeding."
exit 0
