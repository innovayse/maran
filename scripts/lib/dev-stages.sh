#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# THE BRING-UP, one function per numbered stage, in the order a stack comes up: database, schema,
# agent, api, frontend — preceded by the polygon image check, which run-dev.sh runs before anything
# is started. Steps are numbered so a hang has a name, and each stage ends at a
# readiness gate (`scripts/lib/dev-readiness.sh`) rather than at the return of the command that
# started it — a process that has been launched is not a process that is serving.
#
# What lives here:
#
#   dev_stage_compose               every compose command that concerns the agent service
#   dev_stage_require_polygon_image refuses an image that is missing or not built from this tree
#   dev_stage_build_agent           cargo, every time; cargo decides staleness
#   dev_stage_verify_running_binary the container must serve the bytes the host just built
#   dev_stage_database              reuse a running database, or start one this run will stop
#   dev_stage_schema                the developer's deliberate migration step
#   dev_stage_agent                 build, socket guard, container, binary identity
#   dev_stage_api                   dotnet run, then /health BY VALUE
#   dev_stage_frontend              vite on this instance's port
#
# Caller's contract: these stages read the instance parameters `run-dev.sh` derives ($compose,
# $project, $instance, $with_agent, $steps, $logs, $socket_dir, $socket_path, $api_url, $api_port,
# $spa_url, $spa_port, $agent_binary, $agent_container, $postgres_was_running) and publish
# $postgres_started_at, $api_pid, $spa_pid and $health_payload for the teardown and the self-check.

# dev_stage_compose: every compose command that concerns the agent service, under this instance's
# project so two instances never reconcile each other's containers. `--no-build` everywhere: the
# compose file carries a build section for documentation's sake, but a missing polygon image must
# be a REFUSAL naming the build command, never a silent 1.9GB build into a log file — that is the
# half-start this command exists to refuse.
dev_stage_compose() {
  docker compose -f "$compose" -p "$project" --profile agent "$@"
}

# dev_stage_require_polygon_image: refuses to bring the stack up on a polygon image that is absent
# or was not built from this tree, naming the one command that rebuilds it.
#
# WHY IT REFUSES. It used to warn and carry on, on the argument that a dev stack scores nothing.
# Issue #238 is the measurement against that argument: the stale image's old `systemctl` stand-in
# read `--` as a unit name and reported every unknown unit as active, and the bring-up's warning —
# printed, restated, scrolled away — did not stop that from being read as a product defect (#215).
# A stack that runs the previous tree's OS layer answers questions about a tree that is not this
# one, and a warning that lets it start is a warning nobody reads.
#
# WHY NOT REBUILD HERE: a rebuild takes minutes, and a `maran dev` that vanishes into a silent image
# build is indistinguishable from a hang (the same reason `dev_stage_compose` passes `--no-build`).
# The refusal names `maran polygon build ubuntu24`, which streams its build and then proves the
# result current.
#
# The currency question itself is `polygon_require_current`: one definition of "built from this
# tree" for this lane and the scoring lane. `polygon.sh` is a pure function library, sourced here
# rather than at the top of this file so that the bring-up's source-time behaviour is unchanged.
# It runs before the database is started (run-dev.sh), so a refusal leaves nothing behind.
dev_stage_require_polygon_image() {
  local image="maran-polygon-ubuntu24:latest"
  # shellcheck disable=SC1091
  . "$root/scripts/lib/polygon.sh"
  if ! polygon_require_current "$root" ubuntu24 "$image"; then
    echo "REFUSED — maran dev does not run on a polygon image that is not built from this tree." >&2
    exit 1
  fi
  echo "     image    $image — CURRENT (built from exactly these sources)"
}

# dev_stage_build_agent: cargo, every time. Cargo itself is the only honest staleness oracle — it
# tracks the proto contract through build.rs where an mtime comparison lies in both directions
# (scripts/lib/polygon.sh explains the trap) — and an incremental no-op build costs about a
# second. A dev stack running yesterday's agent against today's proto is the lie this gate ends;
# yesterday it was caught only because Ruling 60 demanded `strings` on the running binary.
dev_stage_build_agent() {
  echo "     building the agent (incremental; cargo decides staleness)"
  if ! "$root/scripts/lib/agent.sh" build >"$logs/agent-build.log" 2>&1 9>&-; then
    echo "REFUSED — the agent did not build, and a stack on a stale binary would lie:" >&2
    tail -30 "$logs/agent-build.log" >&2
    exit 1
  fi
}

