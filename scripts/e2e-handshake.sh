#!/usr/bin/env bash
# Proves the two processes actually talk: starts the Rust agent on a temporary unix socket, starts
# the C# API pointed at that socket, and asserts /health reports the agent as connected.
#
# This is the one check neither side can pass alone — the backend suite stubs the agent, and the
# agent suite has no API — so it is what catches a contract that compiles on both sides and still
# does not match on the wire.
#
# Prints HANDSHAKE-OK and exits 0 on success. Requires a reachable panel database: the API refuses
# to start without one, and this script starts the development container when Docker is available.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev-env.sh"

work="$(mktemp -d)"
socket="$work/agent.sock"
api_url="http://127.0.0.1:5081"
api_log="$work/api.log"
agent_log="$work/agent.log"
agent_pid=""
api_pid=""

cleanup() { # always stop both processes, whatever went wrong
  trap - EXIT INT TERM
  [ -n "$api_pid" ] && kill "$api_pid" 2>/dev/null || true
  [ -n "$agent_pid" ] && kill "$agent_pid" 2>/dev/null || true
  wait 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT INT TERM

fail() { # report why, with the logs that explain it, and stop
  echo "HANDSHAKE-FAILED: $1" >&2
  echo "--- agent log ---" >&2
  cat "$agent_log" >&2 2>/dev/null || true
  echo "--- api log ---" >&2
  tail -40 "$api_log" >&2 2>/dev/null || true
  exit 1
}

# A database the API can reach. In CI a service container already listens; locally the development
# compose file is the same database `scripts/run-dev.sh` uses.
if ! (exec 3<>/dev/tcp/127.0.0.1/5432) 2>/dev/null; then
  if command -v docker >/dev/null 2>&1; then
    echo "starting the development database"
    docker compose -f "$root/docker/docker-compose.dev.yml" up -d >/dev/null 2>&1
    for _ in $(seq 60); do
      [ "$(docker inspect -f '{{.State.Health.Status}}' maran-postgres 2>/dev/null)" = "healthy" ] && break
      sleep 1
    done
  else
    fail "no database on 127.0.0.1:5432 and no docker to start one"
  fi
fi

echo "building the agent"
(cd "$root/agent" && cargo build -p maran-agent) >"$agent_log" 2>&1 || fail "the agent did not build"

echo "starting the agent"
# --allow-uid is this user: the peer-cred guard permits exactly one uid, and in production the
# installer passes the panel user's. Same code path, different number.
"$root/agent/target/debug/maran-agent" --socket "$socket" --allow-uid "$(id -u)" \
  >>"$agent_log" 2>&1 &
agent_pid=$!

for _ in $(seq 30); do
  [ -S "$socket" ] && break
  sleep 1
done
[ -S "$socket" ] || fail "the agent never created its socket"

echo "starting the api"
(cd "$root/backend/src/Maran.Host" \
  && ASPNETCORE_ENVIRONMENT=Development \
     ASPNETCORE_URLS="$api_url" \
     Agent__SocketPath="$socket" \
     dotnet run) >"$api_log" 2>&1 &
api_pid=$!

for _ in $(seq 90); do
  curl -fsS --max-time 2 "$api_url/health/live" >/dev/null 2>&1 && break
  sleep 1
done

health="$(curl -fsS --max-time 5 "$api_url/health" 2>/dev/null)" || fail "the api never answered"

case "$health" in
  *'"agent":"connected"'*)
    echo "$health"
    echo "HANDSHAKE-OK"
    ;;
  *)
    fail "the api did not reach the agent: $health"
    ;;
esac
