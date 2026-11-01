#!/usr/bin/env bash
# panel-archive.sh: what `maran panel-backup` and `maran panel-restore` agree on — the archive's
# layout, the passphrase file's rules, the private staging directory and gpg's invocation.
#
# Both verbs handle the panel's encryption key in plaintext for the length of one run, so the
# plaintext only ever exists inside one stage directory under the root-only staging base, and
# every exit removes it. docs/superpowers/notes/2026-11-01-panel-disaster-recovery-threat-note.md
# carries the argument.

# The archive's format version, written into its manifest and required verbatim by a restore. A
# change to the layout below raises it; a restore never guesses at a layout it does not know.
readonly MARAN_PANEL_ARCHIVE_FORMAT="1"
# The members of the inner (plaintext) tar, by exact name. Nothing else is ever read out of it.
readonly MARAN_PANEL_ARCHIVE_MANIFEST="manifest.txt"
readonly MARAN_PANEL_ARCHIVE_DATABASE="database.dump"
readonly MARAN_PANEL_ARCHIVE_ETC="etc-maran.tar"
readonly MARAN_PANEL_ARCHIVE_STATE="var-lib-maran.tar"
# The trees the archive carries whole, and the service the restore stops and starts.
readonly MARAN_PANEL_ETC_DIR="/etc/maran"
readonly MARAN_PANEL_STATE_DIR="/var/lib/maran"
readonly MARAN_PANEL_SERVICE="maran-api.service"
# A passphrase shorter than this is refused. A floor against the obvious mistake, not a strength
# meter: the archive is as sensitive as root on the server.
readonly MARAN_PANEL_PASSPHRASE_MIN_LENGTH=20
# Held for the whole run, so a backup and a restore never share the staging base at once, and so a
# stage directory left by a run that was killed can be removed safely by the next one.
readonly MARAN_PANEL_LOCK_FILE="panel-archive.lock"
readonly MARAN_PANEL_STAGE_PREFIX="panel-archive."

# cli_panel_require_passphrase_file: refuses unless $1 is a regular, non-symlink file owned by root,
# readable and writable by root alone (0600 or 0400), whose first line is long enough. gpg reads the
# file itself; the passphrase never reaches argv, the environment or this shell's variables.
cli_panel_require_passphrase_file() {
  local file="$1" mode length
  [[ "$file" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$file" != *..* ]] \
    || cli_fail "--passphrase-file must be an absolute path of letters, digits, '.', '_', '-' and '/'"
  [ -f "$file" ] && [ ! -L "$file" ] || cli_fail "--passphrase-file is not a regular file"
  [ "$(stat -c '%u' -- "$file")" = "0" ] || cli_fail "--passphrase-file must be owned by root"
  mode="$(stat -c '%a' -- "$file")"
  [[ "$mode" =~ ^[46]00$ ]] || cli_fail "--passphrase-file must have mode 0600 or 0400"
  # Measured by awk on the first line, which is the line gpg takes: the length is the only fact
  # about the passphrase this script ever learns.
  length="$(awk 'NR == 1 { print length($0); exit }' -- "$file")"
  [ "${length:-0}" -ge "$MARAN_PANEL_PASSPHRASE_MIN_LENGTH" ] \
    || cli_fail "the passphrase must be at least ${MARAN_PANEL_PASSPHRASE_MIN_LENGTH} characters long"
}

# cli_panel_open_stage: takes the lock, clears stage directories a killed run left behind, and
# creates this run's stage directory with a private GNUPGHOME inside it. Sets CLI_PANEL_STAGE and
# installs the EXIT trap that runs the caller's own cleanup function ($1, a fixed function name the
# verb passes — never text from the operator) and then removes the stage.
cli_panel_open_stage() {
  local on_exit="$1" stale
  cli_prepare_staging_base
  # Descriptor 8 stays open for the life of the process; the kernel drops the lock when it exits,
  # however it exits, so a killed run never leaves the lock held.
  exec 8> "${MARAN_CLI_STAGING_BASE}/${MARAN_PANEL_LOCK_FILE}"
  flock -n 8 || cli_fail "another panel backup or restore is running"
  for stale in "${MARAN_CLI_STAGING_BASE}/${MARAN_PANEL_STAGE_PREFIX}"*; do
    [ -d "$stale" ] && [ ! -L "$stale" ] && rm -rf -- "$stale"
  done
  CLI_PANEL_STAGE="$(mktemp -d "${MARAN_CLI_STAGING_BASE}/${MARAN_PANEL_STAGE_PREFIX}XXXXXX")"
  install -d -m 0700 -o root -g root "${CLI_PANEL_STAGE}/gnupg"
  CLI_PANEL_ON_EXIT="$on_exit"
  # Signals become exits so the EXIT trap runs for them too.
  trap 'exit 130' INT
  trap 'exit 143' TERM HUP
  trap '"$CLI_PANEL_ON_EXIT"; rm -rf -- "$CLI_PANEL_STAGE"' EXIT
}

# cli_panel_gpg: gpg with a private home inside the stage, no tty, no agent prompt, and the
# passphrase taken from the operator's file. The remaining arguments are gpg's.
#   $1 = passphrase file, the rest = gpg arguments.
cli_panel_gpg() {
  local passphrase_file="$1"
  shift
  gpg --homedir "${CLI_PANEL_STAGE}/gnupg" --batch --quiet --no-tty --pinentry-mode loopback \
    --passphrase-file "$passphrase_file" "$@"
}
