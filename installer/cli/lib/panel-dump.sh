#!/usr/bin/env bash
# panel-dump.sh: the ONE place the panel database is dumped and restored (issue #128; moved here
# from panel-backup.sh and panel-restore.sh, which each carried their own copy).
#
# Sourced by /usr/local/sbin/maran with the other libraries; never executed on its own. Callers:
# panel-backup.sh and the pre-update dump of `maran update` use cli_dump_database; panel-restore.sh
# and `maran rollback --restore-database` use cli_restore_database.
#
# The rule both functions keep: the database is read and written by the SERVICE ACCOUNT over
# PostgreSQL's peer authentication (runuser), so root never authenticates to PostgreSQL. Root only
# opens or reads the dump file and hands the descriptor over, so the service account never gets a
# path into a root directory and never writes into one.

# CLI_DUMP_PG_ARGUMENTS: the connection options both functions give pg_dump and pg_restore. A
# global, not a local, because bash cannot return an array from a function.
CLI_DUMP_PG_ARGUMENTS=()

# cli_dump_read_connection: fills CLI_DUMP_PG_ARGUMENTS from the live panel.env (Database__Host,
# __Database and __Username) and refuses unless all three are set.
cli_dump_read_connection() {
  local host database username
  host="$(cli_panel_env_value Database__Host)"
  database="$(cli_panel_env_value Database__Database)"
  username="$(cli_panel_env_value Database__Username)"
  [ -n "$host" ] && [ -n "$database" ] && [ -n "$username" ] \
    || cli_fail "${MARAN_CLI_ENV_FILE} does not name the panel database (Database__Host, __Database, __Username)"
  CLI_DUMP_PG_ARGUMENTS=(--host="$host" --username="$username" --dbname="$database")
}

# cli_dump_require_private_parent: refuses unless the directory holding $1 is a plain (non-symlink)
# directory owned by root with mode 0700, so nobody but root can plant, read or swap the dump.
# The path must also be absolute and made of plain characters, the alphabet every other verb uses.
cli_dump_require_private_parent() {
  local outfile="$1" parent
  [[ "$outfile" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$outfile" != *..* ]] \
    || cli_fail "the dump path must be absolute and made of letters, digits, '.', '_', '-' and '/'"
  parent="$(dirname -- "$outfile")"
  if [ -L "$parent" ] || [ ! -d "$parent" ] || [ "$(stat -c '%u %a' -- "$parent")" != "0 700" ]; then
    cli_fail "${parent} is not a root-owned 0700 directory; refusing to write a database dump there"
  fi
}

# cli_dump_database: writes a custom-format dump of the panel database to $1, as a file of mode
# 0600. Every schema is dumped, Wolverine's message store included: a restore that dropped
# in-flight messages would lose work the panel had accepted. --no-owner because the restoring role
# is the panel's own on the host it is restored to, whatever this one was called.
#
# The service account runs pg_dump and writes to a descriptor root opened, under umask 077. Refuses
# when the file's directory is not a root-owned 0700 directory, when pg_dump fails, and when it
# succeeds but leaves nothing (a dump that is not a dump must never be taken for one); a failed run
# removes what it wrote.
#   $1 = the file to create, an absolute path.
cli_dump_database() {
  local outfile="$1"
  cli_dump_require_private_parent "$outfile"
  cli_dump_read_connection
  if ! (
    umask 077
    exec runuser -u "$MARAN_CLI_SERVICE_USER" -- pg_dump --format=custom --no-owner --no-privileges \
      "${CLI_DUMP_PG_ARGUMENTS[@]}" > "$outfile"
  ); then
    rm -f -- "$outfile"
    cli_fail "pg_dump of the panel database failed; nothing was written"
  fi
  if [ ! -s "$outfile" ]; then
    rm -f -- "$outfile"
    cli_fail "pg_dump of the panel database produced no output; nothing was written"
  fi
}

# CLI_RESTORE_CLEAR_SQL: the first statement of every restore. Drops every schema in the database
# except `public` and the system ones, with everything in them, so a schema or table that a NEWER
# release's migrations created after the dump was taken cannot survive into the restored database or
# make the restore refuse. `public` is kept because the panel creates nothing in it (every module
# owns a schema named after it) and the dump's own --clean handles what it does hold. Schemas are
# found by catalog query, never by a list, so a module added by a later release is covered.
readonly CLI_RESTORE_CLEAR_SQL='DO $clear$
DECLARE doomed name;
BEGIN
  FOR doomed IN
    SELECT nspname FROM pg_catalog.pg_namespace
    WHERE nspname <> $$public$$ AND nspname <> $$information_schema$$ AND nspname NOT LIKE $$pg\_%$$
  LOOP
    EXECUTE pg_catalog.format($$DROP SCHEMA %I CASCADE$$, doomed);
  END LOOP;
END
$clear$;'

# cli_restore_database: returns the panel database to the state of the dump in $1, as the service
# account, in ONE transaction: a failure part-way rolls everything back and leaves the database as it
# was. Root opens every file and hands descriptors over, so the service account never needs a path
# into a root directory.
#
# Two steps, because pg_restore cannot do this alone. --clean drops only the objects the DUMP lists,
# so what a newer release added after the dump (a schema, a table) would stay and the restore would
# refuse with "already exists". So pg_restore first renders the dump as a SQL script (no database
# connection: a dump that cannot be read fails here, before the database is touched), the script is
# prefixed with CLI_RESTORE_CLEAR_SQL, and psql runs the whole file in one transaction with
# ON_ERROR_STOP. The script is written whole before psql starts: a pg_restore that died half way
# must never reach psql as a truncated script it could commit.
#
# Needs the stage directory of cli_panel_open_stage (root-only, removed on exit): the script holds
# the database's contents and lives only there, mode 0600, and is removed as soon as it has run.
#   $1 = the custom-format dump to restore.
cli_restore_database() {
  local dump="$1" script="${CLI_PANEL_STAGE:?cli_restore_database needs the stage of cli_panel_open_stage}/restore.sql"
  cli_dump_read_connection
  if ! (
    umask 077
    {
      printf '%s\n' "$CLI_RESTORE_CLEAR_SQL"
      runuser -u "$MARAN_CLI_SERVICE_USER" -- pg_restore --clean --if-exists --no-owner --no-privileges \
        --file=- < "$dump"
    } > "$script"
  ); then
    rm -f -- "$script"
    cli_fail "the dump could not be read by pg_restore; nothing was changed"
  fi
  if ! runuser -u "$MARAN_CLI_SERVICE_USER" -- psql --no-psqlrc --quiet --single-transaction \
    --set=ON_ERROR_STOP=1 --output=/dev/null "${CLI_DUMP_PG_ARGUMENTS[@]}" < "$script"; then
    rm -f -- "$script"
    cli_fail "the restore failed and was rolled back; nothing was changed"
  fi
  rm -f -- "$script"
}
