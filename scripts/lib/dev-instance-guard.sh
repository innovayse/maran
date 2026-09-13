#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# WHAT MUST BE TRUE BEFORE A BRING-UP BINDS ANYTHING: nothing else already listens where this
# instance is about to, and no second run of this instance is in flight. Both answers come from
# facts the kernel keeps — the socket table and an flock — never from a process-name pattern, and
# both refusals name the thing in the way.
#
# What lives here:
#
#   dev_guard_listener_on_port  the `ss` line of whatever holds a local TCP port, empty if free
#   dev_guard_require_free_port refuses a bring-up whose port is taken, naming the holder
#   dev_guard_acquire_lock      at most one run per instance, for exactly as long as it runs
#
# Caller's contract: `dev_guard_acquire_lock` reads `$instance` and `$logs`, and leaves fd 9 held
# by this shell for the rest of the run — every child a stage spawns must be given `9>&-`.

# dev_guard_listener_on_port: the `ss` line of whatever is LISTENING on a local TCP port, empty
# when the port is free. This is how a port is checked BEFORE binding and how the refusal names
# its holder — `pgrep -f` is deliberately not used anywhere in this command, because a pattern can
# match the checker itself (it has), while a socket table cannot.
dev_guard_listener_on_port() {
  ss -ltnpH 2>/dev/null | awk -v port=":$1" '{ if (index($4, port) == length($4) - length(port) + 1) { print; exit } }'
}

# dev_guard_require_free_port: refuse the bring-up when something already listens where this
# instance is about to. Without this a second `maran dev` "succeeded": its own dotnet died on the
# bind, the FIRST stack answered the readiness poll, and the run reported a panel it was not
# running.
dev_guard_require_free_port() {
  local port="$1" name="$2" line
  line="$(dev_guard_listener_on_port "$port")"
  if [ -n "$line" ]; then
    echo "REFUSED — port $port ($name) is already in use:" >&2
    echo "    $line" >&2
    echo "another stack (or an orphan of a crashed one) holds it. Stop it first — 'maran dev --stop'" >&2
    echo "for the containers, or kill the pid above. The self-check uses its own ports (5081/5174)" >&2
    echo "precisely so it never meets this refusal while a real stack runs." >&2
    exit 1
  fi
}

# dev_guard_acquire_lock: at most one run per instance, held EXACTLY as long as this script runs.
# Every child is spawned with `9>&-` so the fd cannot outlive the run in somebody else's hands:
# the first version let children inherit it, and MSBuild's node-reuse daemons — which `dotnet`
# deliberately leaves running for fifteen minutes — were measured holding the flock after a green
# self-check had torn everything down, refusing every subsequent run while nothing of the stack
# survived. A refusal that names a stack that does not exist is the same defect as a pass that
# names one. Orphaned api/spa processes of a SIGKILLed run are the PORT PREFLIGHT's job, which
# names them from the socket table; the lock's only claim is "this script, once".
dev_guard_acquire_lock() {
  mkdir -p "$logs"
  exec 9>>"$logs/run-dev.lock"
  if ! flock -n 9; then
    echo "REFUSED — a 'maran dev' ($instance instance) is already running or its processes survive:" >&2
    sed 's/^/    lock holder: /' "$logs/run-dev.lock" >&2 || true
    echo "    lock file:   $logs/run-dev.lock" >&2
    echo "stop that run (Ctrl+C, or kill the pid and its children) and try again." >&2
    exit 1
  fi
  printf 'pid %s, started %s\n' "$$" "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" >"$logs/run-dev.lock"
}
