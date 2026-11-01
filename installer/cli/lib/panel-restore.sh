#!/usr/bin/env bash
# panel-restore.sh: `maran panel-restore` — turns a clean install into the panel a
# `maran panel-backup` archive was taken from (issue #129).
#
# What it puts back: the panel database, the encryption and signing keys (two lines of this host's
# panel.env — every other line describes THIS host and stays as the installer wrote it) and the
# licence. Then it brings the schema forward with the installed release's migrations and starts the
# panel. The archive's whole /etc/maran and /var/lib/maran are inside it for anything else an
# operator wants by hand.
#
# Nothing from the archive is executed or sourced: the manifest is matched against fixed lines,
# tar members are extracted by exact name into the private stage, and the two key values are
# validated against the base64 alphabet before they are written (rules/security.md item 4).

readonly MARAN_PANEL_RESTORE_USAGE="Usage: maran panel-restore --passphrase-file FILE --replace-this-panel ARCHIVE"
readonly MARAN_PANEL_RESTORE_KEYS="Security__EncryptionKey Jwt__SigningKey"
readonly MARAN_PANEL_LICENCE_FILE="licence.json"
readonly MARAN_PANEL_MIGRATE_BINARY="/usr/local/maran/api/Maran.Host"
# Both keys are base64 of at least 32 bytes, which is 44 characters; nothing outside the alphabet
# (no newline, no carriage return, no control character) can pass.
readonly MARAN_PANEL_KEY_PATTERN='^[A-Za-z0-9+/]{43,}={0,2}$'

# cli_panel_restore_cleanup: the EXIT-trap half of this verb. A prepared panel.env that was never
# moved into place is removed, and a panel this run stopped is started again, so a refused restore
# leaves the server serving what it served before.
cli_panel_restore_cleanup() {
  [ -z "${CLI_PANEL_RESTORE_ENV_TEMP:-}" ] || rm -f -- "$CLI_PANEL_RESTORE_ENV_TEMP"
  if [ "${CLI_PANEL_RESTORE_STOPPED:-0}" -eq 1 ]; then
    echo "maran: the restore did not finish; starting ${MARAN_PANEL_SERVICE} again" >&2
    systemctl start "$MARAN_PANEL_SERVICE" || echo "maran: ${MARAN_PANEL_SERVICE} did not start" >&2
  fi
}

# cli_panel_restore_member: extracts the one member named $2 from tar file $1 into directory $3 and
# refuses unless what arrived is a regular, non-symlink file. Prints its path.
cli_panel_restore_member() {
  local tarfile="$1" member="$2" dir="$3"
  install -d -m 0700 -o root -g root "$dir"
  tar -xf "$tarfile" -C "$dir" --no-same-owner --no-same-permissions -- "./${member}" \
    || cli_fail "the archive has no ${member}"
  [ -f "${dir}/${member}" ] && [ ! -L "${dir}/${member}" ] \
    || cli_fail "the archive's ${member} is not a regular file"
  printf '%s' "${dir}/${member}"
}

# cli_panel_restore_key: prints the value the archived panel.env ($1) gives the key named $2,
# refusing unless exactly one line sets it and the value is a well-formed key.
cli_panel_restore_key() {
  local env_file="$1" name="$2" lines value
  lines="$(grep -c -- "^${name}=" "$env_file" || true)"
  [ "$lines" = "1" ] || cli_fail "the archived panel.env sets ${name} ${lines} times, expected once"
  value="$(sed -n "s/^${name}=//p" -- "$env_file")"
  [[ "$value" =~ $MARAN_PANEL_KEY_PATTERN ]] || cli_fail "the archived ${name} is not a base64 key"
  printf '%s' "$value"
}

# cli_panel_restore_prepare_env: writes, beside the live panel.env, a copy whose two key lines carry
# the archived values, root:maran 0640 like the original. Moved into place only after the database
# is restored. The values reach awk through its environment, never through argv.
#   $1 = the archived panel.env.
cli_panel_restore_prepare_env() {
  local archived="$1" name
  for name in $MARAN_PANEL_RESTORE_KEYS; do
    [ "$(grep -c -- "^${name}=" "$MARAN_CLI_ENV_FILE" || true)" = "1" ] \
      || cli_fail "${MARAN_CLI_ENV_FILE} does not set ${name} exactly once; run the installer first"
  done
  local encryption signing
  encryption="$(cli_panel_restore_key "$archived" Security__EncryptionKey)"
  signing="$(cli_panel_restore_key "$archived" Jwt__SigningKey)"
  CLI_PANEL_RESTORE_ENV_TEMP="$(mktemp "${MARAN_CLI_ENV_FILE}.restore.XXXXXX")"
  MARAN_RESTORE_ENCRYPTION="$encryption" MARAN_RESTORE_SIGNING="$signing" awk '
    /^Security__EncryptionKey=/ { print "Security__EncryptionKey=" ENVIRON["MARAN_RESTORE_ENCRYPTION"]; next }
    /^Jwt__SigningKey=/ { print "Jwt__SigningKey=" ENVIRON["MARAN_RESTORE_SIGNING"]; next }
    { print }
  ' "$MARAN_CLI_ENV_FILE" > "$CLI_PANEL_RESTORE_ENV_TEMP" \
    || cli_fail "could not prepare the restored ${MARAN_CLI_ENV_FILE}"
  chown "root:${MARAN_CLI_SERVICE_USER}" -- "$CLI_PANEL_RESTORE_ENV_TEMP"
  chmod 0640 -- "$CLI_PANEL_RESTORE_ENV_TEMP"
}

