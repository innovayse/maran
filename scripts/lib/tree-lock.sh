#!/usr/bin/env bash
# The working-tree mutation lock, and the crash record that makes a killed run recoverable.
#
# WHY THIS EXISTS, MEASURED
#
# `maran mutate` writes a mutation into the WORKING TREE, scores the suite, and restores. That is
# safe for one runner and unsafe for two, and the unsafety is silent: a run that scores while
# ANOTHER run's mutation is applied does not fail, it produces a plausible verdict about code
# nobody asked about. Reproduced here on 2026-09-08 — two `maran mutate` runs started in the same
# second against two different files both passed every existing guard, and both mutants were live
# in the tree at once, and one published a KILLED verdict naming a test the OTHER mutant had killed.
#
# WHY IT IS NOT THE GUARD THAT ALREADY EXISTS
#
# `suite_refuse_concurrency` observes BUILDS: cargo's flock on the target directory, and build
# processes whose cwd is inside the tree. It is blind to a mutation, and it caught the 5-second-
# offset reproduction only because the first run happened to be compiling at that instant. The two
# are complementary and neither subsumes the other:
#   - the build guard sees a bare `cargo test`, a `maran test`, an IDE build — none of which take
#     this lock, because none of them WRITE to the tree;
#   - this lock sees a mutation that is applied while no compiler is running, which is precisely
#     the window the reproduction fell through.
# So this is added beside that guard, not in place of it.
#
# WHY PER-TREE AND NOT PER-FILE OR PER-STACK
#
# Per-file protects nothing: run B mutating `name.rs` still SCORES against a tree carrying run A's
# mutation in `domain.rs`, and the verdict it prints is about neither experiment.
# Per-stack is tempting — a Rust mutant cannot change a backend test's result — and is still wrong:
# `maran mutate --polygon` rsyncs the WHOLE tree into its snapshot (its excludes are `.git/`,
# `target/`, `node_modules/`, `bin/`, `obj/`, `dist/` — every source file of every stack is copied),
# so a backend mutation in flight is frozen into a Rust polygon run's snapshot. A lock whose
# correctness depends on the reader remembering which files "count" is the kind of reasoning that
# failed here today. One tree, one mutation at a time.
#
# WHY flock AND NOT A PID FILE
#
# The common case in this repository is the holder being KILLED — twice in one day, each time
# leaving a mutation applied for minutes. A lock file holding a pid is exactly wrong for that: it
# outlives its owner, pids are reused, and a stale lock nobody can clear is worse than no lock. An
# `flock(2)` held on an open descriptor is released BY THE KERNEL when the process dies, for any
# reason, SIGKILL included — so a stale lock cannot exist by construction, and there is nothing to
# clear by hand. The pid and the file name are written BESIDE the lock as text, for the refusal
# message only; they are never consulted to decide whether the lock is held.
#
# WHAT THE LOCK DOES NOT DO, AND WHAT DOES
#
# The kernel releasing the lock does not restore the tree: a SIGKILLed bash runs no EXIT trap, so
# the mutation stays applied. The lock alone would therefore hand the NEXT run a clean lock and a
# dirty tree — the same wrong answer, one refusal later. So a killed run leaves a PENDING RECORD:
# written before the mutation lands, deleted only after a cmp-verified restore. Every run refuses to
# start while a pending record exists for this tree, names the file and the backup, and prints the
# recovery command. Because the check is made AFTER this run has taken the lock, any record still
# present belongs to a run that is no longer alive — no pid-liveness guessing is needed to say so.

# tree_lock_home: the fixed directory holding the lock and the pending records.
#
# Deliberately NOT `MARAN_MUTATE_BACKUP_DIR`: that variable is per-run and settable, and two agents
# with two different values for it would take two different locks and both proceed. The lock's path
# depends on the TREE and on nothing else a caller can vary.
tree_lock_home() {
  echo "${XDG_CACHE_HOME:-$HOME/.cache}/maran/tree-lock"
}

# tree_lock_key: a stable short name for one working tree, from its real path.
tree_lock_key() {
  local root="$1"
  printf '%s' "$(cd "$root" && pwd -P)" | sha256sum | cut -c1-16
}

# tree_lock_require_tool: refuses when python3 is missing.
#
# A lock that silently degrades to no lock is the shape rules/testing.md calls decoration: the run
# would proceed, print nothing, and the reader would believe it had been serialised.
tree_lock_require_tool() {
  if ! command -v python3 >/dev/null 2>&1; then
    echo "REFUSED: python3 is not on PATH, so this run cannot take the tree lock." >&2
    echo "         Running unlocked is not an option here: two mutations in one tree produce a" >&2
    echo "         plausible verdict about the wrong code." >&2
    return 1
  fi
  return 0
}

