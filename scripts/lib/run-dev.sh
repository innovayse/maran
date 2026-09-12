#!/usr/bin/env bash
# Runs the whole Maran stack for local development: the PostgreSQL container, the ROOT AGENT (in
# the polygon container, with MariaDB inside), the API and the SPA. Docker carries dev
# dependencies only (rules/architecture.md), so the API and the SPA run natively against it —
# exactly as they do on a server, where no container is involved.
#
# THE AGENT IS PART OF THE STACK, BY DEFAULT. This script once started a database, an API and an
# SPA and called that "the whole stack", so every local run and every browser pass drove a panel
# whose agent was permanently `unavailable` — and three "live" passes were attempted in exactly
# that mode before anyone noticed. Nearly every interesting failure in this product happens on the
# agent's side of the socket, so the agent is on unless `--no-agent` says otherwise, and BOTH
# modes now assert what `/health` actually answers instead of printing it and hoping: `connected`
# with the agent, `unavailable` without — a panel that silently starts agentless is the trap this
# arrangement exists to end. The agent runs in the polygon container, as root; the argument for
# that placement, and for how the socket is reached without a `chown`, is in
# docker-compose.dev.yml beside the service.
#
# TWO INSTANCES OF THE STACK CAN COEXIST. Everything that names or binds — ports, the compose
# project, the agent container, the socket directory, the log directory, the lock — is derived
# from one instance name, so `maran dev --selfcheck` proves the bring-up end to end beside a
# stack that is already running instead of fighting it for :5080 and :5173.
#
# A RUN TEARS DOWN WHAT IT STARTED AND NOTHING ELSE. Two ways of getting that wrong were found by
# this command's first outside user, and both had the same shape — a decision made from a NAME
# instead of from a fact about ownership. A Ctrl+C ran `docker compose stop` with no service, so
# it stopped the shared `maran-postgres` a peer had been using for a day; and the stale-socket
# guard asked whether THIS instance's container was running, so it unlinked a socket a different
# container was holding. Since then: `postgres_was_running` is settled before the trap is armed
# and honoured on every exit path in both instances, a running database container is reused
# rather than reconciled by `up -d`, the compose `stop` names the agent service, and the socket
# question is answered by a connect() to the socket rather than by a container's name. The three
# exit paths that must leave a foreign container alone are a clean exit, a Ctrl+C, and a stage
# failing part-way through bring-up; `--stop` is the deliberate exception and announces itself.
#
# THIS FILE IS THE COMMAND, NOT THE MACHINERY. What is left here is what a reader must hold to
# understand a run: which instance it is, what it refuses before it binds anything, the order of
# the stages, and where the exits are. Each mechanism is a unit of its own beside it, because at
# 1112 lines this file was past the review trigger rules/architecture.md sets at 400, and a file
# nobody can hold in their head is a file whose next defect hides in it — two of this command's
# were found by an outside user rather than by review. The parts:
#
#   dev-instance-guard.sh  ports and the per-instance lock: what must be true before binding
#   dev-stages.sh          the five bring-up stages, one function each
#   dev-readiness.sh       the gates each stage ends at, answered by what the process reports
#   dev-socket.sh          the stale-socket guard: is anything LISTENING, and may the file go
#   dev-teardown-plan.sh   the teardown DECISION, as data, so a check can read it
#   dev-teardown.sh        the execution of that decision, and `--stop` beside it
#   dev-selfcheck.sh       stages 6-9, the verdict, and the `--break` inverse control
#
# Usage:
#   scripts/maran dev              start everything, stream logs, stop cleanly on Ctrl+C
#   scripts/maran dev --no-agent   start without the agent (API and SPA work only, loudly)
#   scripts/maran dev --stop       stop every container this compose file describes, the SHARED
#                                  database included — the one exit path that reaches past what a
#                                  run started, and it says so before it does it
#   scripts/maran dev --selfcheck  stand a SECOND stack up from nothing on its own ports, assert
#                                  /health's values, drive one real agent operation through the
#                                  API, tear down, assert nothing of this run is left AND that
#                                  nothing of anybody else's was touched, then assert the teardown
#                                  plan of BOTH instances and exercise the stale-socket guard in
#                                  both directions on a fixture
#   scripts/maran dev --selfcheck --break socket-group
#                                  the inverse control (rules/testing.md: a refusing gate needs
#                                  one): sabotage the socket's group on purpose and PASS only if
#                                  the self-check fails naming exactly that
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
compose="$root/docker/docker-compose.dev.yml"

# shellcheck disable=SC1091
. "$root/scripts/dev"

