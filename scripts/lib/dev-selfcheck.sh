#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# THE SELF-CHECK: one real operation through the whole product, observed on the system itself,
# then a teardown that is asserted rather than trusted — and, beside it, the inverse control that
# makes a green self-check worth believing at all (rules/testing.md: a refusing gate needs one,
# and a check that cannot be shown to fail is decoration).
#
# What lives here, in the order stages 6-9 run:
#
#   dev_selfcheck_inverse_control     `--break socket-group`: PASSES only when the check FAILS
#   dev_selfcheck_check               one named assertion, printing its own evidence either way
#   dev_selfcheck_api_call            one HTTP request, body captured, value checked by the caller
#   dev_selfcheck_json_field          first occurrence of a string field, order-independent
#   dev_selfcheck_operation           stage 6: an account, created by the agent, seen in passwd
#   dev_selfcheck_teardown            stage 7: the teardown, then the absence of everything
#   dev_selfcheck_teardown_plan_cases stage 8: the plan of the instance this run is NOT
#   dev_selfcheck_socket_cases        stage 9: the guard refuses a held socket, clears an abandoned one
#   dev_selfcheck_verdict             the count, and the evidence behind a green line
#
# Caller's contract: the stages share `$failures` (the count), `$health_payload`,
# `$sc_passwd_line` and the postgres facts `run-dev.sh` settled before the trap was armed. They
# are globals on purpose — each stage's evidence is quoted by the verdict at the end, and a
# verdict that cannot name what it is judging is the thing this file exists to prevent.

# dev_selfcheck_inverse_control: re-runs the self-check with the socket's group sabotaged and
# PASSES only when the check fails naming exactly that: the health gate's AGENT-NOT-CONNECTED plus
# a socket stat of `root root`. A pass here means the green self-check is worth believing; a check
# that stays green under this sabotage observes nothing.
dev_selfcheck_inverse_control() {
  local break_log="$root/.dev-logs/selfcheck-break.log"
  mkdir -p "$root/.dev-logs"
  echo "inverse control: re-running the self-check with the socket group sabotaged (agent gid 0)."
  echo "    this MUST fail at the health gate; its output goes to $break_log"
  if MARAN_SELFCHECK_BREAK=socket-group "$0" --selfcheck >"$break_log" 2>&1; then
    echo "INVERSE-CONTROL FAILED — the self-check PASSED against a socket the panel cannot open." >&2
    echo "The health gate observes nothing; do not believe a green self-check until this is fixed." >&2
    tail -20 "$break_log" >&2
    exit 1
  fi
  if grep -q "AGENT-NOT-CONNECTED" "$break_log" && grep -q "root root" "$break_log"; then
    echo "INVERSE-CONTROL OK — the self-check failed, and it named the sabotage:"
    grep -m1 "AGENT-NOT-CONNECTED" "$break_log" | sed 's/^/    /'
    grep -m1 "root root" "$break_log" | sed 's/^/    /'
    exit 0
  fi
  echo "INVERSE-CONTROL FAILED — the self-check failed, but not for the planted reason:" >&2
  tail -30 "$break_log" >&2
  exit 1
}

dev_selfcheck_check() { # named absence assertion: prints its own evidence either way
  local label="$1" ok="$2" detail="$3"
  if [ "$ok" = "yes" ]; then
    echo "     ok       $label"
  else
    echo "     FAILED   $label — $detail"
    failures=$((failures + 1))
  fi
}

# dev_selfcheck_api_call: one HTTP request, body captured, VALUE checked by the caller. `-f` is
# deliberately not used: it hides the response body, and the body is the diagnosis.
dev_selfcheck_api_call() {
  local out http args=(-sS --max-time 20 -H 'Content-Type: application/json')
  [ -n "${sc_token:-}" ] && args+=(-H "Authorization: Bearer $sc_token")
  out="$(curl "${args[@]}" -w $'\n%{http_code}' "$@")" || { echo "curl $* failed outright" >&2; return 1; }
  http="${out##*$'\n'}"
  out="${out%$'\n'*}"
  case "$http" in
    2*) printf '%s' "$out" ;;
    *) echo "HTTP $http from $*:" >&2; printf '%s\n' "$out" >&2; return 1 ;;
  esac
}

dev_selfcheck_json_field() { # first occurrence of a string field, order-independent
  grep -o "\"$2\":\"[^\"]*\"" <<<"$1" | head -1 | cut -d'"' -f4
}

