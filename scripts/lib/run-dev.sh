#!/usr/bin/env bash
# Runs the whole Maran stack for local development: the PostgreSQL container, the ROOT AGENT, the
# API and the SPA. Docker carries dev dependencies only (rules/architecture.md), so the API and
# the SPA run natively against it — exactly as they do on a server, where no container is
# involved.
#
# THE AGENT IS PART OF THE STACK, and until now it was not. This script started a database, an API
# and an SPA and called that "the whole stack", so every local run and every browser pass drove a
# panel whose agent was permanently `unavailable`. Nearly every interesting failure in this product
# happens on the agent's side of the socket, so a defect that lives there — a function with no
# caller, a deletion that reports success while releasing nothing — could not be exhibited by the
# only stack anybody ran. The agent runs in the polygon container, as root; the argument for that
# placement, and for how the socket is reached without a `chown`, is in docker-compose.dev.yml
# beside the service.
#
# Usage:
#   scripts/maran dev             start everything, stream logs, stop cleanly on Ctrl+C
#   scripts/maran dev --no-agent  start without the agent (API and SPA work only)
#   scripts/maran dev --stop      stop every container this script starts, leaving nothing running
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
compose="$root/docker/docker-compose.dev.yml"
logs="$root/.dev-logs"

# shellcheck disable=SC1091
. "$root/scripts/dev"

# The API listens where the nginx vhost proxies in production, so a developer meets the same
# origin locally; the SPA dev server proxies to it.
api_url="http://127.0.0.1:5080"
spa_url="http://127.0.0.1:5173"

# The socket directory is inside the working tree so that a bind mount reaches it from the
# container and the developer can see it. 0750 and not 0755: the socket the agent binds is
# root:<developer's group> 0660, and a directory another local account could traverse would
# widen what the mode narrows.
socket_dir="$root/.dev-agent"
socket_path="$socket_dir/agent.sock"

# Passed to compose, which gives the agent process uid 0 and THIS gid — the production
# `User=root` / `Group=panel` pair with the developer's group in place of panel's.
MARAN_DEV_UID="$(id -u)"
MARAN_DEV_GID="$(id -g)"
export MARAN_DEV_UID MARAN_DEV_GID

with_agent=1

# kill_tree: signals a process and everything it started, deepest first.
#
# `kill $spa_pid` alone was not enough and the cost was visible: the SPA is started as a subshell
# that runs `npm`, which runs `vite`, which runs `node`. Killing the subshell left the node process
# holding port 5173, and the NEXT `maran dev` failed with "Port 5173 is already in use" — a stack
# broken by the teardown of the one before it. Children first, so a parent cannot re-parent them to
# init while it is being stopped.
kill_tree() {
  local pid="$1" child
  for child in $(pgrep -P "$pid" 2>/dev/null); do
    kill_tree "$child"
  done
  kill "$pid" 2>/dev/null || true
}

stop_stack() { # tears down every process this script started, in reverse order
  trap - INT TERM EXIT
  echo
  echo "stopping..."
  [ -n "${tail_pid:-}" ] && kill_tree "$tail_pid"
  [ -n "${spa_pid:-}" ] && kill_tree "$spa_pid"
  [ -n "${api_pid:-}" ] && kill_tree "$api_pid"
  wait 2>/dev/null || true
  docker compose -f "$compose" --profile agent stop >/dev/null 2>&1 || true
  echo "stopped."
}

case "${1:-}" in
  --stop)
    # --profile agent, or compose does not consider the agent service its business and leaves a
    # privileged root container running after a command whose whole promise is "nothing running".
    docker compose -f "$compose" --profile agent down
    exit 0
    ;;
  --no-agent) with_agent=0 ;;
  "") : ;;
  *) echo "usage: maran dev [--no-agent|--stop]" >&2; exit 1 ;;
esac

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

wait_for_socket() { # blocks until the agent has bound its socket
  local attempt
  for attempt in $(seq 60); do
    [ -S "$socket_path" ] && return 0
    sleep 1
  done
  echo "the agent never created $socket_path" >&2
  return 1
}

# `if`, not `[ … ] && steps=4`: under `set -e` a test that is simply false is a command that
# returned 1, and the script would exit at the very line that only meant to leave a default alone.
steps=5
if [ "$with_agent" -eq 0 ]; then
  steps=4
fi

mkdir -p "$logs"
trap stop_stack INT TERM EXIT

echo "1/$steps  database"
docker compose -f "$compose" up -d --quiet-pull >"$logs/database.log" 2>&1
wait_for_database

# The database the panel is configured to use and the database the container creates must be the
# same one. They were not: `.env` said `maran_live` while compose created `maran_dev`, so a panel
# could boot, connect, answer `{"status":"ok"}` and be looking at an empty or foreign schema. The
# compose file is authoritative — it is what actually exists.
#
# A DELIBERATE OVERRIDE IS NOT DRIFT, and the two are distinguished rather than guessed at.
# `scripts/dev` records in MARAN_ENV_APPLIED which names it supplied from `.env`, so a value that
# is NOT in that list was set by the caller on purpose — `Database__Database=maran_scratch maran dev`
# — and that is a supported thing to do: the run says which database it is using and carries on.
# A disagreement that came from the file is the accident, and it refuses.
compose_database="$(docker compose -f "$compose" config --format json 2>/dev/null \
  | grep -o '"POSTGRES_DB": *"[^"]*"' | head -1 | sed 's/.*"POSTGRES_DB": *"\([^"]*\)".*/\1/')"
