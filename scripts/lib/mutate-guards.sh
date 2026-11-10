#!/usr/bin/env bash
# `maran mutate`, step 2: --recover, the refusals that only read, the tree lock and the run's private directory and snapshot.
#
# SOURCED by scripts/lib/mutate.sh (split out of it, issue #355), in the order the run needs it. This
# file runs at the top level of that script and shares its variables, traps and `-euo pipefail`.

# --recover is its own command: it takes the same tree lock as a run, so it cannot fight a live one,
# and everything it finds afterwards therefore belongs to a run that is no longer alive.
if [ "$recover" = 1 ]; then
  tree_lock_acquire "$root" "$wait_for" "recovery" ||
    did_not_run "the tree lock is held by a live run (named above)."
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
# was measured blind to a second mutation run twice on 2026-09-08. Two `maran mutate` runs started in the same second
# both passed it, both applied a mutant to this tree, and one of them published a KILLED verdict
# naming a test the OTHER agent's mutant had killed. See scripts/lib/tree-lock.sh for why the lock
# is per-tree, why it is an flock, and why a killed holder leaves no stale lock.
tree_lock_acquire "$root" "$wait_for" "mutate $file" ||
  did_not_run "the tree lock is held by another run (named above)."
trap tree_lock_release EXIT INT TERM

# Under the lock, no other mutation run can be alive — so anything still recorded as pending was
# left by a run that was KILLED, and its mutation is still in these files. Refuse rather than score.
tree_lock_refuse_pending "$root" ||
  did_not_run "a killed run left its mutant in this tree (named above)."

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