# The units this command is assembled from. Sourced here, all of them, before any of them is
# called: a bring-up stage calls a readiness gate which calls the socket guard, and an order that
# happened to work because of where a call sat would be a trap for the next edit.
# shellcheck disable=SC1091
. "$root/scripts/lib/dev-instance-guard.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/dev-socket.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/dev-readiness.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/dev-teardown-plan.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/dev-teardown.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/dev-stages.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/dev-selfcheck.sh"

usage() {
  echo "usage: maran dev [--no-agent] | --stop | --selfcheck [--break socket-group]" >&2
  exit 1
}

mode="dev"
with_agent=1
break_kind=""
while [ $# -gt 0 ]; do
  case "$1" in
    --no-agent)  with_agent=0 ;;
    --stop)      mode="stop" ;;
    --selfcheck) mode="selfcheck" ;;
    --break)     [ $# -ge 2 ] || usage; break_kind="$2"; shift ;;
    --break=*)   break_kind="${1#--break=}" ;;
    *) usage ;;
  esac
  shift
done
if [ -n "$break_kind" ] && { [ "$mode" != "selfcheck" ] || [ "$break_kind" != "socket-group" ]; }; then
  echo "--break socket-group is only meaningful with --selfcheck" >&2
  exit 1
fi
if [ "$mode" = "selfcheck" ] && [ "$with_agent" -eq 0 ]; then
  echo "--selfcheck exists to prove the agent half; combining it with --no-agent proves nothing" >&2
  exit 1
fi

# The inverse control runs the self-check as a child and judges its FAILURE, so it never reaches
# the instance parameters below (dev-selfcheck.sh; it exits either way).
if [ -n "$break_kind" ]; then
  dev_selfcheck_inverse_control
fi

# ---------------------------------------------------------------------------------------------
# Instance parameters. The `dev` instance is the stack a developer works against, on the ports
# the rest of the repository documents; `selfcheck` is the same stack under different names so
# both can exist at once. The self-check's database lives on the SHARED postgres server rather
# than a second container, deliberately: `maran drift` reaches the database through the
# `maran-postgres` container by name, and a private database name on the shared server is the
# already-supported override path (`Database__Database=x maran dev`) — the same thing both live
# runs did by hand (`maran_liverun`, `maran_stackproof`).
# ---------------------------------------------------------------------------------------------
if [ "$mode" = "selfcheck" ]; then
  instance="selfcheck"
  api_port=5081 spa_port=5174
  project="maran-selfcheck"
  agent_container="maran-selfcheck-agent"
  socket_dir="$root/.dev-agent/selfcheck"
  logs="$root/.dev-logs/selfcheck"
else
  instance="dev"
  api_port=5080 spa_port=5173
  project="maran"
  agent_container="maran-agent-dev"
  socket_dir="$root/.dev-agent"
  logs="$root/.dev-logs"
fi
api_url="http://127.0.0.1:$api_port"
spa_url="http://127.0.0.1:$spa_port"
socket_path="$socket_dir/agent.sock"
agent_binary="$root/agent/target/debug/maran-agent"

# Passed to compose. The gid gives the agent process uid 0 and THIS gid — the production
# `User=root` / `Group=maran` pair with the developer's group in place of maran's, which is the
# whole no-chown story (docker-compose.dev.yml). The container name and socket directory are the
# two things a second instance must not share.
MARAN_DEV_UID="$(id -u)"
MARAN_DEV_GID="$(id -g)"
MARAN_AGENT_CONTAINER="$agent_container"
MARAN_AGENT_SOCKET_DIR="$socket_dir"
export MARAN_DEV_UID MARAN_DEV_GID MARAN_AGENT_CONTAINER MARAN_AGENT_SOCKET_DIR

# The inverse control, honored only inside the self-check instance: force the agent's gid to 0 so
# the socket comes up `root:root 0660`, which the uid-1000 panel cannot open. A self-check that
# cannot be made to fail is decoration (rules/testing.md), and this is the deliberate breakage
# that proves the group mechanism is load-bearing.
if [ "$instance" = "selfcheck" ] && [ "${MARAN_SELFCHECK_BREAK:-}" = "socket-group" ]; then
  MARAN_DEV_GID=0
  echo "BREAK ACTIVE (socket-group): agent gid forced to 0 — the socket will be root:root and the panel MUST fail to reach it"
fi

