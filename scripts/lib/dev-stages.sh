#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# THE BRING-UP, one function per numbered stage, in the order a stack comes up: database, schema,
# agent, api, frontend. Steps are numbered so a hang has a name, and each stage ends at a
# readiness gate (`scripts/lib/dev-readiness.sh`) rather than at the return of the command that
# started it — a process that has been launched is not a process that is serving.
#
# What lives here:
#
#   dev_stage_compose               every compose command that concerns the agent service
#   dev_stage_require_polygon_image refuses a missing image; says loudly when the one present was
#                                   not built from this tree
#   dev_stage_build_agent           cargo, every time; cargo decides staleness
#   dev_stage_verify_running_binary the container must serve the bytes the host just built
#   dev_stage_database              reuse a running database, or start one this run will stop
#   dev_stage_schema                the developer's deliberate migration step
#   dev_stage_agent                 image, build, socket guard, container, binary identity
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

# DEV_STALE_POLYGON_IMAGE: set by the stage below when the image this stack runs on was NOT built
# from this tree. It exists so that the fact survives the one line that reported it: a warning
# scrolled off the top of a bring-up is a warning nobody read, and the stage that ends with
# `binary  running binary sha256 = built binary sha256` must not be allowed to read as though it
# covered the image too.
DEV_STALE_POLYGON_IMAGE=""

# dev_stage_require_polygon_image: refuse a MISSING image with the exact remedy, and say loudly —
# without refusing — when the image that IS here was built from other sources than this tree.
#
# WHAT CHANGED AND WHY. This used to ask only whether the image EXISTED, and print its build date:
# "the OS layer floats by design, so the date is printed so a very old image is at least visible".
# That was the honest answer while nothing could ask the better question. Now something can:
# `polygon_fingerprint` hashes `docker/polygon/**` + `installer/**`, every build through
# `polygon_build_labelled` records that hash in the image as `maran.polygon.fingerprint`, and the
# scoring lane REFUSES a log whose image does not match. Measured here, on this host, before this
# change: this stage printed `image  maran-polygon-ubuntu24:latest (built 2026-09-11)` and returned
# 0 for the very image `polygon_stamp` refuses as recording no label at all. Two lanes, one image,
# opposite answers, and the weaker question was the one the developer saw.
#
# WHY IT WARNS RATHER THAN REFUSES, argued rather than assumed. This is a developer's stack
# bring-up; it scores nothing and gates nothing, and a person may knowingly want the image they
# have — offline, mid-debug, or unwilling to spend twenty minutes right now. This file only ever
# refuses what would make the STACK LIE: an absent image (nothing can run) and a stale agent binary
# (the container would serve bytes nobody built), and the binary half is verified by sha256 against
# the running process a few lines below. A drifting OS layer is documented as drifting. And refusing
# would block every `maran dev` on this host today over a labelling migration rather than over a
# defect, which teaches people to route around the check — the failure mode rules/testing.md warns
# about for a gate that cries on the common correct case.
#
# WHY NOT REBUILD: the comment on dev_stage_compose still holds. A first `maran dev` disappearing
# into a silent 1.9GB build inside a log file is indistinguishable from a hang.
#
# WHY NOT AN OVERRIDE FLAG: a flag would be exported in everyone's shell within a week, and a
# warning that is always printed is stronger than one that can be switched off.
#
# The one thing it may never do is let the image LOOK current. So there are three outcomes and each
# says which it is; none of them is a crash.
dev_stage_require_polygon_image() {
  local created recorded expected image="maran-polygon-ubuntu24:latest"
  if ! created="$(docker image inspect -f '{{.Created}}' "$image" 2>/dev/null)"; then
    echo "REFUSED — the polygon image $image does not exist on this machine." >&2
    echo "Build it first (docker/README.md), from the repository root:" >&2
    echo "    docker build -f docker/polygon/ubuntu24.Dockerfile -t maran-polygon-ubuntu24 ." >&2
    exit 1
  fi

  # The currency mechanism is sourced, never reimplemented: one definition of "built from this
  # tree" for the scoring lane and for this one, or the two drift apart and the second is wrong.
  # `polygon.sh` is a pure function library — nothing in it runs at source time — and it is sourced
  # here rather than at the top of this file so that the dev bring-up's source-time behaviour is
  # unchanged.
  # shellcheck disable=SC1091
  . "$root/scripts/lib/polygon.sh"
  expected="$(polygon_fingerprint "$root" ubuntu24)"
  recorded="$(polygon_image_fingerprint "$image")"

  if [ -n "$recorded" ] && [ "$recorded" = "$expected" ]; then
    echo "     image    $image — CURRENT (built from exactly these sources, $expected)"
    return 0
  fi

  DEV_STALE_POLYGON_IMAGE="$image"
  echo "     image    $image — NOT CURRENT (built ${created%%T*})"
  if [ -z "$recorded" ]; then
    echo "              It records no $POLYGON_FINGERPRINT_LABEL label, so what it was built from"
    echo "              is UNKNOWN. Built before this label existed, or built by hand."
  else
    echo "              It was built from OTHER SOURCES than this tree describes:"
    echo "                  the image records : $recorded"
    echo "                  this tree hashes  : $expected"
  fi
  echo "              This stack will run the OS and installer layer of a tree that is not this"
  echo "              one — docker/polygon/** and installer/** have moved on. NOT REFUSED: a dev"
  echo "              stack scores nothing, and the agent binary and its contract are verified by"
  echo "              sha256 against the running process below. But nothing here has observed the"
  echo "              image, so do not read a green bring-up as evidence about the installer."
  echo "              Rebuild it when a system behaviour surprises you:"
  echo "                  docker build --label $POLYGON_FINGERPRINT_LABEL=$expected \\"
  echo "                    -f docker/polygon/ubuntu24.Dockerfile -t $image ."
  return 0
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
    echo "     started maran-postgres (nothing was running) — this run will stop it again"
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
    dev_stage_require_polygon_image
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
    # RESTATED, because the line above is the one people read. `binary ... = ...` is a real check
    # and it covers the agent and nothing else; printed alone at the end of a stage that began with
    # a stale image, it reads as "stage 3 is clean". So the stage's last word is the thing that is
    # NOT verified, whenever there is one.
    if [ -n "$DEV_STALE_POLYGON_IMAGE" ]; then
      echo "     image    STILL NOT CURRENT — $DEV_STALE_POLYGON_IMAGE was not built from this tree"
      echo "              (see the block above). The binary line just printed covers the agent only."
    fi
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
