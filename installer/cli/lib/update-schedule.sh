#!/usr/bin/env bash
# update-schedule.sh: `maran update-schedule enable|disable|status` — switches the unattended
# security-patch update on and off (issue #383).
#
# The installer puts maran-update-security.service and .timer in place and enables the timer on a first
# install (installer/lib/69-update-schedule.sh). This verb is the operator's switch afterwards. It is
# the whole of the switch: `enable` and `disable` are one `systemctl` call on the one timer, and no file
# is written, so there is no configured value to validate and nothing a drop-in could smuggle into a
# unit (rules/security.md item 4). The timer carries its own OnCalendar= and RandomizedDelaySec=.
# `systemctl disable` removes the enablement link, which survives a reboot, an installer re-run (the
# installer enables the timer only the first time it installs the unit) and `maran update`.
# docs/superpowers/notes/2026-11-07-security-patch-updates-threat-note.md section 3 carries the argument.

readonly MARAN_USCHED_USAGE="Usage: maran update-schedule enable|disable|status"
readonly MARAN_USCHED_TIMER="$MARAN_CLI_UPDATE_TIMER_UNIT"
readonly MARAN_USCHED_SERVICE="maran-update-security.service"
readonly MARAN_USCHED_UNIT_DIR="/etc/systemd/system"

# cli_usched_require_units: refuses unless both units are installed, so `enable` never claims to have
# switched on a timer that does not exist (an installer that predates the feature).
cli_usched_require_units() {
  local unit
  for unit in "$MARAN_USCHED_TIMER" "$MARAN_USCHED_SERVICE"; do
    [ -f "${MARAN_USCHED_UNIT_DIR}/${unit}" ] \
      || cli_fail "${unit} is not installed; update Maran with its installer first"
  done
}

# cli_usched_enable: starts the timer and keeps it enabled across reboots.
cli_usched_enable() {
  cli_usched_require_units
  systemctl enable --now "$MARAN_USCHED_TIMER" >/dev/null 2>&1 \
    || cli_fail "could not enable ${MARAN_USCHED_TIMER}; see: systemctl status ${MARAN_USCHED_TIMER}"
  cli_audit_log update-schedule enabled
  echo "Automatic security-patch updates enabled."
  echo "Check the next run with: maran update-schedule status"
}

# cli_usched_disable: stops the timer and keeps it off across reboots, installer re-runs and updates.
# A run already in progress is not interrupted: the timer is stopped, the service is left to finish.
cli_usched_disable() {
  cli_usched_require_units
  systemctl disable --now "$MARAN_USCHED_TIMER" >/dev/null 2>&1 \
    || cli_fail "could not disable ${MARAN_USCHED_TIMER}; see: systemctl status ${MARAN_USCHED_TIMER}"
  cli_audit_log update-schedule disabled
  echo "Automatic security-patch updates disabled. Updates now happen only when you run: maran update"
}

# cli_usched_property: prints one systemd property of a unit as KEY=VALUE (empty value when unset).
#   $1 = unit, $2 = property name (a fixed word from this file).
cli_usched_property() {
  systemctl show --property="$2" -- "$1" 2>/dev/null || echo "${2}="
}

# cli_usched_status: prints whether the timer is enabled and active, when it fires next, how the last
# run ended and the installed release. It reads systemd and the release layout only and writes nothing.
# Exit 0 when the timer is switched on and running, 1 otherwise (including a deliberate disable), the
# same convention as `maran panel-backup-schedule status`.
cli_usched_status() {
  local enabled active
  enabled="$(systemctl is-enabled -- "$MARAN_USCHED_TIMER" 2>/dev/null || true)"
  active="$(systemctl is-active -- "$MARAN_USCHED_TIMER" 2>/dev/null || true)"
  echo "Automatic security-patch updates:"
  printf '  %-12s %s\n' "enabled" "${enabled:-not-installed}"
  printf '  %-12s %s\n' "active" "${active:-unknown}"
  printf '  %-12s %s\n' "installed" "$(cli_layout_current_version 2>/dev/null || true)"
  echo "Timer:"
  cli_usched_property "$MARAN_USCHED_TIMER" NextElapseUSecRealtime | sed 's/^/  /'
  cli_usched_property "$MARAN_USCHED_TIMER" LastTriggerUSec | sed 's/^/  /'
  echo "Last run:"
  cli_usched_property "$MARAN_USCHED_SERVICE" Result | sed 's/^/  /'
  cli_usched_property "$MARAN_USCHED_SERVICE" ExecMainStatus | sed 's/^/  /'
  cli_usched_property "$MARAN_USCHED_SERVICE" ExecMainExitTimestamp | sed 's/^/  /'
  echo "The journal line of each run: journalctl -t maran-cli | grep 'verb=update '"
  [ "$enabled" = "enabled" ] && [ "$active" = "active" ]
}

# cli_update_schedule: the verb. Argument: enable, disable or status, and nothing else.
cli_update_schedule() {
  [ "$#" -eq 1 ] || cli_fail "$MARAN_USCHED_USAGE"
  case "$1" in
    enable) cli_usched_enable ;;
    disable) cli_usched_disable ;;
    status) cli_usched_status ;;
    *) cli_fail "unknown action. ${MARAN_USCHED_USAGE}" ;;
  esac
}
