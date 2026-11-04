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

# cli_restore_database: replaces the panel database with the dump in $1, as the service account,
# in ONE transaction: a failure part-way rolls everything back and leaves the database as it was.
# Root opens the file and the service account reads it on standard input, so it never needs a path
# into a root directory.
#
# Known limit, deliberately not worked around: --clean drops only the objects the DUMP contains. A
# schema or table that a NEWER release's migrations created after the dump was taken is not in it, so
# it is not dropped, and the restore then fails inside the transaction ("already exists" and similar).
# That is the fail-closed outcome: the transaction rolls back and nothing changes. No DROP is issued
# here to clear such objects; deleting what a newer release created is an operator decision.
#   $1 = the custom-format dump to restore.
cli_restore_database() {
  local dump="$1"
  cli_dump_read_connection
  runuser -u "$MARAN_CLI_SERVICE_USER" -- pg_restore --clean --if-exists --no-owner --no-privileges \
    --single-transaction --exit-on-error "${CLI_DUMP_PG_ARGUMENTS[@]}" \
    < "$dump" \
    || cli_fail "pg_restore failed; nothing was changed. If the newer release added schemas or tables," \
      "the restore cannot remove them; see docs/operations/panel-update.md"
}
