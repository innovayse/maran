#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# THE EXECUTION of a teardown, kept apart from the DECISION it carries out
# (`scripts/lib/dev-teardown-plan.sh`). One trap serves both modes; the self-check adds its own
# assertions afterwards, because a teardown that is not observed is a teardown that is believed.
#
# A run tears down what it started and nothing else. Two ways of getting that wrong were found by
# this command's first outside user, and both had the same shape — a decision made from a NAME
# instead of from a fact about ownership. The three exit paths that must leave a foreign container
# alone are a clean exit, a Ctrl+C, and a stage failing part-way through bring-up; `--stop` is the
# deliberate exception and announces itself, which is why it lives here beside them rather than
# pretending to be one of them.
#
# What lives here:
#
#   dev_teardown_kill_tree      signals a process and everything it started, deepest first
#   dev_teardown_stop_stack     executes the plan: processes, then the containers it names
#   dev_teardown_on_failure     the trap: names the stage, then stops
#   dev_teardown_all_containers `--stop`: the whole compose project, the shared database included
#
# Caller's contract: `dev_teardown_stop_stack` reads `$instance`, `$project`, `$compose`,
# `$agent_container`, `$socket_dir`, `$postgres_was_running` and the `${tail,spa,api}_pid` a
# bring-up published; `dev_teardown_on_failure` reads `$mode` and `$stage`.

# dev_teardown_kill_tree: signals a process and everything it started, deepest first.
#
# `kill $spa_pid` alone was not enough and the cost was visible: the SPA is started as a subshell
# that runs `npm`, which runs `vite`, which runs `node`. Killing the subshell left the node process
# holding port 5173, and the NEXT `maran dev` failed with "Port 5173 is already in use" — a stack
# broken by the teardown of the one before it. Children first, so a parent cannot re-parent them to
# init while it is being stopped.
dev_teardown_kill_tree() {
  local pid="$1" child
  for child in $(pgrep -P "$pid" 2>/dev/null); do
    dev_teardown_kill_tree "$child"
  done
  kill "$pid" 2>/dev/null || true
}

dev_teardown_stop_stack() { # tears down every process this script started, in reverse order
  trap - INT TERM EXIT
  echo
  echo "stopping..."
  [ -n "${tail_pid:-}" ] && dev_teardown_kill_tree "$tail_pid"
  [ -n "${spa_pid:-}" ] && dev_teardown_kill_tree "$spa_pid"
  [ -n "${api_pid:-}" ] && dev_teardown_kill_tree "$api_pid"
  wait 2>/dev/null || true
  if [ "$instance" = "selfcheck" ]; then
    # Both before the containers go: the agent is still running, so root's own hand removes what
    # root wrote into the bind mount, and the database server is still up, so the check's own
    # disposable database can still be dropped through it.
    docker exec "$agent_container" find /run/maran-dev -mindepth 1 -delete >/dev/null 2>&1 || true
    docker exec -e PGPASSWORD="${Database__Password:-maran_dev}" maran-postgres \
      dropdb -f --if-exists -U "${Database__Username:-maran_dev}" "$Database__Database" >/dev/null 2>&1 || true
  fi
  # `postgres_was_running` is settled before the trap is armed, so a Ctrl+C in the first
  # millisecond of stage 1 reads a real value rather than a default — and the default it would
  # fall back to (1) is the safe one: leave a container we cannot prove we started.
  while IFS= read -r plan_line; do
    [ -n "$plan_line" ] || continue
    if [ "$plan_line" = "stop postgres" ]; then
      echo "stopping maran-postgres (this run started it; it was not running before)"
    fi
    # shellcheck disable=SC2086
    # Deliberately unquoted: each line of the plan is an argument LIST this script itself wrote,
    # never a value from outside, and word splitting is how it becomes those arguments again.
    docker compose -f "$compose" $plan_line >/dev/null 2>&1 || true
  done < <(dev_teardown_plan "$instance" "$project" "${postgres_was_running:-1}")
  if [ "$instance" = "selfcheck" ]; then
    # After the container is gone: while it runs it holds the bind mount, and the directory
    # cannot be removed from under it.
    rm -rf "$socket_dir" 2>/dev/null || true
  fi
  echo "stopped."
}

# dev_teardown_on_failure: entered by the trap on signals and on any `set -e` failure. Naming the
# stage is the difference between a report and a mystery: every stage sets $stage first.
dev_teardown_on_failure() {
  local code=$?
  if [ "$code" -ne 0 ] && [ "$mode" = "selfcheck" ]; then
    echo "SELF-CHECK FAILED at stage: ${stage:-startup} (details above)" >&2
  fi
  dev_teardown_stop_stack
  exit "$code"
}

# dev_teardown_all_containers: `--stop` — both instances' containers, and an honest report of what
# a stop cannot reach.
#
# THE ONE EXIT PATH THAT DELIBERATELY REACHES PAST WHAT IT STARTED, and it says so before it
# does it. Every other path — a clean exit, Ctrl+C, a stage failing part-way through bring-up —
# stops only what that run started and leaves `maran-postgres` exactly as it found it. `--stop`
# is a different question being asked: its documented promise is that nothing this compose file
# describes is left running, and a stop that quietly left the database up would break that
# promise the way the trap used to break the other one. A developer who wants only the agent
# gone stops that container by name.
dev_teardown_all_containers() {
  local port line
  echo "--stop takes the WHOLE dev compose project down, the shared maran-postgres container"
  echo "included. Ctrl+C on a running 'maran dev' does not: it stops only what that run started."
  # --profile agent, or compose does not consider the agent service its business and leaves a
  # privileged root container running after a command whose whole promise is "nothing running".
  docker compose -f "$compose" -p maran-selfcheck --profile agent down --remove-orphans >/dev/null 2>&1 || true
  docker compose -f "$compose" -p maran --profile agent down
  rm -rf "$root/.dev-agent/selfcheck" 2>/dev/null || true
  # Orphaned api/spa processes are not this command's to kill — it cannot know they are ours —
  # but leaving them unmentioned is how the next bring-up gets refused with no explanation.
  for port in 5080 5173 5081 5174; do
    line="$(dev_guard_listener_on_port "$port")"
    [ -n "$line" ] && echo "still listening on :$port (not a container; kill it if it is a leftover): $line"
  done
  # Explicit, and not decoration: the loop's last command is a test that is FALSE whenever the
  # final port is free — the ordinary case — and a function whose body ends that way returns 1.
  # At top level that value was discarded by the `exit 0` that followed; from a function under
  # `set -e` it would end the run with a failure the command did not have.
  return 0
}