if [ -n "$compose_database" ] && [ "${Database__Database:-}" != "$compose_database" ]; then
  case " ${MARAN_ENV_APPLIED:-} " in
    *" Database__Database "*)
      echo "the panel is configured for database '${Database__Database:-<unset>}' but $compose" >&2
      echo "creates '$compose_database'. Fix Database__Database in .env (or POSTGRES_DB in" >&2
      echo "docker/.env) — a panel that boots against a database nobody migrates is how a schema" >&2
      echo "fifteen migrations old passed for healthy." >&2
      exit 1
      ;;
    *)
      echo "     using database '$Database__Database' (an override; $compose creates '$compose_database')"
      # The override's database may not exist yet — creating it here is what makes a clean-slate
      # run possible at all, and `createdb` on one that exists is a no-op rather than an error.
      docker exec -e PGPASSWORD="${Database__Password:-maran_dev}" maran-postgres \
        createdb -U "${Database__Username:-maran_dev}" "$Database__Database" >/dev/null 2>&1 || true
      ;;
  esac
fi

echo "2/$steps  schema"
# APPLIED IN DEVELOPMENT, DELIBERATELY, AND NEVER BY THE PANEL ITSELF. rules/architecture.md is
# explicit that a starting process must not migrate: on a server the installer applies migrations
# after taking a dump, so that a bad migration is recoverable. Nothing about that reasoning is
# weakened here — the panel still does not migrate. This is the developer's deliberate step, run
# by the developer's own command, one moment before the panel starts, and it is loud about what it
# did. The alternative, leaving the drift to be discovered, is what produced a panel answering
# `{"status":"ok"}` against a schema fifteen migrations old; a check that cannot observe what it
# reports on is the exact failure this repository has spent the week removing.
# `stdbuf -oL`, not a bare pipe: sed buffers by the block when its output is not a terminal, so a
# five-minute apply printed nothing at all until it had finished and the run looked hung at the
# step most likely to be slow. Measured: the first version of this line showed "2/5 schema" and no
# further output for the whole apply.
stdbuf -oL "$root/scripts/lib/schema-drift.sh" --apply | stdbuf -oL sed 's/^/     /'

# Development, explicitly: `dotnet run` defaults to Production, and a Production host reads
# appsettings.json — whose database host is the unix socket a server has and a workstation does
# not. Without this the API starts against nothing and dies at Wolverine's first migration.
if [ "$with_agent" -eq 1 ]; then
  echo "3/$steps  agent"
  # Built on the host and mounted, as the polygon suites do it: host and image share a glibc, and
  # a binary compiled inside the container would not be the one being edited.
  if [ ! -x "$root/agent/target/debug/maran-agent" ]; then
    echo "     building the agent (first run only)"
    "$root/scripts/lib/agent.sh" build >"$logs/agent-build.log" 2>&1 \
      || { echo "the agent did not build:" >&2; tail -30 "$logs/agent-build.log" >&2; exit 1; }
  fi
  install -d -m 0750 "$socket_dir"
  rm -f "$socket_path"
  docker compose -f "$compose" --profile agent up -d --quiet-pull agent \
    >>"$logs/database.log" 2>&1
  wait_for_socket || { docker logs maran-agent-dev >"$logs/agent.log" 2>&1 || true; \
    tail -30 "$logs/agent.log" >&2; exit 1; }

  # The socket is reported rather than assumed: this line is the evidence that no `chown` is
  # needed. It must read `srw-rw---- root <developer's group>`.
  echo "     socket   $(stat -c '%A %U:%G' "$socket_path") $socket_path"
  export Agent__SocketPath="$socket_path"
else
  echo "3/$steps  agent — skipped (--no-agent): /health will report it unavailable"
fi

echo "$((steps - 1))/$steps  api"
(cd "$root/backend/src/Maran.Host" \
  && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$api_url" dotnet run) >"$logs/api.log" 2>&1 &
api_pid=$!
wait_for_http "$api_url/health/live" "api" || { cat "$logs/api.log"; exit 1; }

echo "$steps/$steps  frontend"
(cd "$root/frontend" && npm run dev -- --port 5173 --strictPort) >"$logs/frontend.log" 2>&1 &
spa_pid=$!
wait_for_http "$spa_url" "frontend" || { cat "$logs/frontend.log"; exit 1; }

echo
echo "  panel   $spa_url"
echo "  api     $api_url"
echo "  health  $api_url/health -> $(curl -fsS --max-time 5 "$api_url/health" 2>/dev/null)"
echo "  logs    $logs/{api,frontend}.log"
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
tail -f "$logs/api.log" "$logs/frontend.log" \
  | grep --line-buffered -iE "warn|error|fail|exception" &
tail_pid=$!
wait "$tail_pid" || true