# dev_selfcheck_operation: stage 6 — one real agent operation, end to end. A row in the panel's
# database is not the claim being tested; the system user inside the agent's container is.
dev_selfcheck_operation() {
  echo "6/$steps  one real agent operation, end to end"
  sc_name="sc$(date +%s | tail -c 6)"
  sc_pass='Selfcheck-2026-Pass!'

  resp="$(dev_selfcheck_api_call -X POST "$api_url/api/v1/setup" \
    -d "{\"token\":\"${Setup__Token:-dev-setup-token}\",\"username\":\"scadmin\",\"email\":\"scadmin@selfcheck.test\",\"password\":\"$sc_pass\"}")"
  echo "     setup    administrator '$(dev_selfcheck_json_field "$resp" username)' created"
  resp="$(dev_selfcheck_api_call -X POST "$api_url/api/v1/auth/login" \
    -d "{\"username\":\"scadmin\",\"password\":\"$sc_pass\"}")"
  sc_token="$(dev_selfcheck_json_field "$resp" accessToken)"
  [ -n "$sc_token" ] || { echo "login answered without an accessToken: $resp" >&2; exit 1; }
  resp="$(dev_selfcheck_api_call "$api_url/api/v1/accounts/plans")"
  sc_plan="$(dev_selfcheck_json_field "$resp" id)"
  [ -n "$sc_plan" ] || { echo "no plan id in: $resp" >&2; exit 1; }
  resp="$(dev_selfcheck_api_call -X POST "$api_url/api/v1/accounts" \
    -d "{\"name\":\"$sc_name\",\"primaryDomain\":\"$sc_name.test\",\"planId\":\"$sc_plan\"}")"
  case "$resp" in
    *"\"name\":\"$sc_name\""*) : ;;
    *) echo "the accounts endpoint did not answer with the account it was asked for: $resp" >&2; exit 1 ;;
  esac
  # The observation that makes this an AGENT operation and not a database row: the system user
  # must exist inside the agent's container, created by the root agent on the panel's command.
  sc_passwd_line=""
  for attempt in $(seq 30); do
    sc_passwd_line="$(docker exec "$agent_container" getent passwd "$sc_name" 2>/dev/null || true)"
    [ -n "$sc_passwd_line" ] && break
    sleep 1
  done
  if [ -z "$sc_passwd_line" ]; then
    echo "ACCOUNT-NOT-ON-SYSTEM — the api answered success but 'getent passwd $sc_name' inside" >&2
    echo "$agent_container finds nothing after 30s: the panel wrote a row, the agent created no user." >&2
    exit 1
  fi
  echo "     account  $sc_passwd_line   (getent inside $agent_container)"
}

# dev_selfcheck_teardown: stage 7 — the teardown, then the absence of everything, asserted. Both
# halves matter: that nothing of THIS run is left, and that nothing of anybody else's was touched.
dev_selfcheck_teardown() {
  echo "7/$steps  teardown, then the absence of everything, asserted"
  trap - INT TERM EXIT
  dev_teardown_stop_stack

  failures=0
  left="$(docker ps -a --format '{{.Names}}' | grep -x "$agent_container" || true)"
  dev_selfcheck_check "no $agent_container container remains" "$([ -z "$left" ] && echo yes || echo no)" "docker ps -a still lists: $left"
  dev_selfcheck_check "no socket file remains" "$([ ! -e "$socket_path" ] && echo yes || echo no)" "$socket_path still exists"
  dev_selfcheck_check "no socket directory remains" "$([ ! -e "$socket_dir" ] && echo yes || echo no)" "$socket_dir still exists: $(ls -la "$socket_dir" 2>/dev/null | tail -n +2 | tr '\n' ' ')"
  left="$(dev_guard_listener_on_port "$api_port")"
  dev_selfcheck_check "port $api_port is free again" "$([ -z "$left" ] && echo yes || echo no)" "$left"
  left="$(dev_guard_listener_on_port "$spa_port")"
  dev_selfcheck_check "port $spa_port is free again" "$([ -z "$left" ] && echo yes || echo no)" "$left"
  alive=""
  kill -0 "$api_pid" 2>/dev/null && alive="api $api_pid"
  kill -0 "$spa_pid" 2>/dev/null && alive="$alive spa $spa_pid"
  dev_selfcheck_check "no api/spa process of this run survives" "$([ -z "$alive" ] && echo yes || echo no)" "still alive:$alive"
  if [ "$(docker inspect -f '{{.State.Running}}' maran-postgres 2>/dev/null)" = "true" ]; then
    left="$(docker exec -e PGPASSWORD="${Database__Password:-maran_dev}" maran-postgres \
      psql -qtAX -U "${Database__Username:-maran_dev}" -d postgres \
      -c "select 1 from pg_database where datname = '$Database__Database'" 2>/dev/null || true)"
    dev_selfcheck_check "database $Database__Database is dropped" "$([ "$left" != "1" ] && echo yes || echo no)" "pg_database still lists it"
  else
    # An assertion that cannot observe says so instead of passing (rules/testing.md): postgres is
    # down here only when this check started it and stopped it again, AFTER the dropdb above ran.
    echo "     note     database drop UNOBSERVED HERE — postgres is stopped (this check started it, so it stopped it)"
  fi

  # WHAT THIS RUN DID NOT OWN. The teardown above is judged against a stack that demonstrably ran:
  # `/health` answered by value and the account appeared in the container's own passwd file, and
  # the absences above show it is gone again. That is the positive control this next assertion
  # needs — "the database container is still up" is true of a run that never started anything, and
  # a green line saying so would certify exactly the defect it is here to catch.
  echo "     control  the stack this teardown is judged against really ran and really came down:"
  echo "              health $health_payload"
  echo "              account $sc_passwd_line"
  pg_running_now="$(docker inspect -f '{{.State.Running}}' maran-postgres 2>/dev/null || true)"
  pg_health_now="$(docker inspect -f '{{.State.Health.Status}}' maran-postgres 2>/dev/null || true)"
  pg_started_now="$(docker inspect -f '{{.State.StartedAt}}' maran-postgres 2>/dev/null || true)"
  if [ "$postgres_was_running" -eq 1 ]; then
    # StartedAt is on the assertion, not just Running: a container this run stopped and started
    # again is "still running" too, and `docker compose up -d` recreating a drifted container is
    # precisely how a peer's 23-hour-old database came back as `Up 17 seconds`.
    dev_selfcheck_check "the pre-existing maran-postgres is untouched — running, healthy, never restarted" \
      "$([ "$pg_running_now" = "true" ] && [ "$pg_health_now" = "healthy" ] \
          && [ "$pg_started_now" = "$postgres_started_at" ] && echo yes || echo no)" \
      "running=$pg_running_now health=$pg_health_now startedAt=$pg_started_now (was $postgres_started_at)"
  else
    dev_selfcheck_check "maran-postgres is stopped again — this check started it, so this check stopped it" \
      "$([ "$pg_running_now" != "true" ] && echo yes || echo no)" \
      "it is still running=$pg_running_now"
  fi
}