# tree_lock_acquire: takes the exclusive mutation lock on one working tree.
#
#   tree_lock_acquire <root> <wait-seconds> <what>
#
# WHY A HELPER PROCESS AND NOT `exec 9>lock; flock -n 9`
#
# That was the first implementation and it WEDGED THE TREE, measured: an flock lives on the open
# file description, and a descriptor opened by bash is inherited across fork AND exec. The processes
# a test run leaves behind are exactly the ones that outlive it — MSBuild's `nodeReuse` daemons,
# VBCSCompiler, cargo's workers — and eight MSBuild daemons were observed holding fd 9 on the lock
# file minutes after the run that opened it had exited and released it. The next run then waited for
# a holder that did not exist, on a lock nothing could clear, which is the precise failure this file
# claims to make impossible. Bash cannot set close-on-exec on a redirection (measured on bash 5.2:
# an fd from `exec {v}>file` is still visible in an exec'd child), so the descriptor cannot stay in
# the shell.
#
# So the lock is held by a small python process that (a) opens the file O_CLOEXEC, so no build tool
# can ever inherit it, and (b) asks the kernel for PR_SET_PDEATHSIG(SIGKILL), so the kernel destroys
# it the moment this shell dies — by any means, SIGKILL included. Both halves of the guarantee are
# the kernel's, which is the property that made an flock the right primitive in the first place.
tree_lock_acquire() {
  local root="$1" wait_for="${2:-0}" what="${3:-mutation}"
  tree_lock_require_tool || return 1

  local home key lock
  home="$(tree_lock_home)"
  mkdir -p "$home/pending"
  key="$(tree_lock_key "$root")"
  lock="$home/tree-$key.lock"
  TREE_LOCK_FILE="$lock"
  TREE_LOCK_HOLDER="$lock.holder"
  TREE_LOCK_STATUS="$(mktemp)"

  python3 - "$lock" "$TREE_LOCK_STATUS" "$wait_for" <<'PYHOLD' &
"""Holds one working tree's mutation lock for exactly as long as its parent shell lives."""
import ctypes
import fcntl
import os
import signal
import sys
import time

PR_SET_PDEATHSIG = 1
path, status, wait_for = sys.argv[1], sys.argv[2], float(sys.argv[3])

ctypes.CDLL("libc.so.6", use_errno=True).prctl(PR_SET_PDEATHSIG, signal.SIGKILL)
# The parent can die between the fork and the prctl above, in which case the signal never comes;
# reparenting to init is how that is noticed, and it is checked AFTER arming rather than before.
if os.getppid() == 1:
    os._exit(0)

# O_CLOEXEC is the whole point: a build tool this run spawns must never inherit the lock.
handle = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_CLOEXEC, 0o644)
deadline = time.monotonic() + wait_for
while True:
    try:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        break
    except OSError:
        if time.monotonic() >= deadline:
            with open(status, "w", encoding="utf-8") as note:
                note.write("BUSY\n")
            os._exit(1)
        time.sleep(0.5)

with open(status, "w", encoding="utf-8") as note:
    note.write("HELD\n")