# dev_stage_verify_running_binary: the running container must be serving the bytes the host just
# built. A single-file bind mount pins the file it was mounted from, so a container that survived a
# rebuild keeps executing the OLD binary while `ls` on the host shows the new one — the exact
# staleness `strings /proc/1/exe` caught by hand yesterday, made mechanical. One recreate is
# attempted; a second mismatch is a refusal, not a shrug.
dev_stage_verify_running_binary() {
  local host_sha container_sha
  host_sha="$(sha256sum "$agent_binary" | cut -d' ' -f1)"
  container_sha="$(docker exec "$agent_container" sha256sum /usr/local/bin/maran-agent 2>/dev/null | cut -d' ' -f1 || true)"
  if [ "$host_sha" = "$container_sha" ]; then
    return 0
  fi
  echo "     the container is running a binary that is not the one just built — recreating it"
  dev_stage_compose up -d --no-build --force-recreate agent >>"$logs/database.log" 2>&1
  dev_ready_agent_container
  container_sha="$(docker exec "$agent_container" sha256sum /usr/local/bin/maran-agent 2>/dev/null | cut -d' ' -f1 || true)"
  if [ "$host_sha" != "$container_sha" ]; then
    echo "REFUSED — even after a recreate the running agent (${container_sha:-unreadable}) is not" >&2
    echo "the built one ($host_sha). A stack on a stale agent is the lie this check exists to end." >&2
    exit 1
  fi
}

# dev_stage_database: stage 1 — the database container, and the agreement between the database the
# panel is configured for and the database the compose file creates.
dev_stage_database() {
  echo "1/$steps  database"
  if [ "$postgres_was_running" -eq 1 ]; then
    # REUSED, NOT RECREATED. `docker compose up -d` reconciles a running container against the
    # compose file and RESTARTS it when the rendered configuration has drifted — which it did, for
    # this command's first outside user: a database container that had been up for 23 hours came
    # back `Up 17 seconds` because interpolated variables belonging to the AGENT service changed
    # the project's rendered config. A developer sharing that container with a peer had it
    # restarted underneath them without a word. This run did not start it and does not touch it;
    # it only waits for the health the container itself reports.
    echo "     reusing the running maran-postgres container (started $postgres_started_at) —"
    echo "     this run neither recreates nor stops it"
  else
    docker compose -f "$compose" up -d --quiet-pull postgres >"$logs/database.log" 2>&1
    postgres_started_at="$(docker inspect -f '{{.State.StartedAt}}' maran-postgres 2>/dev/null || true)"
    echo "     started maran-postgres on 127.0.0.1:${Database__Port:-5432} (nothing was running) — this run will stop it again"
  fi
  dev_ready_database

  if [ "$instance" = "selfcheck" ]; then
    # From NOTHING, literally. A crashed previous check leaves its database behind with an
    # administrator already in it, and `/setup` stops working the moment any user exists — so the
    # next run would fail for yesterday's reason. This database is the check's own and disposable
    # by definition; a developer's override database is never touched (this branch is instance-,
    # not override-, gated).
    docker exec -e PGPASSWORD="${Database__Password:-maran_dev}" maran-postgres \
      dropdb -f --if-exists -U "${Database__Username:-maran_dev}" "$Database__Database" >/dev/null 2>&1 || true
  fi

  # The database the panel is configured to use and the database the container creates must be the
  # same one. They were not: `.env` said `maran_live` while compose created `maran_dev`, so a panel
  # could boot, connect, answer `{"status":"ok"}` and be looking at an empty or foreign schema. The
  # compose file is authoritative — it is what actually exists.
  #
  # A DELIBERATE OVERRIDE IS NOT DRIFT, and the two are distinguished rather than guessed at.
  # `scripts/dev` records in MARAN_ENV_APPLIED which names it supplied from `.env`, so a value that
  # is NOT in that list was set by the caller on purpose — `Database__Database=maran_scratch maran dev`,
  # or this script's own self-check instance — and that is a supported thing to do: the run says
  # which database it is using and carries on. A disagreement that came from the file is the
  # accident, and it refuses.
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
}

