#!/usr/bin/env bash
# Step 67: install the units of the scheduled panel backup (issue #129) and the directory it writes
# into. It runs right after 66-cli.sh because the units run `maran panel-backup --scheduled`, the
# verb that step installs.
#
# What this step does NOT do, and why each omission is deliberate:
#
# - It does not enable the timer, and the timer it installs has no OnCalendar= of its own. A backup
#   needs a passphrase file and a schedule that only the operator can choose, and an unattended root
#   job that reads the panel's encryption key must not exist before they have. The operator writes
#   /etc/maran/panel-backup.conf and runs `maran panel-backup-schedule enable`, which validates the
#   file, renders the schedule into a drop-in and enables the timer. A fresh install, and an upgrade
#   of one that never enabled it, therefore run nothing.
# - It does not write the config file or any credential, and it never reads one.
# - It does not touch an existing archive. The output directory is created when absent and its
#   ownership and mode are put right when present; nothing in it is listed, moved or removed.
#
# Every action is idempotent, because installers get re-run: `install` overwrites a unit with the
# same bytes, `install -d` accepts an existing directory, and `daemon-reload` is repeatable. An
# upgrade replaces the unit files but leaves the operator's drop-ins and enablement alone, so a
# schedule that was on stays on.
set -euo pipefail

readonly MARAN_PANEL_BACKUP_UNIT_DIR="/etc/systemd/system"
# The default OUT_DIR. A SIBLING of /var/backups/maran (the agent's customer backups) and not a child
# of it: retention deletes archives here, and it must never be possible to aim that at a customer's
# backup tree. The unit's ReadWritePaths= names this exact path, so a rename here without one there
# stops the service from starting.
readonly MARAN_PANEL_BACKUP_OUT_DIR="/var/backups/maran-panel"
# The verb's plaintext staging base (MARAN_CLI_STAGING_BASE in installer/cli/lib/common.sh). Created
# here as well as by the verb because the unit's ProtectSystem=strict makes /var/lib read-only to the
# verb, so a directory that does not exist yet could not be created from inside the unit.
readonly MARAN_PANEL_BACKUP_STAGING_DIR="/var/lib/maran-cli-staging"
# The documented example config, shipped beside the CLI libraries (66-cli.sh's install tree, which
# uninstall.sh removes whole). Not placed in /etc/maran: the operator copies it, so the live file
# only ever exists because a human chose to create it.
readonly MARAN_PANEL_BACKUP_EXAMPLE_SOURCE="${SCRIPT_DIR:?must be set by install.sh}/panel-backup.conf.example"
readonly MARAN_PANEL_BACKUP_EXAMPLE_DEST="/usr/local/maran/cli/panel-backup.conf.example"

# panel_backup_require_private_directory: creates $1 root:root 0700 when absent, then OBSERVES it.
#
# `install -d` accepts an existing path and re-modes a directory, but it follows a symbolic link, so
# a link planted at the name would send root's archives elsewhere. A link or a non-directory is
# therefore a refusal made BEFORE anything is created or changed, and the result is read back rather
# than assumed — the same discipline 66-cli.sh applies to /usr/local/sbin.
panel_backup_require_private_directory() {
  local path="$1" observed
  if [ -L "$path" ] || { [ -e "$path" ] && [ ! -d "$path" ]; }; then
    echo "67-panel-backup.sh: ${path} is a symbolic link or not a directory. Refusing to write" >&2
    echo "  root's backups through it. Move it aside and re-run the installer." >&2
    exit 1
  fi
  install -d -m 0700 -o root -g root "$path"
  observed="$(stat -c '%a %u %g' -- "$path")"
  if [ "$observed" != "700 0 0" ]; then
    echo "67-panel-backup.sh: ${path} is '${observed}' (mode owner group) but must be '700 0 0'." >&2
    exit 1
  fi
}

# install_panel_backup_units: copies the service and the timer to /etc/systemd/system, root:root 0644.
#
# Neither file has a placeholder, so they are installed as they stand from the installer package
# (the way 70-services.sh installs the tmpfiles snippets with nothing to substitute). A source that
# is missing stops the install while the operator is watching, not at the first night the timer
# should have fired.
install_panel_backup_units() {
  local unit
  for unit in maran-panel-backup.service maran-panel-backup.timer; do
    [ -f "${LIB_DIR}/../systemd/${unit}" ] \
      || { echo "67-panel-backup.sh: ${LIB_DIR}/../systemd/${unit} is missing from the installer package." >&2; exit 1; }
    install -m 0644 -o root -g root "${LIB_DIR}/../systemd/${unit}" "${MARAN_PANEL_BACKUP_UNIT_DIR}/${unit}"
  done
}

step_panel_backup() {
  echo "Installing the scheduled panel backup units..."
  panel_backup_require_private_directory "$MARAN_PANEL_BACKUP_OUT_DIR"
  panel_backup_require_private_directory "$MARAN_PANEL_BACKUP_STAGING_DIR"
  install_panel_backup_units

  [ -f "$MARAN_PANEL_BACKUP_EXAMPLE_SOURCE" ] \
    || { echo "67-panel-backup.sh: ${MARAN_PANEL_BACKUP_EXAMPLE_SOURCE} is missing from the installer package." >&2; exit 1; }
  install -d -m 0755 -o root -g root /usr/local/maran/cli
  install -m 0644 -o root -g root "$MARAN_PANEL_BACKUP_EXAMPLE_SOURCE" "$MARAN_PANEL_BACKUP_EXAMPLE_DEST"

  systemctl daemon-reload
  echo "Panel backups go to ${MARAN_PANEL_BACKUP_OUT_DIR} (root only). The schedule is NOT enabled: copy ${MARAN_PANEL_BACKUP_EXAMPLE_DEST} to /etc/maran/panel-backup.conf (root:root 0600), then run: maran panel-backup-schedule enable"
}