# cli_panel_restore_database: replaces the panel database with the archived dump ($1), as the
# service account, in ONE transaction: a failure part-way rolls everything back and leaves the
# database this install created untouched.
cli_panel_restore_database() {
  local dump="$1" host database username
  host="$(cli_panel_env_value Database__Host)"
  database="$(cli_panel_env_value Database__Database)"
  username="$(cli_panel_env_value Database__Username)"
  [ -n "$host" ] && [ -n "$database" ] && [ -n "$username" ] \
    || cli_fail "${MARAN_CLI_ENV_FILE} does not name the panel database (Database__Host, __Database, __Username)"
  runuser -u "$MARAN_CLI_SERVICE_USER" -- pg_restore --clean --if-exists --no-owner --no-privileges \
    --single-transaction --exit-on-error --host="$host" --username="$username" --dbname="$database" \
    < "$dump" \
    || cli_fail "pg_restore failed and was rolled back; the database is as the install left it"
}

# cli_panel_restore_migrate: brings the restored schema forward to the installed release, the way
# installer/lib/65-schema.sh does: the panel's own binary, as the service account, with the panel's
# environment read in a subshell.
cli_panel_restore_migrate() {
  [ -x "$MARAN_PANEL_MIGRATE_BINARY" ] || cli_fail "${MARAN_PANEL_MIGRATE_BINARY} is missing"
  (
    set -a
    # shellcheck disable=SC1090
    . "$MARAN_CLI_ENV_FILE"
    set +a
    cd "$(dirname "$MARAN_PANEL_MIGRATE_BINARY")" || exit 1
    exec runuser -u "$MARAN_CLI_SERVICE_USER" -- "$MARAN_PANEL_MIGRATE_BINARY" --migrate
  ) || cli_fail "the restored database could not be migrated to this release"
}

# cli_panel_restore_licence: installs the archived licence, when the archive carries one, with the
# owner and mode FileLicenceWriter gives it, by rename so a reader never sees half a file.
#   $1 = the archived /var/lib/maran tar.
cli_panel_restore_licence() {
  local state_tar="$1" licence temp
  # The listing is written out first: `tar | grep -q` under pipefail reports tar's SIGPIPE when grep
  # stops reading at its match, which would read as "no licence" for the very archive that has one.
  tar -tf "$state_tar" > "${CLI_PANEL_STAGE}/state.list" || cli_fail "could not list the archived ${MARAN_PANEL_STATE_DIR}"
  grep -qxF -- "./${MARAN_PANEL_LICENCE_FILE}" "${CLI_PANEL_STAGE}/state.list" || return 0
  licence="$(cli_panel_restore_member "$state_tar" "$MARAN_PANEL_LICENCE_FILE" "${CLI_PANEL_STAGE}/state")"
  temp="$(mktemp "${MARAN_PANEL_STATE_DIR}/.${MARAN_PANEL_LICENCE_FILE}.restore.XXXXXX")"
  install -m 0640 -o "$MARAN_CLI_SERVICE_USER" -g "$MARAN_CLI_SERVICE_USER" -- "$licence" "$temp"
  mv -f -- "$temp" "${MARAN_PANEL_STATE_DIR}/${MARAN_PANEL_LICENCE_FILE}"
}

