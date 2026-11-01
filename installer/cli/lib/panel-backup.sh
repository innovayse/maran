#!/usr/bin/env bash
# panel-backup.sh: `maran panel-backup` — an encrypted archive of everything a clean install needs
# to become this panel again: its database, its encryption and signing keys, its licence and its
# configuration (issue #129).
#
# Account backups carry customers' files and databases; this carries the panel itself. Without it a
# dead server's encrypted columns are unreadable anywhere else, because the key that wrote them
# lived only in this host's /etc/maran/panel.env.
#
# The database is read by the service account (pg_dump over the unix socket, peer authentication):
# root opens the output file and hands the descriptor over, so root never authenticates to
# PostgreSQL and the service account never writes into a root directory.

readonly MARAN_PANEL_BACKUP_DEFAULT_OUT="/root"
readonly MARAN_PANEL_BACKUP_USAGE="Usage: maran panel-backup --passphrase-file FILE [--out DIR]"

# cli_panel_backup_cleanup: the EXIT-trap half of this verb — an archive that was created but not
# finished is removed, so a failed run never leaves a truncated file that looks like a backup.
cli_panel_backup_cleanup() {
  if [ "${CLI_PANEL_BACKUP_CREATED:-0}" -eq 1 ] && [ "${CLI_PANEL_BACKUP_OK:-0}" -ne 1 ]; then
    rm -f -- "$CLI_PANEL_BACKUP_TARGET"
  fi
}

# cli_panel_backup_stage: writes the plaintext members into $1, the inner tar's directory.
cli_panel_backup_stage() {
  local dir="$1" host database username
  host="$(cli_panel_env_value Database__Host)"
  database="$(cli_panel_env_value Database__Database)"
  username="$(cli_panel_env_value Database__Username)"
  [ -n "$host" ] && [ -n "$database" ] && [ -n "$username" ] \
    || cli_fail "${MARAN_CLI_ENV_FILE} does not name the panel database (Database__Host, __Database, __Username)"

  {
    echo "format=${MARAN_PANEL_ARCHIVE_FORMAT}"
    echo "created=$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    echo "database=${database}"
  } > "${dir}/${MARAN_PANEL_ARCHIVE_MANIFEST}"

  # Every schema, Wolverine's message store included: a restore that dropped in-flight messages
  # would lose work the panel had accepted. --no-owner because the restoring role is the panel's
  # own on the new host, whatever this one was called.
  runuser -u "$MARAN_CLI_SERVICE_USER" -- pg_dump --format=custom --no-owner --no-privileges \
    --host="$host" --username="$username" --dbname="$database" \
    > "${dir}/${MARAN_PANEL_ARCHIVE_DATABASE}" \
    || cli_fail "pg_dump of the panel database failed; nothing was written"

  # The two trees travel whole, numeric owners kept, so an operator restoring by hand on the new
  # host has every file; panel-restore puts back only what a fresh install cannot regenerate.
  tar --numeric-owner -cf "${dir}/${MARAN_PANEL_ARCHIVE_ETC}" -C "$MARAN_PANEL_ETC_DIR" . \
    || cli_fail "could not archive ${MARAN_PANEL_ETC_DIR}; nothing was written"
  tar --numeric-owner -cf "${dir}/${MARAN_PANEL_ARCHIVE_STATE}" -C "$MARAN_PANEL_STATE_DIR" . \
    || cli_fail "could not archive ${MARAN_PANEL_STATE_DIR}; nothing was written"
}

# cli_panel_backup: the verb. Arguments: --passphrase-file FILE [--out DIR].
cli_panel_backup() {
  local out="$MARAN_PANEL_BACKUP_DEFAULT_OUT" passphrase_file="" arg
  while [ "$#" -gt 0 ]; do
    arg="$1"
    case "$arg" in
      --out|--passphrase-file)
        [ "$#" -ge 2 ] || cli_fail "${arg} needs a value. ${MARAN_PANEL_BACKUP_USAGE}"
        if [ "$arg" = "--out" ]; then out="$2"; else passphrase_file="$2"; fi
        shift
        ;;
      *) cli_fail "unknown option. ${MARAN_PANEL_BACKUP_USAGE}" ;;
    esac
    shift
  done
  [ -n "$passphrase_file" ] || cli_fail "$MARAN_PANEL_BACKUP_USAGE"

  cli_require_private_out_dir "$out"
  cli_panel_require_passphrase_file "$passphrase_file"
  [ -d "$MARAN_PANEL_ETC_DIR" ] && [ -d "$MARAN_PANEL_STATE_DIR" ] \
    || cli_fail "${MARAN_PANEL_ETC_DIR} or ${MARAN_PANEL_STATE_DIR} is missing; is Maran installed?"

  # Globals, not locals: the EXIT trap runs after this function has returned.
  CLI_PANEL_BACKUP_TARGET=""
  CLI_PANEL_BACKUP_CREATED=0
  CLI_PANEL_BACKUP_OK=0
  cli_panel_open_stage cli_panel_backup_cleanup
  local inner="${CLI_PANEL_STAGE}/inner"
  install -d -m 0700 -o root -g root "$inner"
  cli_panel_backup_stage "$inner"

  local target
  target="${out%/}/maran-panel-backup-$(date -u '+%Y%m%dT%H%M%SZ')-$$.tar.gpg"
  # noclobber makes the redirection an O_EXCL create, so a planted file or symlink at the target is
  # refused rather than followed; umask 077 makes it 0600 from its first byte. The group confines
  # the 2>/dev/null to the open alone (see diagnostics.sh for the defect that taught this).
  set -C
  if ! { exec 9> "$target"; } 2>/dev/null; then
    set +C
    cli_fail "refusing to write ${target}: it already exists or cannot be created"
  fi
  set +C
  CLI_PANEL_BACKUP_TARGET="$target"
  CLI_PANEL_BACKUP_CREATED=1

  # Iterated-and-salted S2K over SHA-512 at gpg's highest count; gpg adds its integrity protection
  # to symmetric output on every supported family, and the restore relies on it being checked.
  tar --owner=0 --group=0 --numeric-owner -cf - -C "$inner" . \
    | cli_panel_gpg "$passphrase_file" --symmetric --cipher-algo AES256 \
        --s2k-mode 3 --s2k-digest-algo SHA512 --s2k-count 65011712 --compress-algo none \
        --output - >&9 \
    || cli_fail "could not encrypt the archive; nothing was written"
  exec 9>&-
  CLI_PANEL_BACKUP_OK=1
  cli_audit_log panel-backup written

  echo "Panel backup: ${target}"
  echo
  echo "This archive holds the panel's database and the key that decrypts every secret in it."
  echo "Whoever has the archive and its passphrase has this panel. Copy it off this server, keep"
  echo "the passphrase apart from it, and restore it with: maran panel-restore"
}