# dev_stage_schema: stage 2 — the migrations, APPLIED IN DEVELOPMENT, DELIBERATELY, AND NEVER BY
# THE PANEL ITSELF. rules/architecture.md is explicit that a starting process must not migrate: on
# a server the installer applies migrations after taking a dump, so that a bad migration is
# recoverable. Nothing about that reasoning is weakened here — the panel still does not migrate.
# This is the developer's deliberate step, run by the developer's own command, one moment before
# the panel starts, and it is loud about what it did. The alternative, leaving the drift to be
# discovered, is what produced a panel answering `{"status":"ok"}` against a schema fifteen
# migrations old; a check that cannot observe what it reports on is the exact failure this
# repository has spent the week removing.
dev_stage_schema() {
  echo "2/$steps  schema"
  # `stdbuf -oL`, not a bare pipe: sed buffers by the block when its output is not a terminal, so a
  # five-minute apply printed nothing at all until it had finished and the run looked hung at the
  # step most likely to be slow. Measured: the first version of this line showed "2/5 schema" and no
  # further output for the whole apply.
  stdbuf -oL "$root/scripts/lib/schema-drift.sh" --apply 9>&- | stdbuf -oL sed 's/^/     /'
}

# dev_stage_agent: stage 3 — the root agent in the polygon container, or a loud skip. Everything
# that could leave a stack running a lie happens here: a missing image, a stale binary, and a
# socket somebody else is holding.
dev_stage_agent() {
  if [ "$with_agent" -eq 1 ]; then
    stage="agent"
    echo "3/$steps  agent"
    dev_stage_build_agent
    install -d -m 0750 "$socket_dir"
    # A socket file is removed only once a connect() has been REFUSED on it — the fact, not the
    # inference from a container name that produced defect #7. A holder that is not ours refuses
    # the bring-up here, before the agent container whose entrypoint would clobber the path starts.
    dev_socket_reconcile "$socket_path" || exit 1
    # Always `up -d`: compose's own config diffing makes this a no-op for a current running
    # container, a start for an exited one, and a recreate for one whose configuration drifted —
    # the three stale-state shapes a crashed or older run leaves behind.
    dev_stage_compose up -d --no-build --quiet-pull agent >>"$logs/database.log" 2>&1
    dev_ready_agent_container
    dev_stage_verify_running_binary
    # The socket is reported rather than assumed: this line is the evidence that no `chown` is
    # needed. It must read `srw-rw---- root <developer's group>`.
    echo "     socket   $(stat -c '%A %U:%G' "$socket_path") $socket_path"
    echo "     binary   running binary sha256 = built binary sha256"
    export Agent__SocketPath="$socket_path"
  else
    echo "3/$steps  agent — SKIPPED (--no-agent): every system operation WILL fail; /health must say \"unavailable\""
  fi
}

# dev_stage_api: stage 4 (3 without the agent) — the panel process, and THE readiness truth for the
# api+agent pair, asserted by VALUE. `/health/live` only says the process serves requests; it said
# exactly that for three passes of an agentless panel.
dev_stage_api() {
  echo "$((3 + with_agent))/$steps  api"
  # Development, explicitly: `dotnet run` defaults to Production, and a Production host reads
  # appsettings.json — whose database host is the unix socket a server has and a workstation does
  # not. Without this the API starts against nothing and dies at Wolverine's first migration.
  (cd "$root/backend/src/Maran.Host" \
    && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$api_url" dotnet run) >"$logs/api.log" 2>&1 9>&- &
  api_pid=$!
  dev_ready_http "$api_url/health/live" "api" "$api_pid" || { cat "$logs/api.log"; exit 1; }
  if [ "$with_agent" -eq 1 ]; then
    dev_ready_panel_health "connected"
  else
    dev_ready_panel_health "unavailable"
  fi
  echo "     health   $health_payload"
}

# dev_stage_frontend: stage 5 (4 without the agent) — the SPA, on this instance's own port and
# `--strictPort`, so a vite that cannot have the port fails here instead of moving to another one
# and being reported as this stack.
dev_stage_frontend() {
  echo "$((4 + with_agent))/$steps  frontend"
  (cd "$root/frontend" && npm run dev -- --port "$spa_port" --strictPort) >"$logs/frontend.log" 2>&1 9>&- &
  spa_pid=$!
  dev_ready_http "$spa_url" "frontend" "$spa_pid" || { cat "$logs/frontend.log"; exit 1; }
}
