#!/usr/bin/env bash
# panel-backup-schedule.sh: `maran panel-backup-schedule enable|disable|status` — switches the
# scheduled panel backup on and off (issue #129).
#
# The installer puts maran-panel-backup.service and .timer in place and enables neither: an
# unattended root job must not exist until an operator has chosen a passphrase file and written
# /etc/maran/panel-backup.conf. `enable` is that decision. It validates the configuration with the
# same reader the job itself uses, so a configuration the job would refuse is refused here, and then
# writes the one thing the units cannot hold themselves: the OnCalendar value, as a drop-in.
#
# The drop-in is the only place a configured value is written into a systemd unit, so the value is
# the validated SCHEDULE and nothing else, rendered by root, never an echo of anything typed
# (rules/security.md item 4). docs/superpowers/notes/2026-11-04-panel-backup-schedule-threat-note.md
# section 5 carries the argument.

readonly MARAN_PBSCHED_USAGE="Usage: maran panel-backup-schedule enable|disable|status"
readonly MARAN_PBSCHED_TIMER="maran-panel-backup.timer"
readonly MARAN_PBSCHED_SERVICE="maran-panel-backup.service"
readonly MARAN_PBSCHED_UNIT_DIR="/etc/systemd/system"
readonly MARAN_PBSCHED_DROPIN_DIR="${MARAN_PBSCHED_UNIT_DIR}/${MARAN_PBSCHED_TIMER}.d"
readonly MARAN_PBSCHED_DROPIN="${MARAN_PBSCHED_DROPIN_DIR}/schedule.conf"

# cli_pbsched_cleanup: the EXIT-trap half of `enable`. A drop-in temp file that was never renamed
# into place is removed, so a refused run leaves no half-written file beside the real one.
cli_pbsched_cleanup() {
  [ -z "${CLI_PBSCHED_TEMP:-}" ] || rm -f -- "$CLI_PBSCHED_TEMP"
}

# cli_pbsched_require_units: refuses unless both units are installed, so `enable` never writes a
# drop-in for a timer that does not exist (an installer that predates the feature).
cli_pbsched_require_units() {
  local unit
  for unit in "$MARAN_PBSCHED_TIMER" "$MARAN_PBSCHED_SERVICE"; do
    [ -f "${MARAN_PBSCHED_UNIT_DIR}/${unit}" ] \
      || cli_fail "${unit} is not installed; update Maran with its installer first"
  done
}

# cli_pbsched_write_dropin: renders the drop-in from the validated schedule ($1) and moves it into
# place by rename, so systemd never reads half a file. The empty OnCalendar= line first clears any
# calendar the timer file itself names, so the configured value is the only one.
#   $1 = a schedule that cli_pbconfig_load has already accepted.
cli_pbsched_write_dropin() {
  local schedule="$1"
  install -d -m 0755 -o root -g root "$MARAN_PBSCHED_DROPIN_DIR"
  CLI_PBSCHED_TEMP="$(mktemp "${MARAN_PBSCHED_DROPIN_DIR}/.schedule.XXXXXX")"
  {
    echo "# Written by 'maran panel-backup-schedule enable' from SCHEDULE in /etc/maran/panel-backup.conf."
    echo "# Edit the configuration and run enable again; changes here are overwritten."
    echo "[Timer]"
    echo "OnCalendar="
    printf 'OnCalendar=%s\n' "$schedule"
  } > "$CLI_PBSCHED_TEMP" || cli_fail "could not write the timer drop-in"
  chown root:root -- "$CLI_PBSCHED_TEMP"
  chmod 0644 -- "$CLI_PBSCHED_TEMP"
  mv -f -- "$CLI_PBSCHED_TEMP" "$MARAN_PBSCHED_DROPIN"
  CLI_PBSCHED_TEMP=""
  # A file written by install/mv carries the staging context, not the one policy gives its path.
  if command -v restorecon >/dev/null 2>&1; then
    restorecon -- "$MARAN_PBSCHED_DROPIN" || true
  fi
}