# dev_selfcheck_teardown_plan_cases: stage 8 — the teardown plan, for BOTH instances and both
# ownership cases. Stage 7 observes this run's own teardown; this observes the branch this run is
# not: the `dev` instance, whose compose project is the same one the database lives in, which is
# where both teardown defects were. `dev_teardown_plan` is the list `dev_teardown_stop_stack`
# executes, so these four cases are assertions about what the command will actually do — not about
# what the file says.
dev_selfcheck_teardown_plan_cases() {
  echo "8/$steps  the teardown plan, for the instance this run is not"
  plan_dev_found="$(dev_teardown_plan dev maran 1 | tr '\n' '|')"
  plan_dev_started="$(dev_teardown_plan dev maran 0 | tr '\n' '|')"
  plan_self_found="$(dev_teardown_plan selfcheck maran-selfcheck 1 | tr '\n' '|')"
  plan_self_started="$(dev_teardown_plan selfcheck maran-selfcheck 0 | tr '\n' '|')"
  dev_selfcheck_check "dev + a database it FOUND running: stops the agent service by name, and nothing else" \
    "$([ "$plan_dev_found" = "-p maran --profile agent stop agent|" ] && echo yes || echo no)" \
    "the plan is: $plan_dev_found"
  dev_selfcheck_check "dev + a database it STARTED: stops the agent service, then that database" \
    "$([ "$plan_dev_started" = "-p maran --profile agent stop agent|stop postgres|" ] && echo yes || echo no)" \
    "the plan is: $plan_dev_started"
  dev_selfcheck_check "selfcheck + a database it FOUND running: downs its own project, and nothing else" \
    "$([ "$plan_self_found" = "-p maran-selfcheck --profile agent down --remove-orphans|" ] && echo yes || echo no)" \
    "the plan is: $plan_self_found"
  dev_selfcheck_check "selfcheck + a database it STARTED: downs its own project, then that database" \
    "$([ "$plan_self_started" = "-p maran-selfcheck --profile agent down --remove-orphans|stop postgres|" ] && echo yes || echo no)" \
    "the plan is: $plan_self_started"
  echo "              dev/found     $plan_dev_found"
  echo "              dev/started   $plan_dev_started"
  echo "              self/found    $plan_self_found"
  echo "              self/started  $plan_self_started"
}

