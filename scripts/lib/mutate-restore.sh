#!/usr/bin/env bash
# `maran mutate`, step 3: the restore guard, the build directories, the banner and the toolchain and concurrency checks.
#
# SOURCED by scripts/lib/mutate.sh (split out of it, issue #355), in the order the run needs it. This
# file runs at the top level of that script and shares its variables, traps and `-euo pipefail`.

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

suite_require_toolchain "$stack" ||
  did_not_run "the toolchain cannot run the $stack suite (see above)."
# The parsers that will produce this run's verdict are proven able to see a failure BEFORE the
# suite runs, not after: a blind parser answers GREEN, and GREEN is the verdict an author quotes
# when arguing a check can be deleted.
suite_selftest_parsers "$stack" ||
  did_not_run "the log parsers are blind, so no score would mean anything."
if [ "$polygon" = 1 ]; then
  polygon_require_docker ||
    did_not_run "docker is not usable here, so the polygon lanes cannot run."
  export CARGO_TARGET_DIR="$host_target"
  mkdir -p "$host_target"
fi
suite_refuse_concurrency "$stack" "$work_root" ||
  did_not_run "another build is writing the outputs this run would score."
if [ "$polygon" = 1 ]; then
  polygon_note "in polygon mode this concurrency check is asked about the SNAPSHOT, not the tree:"
  polygon_note "no lane reads or writes agent/target, so another agent's cargo build cannot corrupt"
  polygon_note "this run's outputs. It can still compete for CPU, and is reported above as LOAD."
fi
echo