# strip_env_applied: forget that `.env` supplied these names, because THIS RUN overrides them on
# purpose. `scripts/dev` re-applies any name listed in MARAN_ENV_APPLIED every time it is
# re-sourced — which `maran drift` does — so an override exported here would be silently undone in
# the child unless the name is removed from that list first.
strip_env_applied() {
  local names=" ${MARAN_ENV_APPLIED:-} " name
  for name in "$@"; do
    names="${names// $name / }"
  done
  MARAN_ENV_APPLIED="${names# }"
  MARAN_ENV_APPLIED="${MARAN_ENV_APPLIED% }"
  export MARAN_ENV_APPLIED
}

if [ "$instance" = "selfcheck" ]; then
  Database__Database="maran_selfcheck"
  export Database__Database
  strip_env_applied Database__Database
fi

# --stop: both instances' containers, and an honest report of what a stop cannot reach
# (dev-teardown.sh — it is the one exit path that deliberately reaches past what a run started).
if [ "$mode" = "stop" ]; then
  dev_teardown_all_containers
  exit 0
fi

# ---------------------------------------------------------------------------------------------
# Bring-up, both modes. Steps are numbered so a hang has a name.
# ---------------------------------------------------------------------------------------------
# `if`, not `[ … ] && steps=…`: under `set -e` a test that is simply false is a command that
# returned 1, and the script would exit at the very line that only meant to leave a default alone.
steps=5
if [ "$with_agent" -eq 0 ]; then
  steps=4
fi
if [ "$mode" = "selfcheck" ]; then
  steps=9
fi

dev_guard_acquire_lock
dev_guard_require_free_port "$api_port" "api"
dev_guard_require_free_port "$spa_port" "frontend"

# Settled BEFORE the trap is armed, so there is no window in which a signal can reach a teardown
# that has not yet learned whether the database was ours. `StartedAt` is recorded beside it
# because "still running afterwards" is satisfied by a container we stopped and started again —
# the timestamp is what tells reuse from a recreate, and the self-check asserts it.
postgres_was_running=1
if [ "$(docker inspect -f '{{.State.Running}}' maran-postgres 2>/dev/null)" != "true" ]; then
  postgres_was_running=0
fi
postgres_started_at="$(docker inspect -f '{{.State.StartedAt}}' maran-postgres 2>/dev/null || true)"

stage="database"
trap dev_teardown_on_failure INT TERM EXIT

dev_stage_database

stage="schema"
dev_stage_schema

# `stage` is set INSIDE this one, in its agent branch only: with `--no-agent` there is no agent
# stage to name, and a failure of the api that followed would otherwise be reported against a
# stage this run skipped.
dev_stage_agent

stage="api"
dev_stage_api

stage="frontend"
dev_stage_frontend

# ---------------------------------------------------------------------------------------------
# Self-check: one real operation through the whole product, observed on the system itself, then a
# teardown that is asserted rather than trusted, then the two decisions a running stack cannot
# show — the teardown plan of the instance this run is NOT, and the socket guard on a fixture
# (dev-selfcheck.sh; the verdict exits).
# ---------------------------------------------------------------------------------------------
if [ "$mode" = "selfcheck" ]; then
  stage="agent-operation"
  dev_selfcheck_operation

  stage="teardown"
  dev_selfcheck_teardown

  stage="teardown-plan"
  dev_selfcheck_teardown_plan_cases

  stage="socket-cases"
  dev_selfcheck_socket_cases

  dev_selfcheck_verdict
fi

# ---------------------------------------------------------------------------------------------
# Interactive mode: report, then stream until Ctrl+C.
# ---------------------------------------------------------------------------------------------
echo
echo "  panel   $spa_url"
echo "  api     $api_url"
echo "  health  $api_url/health -> $health_payload"
echo "  logs    $logs/{api,frontend}.log"
if [ "$with_agent" -eq 0 ]; then
  echo
  echo "  NO AGENT (--no-agent): the panel is honest about it (health above) and every system"
  echo "  operation — accounts, sites, databases, backups — will fail until one runs."
fi
echo
echo "Ctrl+C stops everything. Live stream of warnings and errors follows:"
echo

# Only warnings and errors are surfaced: a live run is for spotting problems, and the full
# streams stay on disk in $logs for anything the filter drops.
#
# BACKGROUNDED AND WAITED ON, not run in the foreground. bash defers a trap until the current
# foreground command finishes, and `tail -f` never finishes — so at a terminal Ctrl+C worked only
# because the tty signals the whole process group, and a `kill` from a script or a supervisor left
# the stack running with the teardown never reached. Measured: `kill -INT` on this script did
# nothing at all until `tail` itself was killed. `wait` is interruptible, so the trap runs at once.
tail -f "$logs/api.log" "$logs/frontend.log" 9>&- \
  | grep --line-buffered -iE "warn|error|fail|exception" 9>&- &
tail_pid=$!
wait "$tail_pid" || true