# dev_selfcheck_socket_cases: stage 9 — the stale-socket guard, exercised on a fixture: the SAME
# function the agent stage calls (`dev_socket_reconcile`), not a re-statement of its reasoning
# somewhere the real bring-up never goes. Two cases, in the two directions that matter: a socket
# somebody is holding must SURVIVE, and a socket nobody is holding must GO. One of them alone is
# worthless — a guard that never removes anything passes the first, and the guard this replaces
# passed the second while failing the first in the field.
dev_selfcheck_socket_cases() {
  echo "9/$steps  the stale-socket guard: a held socket survives, an abandoned one is cleared"
  cases_dir="$logs/socket-cases"
  rm -rf "$cases_dir"
  mkdir -p "$cases_dir"
  case_socket="$cases_dir/agent.sock"
  case_log="$cases_dir/guard.log"
  if ! command -v python3 >/dev/null 2>&1; then
    # Not a pass with a note: without python3 there is no way to bind a socket to hold, so the
    # cases cannot run, and "no case ran" is a failure and never a pass (rules/testing.md).
    dev_selfcheck_check "the socket cases can run at all" "no" "python3 is absent, so no listener can be bound"
  else
    cat >"$cases_dir/holder.py" <<'HOLDER'
"""Binds and listens on the unix socket path given as its argument, then waits to be killed.

It is the fixture for the two stale-socket cases: while it lives the path is held by a real
listener, and a SIGKILL leaves the socket FILE behind with nothing behind it, which is exactly
the leftover the guard must still clear.
"""
import socket
import sys
import time

holder = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
holder.bind(sys.argv[1])
holder.listen(1)
time.sleep(900)
HOLDER
    python3 "$cases_dir/holder.py" "$case_socket" >/dev/null 2>&1 &
    holder_pid=$!
    for attempt in $(seq 50); do
      [ -S "$case_socket" ] && break
      sleep 0.1
    done
    if [ ! -S "$case_socket" ]; then
      kill -KILL "$holder_pid" 2>/dev/null || true
      dev_selfcheck_check "the fixture listener bound its socket" "no" "$case_socket never appeared"
    else
      held_inode="$(stat -c '%i' "$case_socket")"
      guard_verdict="held"
      dev_socket_reconcile "$case_socket" >"$case_log" 2>&1 || guard_verdict="refused"
      dev_selfcheck_check "a socket a FOREIGN process is holding is refused, not unlinked" \
        "$([ "$guard_verdict" = "refused" ] && [ -S "$case_socket" ] \
            && [ "$(stat -c '%i' "$case_socket" 2>/dev/null)" = "$held_inode" ] && echo yes || echo no)" \
        "verdict=$guard_verdict, file $( [ -e "$case_socket" ] && echo "still present inode $(stat -c '%i' "$case_socket")" || echo GONE ): $(head -1 "$case_log")"
      sed 's/^/              /' "$case_log"

      # SIGKILL, not a clean stop: a process that exits normally could unlink its own socket, and
      # then the second case would be measuring an absent file rather than an abandoned one.
      kill -KILL "$holder_pid" 2>/dev/null || true
      wait "$holder_pid" 2>/dev/null || true
      if [ ! -S "$case_socket" ]; then
        dev_selfcheck_check "the abandoned socket file outlives its process (the case is observable)" "no" \
          "$case_socket vanished when the holder was killed, so there is nothing to clear"
      else
        guard_verdict="cleared"
        dev_socket_reconcile "$case_socket" >"$case_log" 2>&1 || guard_verdict="refused"
        dev_selfcheck_check "a socket NOBODY is holding is still cleared" \
          "$([ "$guard_verdict" = "cleared" ] && [ ! -e "$case_socket" ] && echo yes || echo no)" \
          "verdict=$guard_verdict, file $( [ -e "$case_socket" ] && echo "STILL PRESENT" || echo gone ): $(head -1 "$case_log")"
        sed 's/^/              /' "$case_log"
      fi
    fi
  fi
  rm -rf "$cases_dir" 2>/dev/null || true
}

# dev_selfcheck_verdict: the count, and the evidence behind the green line. It exits the script
# either way — a self-check that returned to the caller would leave the interactive stream below
# it running against a stack it has already torn down.
dev_selfcheck_verdict() {
  echo
  if [ "$failures" -gt 0 ]; then
    echo "SELF-CHECK FAILED — the stack worked but the teardown left $failures thing(s) behind (named above)."
    exit 1
  fi
  echo "SELF-CHECK OK — stack up from nothing, health asserted by value, one real agent operation"
  echo "observed on the system, teardown left nothing of this run's and nothing of anybody else's,"
  echo "and the stale-socket guard was shown to refuse a held socket and still clear an abandoned"
  echo "one. Evidence:"
  echo "    health   $health_payload"
  echo "    account  $sc_passwd_line"
  echo "    postgres running=$pg_running_now health=$pg_health_now startedAt=$pg_started_now"
  echo "             (before this run: running=$([ "$postgres_was_running" -eq 1 ] && echo true || echo false), startedAt=$postgres_started_at)"
  echo "    logs     $logs/{api,frontend,database,agent-build}.log"
  exit 0
}
