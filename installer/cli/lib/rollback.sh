#!/usr/bin/env bash
# rollback.sh: `maran rollback [--restore-database] [--yes]`, the one verb that moves the panel
# BACK to the release it ran before the last `maran update` (issue #128).
#
# What it does: points `current` at the PREVIOUS release already on disk (never a download),
# restarts the agent then the api, waits for the health gate, and records the result in
# release.state. It never runs migrations: expand-then-contract (rules/architecture.md) is what lets
# the previous release run against the newer schema.
#
# With --restore-database it ALSO replaces the database with the newest dump taken for the update
# being undone. That discards every row written, and every queued message accepted, since the dump,
# so it sits behind a loud confirmation (--yes, or a typed word on a terminal) and is the only path
# in the product that restores a dump: the automatic rollback of `maran update` never does.
#
# Three situations reach this verb, told apart by release.state and `current`:
#   switch   PREVIOUS names another release than the running one: the ordinary rollback.
#   finish   PREVIOUS is already running but VERSION still names the release left: an earlier
#            rollback was killed between the switch and the state write. The state write is
#            completed, then the run continues (restart, gate, optional restore).
#   restore  PREVIOUS, VERSION and `current` agree: `maran update` already rolled back by itself.
#            Nothing is switched; only with --restore-database, to put back the dump taken for the
#            update that failed.
#
# Order of a --restore-database run in switch mode: stop the services, switch `current`, restore
# the database, write release.state, restart, gate. The switch comes BEFORE the restore on purpose:
# a kill or a switch failure after the restore would let the EXIT trap start the services with the
# NEW release against the OLD schema. Done this way, every interruption after the switch leaves
# `current` on the previous release over a database that is either untouched (pg_restore is one
# transaction) or already restored, and release.state still naming the release left: the `finish`
# situation above, which `maran rollback --restore-database` recovers by running again.
#
# docs/superpowers/notes/2026-11-04-panel-self-update-threat-note.md section 5 and 6 carry the
# argument. Library functions used here are defined in release-version.sh, release-layout.sh,
# panel-dump.sh, update-dumps.sh and update-health.sh (function names are the contract).

readonly MARAN_ROLLBACK_USAGE="Usage: maran rollback [--restore-database] [--yes]"
# The word an operator types to confirm discarding writes; --yes replaces it for scripts.
readonly MARAN_ROLLBACK_CONFIRM_WORD="restore-database"
# How long the health gate waits for the previous release to answer.
readonly MARAN_ROLLBACK_GATE_SECONDS=120

# cli_rollback_usage: ends the command with the usage exit code (64), for a malformed command line.
cli_rollback_usage() {
  echo "maran: $1. ${MARAN_ROLLBACK_USAGE}" >&2
  exit 64
}

# cli_rollback_refuse: records the refusal in the audit log and ends the command with exit 1.
#   $1 = the reason, shown to the operator.
cli_rollback_refuse() {
  cli_audit_log rollback refused
  cli_fail "$1"
}

# cli_rollback_cleanup: the EXIT-trap half of this verb. A run that ended while an outcome was
# still pending is audited with it (failed, or restore-failed), and a restore that failed after the
# switch tells the operator the exact command that retries it. A service this run stopped and never
# started again is started, so a refused rollback leaves the server serving something. The service
# order is the agent first, the api second, the same as a rollback restart.
cli_rollback_cleanup() {
  local status=$?
  if [ "$status" -ne 0 ] && [ -n "${CLI_ROLLBACK_PENDING:-}" ]; then
    cli_audit_log rollback "$CLI_ROLLBACK_PENDING"
    if [ "$CLI_ROLLBACK_PENDING" = "restore-failed" ]; then
      echo "maran: ${CLI_ROLLBACK_PREVIOUS} is already the current release and the database is unchanged." >&2
      echo "maran: to retry the restore, run: maran rollback --restore-database" >&2
    fi
  fi
  if [ "${CLI_ROLLBACK_STOPPED:-0}" -eq 1 ]; then
    echo "maran: the rollback did not finish; starting the panel services again" >&2
    systemctl start "$MARAN_CLI_AGENT_UNIT" || echo "maran: ${MARAN_CLI_AGENT_UNIT} did not start" >&2
    systemctl start "$MARAN_CLI_API_UNIT" || echo "maran: ${MARAN_CLI_API_UNIT} did not start" >&2
  fi
}

