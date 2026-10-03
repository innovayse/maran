#!/usr/bin/env bash
# 65-schema.sh: applies every module's database migrations, before anything starts the panel.
#
# This step exists because nothing applied them. rules/architecture.md and scripts/lib/migrations.sh
# both state the design — "migrations are never applied by a starting process; the installer and the
# update command apply them deliberately" — and the installer had no step that did. The starting
# process honoured the rule, the installer did not perform its half, and so EVERY fresh install came
# up with no module schema at all: the panel served its setup page and answered the operator's first
# action with `42P01: relation "identity.Users" does not exist` (issue #66).
#
# It ran undetected through three betas and thirteen distributions because every check reached the
# panel with `curl` and `/health` reports that the database is REACHABLE — a question about the
# connection, never about whether anything is in it.
#
# Why the panel's own binary and not `dotnet ef`: a server carries the self-contained panel and no
# .NET SDK, so the EF tooling does not exist there. `Maran.Host --migrate` applies the migrations
# through the same EF API and the same migration-history table, then exits without serving anything.
#
# Placed after 60-config.sh, which writes the connection settings this reads, and before
# 70-services.sh, which starts the panel. A failure here stops the install while the operator is
# still watching, instead of becoming an HTTP 500 with a correlation id an hour later.

readonly MARAN_SCHEMA_BINARY="/usr/local/maran/api/Maran.Host"
readonly MARAN_SCHEMA_ENV_FILE="/etc/maran/panel.env"

# run_migrations: runs the panel's migration mode as the service account, with the panel's own
# environment, and refuses on anything but success.
#
# As MARAN_USER and not as root: the migrations connect over the unix socket with the panel's own
# identity, so running them as root would prove nothing about whether the PANEL can reach its
# database — and would be the one thing in this installer that touches the schema with privileges
# the running product does not have.
run_migrations() {
  [ -x "$MARAN_SCHEMA_BINARY" ] \
    || { echo "65-schema.sh: ${MARAN_SCHEMA_BINARY} is missing; 50-artifacts.sh must run first." >&2; exit 1; }
  [ -r "$MARAN_SCHEMA_ENV_FILE" ] \
    || { echo "65-schema.sh: ${MARAN_SCHEMA_ENV_FILE} is missing; 60-config.sh must run first." >&2; exit 1; }

  # The panel's environment, read the way systemd reads it, so this step and the running service
  # resolve the same database. `set -a` exports every assignment the file makes.
  local output status=0
  output="$(
    set -a
    # shellcheck disable=SC1090
    . "$MARAN_SCHEMA_ENV_FILE"
    set +a
    # From the panel's OWN directory. `WebApplication.CreateBuilder` takes the content root from
    # the current working directory, and this step runs wherever the operator launched the
    # installer — `/root/installer` for a `curl | sudo bash`, which the service account cannot
    # even enter. The panel then aborts before any of its own code runs:
    #
    #     Unhandled exception. System.IO.DirectoryNotFoundException: /root/installer/
    #
    # The unit sets no WorkingDirectory, so systemd starts it in `/`; what matters is only that
    # the directory exists and the service account can read it, and its own install directory is
    # the obvious choice.
    cd "$(dirname "$MARAN_SCHEMA_BINARY")" || exit 1
    runuser -u "$MARAN_USER" -- "$MARAN_SCHEMA_BINARY" --migrate 2>&1
  )" || status=$?

  printf '%s\n' "$output" | sed 's/^/  /'

  if [ "$status" -ne 0 ]; then
    cat >&2 <<EOF
65-schema.sh: applying the database migrations failed (exit ${status}).

The panel cannot work without its schema: every module keeps its own tables, and a panel
started against an empty database answers its own setup page with
'relation "identity.Users" does not exist'. Nothing is started until this succeeds.

Read the output above first. If it names a connection problem, check that PostgreSQL is
running and that /etc/maran/panel.env points at it:

    systemctl status postgresql
EOF
    exit 1
  fi

  # The vacuity guard. A migration mode that found no context to migrate exits 0 having done
  # nothing, and "up to date" and "there was nothing to look at" read identically in a log
  # (rules/testing.md). The binary refuses in that case; this checks that it said something at all.
  printf '%s' "$output" | grep -q . \
    || { echo "65-schema.sh: the migration run printed nothing, so it is not evidence that any schema exists." >&2; exit 1; }
}

step_schema() {
  echo "Applying database migrations..."
  run_migrations
  echo "Database schema is current."
}
