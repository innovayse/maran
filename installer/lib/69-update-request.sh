#!/usr/bin/env bash
# Step 69 (the panel's half): install the units through which the panel's "Update now" button starts
# `maran update --requested` (issue #382) and its "Check for updates" button starts `maran update --check`
# (issue #516), build the directory the panel puts its requests in, and turn both path units on. It runs with 69-update-schedule.sh, after the verb it runs (66) and before the
# services start (70): the API unit names the request directory in ReadWritePaths=, and a unit whose
# writable directory does not exist does not start (226/NAMESPACE).
# Threat notes, written first: docs/superpowers/notes/2026-11-07-panel-update-from-panel-threat-note.md
# (second review OUTSTANDING; the privileged path is the plan's recommended option for decision D8,
# which the owner has not yet confirmed in writing) and
# docs/superpowers/notes/2026-11-09-panel-update-check-threat-note.md for the check (second review
# OUTSTANDING). The check is the lesser of the two: it installs nothing.
#
# THE DEFAULT IS ON, AND IT IS TOLD. With the path unit enabled an administrator signed in to the panel
# can cause `maran update` to run as root, by pressing a button that sends no value: the root side
# installs only the newer signed release the recorded channel names, with the dump, the health gate and
# the rollback of `maran update`. Switching it off is `systemctl disable --now maran-update-request.path`.
#
# What this step does NOT do, and why each omission is deliberate:
#
# - It writes no marker, and nothing here ever runs an update. The directory is empty after the step.
# - It does not make the directory writable to anyone but the panel's group. The directory is root-owned
#   0770 inside root-owned /run, so the panel's account can create and remove entries in it and can
#   never replace the directory (rules/security.md item 2's reason, applied to a trigger).
# - It adds no configuration and no drop-in: nothing an operator or the panel typed reaches a unit.
#
# Every action is idempotent, because installers get re-run: `install` overwrites a file with the same
# bytes, `install -d` accepts an existing directory, `enable` and `daemon-reload` are repeatable.
set -euo pipefail

readonly MARAN_UPDATE_REQUEST_UNIT_DIR="/etc/systemd/system"
readonly MARAN_UPDATE_REQUEST_PATH_UNIT="maran-update-request.path"
readonly MARAN_UPDATE_REQUEST_SERVICE="maran-update-request.service"
# The release check's pair (issue #516): the same directory, its own marker, and a unit that runs
# `maran update --check` and nothing else.
readonly MARAN_UPDATE_CHECK_PATH_UNIT="maran-update-check.path"
readonly MARAN_UPDATE_CHECK_SERVICE="maran-update-check.service"
readonly MARAN_UPDATE_REQUEST_TMPFILES_DIR="/etc/tmpfiles.d"
readonly MARAN_UPDATE_REQUEST_TMPFILES_NAME="maran-update-request.conf"
# The directory the panel's request goes in. The API unit's ReadWritePaths= and the path unit's
# PathExists= name it; `maran structure` check 24 pairs the API unit's line with the literal
# `install -d` below, so a rename here without one there fails the check and not a boot.
readonly MARAN_UPDATE_REQUEST_DIR="/run/maran-update-request"

# update_request_refuse_foreign_node: refuses when the request directory is a symbolic link or a
# non-directory. `install -d` follows a link, so one planted at the name would put the directory (and
# the group's write access to it) somewhere else. Refused BEFORE anything is created or changed.
update_request_refuse_foreign_node() {
  local path="$1"
  if [ -L "$path" ] || { [ -e "$path" ] && [ ! -d "$path" ]; }; then
    echo "69-update-request.sh: ${path} is a symbolic link or not a directory. Refusing to give the panel's" >&2
    echo "  group write access through it. Move it aside and re-run the installer." >&2
    exit 1
  fi
}

