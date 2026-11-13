#!/usr/bin/env bash
# Step 69: install the units of the unattended security-patch update (issue #383) and leave the timer
# OFF (issue #550). It runs right after 68 and before the services start, because the unit runs
# `maran update --security-only --scheduled`, the verb step 66 installed.
# Threat note, written first: docs/superpowers/notes/2026-11-07-security-patch-updates-threat-note.md
# (second review OUTSTANDING).
#
# THE DEFAULT IS OFF, AND IT IS TOLD. An unattended root job that changes software on a customer's
# server is the operator's decision, not the installer's, so no install (online, offline, first or
# repeated) enables the timer. The operator opts in with `maran update-schedule enable`, and the
# terminal says so. What the timer does once enabled is narrow: the unit applies a release only when the
# signed manifest marks it a security release of the installed major version
# (installer/cli/lib/update-unattended.sh); a feature release is reported and left alone.
#
# What this step does NOT do, and why each omission is deliberate:
#
# - It never runs `systemctl enable` or `start` on the timer, so a re-run of the installer and
#   `maran update` never change an operator's choice either way: `maran update-schedule enable` and
#   `disable` own the enablement link, and this step leaves it alone.
# - It writes no configuration and no drop-in. The timer carries its own interval and nothing an
#   operator typed reaches a unit.
#
# Every action is idempotent, because installers get re-run: `install` overwrites a unit with the same
# bytes, `install -d` accepts an existing directory, and `daemon-reload` is repeatable.
set -euo pipefail

readonly MARAN_UPDATE_SCHEDULE_UNIT_DIR="/etc/systemd/system"
readonly MARAN_UPDATE_SCHEDULE_TIMER="maran-update-security.timer"
readonly MARAN_UPDATE_SCHEDULE_SERVICE="maran-update-security.service"
# Where the pre-update database dumps go (installer/cli/lib/update-dumps.sh). The unit's
# ReadWritePaths= names this exact path and the hardened unit cannot create it, so it exists before
# the first scheduled run. `maran structure` check 24 pairs that line with the literal `install -d`
# below.
readonly MARAN_UPDATE_SCHEDULE_DUMP_DIR="/var/backups/maran-update"

# update_schedule_refuse_foreign_node: refuses when $1 is a symbolic link or a non-directory.
#
# `install -d` accepts an existing path and re-modes a directory, but it follows a symbolic link, so a
# link planted at the name would send root's database dumps elsewhere. The refusal is made BEFORE
# anything is created or changed. The creation itself stays a literal `install -d <path>` in
# step_update_schedule, because `maran structure` check 24 cannot read a path held in a variable.
update_schedule_refuse_foreign_node() {
  local path="$1"
  if [ -L "$path" ] || { [ -e "$path" ] && [ ! -d "$path" ]; }; then
    echo "69-update-schedule.sh: ${path} is a symbolic link or not a directory. Refusing to write" >&2
    echo "  root's database dumps through it. Move it aside and re-run the installer." >&2
    exit 1
  fi
}

# install_update_schedule_units: copies the service and the timer to /etc/systemd/system, root:root
# 0644. Neither file has a placeholder, so they are installed as they stand from the installer
# package. A source that is missing stops the install while the operator is watching, not at the first
# night the timer should have fired.
install_update_schedule_units() {
  local unit
  for unit in "$MARAN_UPDATE_SCHEDULE_SERVICE" "$MARAN_UPDATE_SCHEDULE_TIMER"; do
    [ -f "${LIB_DIR}/../systemd/${unit}" ] \
      || { echo "69-update-schedule.sh: ${LIB_DIR}/../systemd/${unit} is missing from the installer package." >&2; exit 1; }
    install -m 0644 -o root -g root "${LIB_DIR}/../systemd/${unit}" "${MARAN_UPDATE_SCHEDULE_UNIT_DIR}/${unit}"
  done
}

step_update_schedule() {
  echo "Installing the automatic security-patch update units..."
  update_schedule_refuse_foreign_node "$MARAN_UPDATE_SCHEDULE_DUMP_DIR"
  # Literal path, equal to MARAN_UPDATE_SCHEDULE_DUMP_DIR: see update_schedule_refuse_foreign_node.
  install -d -m 0700 -o root -g root /var/backups/maran-update
  install_update_schedule_units
  systemctl daemon-reload

  echo "Automatic security-patch updates are installed but off. To enable them: sudo maran update-schedule enable"
}