# cli_pbsched_enable: validates the configuration, writes the drop-in and starts the timer.
# Nothing is written before every check has passed.
cli_pbsched_enable() {
  CLI_PBSCHED_TEMP=""
  trap 'exit 130' INT
  trap 'exit 143' TERM HUP
  trap cli_pbsched_cleanup EXIT

  cli_pbsched_require_units
  # Called directly, not inside $(...): its refusal must end this command, naming the first problem.
  cli_pbconfig_load
  # The regex checks the shape; systemd is asked whether the value is a calendar it understands.
  systemd-analyze calendar "$MARAN_PBCONF_SCHEDULE" >/dev/null 2>&1 \
    || cli_fail "SCHEDULE is not a calendar expression systemd accepts"

  cli_pbsched_write_dropin "$MARAN_PBCONF_SCHEDULE"
  systemctl daemon-reload || cli_fail "systemctl daemon-reload failed"
  systemctl enable --now "$MARAN_PBSCHED_TIMER" >/dev/null 2>&1 \
    || cli_fail "could not enable ${MARAN_PBSCHED_TIMER}; see: systemctl status ${MARAN_PBSCHED_TIMER}"
  cli_audit_log panel-backup-schedule enabled
  echo "Scheduled panel backup enabled: ${MARAN_PBCONF_SCHEDULE}"
  echo "Check the next run with: maran panel-backup-schedule status"
}

# cli_pbsched_disable: stops and disables the timer and removes the drop-in. Archives and the
# configuration file are never touched: the archives are the point of the feature.
cli_pbsched_disable() {
  systemctl disable --now "$MARAN_PBSCHED_TIMER" >/dev/null 2>&1 \
    || cli_fail "could not disable ${MARAN_PBSCHED_TIMER}; is it installed?"
  rm -f -- "$MARAN_PBSCHED_DROPIN"
  # Only an empty directory goes: anything else in it is not ours to remove.
  rmdir -- "$MARAN_PBSCHED_DROPIN_DIR" 2>/dev/null || true
  systemctl daemon-reload || cli_fail "systemctl daemon-reload failed"
  cli_audit_log panel-backup-schedule disabled
  echo "Scheduled panel backup disabled. Existing archives and the configuration are untouched."
}

# cli_pbsched_property: prints one systemd property of a unit as KEY=VALUE (empty value when unset).
#   $1 = unit, $2 = property name (a fixed word from this file).
cli_pbsched_property() {
  systemctl show --property="$2" -- "$1" 2>/dev/null || echo "${2}="
}

# cli_pbsched_status: prints whether the timer is enabled and active, when it fires next, and how
# the last run ended. It reads systemd only — never the configuration — and writes nothing, so it
# shows no path, endpoint or credential.
cli_pbsched_status() {
  local enabled active
  enabled="$(systemctl is-enabled -- "$MARAN_PBSCHED_TIMER" 2>/dev/null || true)"
  active="$(systemctl is-active -- "$MARAN_PBSCHED_TIMER" 2>/dev/null || true)"
  echo "Scheduled panel backup:"
  printf '  %-12s %s\n' "enabled" "${enabled:-not-installed}"
  printf '  %-12s %s\n' "active" "${active:-unknown}"
  echo "Timer:"
  cli_pbsched_property "$MARAN_PBSCHED_TIMER" NextElapseUSecRealtime | sed 's/^/  /'
  cli_pbsched_property "$MARAN_PBSCHED_TIMER" LastTriggerUSec | sed 's/^/  /'
  echo "Last run:"
  cli_pbsched_property "$MARAN_PBSCHED_SERVICE" Result | sed 's/^/  /'
  cli_pbsched_property "$MARAN_PBSCHED_SERVICE" ExecMainStatus | sed 's/^/  /'
  cli_pbsched_property "$MARAN_PBSCHED_SERVICE" ExecMainExitTimestamp | sed 's/^/  /'
  # Healthy means switched on and running its timer; a failed last run is shown above.
  [ "$enabled" = "enabled" ] && [ "$active" = "active" ]
}

# cli_panel_backup_schedule: the verb. Argument: enable, disable or status, and nothing else.
cli_panel_backup_schedule() {
  [ "$#" -eq 1 ] || cli_fail "$MARAN_PBSCHED_USAGE"
  case "$1" in
    enable) cli_pbsched_enable ;;
    disable) cli_pbsched_disable ;;
    status) cli_pbsched_status ;;
    *) cli_fail "unknown action. ${MARAN_PBSCHED_USAGE}" ;;
  esac
}