# cli_rollback_require_release: refuses unless release directory $1 holds every component as a real
# directory. A rollback onto half a release would be a second outage.
cli_rollback_require_release() {
  local version="$1" part dir="${MARAN_LAYOUT_ROOT}/releases/$1"
  [ -d "$dir" ] && [ ! -L "$dir" ] \
    || cli_rollback_refuse "the previous release ${version} is not on this server (${dir} is missing)"
  for part in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    [ -d "${dir}/${part}" ] && [ ! -L "${dir}/${part}" ] \
      || cli_rollback_refuse "the previous release ${version} is incomplete (${dir}/${part} is missing)"
  done
}

# cli_rollback_abandoned_release: prints the highest release on disk that is newer than the running
# one $1 and is neither current nor named by release.state. After an automatic rollback that is
# the release the failed update installed, which stays on disk, and the update this verb undoes.
# Prints nothing when there is none.
cli_rollback_abandoned_release() {
  local running="$1" dir name best=""
  for dir in "${MARAN_LAYOUT_ROOT}/releases/"*; do
    [ -d "$dir" ] && [ ! -L "$dir" ] || continue
    name="${dir##*/}"
    cli_version_is_release_name "$name" || continue
    cli_version_gt "$name" "$running" || continue
    if [ -z "$best" ] || cli_version_gt "$name" "$best"; then
      best="$name"
    fi
  done
  printf '%s' "$best"
}

# cli_rollback_find_dump: prints the path of the newest pre-update dump taken for the update to
# release $1, refusing when there is none. The name contract and the ordering live in
# update-dumps.sh. The directory must be private (root-only, not a link) and the dump itself a
# regular, non-symlink file owned by root with mode 0600: a dump anyone else could have written is
# never fed to pg_restore.
#   $1 = the release being left (the "to" of the update).
cli_rollback_find_dump() {
  local left="$1" dump
  cli_dumps_require_private
  dump="$(cli_dumps_newest_to "$left")" || cli_rollback_refuse "no dump taken for the update to ${left} can be used"
  [ -n "$dump" ] || cli_rollback_refuse "no dump taken for the update to ${left} is in ${MARAN_LAYOUT_BACKUP_DIR}"
  [ -f "$dump" ] && [ ! -L "$dump" ] || cli_rollback_refuse "the dump ${dump} is not a regular file"
  [ "$(stat -c '%u:%a' -- "$dump")" = "0:600" ] \
    || cli_rollback_refuse "the dump ${dump} must be owned by root with mode 0600"
  printf '%s' "$dump"
}

# cli_rollback_confirm: shows what restoring the database destroys and asks for consent. --yes ($1
# is 1) is the consent; otherwise a terminal must be attached and the operator must type the word.
# A non-interactive run without --yes refuses, so no pipeline discards data by accident.
#   $1 = 1 when --yes was given, $2 = the dump, $3 = the release left, $4 = what happens to the
#   releases, as a sentence.
cli_rollback_confirm() {
  local yes="$1" dump="$2" left="$3" outcome="$4" typed=""
  cat >&2 <<EOF

WARNING: --restore-database REPLACES the panel database with ${dump}.

Every row written and every queued message accepted since that dump was taken is DROPPED and
cannot be recovered: customers, sites, tasks, audit entries, settings changed after the update to
${left}. The panel is stopped while this runs. ${outcome}

Without --restore-database the rollback keeps the database as it is, which the previous release
is built to run against.

EOF
  [ "$yes" -eq 1 ] && return 0
  [ -t 0 ] || cli_rollback_refuse "--restore-database needs --yes when no terminal is attached"
  printf 'Type "%s" to continue: ' "$MARAN_ROLLBACK_CONFIRM_WORD" >&2
  read -r typed || typed=""
  [ "$typed" = "$MARAN_ROLLBACK_CONFIRM_WORD" ] || cli_rollback_refuse "not confirmed; nothing was changed"
}

