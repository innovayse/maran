#!/usr/bin/env bash
# Signal disposition for `maran dev`: makes sure Ctrl+C-class interrupts (SIGINT) can reach the
# teardown trap even when the command was started in the background.
#
# THE QUESTION THIS UNIT ANSWERS: "if something sends this process SIGINT, will the teardown trap
# run?" A shell started as an asynchronous command (`maran dev &`) from a script, a supervisor or
# any shell without job control inherits SIGINT as IGNORED — the POSIX rule for background jobs —
# and bash cannot trap a signal that was ignored when it started, so `trap ... INT` is accepted and
# never fires. `kill -INT` then did nothing, and only SIGTERM stopped the stack (issue #395). At a
# terminal it never showed, because the tty delivers Ctrl+C to the whole process group and the
# foreground job does not inherit the ignore.
#
# The ignore is undone by re-executing the script once with the disposition reset to the default,
# which `exec` then preserves. Nothing else in bash can do it. perl is used because it is on every
# machine that has git; python3 is the fallback. With neither, the script says so and carries on:
# SIGTERM still stops the stack, and a warning is honest where silence would look like a fix.
#
# Functions:
#   dev_signals_int_ignored      succeeds when this process ignores SIGINT
#   dev_signals_restore_int      re-executes "$@" with SIGINT at its default when it was ignored

# dev_signals_int_ignored: reads the kernel's own answer (SigIgn in /proc/self/status, a hex mask
# in which bit 1 is SIGINT) instead of probing with `trap -p`, which reports what bash was told,
# not what the kernel will do. Where /proc is absent the answer is "no", which leaves behaviour
# exactly as it was.
dev_signals_int_ignored() {
  local mask
  mask="$(awk '/^SigIgn:/ {print $2}' /proc/self/status 2>/dev/null || true)"
  [ -n "$mask" ] || return 1
  [ $(( 0x$mask & 0x2 )) -ne 0 ]
}

# dev_signals_restore_int: call first thing, as `dev_signals_restore_int "$0" "$@"`. A no-op when
# SIGINT is already deliverable, so the re-executed process (where it is) never loops.
dev_signals_restore_int() {
  dev_signals_int_ignored || return 0
  if command -v perl >/dev/null 2>&1; then
    exec perl -e '$SIG{INT} = "DEFAULT"; exec @ARGV or die "exec: $!"' -- bash "$@"
  elif command -v python3 >/dev/null 2>&1; then
    exec python3 -c 'import os, signal, sys; signal.signal(signal.SIGINT, signal.SIG_DFL); os.execvp("bash", ["bash"] + sys.argv[1:])' "$@"
  fi
  echo "warning: SIGINT is ignored here (background start) and neither perl nor python3 can undo it;" >&2
  echo "         stop this run with SIGTERM (kill <pid>)." >&2
}
