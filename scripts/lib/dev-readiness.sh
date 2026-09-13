#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# The readiness gates — the four questions a bring-up must not merely ask but WAIT for an answer
# to, each answered by something the process itself reports rather than by the fact that a command
# returned. Every one of them exists because a stage was once declared ready by a weaker fact and
# the stack that followed was a lie: a health endpoint printed and not read, a socket file that a
# dead container leaves behind, a `dotnet run` that had already died, a container whose MariaDB
# never came up.
#
# What lives here:
#
#   dev_ready_database         the postgres container's own healthcheck
#   dev_ready_http             an endpoint answers, and the process behind it is still alive
#   dev_ready_agent_container  the agent container's compose healthcheck (MariaDB AND the socket)
#   dev_ready_panel_health     /health's VALUES for the api+agent pair — THE readiness truth
#
# Caller's contract: `dev_ready_agent_container` reads `$agent_container`; `dev_ready_panel_health`
# reads `$api_pid`, `$api_url`, `$logs`, `$socket_path`, and publishes `$health_payload` — the
# payload it accepted, which every later report quotes as its evidence.

dev_ready_database() { # blocks until the container reports healthy, so the API never races it
  local attempt
  for attempt in $(seq 60); do
    if [ "$(docker inspect -f '{{.State.Health.Status}}' maran-postgres 2>/dev/null)" = "healthy" ]; then
      return 0
    fi
    sleep 1
  done
  echo "the database did not become healthy within 60 seconds" >&2
  return 1
}

dev_ready_http() { # blocks until an endpoint answers, so the URL printed at the end really works
  # 180 ticks: a cold `dotnet run` compiles the whole solution first, and under a peer's test
  # load that has taken over two minutes. The pid check keeps a DEAD process from consuming the
  # budget — a process that died fails here in one tick, with its log, not after three minutes.
  local url="$1" name="$2" pid="${3:-}" attempt
  for attempt in $(seq 180); do
    if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then
      echo "$name died before it ever answered on $url" >&2
      return 1
    fi
    if curl -fsS --max-time 2 "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "$name did not answer on $url within 180 seconds" >&2
  return 1
}

# dev_ready_agent_container: blocks until the compose healthcheck — MariaDB answering AND the
# socket bound, asked INSIDE the container — says healthy. Waiting for the socket file alone
# proved too little twice: a stale file from a dead container satisfies `-S` instantly, and a
# container whose MariaDB never came up looks identical to one that is ready.
dev_ready_agent_container() {
  local attempt status
  for attempt in $(seq 120); do
    status="$(docker inspect -f '{{.State.Health.Status}}' "$agent_container" 2>/dev/null || true)"
    case "$status" in
      healthy) return 0 ;;
      unhealthy)
        echo "the agent container is UNHEALTHY (MariaDB or the socket, per its healthcheck):" >&2
        docker logs --tail 30 "$agent_container" >&2 2>&1 || true
        return 1
        ;;
    esac
    sleep 1
  done
  echo "the agent container never became healthy within 120 seconds (status: ${status:-absent})" >&2
  docker logs --tail 30 "$agent_container" >&2 2>&1 || true
  return 1
}

# dev_ready_panel_health: THE readiness gate for the api+agent pair. `/health`'s payload is the
# only thing that observes both sides of the socket at once, so the VALUES are asserted — never
# the exit code of a pipeline, which has already reported success having run nothing. The api
# pid is checked on every poll so a stranger answering our port can never be reported as us.
dev_ready_panel_health() {
  local expect_agent="$1" attempt payload=""
  for attempt in $(seq 90); do
    if ! kill -0 "$api_pid" 2>/dev/null; then
      echo "READINESS FAILED — the api process died while waiting for /health:" >&2
      tail -30 "$logs/api.log" >&2
      return 1
    fi
    payload="$(curl -fsS --max-time 2 "$api_url/health" 2>/dev/null || true)"
    if [ -n "$payload" ] \
      && [ "${payload#*\"agent\":\"$expect_agent\"}" != "$payload" ] \
      && [ "${payload#*\"database\":\"reachable\"}" != "$payload" ]; then
      health_payload="$payload"
      return 0
    fi
    sleep 1
  done
  echo "AGENT-NOT-CONNECTED — /health never answered with \"agent\":\"$expect_agent\" and" >&2
  echo "\"database\":\"reachable\" within 90 seconds. Last answer: ${payload:-<none>}" >&2
  if [ -e "$socket_path" ]; then
    echo "socket: $(stat -c '%A %U %G' "$socket_path") $socket_path — the panel runs as uid $(id -u)," >&2
    echo "group $(id -gn); it can open that socket only if the group matches and the mode allows it." >&2
  else
    echo "socket: $socket_path does not exist" >&2
  fi
  tail -15 "$logs/api.log" >&2 || true
  return 1
}