# cli_rollback_plan: decides which of the three situations this is and sets the globals the verb
# reads: CLI_ROLLBACK_MODE (switch, finish or restore), CLI_ROLLBACK_PREVIOUS, CLI_ROLLBACK_LEFT.
# Every refusal here changes nothing on disk. Each read is tested with `||`, because errexit does
# not reach inside $( ): a state that cannot be read must not be reported as "no previous release".
#   $1 = 1 when --restore-database was given.
cli_rollback_plan() {
  local restore="$1" previous running recorded
  previous="$(cli_layout_previous_version)" || cli_rollback_refuse "the previous release could not be read"
  [ -n "$previous" ] || cli_rollback_refuse "there is no previous release to roll back to"
  cli_version_is_release_name "$previous" \
    || cli_rollback_refuse "the recorded previous release is not a release name"
  running="$(cli_layout_current_version)" || cli_rollback_refuse "the running release could not be read"
  [ -n "$running" ] || cli_rollback_refuse "no release is running from ${MARAN_LAYOUT_ROOT}/current"
  cli_version_is_release_name "$running" || cli_rollback_refuse "the running release is not a release name"
  recorded="$(cli_layout_state_read VERSION)" || cli_rollback_refuse "release.state could not be read"
  CLI_ROLLBACK_PREVIOUS="$previous"
  if [ "$previous" != "$running" ]; then
    CLI_ROLLBACK_MODE="switch"
    CLI_ROLLBACK_LEFT="$running"
  elif [ -n "$recorded" ] && [ "$recorded" != "$running" ]; then
    cli_version_is_release_name "$recorded" \
      || cli_rollback_refuse "release.state names a version that is not a release name"
    CLI_ROLLBACK_MODE="finish"
    CLI_ROLLBACK_LEFT="$recorded"
  else
    [ "$restore" -eq 1 ] || cli_rollback_refuse "the previous release is the one already running (${running}); \
the update rolled back by itself. To restore the dump taken for it, run: maran rollback --restore-database"
    CLI_ROLLBACK_MODE="restore"
    CLI_ROLLBACK_LEFT="$(cli_rollback_abandoned_release "$running")"
    [ -n "$CLI_ROLLBACK_LEFT" ] || cli_rollback_refuse "no release newer than ${running} is on this server, \
so there is no failed update whose dump could be restored"
  fi
  cli_rollback_require_release "$previous"
}

# cli_rollback_restore_database: the --restore-database half of a run. Stops everything that writes
# to the database (api first, because it is the one with the queue, then the agent), switches
# `current` when this is a switch, and only then restores the dump. From the first stop the EXIT
# trap starts the services again if this run ends early. The pending outcome is restore-failed
# while pg_restore runs and the trap names the retry command. A failed restore changes nothing in
# the database (one transaction), and `current` already matches that database's schema.
#   $1 = the dump to restore.
cli_rollback_restore_database() {
  CLI_ROLLBACK_STOPPED=1
  systemctl stop "$MARAN_CLI_API_UNIT" || cli_fail "could not stop ${MARAN_CLI_API_UNIT}"
  systemctl stop "$MARAN_CLI_AGENT_UNIT" || cli_fail "could not stop ${MARAN_CLI_AGENT_UNIT}"
  # An `if`, not `||`: a function run as an `||` operand has errexit disabled for its whole body.
  if [ "$CLI_ROLLBACK_MODE" = "switch" ]; then
    cli_layout_switch "$CLI_ROLLBACK_PREVIOUS"
  fi
  CLI_ROLLBACK_PENDING="restore-failed"
  cli_restore_database "$1"
  CLI_ROLLBACK_PENDING="failed"
}

# cli_rollback_report: the closing words of a finished run. Without --restore-database it also says
# where the newest pre-update dump for the release left lives, and that restoring it needs the other
# form of the command: PREVIOUS is written empty, so `maran rollback --restore-database` is refused
# once this run is over.
#   $1 = 1 when --restore-database was given, $2 = the dump restored (empty without it).
cli_rollback_report() {
  local restore="$1" dump="$2" kept=""
  if [ "$CLI_ROLLBACK_MODE" = "restore" ]; then
    echo "Release ${CLI_ROLLBACK_PREVIOUS} keeps running."
  else
    echo "Rolled back from ${CLI_ROLLBACK_LEFT} to ${CLI_ROLLBACK_PREVIOUS}."
  fi
  if [ "$restore" -eq 1 ]; then
    echo "The database was restored from ${dump}; everything written since that dump is gone."
  else
    echo "The database was left as it is, which this release is built to run against."
    kept="$(cli_dumps_newest_to "$CLI_ROLLBACK_LEFT")" || kept=""
    if [ -n "$kept" ]; then
      echo "The dump taken before the update to ${CLI_ROLLBACK_LEFT} is ${kept}."
      echo "To also restore the database, run: maran rollback --restore-database INSTEAD of this command next time."
      echo "To restore that dump by hand now, use pg_restore with that file."
    fi
  fi
  echo "No release is recorded to roll back to now."
  echo "To move forward again, run: maran update. It downloads and verifies the release again and replaces" \
    "the copy of ${CLI_ROLLBACK_LEFT} still on this server."
}

