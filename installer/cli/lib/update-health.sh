#!/usr/bin/env bash
# update-health.sh: the half of `maran update` and `maran rollback` that touches running services:
# migrating the schema with the new release's binary, restarting the two daemons, and deciding whether
# the panel that came back is healthy (issue #128).
#
# Sourced by /usr/local/sbin/maran after common.sh and release-layout.sh; never executed on its own.
# It writes no config and no state file: it runs `Maran.Host --migrate`, `systemctl restart` and
# `curl`, and reports.
#
# Failure convention. Every function here RETURNS non-zero with a one-line reason on stderr instead
# of ending the command through cli_fail, because its caller owns what happens next: a failed
# migration, restart or health gate is exactly the case in which update.sh must switch `current` back
# (docs/superpowers/notes/2026-11-04-panel-self-update-threat-note.md section 6), and an exit here
# would leave the half-updated state in place. A caller MUST test the status explicitly
# (`cli_update_migrate "$v" || ...`); the inherited `set -e` is switched off inside a function that
# runs as an `if` or `||` operand, so nothing below depends on it. Only a malformed argument, which
# is a bug in the caller and not a runtime outcome, ends the command through cli_fail. The one other
# cli_fail that can fire while a migration runs, cli_run_as_service refusing an untrusted panel.env,
# fires inside the command substitution that holds the migration, so it ends that subshell only and
# comes back here as a non-zero status with its message in the captured output.
#
# Nothing here prints a response body or any value read from panel.env: the health probes keep the
# body in a variable they match against and never echo it (rules/security.md item 8). The migration
# prints its own output, and only when it failed, capped to its last lines.

# The panel's and the agent's sockets. Both are DECIDED in installer/install.sh
# (MARAN_API_SOCKET_PATH and MARAN_AGENT_SOCKET_PATH); the CLI is installed on its own and does not
# source install.sh, so the values are repeated here and a change to either must change both.
readonly MARAN_UPDATE_API_SOCKET="/run/maran-api/api.sock"
readonly MARAN_UPDATE_AGENT_SOCKET="/run/maran/agent.sock"
readonly MARAN_UPDATE_POLL_INTERVAL=2
readonly MARAN_UPDATE_PROBE_TIMEOUT=5
# An upper bound on the gate's argument: a typo of minutes for seconds must not hold the lock for days.
readonly MARAN_UPDATE_MAX_WAIT=3600
# How much of a failed migration's output is shown: the end of a log is where the reason is.
readonly MARAN_UPDATE_MIGRATE_TAIL_LINES=40

# cli_update_restart_unit: restarts the one unit named $1 and says so on stderr when it fails.
# Type=simple means a zero status reports only that a process was spawned, which is why the caller
# follows the restarts with the health gate and does not trust this status as health.
cli_update_restart_unit() {
  local unit="$1"
  systemctl restart "$unit" || {
    echo "maran: systemctl restart ${unit} failed" >&2
    return 1
  }
}

# cli_update_restart_services: restarts the api, then the agent LAST. The agent is last so the new
# api never talks to an agent that is mid-restart; for the short window between the two restarts the
# new api drives the old agent, which the additive proto covers for one revision. /health/ready may
# answer 503 in that window, so the health gate runs only after this function has returned. The
# agent is not restarted when the api failed to: the caller rolls back, and a half-restarted pair is
# the state the rollback has to start from anyway.
cli_update_restart_services() {
  cli_update_restart_unit "$MARAN_CLI_API_UNIT" || return 1
  cli_update_restart_unit "$MARAN_CLI_AGENT_UNIT" || return 1
}

# cli_update_restart_for_rollback: restarts the agent, then the api, the reverse of the update.
# Both are attempted even when the first fails: a rollback that stops half-way leaves a worse state
# than one that tried everything it could, and the health gate that follows reports what is running.
cli_update_restart_for_rollback() {
  local failed=0
  cli_update_restart_unit "$MARAN_CLI_AGENT_UNIT" || failed=1
  cli_update_restart_unit "$MARAN_CLI_API_UNIT" || failed=1
  return "$failed"
}

