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
# Two drop-ins are the only places a configured value is written into a systemd unit: the timer's
# OnCalendar= (the validated SCHEDULE) and, when OUT_DIR is not the default, the service's
# ReadWritePaths= (the validated OUT_DIR, because the hardened unit cannot write any other directory).
# Each is rendered by root from a value that already passed the allow-list reader, never an echo of
# anything typed (rules/security.md item 4).
# docs/superpowers/notes/2026-11-04-panel-backup-schedule-threat-note.md section 5 carries the
# argument.

readonly MARAN_PBSCHED_USAGE="Usage: maran panel-backup-schedule enable|disable|status"
readonly MARAN_PBSCHED_TIMER="$MARAN_CLI_BACKUP_TIMER_UNIT"
readonly MARAN_PBSCHED_SERVICE="maran-panel-backup.service"
readonly MARAN_PBSCHED_UNIT_DIR="/etc/systemd/system"
readonly MARAN_PBSCHED_DROPIN_DIR="${MARAN_PBSCHED_UNIT_DIR}/${MARAN_PBSCHED_TIMER}.d"
readonly MARAN_PBSCHED_DROPIN="${MARAN_PBSCHED_DROPIN_DIR}/schedule.conf"
# The service drop-in that makes a non-default OUT_DIR writable under ProtectSystem=strict.
readonly MARAN_PBSCHED_SERVICE_DROPIN_DIR="${MARAN_PBSCHED_UNIT_DIR}/${MARAN_PBSCHED_SERVICE}.d"
readonly MARAN_PBSCHED_OUTDIR_DROPIN="${MARAN_PBSCHED_SERVICE_DROPIN_DIR}/outdir.conf"
# The service runs with ProtectHome=yes, so /root, /home and everything under them are empty to it.
readonly MARAN_PBSCHED_HIDDEN_TREES="/root /home"

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

# cli_pbsched_install_unit_file: writes the systemd unit file $2 (inside the directory $1) with the
# body $3, root:root 0644, and moves it into place by rename, so systemd never reads half a file.
# The body is only ever text this file composed around a value the configuration reader accepted.
#   $1 = directory (created root:root 0755 when absent), $2 = destination path, $3 = file body.
cli_pbsched_install_unit_file() {
  local dir="$1" dest="$2" body="$3"
  install -d -m 0755 -o root -g root "$dir"
  CLI_PBSCHED_TEMP="$(mktemp "${dir}/.pending.XXXXXX")"
  printf '%s\n' "$body" > "$CLI_PBSCHED_TEMP" || cli_fail "could not write ${dest}"
  chown root:root -- "$CLI_PBSCHED_TEMP"
  chmod 0644 -- "$CLI_PBSCHED_TEMP"
  mv -f -- "$CLI_PBSCHED_TEMP" "$dest"
  CLI_PBSCHED_TEMP=""
  # A file written by install/mv carries the staging context, not the one policy gives its path.
  if command -v restorecon >/dev/null 2>&1; then
    restorecon -- "$dest" || true
  fi
}

# cli_pbsched_write_dropin: renders the timer drop-in from the validated schedule ($1). The empty
# OnCalendar= line first clears any calendar the timer file itself names, so the configured value is
# the only one.
#   $1 = a schedule that cli_pbconfig_load has already accepted.
cli_pbsched_write_dropin() {
  local schedule="$1" body
  body="# Written by 'maran panel-backup-schedule enable' from SCHEDULE in /etc/maran/panel-backup.conf.
# Edit the configuration and run enable again; changes here are overwritten.
[Timer]
OnCalendar=
OnCalendar=${schedule}"
  cli_pbsched_install_unit_file "$MARAN_PBSCHED_DROPIN_DIR" "$MARAN_PBSCHED_DROPIN" "$body"
}

