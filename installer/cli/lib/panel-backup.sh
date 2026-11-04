#!/usr/bin/env bash
# panel-backup.sh: `maran panel-backup` — an encrypted archive of everything a clean install needs
# to become this panel again: its database, its encryption and signing keys, its licence and its
# configuration (issue #129).
#
# Account backups carry customers' files and databases; this carries the panel itself. Without it a
# dead server's encrypted columns are unreadable anywhere else, because the key that wrote them
# lived only in this host's /etc/maran/panel.env.
#
# The database is read by the service account (pg_dump, peer authentication), through
# cli_dump_database in panel-dump.sh: root opens the output file and hands the descriptor over, so
# root never authenticates to PostgreSQL and the service account never writes into a root
# directory.
#
# `--scheduled` is the same backup run by the systemd timer: its arguments come from
# /etc/maran/panel-backup.conf (panel-backup-config.sh), and after the archive exists it applies
# retention (panel-backup-retention.sh) and uploads when S3 is configured (panel-backup-upload.sh).

readonly MARAN_PANEL_BACKUP_DEFAULT_OUT="/root"
readonly MARAN_PANEL_BACKUP_USAGE="Usage: maran panel-backup --passphrase-file FILE [--out DIR] | maran panel-backup --scheduled"

# cli_panel_backup_cleanup: the EXIT-trap half of this verb — an archive that was created but not
# finished is removed, so a failed run never leaves a truncated file that looks like a backup.
cli_panel_backup_cleanup() {
  if [ "${CLI_PANEL_BACKUP_CREATED:-0}" -eq 1 ] && [ "${CLI_PANEL_BACKUP_OK:-0}" -ne 1 ]; then
    rm -f -- "$CLI_PANEL_BACKUP_TARGET"
  fi
}

# cli_panel_backup_stage: writes the plaintext members into $1, the inner tar's directory.
cli_panel_backup_stage() {
  local dir="$1" database

  # The dump is panel-dump.sh's (every schema, read by the service account); it refuses first when
  # panel.env does not name the database, so the manifest below never describes a dump that failed.
  cli_dump_database "${dir}/${MARAN_PANEL_ARCHIVE_DATABASE}"

  database="$(cli_panel_env_value Database__Database)"
  {
    echo "format=${MARAN_PANEL_ARCHIVE_FORMAT}"
    echo "created=$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    echo "database=${database}"
  } > "${dir}/${MARAN_PANEL_ARCHIVE_MANIFEST}"

  # The two trees travel whole, numeric owners kept, so an operator restoring by hand on the new
  # host has every file; panel-restore puts back only what a fresh install cannot regenerate.
  tar --numeric-owner -cf "${dir}/${MARAN_PANEL_ARCHIVE_ETC}" -C "$MARAN_PANEL_ETC_DIR" . \
    || cli_fail "could not archive ${MARAN_PANEL_ETC_DIR}; nothing was written"
  tar --numeric-owner -cf "${dir}/${MARAN_PANEL_ARCHIVE_STATE}" -C "$MARAN_PANEL_STATE_DIR" . \
    || cli_fail "could not archive ${MARAN_PANEL_STATE_DIR}; nothing was written"
}

# cli_panel_backup_execute: takes one backup — checks, stages, encrypts — and leaves the archive's
# path in CLI_PANEL_BACKUP_TARGET. Shared by the manual verb and the scheduled job so there is one
# implementation of the backup; the caller decides what to log and print. The stage directory, its
# lock and the EXIT trap stay open on return, so a caller that goes on (retention, upload) works
# inside the same private stage and every exit still cleans up.
#   $1 = passphrase file, $2 = output directory.
cli_panel_backup_execute() {
  local passphrase_file="$1" out="$2"
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
}

# cli_panel_backup_scheduled_run: the work of one scheduled run, in the order that keeps the last
# good archive safe: configuration, backup, retention (only after the backup succeeded), upload. A
# failure of retention or upload does not skip the other, and does not touch the local archive; it
# is reported once, at the end, so the unit fails visibly. Runs in a subshell of
# cli_panel_backup_scheduled, so a refusal (cli_fail) ends only this run.
cli_panel_backup_scheduled_run() {
  cli_pbconfig_load
  cli_panel_backup_execute "$MARAN_PBCONF_PASSPHRASE_FILE" "$MARAN_PBCONF_OUT_DIR"
  local retention_failed=0 upload_failed=0
  cli_pbretention_apply "$MARAN_PBCONF_OUT_DIR" "$MARAN_PBCONF_KEEP" "$CLI_PANEL_BACKUP_TARGET" \
    || retention_failed=1
  if [ -n "$MARAN_PBCONF_S3_ENDPOINT" ]; then
    cli_pbupload_send "$CLI_PANEL_BACKUP_TARGET" || upload_failed=1
  fi
  [ "$retention_failed" -eq 0 ] || cli_fail "the archive was written and kept, but retention failed"
  [ "$upload_failed" -eq 0 ] || cli_fail "the archive was written and kept locally, but the upload failed"
  echo "Scheduled panel backup written: ${CLI_PANEL_BACKUP_TARGET}"
}

# cli_panel_backup_scheduled: `maran panel-backup --scheduled`, what the systemd service runs. Takes
# its arguments from the configuration file, not the command line. Exactly one audit line goes to
# the journal under the maran-cli tag whatever the outcome, carrying the verb and the outcome only,
# and a failure returns non-zero so the unit shows `failed`.
#
# The run is a subshell so that any refusal inside it, however deep, comes back here as a status and
# is logged. errexit is switched off in this shell around it and back on inside it: a subshell that
# is the left side of `||` would run with errexit disabled and silently continue past failures.
cli_panel_backup_scheduled() {
  local status=0
  set +e
  ( set -e; cli_panel_backup_scheduled_run )
  status=$?
  set -e
  if [ "$status" -eq 0 ]; then
    cli_audit_log panel-backup-scheduled written
  else
    cli_audit_log panel-backup-scheduled "failed-${status}"
  fi
  return "$status"
}

# cli_panel_backup: the verb. Arguments: --passphrase-file FILE [--out DIR], or --scheduled alone.
cli_panel_backup() {
  local out="$MARAN_PANEL_BACKUP_DEFAULT_OUT" passphrase_file="" arg
  if [ "${1:-}" = "--scheduled" ]; then
    [ "$#" -eq 1 ] || cli_fail "--scheduled takes no other option. ${MARAN_PANEL_BACKUP_USAGE}"
    cli_panel_backup_scheduled
    return
  fi
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

  cli_panel_backup_execute "$passphrase_file" "$out"
  cli_audit_log panel-backup written

  echo "Panel backup: ${CLI_PANEL_BACKUP_TARGET}"
  echo
  echo "This archive holds the panel's database and the key that decrypts every secret in it."
  echo "Whoever has the archive and its passphrase has this panel. Copy it off this server, keep"
  echo "the passphrase apart from it, and restore it with: maran panel-restore"
}