signal.pause()
PYHOLD
  TREE_LOCK_PID=$!

  # The shell polls the helper's status file rather than blocking, so it can say every 15 seconds
  # who it is waiting for. A wait that keeps saying it is waiting cannot be mistaken for a hang, and
  # "an empty output file is indistinguishable from a dead process" has already cost this repository
  # an afternoon.
  local announced=0 waited=0 state=""
  # Announcing is a function because it is owed on BOTH exits from the poll below: an immediate
  # refusal (the default) reaches the BUSY branch before the first tick, and an earlier version
  # therefore printed its closing advice with nobody named above it.
  _tree_lock_announce() {
    [ "$announced" = 1 ] && return 0
    announced=1
    if [ "$wait_for" -gt 0 ]; then
      echo "WAITING: another mutation run holds the tree lock on this working tree." >&2
    else
      echo "REFUSED: another mutation run holds the tree lock on this working tree." >&2
    fi
    # `>&2 2>/dev/null` and not the other order: with `2>/dev/null` first, `>&2` then aims stdout
    # at the null device the line just opened, and the holder is silently never named. Measured.
    sed 's/^/  holder /' "$TREE_LOCK_HOLDER" >&2 2>/dev/null || echo "  holder (unnamed)" >&2
    echo "  Two mutations in one tree cannot both be measuring: the second one scores a suite" >&2
    echo "  against the first one's mutant and prints a plausible verdict about the wrong code." >&2
  }
  while :; do
    state="$(cat "$TREE_LOCK_STATUS" 2>/dev/null || true)"
    case "$state" in
      HELD*)
        printf 'pid %s\nwhat %s\nstarted %s\ntree %s\n' \
          "$$" "$what" "$(date -Is)" "$(cd "$root" && pwd -P)" >"$TREE_LOCK_HOLDER"
        TREE_LOCK_HELD=1
        rm -f "$TREE_LOCK_STATUS"
        echo "tree lock: HELD on $(cd "$root" && pwd -P) ($lock)"
        return 0
        ;;
      BUSY*)
        _tree_lock_announce
        if [ "$wait_for" -gt 0 ]; then
          echo "  waited ${wait_for}s and the lock is still held. Nothing was mutated." >&2
        else
          echo "  Re-run when it finishes, or queue behind it with: --wait <seconds>" >&2
        fi
        rm -f "$TREE_LOCK_STATUS"
        TREE_LOCK_PID=""
        return 1
        ;;
    esac
    if ! kill -0 "$TREE_LOCK_PID" 2>/dev/null; then
      echo "REFUSED: the tree-lock helper died without answering. Nothing was mutated." >&2
      rm -f "$TREE_LOCK_STATUS"
      TREE_LOCK_PID=""
      return 1
    fi
    [ "$waited" -ge 1 ] && _tree_lock_announce
    [ "$announced" = 1 ] && [ $(( waited % 15 )) -eq 0 ] && [ "$waited" -gt 0 ] &&
      echo "  waiting for the tree lock — $(( wait_for - waited ))s left of ${wait_for}s" >&2
    sleep 1
    waited=$(( waited + 1 ))
  done
}

# tree_lock_release: kills the helper, which is what makes the kernel drop the lock.
#
# Calling this is a courtesy, not the mechanism: PR_SET_PDEATHSIG destroys the helper when this
# shell exits however it exits, which is the whole reason the lock is not a file full of pids.
tree_lock_release() {
  [ -n "${TREE_LOCK_PID:-}" ] && kill "$TREE_LOCK_PID" 2>/dev/null
  [ "${TREE_LOCK_HELD:-0}" = 1 ] && rm -f "$TREE_LOCK_HOLDER"
  TREE_LOCK_PID=""
  TREE_LOCK_HELD=0
  return 0
}

# tree_lock_probe: reports whether ANOTHER process holds the tree lock, without taking it.
#
# For readers rather than writers — `maran test` scores a suite it did not mutate, and a count taken
# while somebody's mutant is in the tree is not a measurement of this repository. The answer comes
# from the kernel: a killed mutation run's lock is already gone by the time this asks, so there is
# no liveness heuristic here and nothing to time out.
tree_lock_probe() {
  local root="$1"
  if ! command -v python3 >/dev/null 2>&1; then
    echo "UNOBSERVED HERE: python3 is missing, so this command cannot tell whether a mutation is"
    echo "UNOBSERVED HERE: in flight in this working tree."
    return 0
  fi
  local lock
  lock="$(tree_lock_home)/tree-$(tree_lock_key "$root").lock"
  [ -e "$lock" ] || return 0
  if python3 - "$lock" <<'PYPROBE'
"""Exits 0 when the tree's mutation lock is free, 1 when a mutation run holds it."""
import fcntl
import os
import sys

handle = os.open(sys.argv[1], os.O_WRONLY | os.O_CLOEXEC)
try:
    fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
except OSError:
    sys.exit(1)
sys.exit(0)
PYPROBE
  then
    return 0
  fi
  echo "REFUSED: a mutation run holds the tree lock on this working tree — a mutant is applied to" >&2
  echo "         the source right now, so this count would be a measurement of somebody's" >&2
  echo "         experiment. (rules/testing.md: a count taken while another build is running is" >&2
  echo "         not a measurement; the same is true, and worse, of a mutated tree.)" >&2
  sed 's/^/  holder /' "$lock.holder" >&2 2>/dev/null || true
  return 1
}

# tree_lock_pending_file: the path of THIS run's crash record.
tree_lock_pending_file() {
  local root="$1"
  echo "$(tree_lock_home)/pending/$(tree_lock_key "$root")-$$.pending"
}

# tree_lock_mark_pending: records what would have to be put back if this run were killed.
#
#   tree_lock_mark_pending <root> <repo-relative file> <backup path>
#
# Written BEFORE the mutation is applied. The ordering is the point: a record written afterwards
# would be missing for exactly the runs that were killed between the write and the record.
tree_lock_mark_pending() {
  local root="$1" file="$2" backup="$3" marker
  marker="$(tree_lock_pending_file "$root")"
  mkdir -p "$(dirname "$marker")"
  printf 'pid %s\nfile %s\nbackup %s\nstarted %s\n' "$$" "$file" "$backup" "$(date -Is)" >"$marker"
}