# cli_pbsched_require_visible_to_unit: refuses when a file the job must read lives where the unit
# cannot see it. The service has ProtectHome=yes, so a passphrase or credential file under /root or
# /home exists for `maran panel-backup` typed in a shell and not for the unit, and the first
# unattended run would fail in the night. Both the path as written and the path with every symlink
# resolved are tested, because a link in a parent directory would otherwise hide the real place.
# Names the first problem, by key and path.
#   $1 = configuration key (a fixed word), $2 = the path from the validated configuration.
cli_pbsched_require_visible_to_unit() {
  local key="$1" path="$2" resolved tree candidate
  resolved="$(realpath -e -- "$path" 2>/dev/null)" || cli_fail "${key} (${path}) cannot be resolved"
  for candidate in "$path" "$resolved"; do
    for tree in $MARAN_PBSCHED_HIDDEN_TREES; do
      case "$candidate" in
        "$tree"|"$tree"/*)
          cli_fail "${key} (${path}) is under ${tree}, which the scheduled job cannot read (ProtectHome=yes); move the file to /etc/maran-backup"
          ;;
      esac
    done
  done
}

# cli_pbsched_require_readable_paths: applies the check above to the passphrase file and, when
# uploads are configured, to both credential files.
cli_pbsched_require_readable_paths() {
  cli_pbsched_require_visible_to_unit PASSPHRASE_FILE "$MARAN_PBCONF_PASSPHRASE_FILE"
  if [ -n "$MARAN_PBCONF_S3_ENDPOINT" ]; then
    cli_pbsched_require_visible_to_unit S3_ACCESS_KEY_ID_FILE "$MARAN_PBCONF_S3_ACCESS_KEY_ID_FILE"
    cli_pbsched_require_visible_to_unit S3_SECRET_FILE "$MARAN_PBCONF_S3_SECRET_FILE"
  fi
}

# cli_pbsched_apply_out_dir: makes the unit able to write OUT_DIR. The unit's own ReadWritePaths=
# names only the default directory; any other must be added by a drop-in or the first run dies with
# a read-only filesystem. A missing directory is refused here because systemd would fail to build the
# unit's mount namespace over it (226/NAMESPACE) rather than create it. The default needs no drop-in,
# and a drop-in left by an earlier configuration is removed, so the writable set always equals the
# configured one.
#   $1 = the validated OUT_DIR.
cli_pbsched_apply_out_dir() {
  local out="${1%/}" body
  if [ "$out" = "$MARAN_PBCONF_DEFAULT_OUT_DIR" ]; then
    cli_pbsched_remove_outdir_dropin
    return 0
  fi
  [ -d "$out" ] && [ ! -L "$out" ] \
    || cli_fail "OUT_DIR (${out}) is not a plain directory; create it (root:root 0700) before enabling"
  body="# Written by 'maran panel-backup-schedule enable' from OUT_DIR in /etc/maran/panel-backup.conf.
# The unit is read-only outside its ReadWritePaths=; this adds the one directory the archives go to.
[Service]
ReadWritePaths=${out}"
  cli_pbsched_install_unit_file "$MARAN_PBSCHED_SERVICE_DROPIN_DIR" "$MARAN_PBSCHED_OUTDIR_DROPIN" "$body"
}

# cli_pbsched_remove_outdir_dropin: removes the service drop-in and its directory when nothing else
# is in it. Only the file this verb wrote is removed; anything else in that directory is not ours.
cli_pbsched_remove_outdir_dropin() {
  rm -f -- "$MARAN_PBSCHED_OUTDIR_DROPIN"
  rmdir -- "$MARAN_PBSCHED_SERVICE_DROPIN_DIR" 2>/dev/null || true
}

# cli_pbsched_enable: validates the configuration, writes the drop-ins and starts the timer.
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
  cli_pbsched_require_readable_paths

  cli_pbsched_apply_out_dir "$MARAN_PBCONF_OUT_DIR"
  cli_pbsched_write_dropin "$MARAN_PBCONF_SCHEDULE"
  systemctl daemon-reload || cli_fail "systemctl daemon-reload failed"
  systemctl enable --now "$MARAN_PBSCHED_TIMER" >/dev/null 2>&1 \
    || cli_fail "could not enable ${MARAN_PBSCHED_TIMER}; see: systemctl status ${MARAN_PBSCHED_TIMER}"
  cli_audit_log panel-backup-schedule enabled
  echo "Scheduled panel backup enabled."
  echo "Check the next run with: maran panel-backup-schedule status"
}

# cli_pbsched_disable: stops and disables the timer and removes both drop-ins. Archives and the
# configuration file are never touched: the archives are the point of the feature.
cli_pbsched_disable() {
  systemctl disable --now "$MARAN_PBSCHED_TIMER" >/dev/null 2>&1 \
    || cli_fail "could not disable ${MARAN_PBSCHED_TIMER}; is it installed?"
  rm -f -- "$MARAN_PBSCHED_DROPIN"
  # Only an empty directory goes: anything else in it is not ours to remove.
  rmdir -- "$MARAN_PBSCHED_DROPIN_DIR" 2>/dev/null || true
  cli_pbsched_remove_outdir_dropin
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