# render_update_request_tmpfiles OUT: substitutes the panel's group into the tmpfiles snippet and
# refuses a placeholder that survives, the way step 70 does for the units it renders.
render_update_request_tmpfiles() {
  local out="$1"
  sed -e "s#__MARAN_GROUP__#${MARAN_GROUP}#g" "${LIB_DIR}/../systemd/maran-update-request.tmpfiles.conf" > "$out"
  if grep -q '__MARAN_' "$out"; then
    echo "69-update-request.sh: maran-update-request.tmpfiles.conf still contains an unsubstituted placeholder; aborting." >&2
    exit 1
  fi
}

# install_update_request_units: copies both path units and both services to /etc/systemd/system, root:root
# 0644, as they stand. A source that is missing stops the install while the operator is watching, not at
# the first press of the button.
install_update_request_units() {
  local unit
  for unit in "$MARAN_UPDATE_REQUEST_SERVICE" "$MARAN_UPDATE_REQUEST_PATH_UNIT" "$MARAN_UPDATE_CHECK_SERVICE" "$MARAN_UPDATE_CHECK_PATH_UNIT"; do
    [ -f "${LIB_DIR}/../systemd/${unit}" ] \
      || { echo "69-update-request.sh: ${LIB_DIR}/../systemd/${unit} is missing from the installer package." >&2; exit 1; }
    install -m 0644 -o root -g root "${LIB_DIR}/../systemd/${unit}" "${MARAN_UPDATE_REQUEST_UNIT_DIR}/${unit}"
  done
}

step_update_request() {
  local tmp
  echo "Installing the units that start a panel update requested from the panel..."
  update_request_refuse_foreign_node "$MARAN_UPDATE_REQUEST_DIR"
  tmp="$(mktemp)"
  render_update_request_tmpfiles "$tmp"
  install -d -m 0755 "$MARAN_UPDATE_REQUEST_TMPFILES_DIR"
  install -m 0644 "$tmp" "${MARAN_UPDATE_REQUEST_TMPFILES_DIR}/${MARAN_UPDATE_REQUEST_TMPFILES_NAME}"
  rm -f "$tmp"
  # Literal path, equal to MARAN_UPDATE_REQUEST_DIR: see the constant. The tmpfiles snippet rebuilds it
  # after every reboot; this line is what makes it exist NOW, before the API unit that names it starts.
  install -d -m 0770 -o root -g "$MARAN_GROUP" /run/maran-update-request
  systemd-tmpfiles --create "${MARAN_UPDATE_REQUEST_TMPFILES_DIR}/${MARAN_UPDATE_REQUEST_TMPFILES_NAME}" \
    || { echo "69-update-request.sh: systemd-tmpfiles refused ${MARAN_UPDATE_REQUEST_TMPFILES_NAME}; see: systemd-tmpfiles --create ${MARAN_UPDATE_REQUEST_TMPFILES_DIR}/${MARAN_UPDATE_REQUEST_TMPFILES_NAME}" >&2; exit 1; }
  install_update_request_units
  systemctl daemon-reload
  systemctl enable --now "$MARAN_UPDATE_REQUEST_PATH_UNIT" >/dev/null 2>&1 \
    || { echo "69-update-request.sh: could not enable ${MARAN_UPDATE_REQUEST_PATH_UNIT}; see: systemctl status ${MARAN_UPDATE_REQUEST_PATH_UNIT}" >&2; exit 1; }
  systemctl enable --now "$MARAN_UPDATE_CHECK_PATH_UNIT" >/dev/null 2>&1 \
    || { echo "69-update-request.sh: could not enable ${MARAN_UPDATE_CHECK_PATH_UNIT}; see: systemctl status ${MARAN_UPDATE_CHECK_PATH_UNIT}" >&2; exit 1; }
  echo "Updating the panel from the panel is ON: an administrator's \"Update now\" runs 'maran update' as root with the"
  echo "same dump, health check and rollback, and installs only the newer signed release. To turn it off:"
  echo "  systemctl disable --now ${MARAN_UPDATE_REQUEST_PATH_UNIT}"
  echo "The same goes for \"Check for updates\", which runs 'maran update --check' as root and installs nothing:"
  echo "  systemctl disable --now ${MARAN_UPDATE_CHECK_PATH_UNIT}"
}
