#!/usr/bin/env bash
# Runs the whole Maran stack for local development: the PostgreSQL container, the API and the
# SPA. Docker carries dev dependencies only (rules/architecture.md), so the API and the SPA run
# natively against it — exactly as they do on a server, where no container is involved.
#
# Usage:
#   scripts/run-dev.sh          start everything, stream logs, stop cleanly on Ctrl+C
#   scripts/run-dev.sh --stop   stop the database container and leave nothing running
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
compose="$root/docker/docker-compose.dev.yml"
logs="$root/.dev-logs"

# shellcheck disable=SC1091
. "$root/scripts/dev-env.sh"

# The API listens where the nginx vhost proxies in production, so a developer meets the same
# origin locally; the SPA dev server proxies to it.
api_url="http://127.0.0.1:5080"
spa_url="http://127.0.0.1:5173"

stop_stack() { # tears down every process this script started, in reverse order
  trap - INT TERM EXIT
  echo
  echo "stopping..."
  [ -n "${spa_pid:-}" ] && kill "$spa_pid" 2>/dev/null || true
  [ -n "${api_pid:-}" ] && kill "$api_pid" 2>/dev/null || true
  wait 2>/dev/null || true
  docker compose -f "$compose" stop >/dev/null 2>&1 || true
  echo "stopped."
}

if [ "${1:-}" = "--stop" ]; then
  docker compose -f "$compose" down
  exit 0
fi

wait_for_database() { # blocks until the container reports healthy, so the API never races it
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

wait_for_http() { # blocks until an endpoint answers, so the URL printed at the end really works
  local url="$1" name="$2" attempt
  for attempt in $(seq 90); do
    if curl -fsS --max-time 2 "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "$name did not answer on $url" >&2
  return 1
}

mkdir -p "$logs"
trap stop_stack INT TERM EXIT

echo "1/3  database"
docker compose -f "$compose" up -d --quiet-pull >"$logs/database.log" 2>&1
wait_for_database

# Development, explicitly: `dotnet run` defaults to Production, and a Production host reads
# appsettings.json — whose database host is the unix socket a server has and a workstation does
# not. Without this the API starts against nothing and dies at Wolverine's first migration.
echo "2/3  api"
(cd "$root/backend/src/Maran.Host" \
  && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$api_url" dotnet run) >"$logs/api.log" 2>&1 &
api_pid=$!
wait_for_http "$api_url/health/live" "api" || { cat "$logs/api.log"; exit 1; }

echo "3/3  frontend"
(cd "$root/frontend" && npm run dev -- --port 5173 --strictPort) >"$logs/frontend.log" 2>&1 &
spa_pid=$!
wait_for_http "$spa_url" "frontend" || { cat "$logs/frontend.log"; exit 1; }

echo
echo "  panel   $spa_url"
echo "  api     $api_url"
echo "  health  $api_url/health"
echo "  logs    $logs/{api,frontend}.log"
echo
echo "Ctrl+C stops everything. Live stream of warnings and errors follows:"
echo

# Only warnings and errors are surfaced: a live run is for spotting problems, and the full
# streams stay on disk in $logs for anything the filter drops.
tail -f "$logs/api.log" "$logs/frontend.log" | grep --line-buffered -iE "warn|error|fail|exception" || true