# cli_rollback: the verb. Arguments: [--restore-database] [--yes], each at most once, no positional
# argument.
cli_rollback() {
  local restore=0 yes=0 arg
  for arg in "$@"; do
    case "$arg" in
      --restore-database)
        [ "$restore" -eq 0 ] || cli_rollback_usage "--restore-database was given twice"
        restore=1
        ;;
      --yes)
        [ "$yes" -eq 0 ] || cli_rollback_usage "--yes was given twice"
        yes=1
        ;;
      -*) cli_rollback_usage "unknown option" ;;
      *) cli_rollback_usage "unexpected argument" ;;
    esac
  done

  # Globals, not locals: the EXIT trap runs after this function has returned. The shared lock is
  # the one panel backup, restore, update and rollback hold, so none of them runs beside a rollback.
  CLI_ROLLBACK_STOPPED=0
  CLI_ROLLBACK_PENDING=""
  CLI_ROLLBACK_MODE=""
  CLI_ROLLBACK_PREVIOUS=""
  CLI_ROLLBACK_LEFT=""
  cli_panel_open_stage cli_rollback_cleanup

  local channel dump="" outcome
  cli_rollback_plan "$restore"
  channel="$(cli_layout_state_read CHANNEL)" || cli_rollback_refuse "release.state could not be read"
  case "$channel" in
    stable | beta) ;;
    "") channel="stable" ;;
    *) cli_rollback_refuse "release.state names an unknown channel" ;;
  esac

  if [ "$restore" -eq 1 ]; then
    dump="$(cli_rollback_find_dump "$CLI_ROLLBACK_LEFT")"
    outcome="It returns to release ${CLI_ROLLBACK_PREVIOUS}."
    [ "$CLI_ROLLBACK_MODE" = "restore" ] && outcome="Release ${CLI_ROLLBACK_PREVIOUS} keeps running."
    cli_rollback_confirm "$yes" "$dump" "$CLI_ROLLBACK_LEFT" "$outcome"
  fi

  # Nothing has been changed until here. Any exit from now on is audited as failed unless a step
  # below names a more exact outcome or the run finishes.
  CLI_ROLLBACK_PENDING="failed"
  if [ "$restore" -eq 1 ]; then
    cli_rollback_restore_database "$dump"
  elif [ "$CLI_ROLLBACK_MODE" = "switch" ]; then
    cli_layout_switch "$CLI_ROLLBACK_PREVIOUS"
  fi
  # PREVIOUS is written EMPTY on purpose. Naming the release just left would let a second rollback
  # return to it, and two rollbacks in a row would swap the releases forever. The release left
  # stays on disk but is neither current nor PREVIOUS, so `maran update` may install over it: going
  # forward again is an update, which verifies a release before it runs. The state is written
  # before the restarts so release.state and `current` agree even if the gate below fails.
  cli_layout_state_write "$channel" "$CLI_ROLLBACK_PREVIOUS" ""
  CLI_ROLLBACK_PENDING=""
  if ! cli_update_restart_for_rollback; then
    CLI_ROLLBACK_STOPPED=0
    cli_audit_log rollback unhealthy
    cli_fail "rolled back to ${CLI_ROLLBACK_PREVIOUS} but a panel service did not restart; \
see: journalctl -u ${MARAN_CLI_API_UNIT} -u ${MARAN_CLI_AGENT_UNIT}"
  fi
  CLI_ROLLBACK_STOPPED=0
  if ! cli_update_wait_healthy "$MARAN_ROLLBACK_GATE_SECONDS"; then
    cli_audit_log rollback unhealthy
    cli_fail "rolled back to ${CLI_ROLLBACK_PREVIOUS} but the panel is still not healthy; \
see: journalctl -u ${MARAN_CLI_API_UNIT} -u ${MARAN_CLI_AGENT_UNIT}"
  fi

  cli_audit_log rollback rolled-back
  cli_rollback_report "$restore" "$dump"
}