# cli_update_migrate: brings the schema forward with the binary of release $1, the way
# installer/lib/65-schema.sh does: the panel's own binary, as the service account, from its own
# directory, with panel.env read in a subshell so no setting reaches this shell (cli_run_as_service
# in common.sh). The NEW release's binary runs because only the code that understands a migration
# may apply it; the previous release keeps running against the expanded schema until the restart
# (rules/architecture.md).
#
# The output is captured and shown only when the run failed, and then only its last
# MARAN_UPDATE_MIGRATE_TAIL_LINES lines, indented, on stderr: a good run prints nothing, and a bad
# one prints the part that says why. It is still required to be non-empty. A migration mode that
# found nothing to migrate exits 0 having done nothing, and "up to date" and "looked at nothing"
# read the same in a log, so a silent success is refused (rules/testing.md).
#   $1 = the release name, validated before a path is built from it.
cli_update_migrate() {
  local release="${1:-}" binary output status=0
  cli_version_is_release_name "$release" || cli_fail "'${release}' is not a release name"
  binary="${MARAN_LAYOUT_ROOT}/releases/${release}/api/Maran.Host"
  if [ ! -f "$binary" ] || [ -L "$binary" ] || [ ! -x "$binary" ]; then
    echo "maran: ${binary} is not an executable file; cannot migrate" >&2
    return 1
  fi
  output="$(cli_run_as_service "$binary" --migrate 2>&1)" || status=$?
  if [ "$status" -ne 0 ]; then
    printf '%s\n' "$output" | tail -n "$MARAN_UPDATE_MIGRATE_TAIL_LINES" | sed 's/^/  /' >&2
    echo "maran: applying the migrations of ${release} failed (exit ${status})" >&2
    return 1
  fi
  if ! grep -q . <<<"$output"; then
    echo "maran: the migration run of ${release} printed nothing, so it is not evidence that the schema is current" >&2
    return 1
  fi
}

# cli_update_api_get: one bounded GET of the path $1 on the api's unix socket; the remaining
# arguments are curl's. The user's curl configuration is not read (--disable) and no proxy is
# consulted (--noproxy), because a unix socket never goes through one and a proxy variable in the
# operator's shell must not be able to answer a health question. Prints what curl prints.
#   $1 = the path, starting with a slash.
cli_update_api_get() {
  local path="$1"
  shift
  curl --disable --noproxy '*' --silent --fail --connect-timeout "$MARAN_UPDATE_PROBE_TIMEOUT" \
    --max-time "$MARAN_UPDATE_PROBE_TIMEOUT" --unix-socket "$MARAN_UPDATE_API_SOCKET" "$@" \
    "http://localhost${path}"
}

# cli_update_probe_health: asks the three questions of the health gate once and prints, on stdout,
# the one-line reason for the first that fails; returns 0 and prints nothing when all three hold.
#   1. GET /health/ready on the api's socket answers 2xx (--fail turns 4xx/5xx into a failure).
#   2. The agent's socket exists and is a socket.
#   3. GET /health reports the agent as connected: {"agent":"connected","database":"reachable"}, the
#      word being a contract the installer and the end-to-end scripts match by value
#      (backend/src/Maran.Host/HealthChecks/Enums/AgentHealth.cs).
# The body of /health is held in a variable and matched; it is never printed. Each curl is bounded by
# a connect and a total timeout so a wedged panel cannot stall the gate past its own deadline.
cli_update_probe_health() {
  local body
  if ! cli_update_api_get /health/ready --output /dev/null; then
    echo "/health/ready did not answer 200 on ${MARAN_UPDATE_API_SOCKET}"
    return 1
  fi
  if [ ! -S "$MARAN_UPDATE_AGENT_SOCKET" ]; then
    echo "the agent socket ${MARAN_UPDATE_AGENT_SOCKET} does not exist as a socket"
    return 1
  fi
  if ! body="$(cli_update_api_get /health)"; then
    echo "/health did not answer on ${MARAN_UPDATE_API_SOCKET}"
    return 1
  fi
  if ! grep -Eq '"agent"[[:space:]]*:[[:space:]]*"connected"' <<<"$body"; then
    echo "/health does not report the agent as connected"
    return 1
  fi
}

# cli_update_wait_healthy: polls cli_update_probe_health every 2 seconds until it passes or the
# deadline of $1 seconds has gone. At least one probe always runs. On expiry it returns 1 after one
# line on stderr naming the probe that failed LAST; no response body is ever printed. When stderr is
# a terminal each poll also prints one progress line, so an operator watching a 120 second gate can
# tell it is alive; a pipe or a log gets nothing but the verdict.
#   $1 = the timeout in whole seconds, 1 to MARAN_UPDATE_MAX_WAIT.
cli_update_wait_healthy() {
  local timeout="${1:-}" deadline reason="no probe ran" remaining
  [[ "$timeout" =~ ^[0-9]+$ ]] && [ "$timeout" -ge 1 ] && [ "$timeout" -le "$MARAN_UPDATE_MAX_WAIT" ] \
    || cli_fail "the health timeout must be 1 to ${MARAN_UPDATE_MAX_WAIT} seconds"
  deadline=$((SECONDS + timeout))
  while true; do
    if reason="$(cli_update_probe_health)"; then
      return 0
    fi
    remaining=$((deadline - SECONDS))
    [ "$remaining" -gt 0 ] || break
    if [ -t 2 ]; then
      echo "  . waiting for the panel (${remaining}s left): ${reason}" >&2
    fi
    if [ "$remaining" -lt "$MARAN_UPDATE_POLL_INTERVAL" ]; then
      sleep "$remaining"
    else
      sleep "$MARAN_UPDATE_POLL_INTERVAL"
    fi
  done
  echo "maran: the panel did not become healthy within ${timeout}s; last check failed: ${reason}" >&2
  return 1
}