# tree_lock_clear_pending: deletes this run's crash record, after a verified restore.
tree_lock_clear_pending() {
  rm -f "$(tree_lock_pending_file "$1")"
}

# tree_lock_refuse_pending: refuses to start while an earlier run left a mutation in the tree.
#
#   tree_lock_refuse_pending <root> [readonly]
#
# Called by a WRITER only AFTER it holds the lock, which is what makes the answer certain rather
# than a guess: no other mutation run can be alive, so every record still on disk belongs to a run
# that is gone. A record whose file already matches its backup is a run that restored and died
# before it could tidy up — that one is cleared silently, because there is nothing wrong with the
# tree.
#
# A READER (`maran test`) holds no lock, so it passes `readonly` and deletes nothing. The
# distinction is not tidiness: tidying away a record whose owner is still alive would throw away a
# live run's only backup, which is the undo this whole mechanism exists to preserve.
tree_lock_refuse_pending() {
  local root="$1" mode="${2:-clean}" key found=0 marker
  key="$(tree_lock_key "$root")"
  for marker in "$(tree_lock_home)/pending/$key-"*.pending; do
    [ -e "$marker" ] || continue
    [ "$marker" = "$(tree_lock_pending_file "$root")" ] && continue
    local file backup
    file="$(sed -n 's/^file //p' "$marker")"
    backup="$(sed -n 's/^backup //p' "$marker")"
    if [ -n "$backup" ] && [ -e "$backup" ] && cmp -s "$backup" "$root/$file"; then
      # `rmdir` and not `rm -rf`: the run directory is removed only if clearing the backup left it
      # empty, so a directory still holding somebody's logs is never destroyed by a tidy-up.
      [ "$mode" = clean ] && { rm -f "$marker" "$backup"; rmdir "$(dirname "$backup")" 2>/dev/null || true; }
      continue
    fi
    found=1
    echo "REFUSED: an earlier mutation run was KILLED and its mutation is still in this tree." >&2
    sed 's/^/  /' "$marker" >&2
    if [ -n "$backup" ] && [ -e "$backup" ]; then
      echo "  The original is in the backup above. Recover it with:" >&2
      echo "    maran mutate --recover" >&2
      echo "  Do NOT run 'git checkout --' on it: that discards uncommitted work (rules/git.md)." >&2
    else
      echo "  THE BACKUP IS GONE TOO. This tree must be repaired by hand; nothing here can do it." >&2
    fi
  done
  [ "$found" = 0 ]
}

# tree_lock_recover: puts back every mutation a killed run left behind, and verifies it with cmp.
#
# Runs under the lock like everything else, so it cannot fight a live run. A restore is only
# reported when `cmp` agrees afterwards; anything else keeps the backup and says so, because a
# restore that says it worked and did not is the failure this whole harness exists to refuse.
tree_lock_recover() {
  local root="$1" key marker any=0 rc=0
  key="$(tree_lock_key "$root")"
  for marker in "$(tree_lock_home)/pending/$key-"*.pending; do
    [ -e "$marker" ] || continue
    [ "$marker" = "$(tree_lock_pending_file "$root")" ] && continue
    any=1
    local file backup
    file="$(sed -n 's/^file //p' "$marker")"
    backup="$(sed -n 's/^backup //p' "$marker")"
    if [ -z "$backup" ] || [ ! -e "$backup" ]; then
      echo "CANNOT RECOVER $file: the backup named in $marker is gone." >&2
      rc=1
      continue
    fi
    if cmp -s "$backup" "$root/$file"; then
      echo "ALREADY CLEAN: $file is byte-identical to the backup taken before that run (cmp)."
      rm -f "$marker" "$backup"
      rmdir "$(dirname "$backup")" 2>/dev/null || true
      continue
    fi
    cp "$backup" "$root/$file"
    touch "$root/$file"
    if cmp -s "$backup" "$root/$file"; then
      echo "RECOVERED: $file is byte-identical to the pre-mutation original (verified with cmp)."
      rm -f "$marker" "$backup"
      rmdir "$(dirname "$backup")" 2>/dev/null || true
    else
      echo "RECOVERY FAILED: $file still differs from $backup — the backup is KEPT." >&2
      rc=1
    fi
  done
  [ "$any" = 0 ] && echo "nothing to recover: this tree has no mutation left behind by a killed run."
  return $rc
}
