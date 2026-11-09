#!/usr/bin/env bash
# Step 69: install the units of the unattended security-patch update (issue #383) and, the first time
# they are installed, switch the timer on. It runs right after 68 and before the services start,
# because the unit runs `maran update --security-only --scheduled`, the verb step 66 installed.
# Threat note, written first: docs/superpowers/notes/2026-11-07-security-patch-updates-threat-note.md
# (second review OUTSTANDING).
#
# THE DEFAULT IS ON, AND IT IS TOLD. A server nobody logs into keeps a known vulnerability running, so a
# first install enables the timer and says so on the terminal with the command that turns it off. What
# it enables is narrow: the unit applies a release only when the signed manifest marks it a security
# release of the installed major version (installer/cli/lib/update-unattended.sh); a feature release is
# reported and left alone.
#
# What this step does NOT do, and why each omission is deliberate:
#
# - It enables the timer ONLY when the timer's unit file did not exist before this run. A re-run of the
#   installer and `maran update` therefore never change an operator's choice: `maran update-schedule
#   disable` removes the enablement link, and a later run finds the unit file present and leaves the
#   link alone. The cost is stated: a server installed before this feature gets the timer enabled by
#   the first installer run that brings the unit, because for that server the unit is new.
# - It does not enable the timer on an offline install (--offline-tarball). A server carried a bundle
#   because it must not reach the internet; a daily job that cannot reach the release host would only
#   fail every day. `maran update-schedule enable` switches it on later.
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
  local first_install=0
  echo "Installing the automatic security-patch update units..."
  update_schedule_refuse_foreign_node "$MARAN_UPDATE_SCHEDULE_DUMP_DIR"
  # Literal path, equal to MARAN_UPDATE_SCHEDULE_DUMP_DIR: see update_schedule_refuse_foreign_node.
  install -d -m 0700 -o root -g root /var/backups/maran-update
  # Decided BEFORE the units are installed: the timer's unit file is the one durable fact that this host
  # has already met this feature, whether or not the operator then disabled it.
  [ -e "${MARAN_UPDATE_SCHEDULE_UNIT_DIR}/${MARAN_UPDATE_SCHEDULE_TIMER}" ] || first_install=1
  install_update_schedule_units
  systemctl daemon-reload

  if [ "$first_install" -eq 0 ]; then
    echo "Automatic security-patch updates: left as they were (see: maran update-schedule status)."
    return 0
  fi
  if [ -n "${MARAN_OFFLINE_TARBALL:-}" ]; then
    echo "Automatic security-patch updates: NOT enabled on an offline install. To enable: maran update-schedule enable"
    return 0
  fi
  systemctl enable --now "$MARAN_UPDATE_SCHEDULE_TIMER" >/dev/null 2>&1 \
    || { echo "69-update-schedule.sh: could not enable ${MARAN_UPDATE_SCHEDULE_TIMER}; see: systemctl status ${MARAN_UPDATE_SCHEDULE_TIMER}" >&2; exit 1; }
  echo "Automatic security-patch updates are ON: once a day (between 00:00 and 04:00 server time) the panel checks for"
  echo "a security release of this major version and installs it, with the same dump, health check and rollback as"
  echo "'maran update'. Feature releases are never installed this way. The check sends nothing but the request for the"
  echo "signed manifest. To turn it off: maran update-schedule disable"
}