# cli_panel_restore: the verb. Arguments: --passphrase-file FILE --replace-this-panel ARCHIVE.
cli_panel_restore() {
  local passphrase_file="" archive="" replace=0
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --passphrase-file)
        [ "$#" -ge 2 ] || cli_fail "--passphrase-file needs a value. ${MARAN_PANEL_RESTORE_USAGE}"
        passphrase_file="$2"
        shift
        ;;
      --replace-this-panel) replace=1 ;;
      -*) cli_fail "unknown option. ${MARAN_PANEL_RESTORE_USAGE}" ;;
      *)
        [ -z "$archive" ] || cli_fail "give exactly one archive. ${MARAN_PANEL_RESTORE_USAGE}"
        archive="$1"
        ;;
    esac
    shift
  done
  [ -n "$passphrase_file" ] && [ -n "$archive" ] || cli_fail "$MARAN_PANEL_RESTORE_USAGE"
  [ "$replace" -eq 1 ] \
    || cli_fail "a restore replaces this panel's database and keys; pass --replace-this-panel to confirm"

  cli_panel_require_passphrase_file "$passphrase_file"
  [[ "$archive" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$archive" != *..* ]] \
    || cli_fail "the archive must be an absolute path of letters, digits, '.', '_', '-' and '/'"
  [ -f "$archive" ] && [ ! -L "$archive" ] || cli_fail "the archive is not a regular file"
  [ "$(stat -c '%u' -- "$archive")" = "0" ] || cli_fail "the archive must be owned by root"
  [ -r "$MARAN_CLI_ENV_FILE" ] || cli_fail "${MARAN_CLI_ENV_FILE} is missing; install Maran first"

  # Globals, not locals: the EXIT trap runs after this function has returned.
  CLI_PANEL_RESTORE_ENV_TEMP=""
  CLI_PANEL_RESTORE_STOPPED=0
  cli_panel_open_stage cli_panel_restore_cleanup
  local inner_tar="${CLI_PANEL_STAGE}/inner.tar" inner="${CLI_PANEL_STAGE}/inner"
  # gpg checks the integrity protection and refuses a modified or truncated archive, or a wrong
  # passphrase, before a byte of it is used.
  cli_panel_gpg "$passphrase_file" --decrypt --output "$inner_tar" -- "$archive" \
    || cli_fail "could not decrypt the archive: wrong passphrase, or the file is damaged"

  # Each member is assigned on its own line: a refusal inside a command substitution that is only
  # an ARGUMENT would not stop this script, while a failing assignment does (set -e).
  local manifest dump etc_tar state_tar archived_env
  manifest="$(cli_panel_restore_member "$inner_tar" "$MARAN_PANEL_ARCHIVE_MANIFEST" "$inner")"
  grep -qxF -- "format=${MARAN_PANEL_ARCHIVE_FORMAT}" "$manifest" \
    || cli_fail "the archive's format is not one this release restores (expected ${MARAN_PANEL_ARCHIVE_FORMAT})"
  dump="$(cli_panel_restore_member "$inner_tar" "$MARAN_PANEL_ARCHIVE_DATABASE" "$inner")"
  etc_tar="$(cli_panel_restore_member "$inner_tar" "$MARAN_PANEL_ARCHIVE_ETC" "$inner")"
  state_tar="$(cli_panel_restore_member "$inner_tar" "$MARAN_PANEL_ARCHIVE_STATE" "$inner")"
  archived_env="$(cli_panel_restore_member "$etc_tar" panel.env "${CLI_PANEL_STAGE}/etc")"
  cli_panel_restore_prepare_env "$archived_env"

  # From here the panel is down; the EXIT trap starts it again on any refusal.
  systemctl stop "$MARAN_PANEL_SERVICE" || cli_fail "could not stop ${MARAN_PANEL_SERVICE}"
  CLI_PANEL_RESTORE_STOPPED=1
  cli_panel_restore_database "$dump"
  # The keys follow the data at once: a database written under the archived key is unreadable
  # under this install's key, so the rename is the very next step after the commit.
  mv -f -- "$CLI_PANEL_RESTORE_ENV_TEMP" "$MARAN_CLI_ENV_FILE"
  CLI_PANEL_RESTORE_ENV_TEMP=""
  cli_panel_restore_licence "$state_tar"
  # A file written by install/mv carries the staging context, not the one policy gives its path.
  if command -v restorecon >/dev/null 2>&1; then
    restorecon -- "$MARAN_CLI_ENV_FILE" "$MARAN_PANEL_STATE_DIR"/* || true
  fi
  cli_panel_restore_migrate

  systemctl start "$MARAN_PANEL_SERVICE" || cli_fail "the restore finished but ${MARAN_PANEL_SERVICE} did not start"
  CLI_PANEL_RESTORE_STOPPED=0
  cli_audit_log panel-restore restored

  echo "Panel restored from ${archive}."
  echo
  echo "The database, the encryption and signing keys and the licence are back; everyone signs in"
  echo "again. Customer files, sites and certificates on disk are not in a panel backup: restore"
  echo "them from the account backups."
}
