#!/usr/bin/env bash
# update.sh: `maran update` moves this panel to a newer signed release (issue #128).
#
# The order, which docs/superpowers/notes/2026-11-04-panel-self-update-threat-note.md binds:
# argument syntax (the tarball path included), lock, interrupted-update check, channel, a small
# staging floor (the full one for an offline bundle, which is copied there), signed manifest,
# version decision (--check ends here), the full floors on the staging, releases and dump
# filesystems, the confirmation, the artifacts, the adoption of a legacy layout (so the dump can
# name the release it leaves), old dumps pruned to KEEP-1, the pre-update dump, the unpack into a
# release directory that is never the live tree, then (steps that can fail and are rolled back)
# switch `current`, migrate with the NEW binary, restart api then agent, and a health gate. Only a
# healthy gate records the new version in release.state.
#
# A failure after the switch puts `current` back and restarts the old release. It NEVER restores
# the database: a restore discards every write since the dump, so the operator is told the dump's
# path and the explicit command (`maran rollback --restore-database`) and decides.
#
# A kill at any point leaves a state cli_layout_check_consistent recognises: `current` and
# release.state disagree only between the switch and the final state write, and the next run then
# refuses and names the recovery. A signal handler never starts a rollback.

readonly MARAN_UPDATE_USAGE="Usage: maran update [--check] [--channel stable|beta] [--offline-tarball PATH] [--yes]"
readonly MARAN_UPDATE_DUMP_KEEP=3
readonly MARAN_UPDATE_MANIFEST_NAME="manifest.json"
# Room required before an UPDATE downloads anything: the manifest carries no sizes, so this is a
# fixed floor on the staging filesystem, the releases filesystem and the dump directory's filesystem.
readonly MARAN_UPDATE_FLOOR_BYTES=$((2 * 1024 * 1024 * 1024))
# Room required to fetch the signed manifest alone, which is all a --check ever stores.
readonly MARAN_UPDATE_MANIFEST_FLOOR_BYTES=$((16 * 1024 * 1024))
readonly MARAN_UPDATE_GATE_SECONDS=120

# cli_update_usage_fail: a usage error names the verb and ends with exit 64.
cli_update_usage_fail() {
  echo "maran update: $*. ${MARAN_UPDATE_USAGE}" >&2
  exit 64
}

# cli_update_attempt: runs the command in $@ in a subshell with errexit ON and records its status in
# CLI_UPDATE_STATUS, always returning 0. Called as a plain statement, never inside `if` or `||`,
# which would switch errexit off inside the subshell; a cli_fail anywhere in the command ends only
# the subshell, so the caller can roll back instead of dying.
cli_update_attempt() {
  set +e
  ( set -e; "$@" )
  CLI_UPDATE_STATUS=$?
  set -e
  return 0
}

# cli_update_cleanup: the EXIT-trap half of this verb, run for every exit (signals become exits in
# the stage's trap). Removes a dump no switch ever followed (and no outcome kept), tells the operator
# how to recover when the run died between the switch and its end, and writes the one audit line.
cli_update_cleanup() {
  local status=$?
  if [ -z "${CLI_UPDATE_OUTCOME:-}" ] && [ "${CLI_UPDATE_SWITCHING:-0}" -eq 0 ] && [ -n "${CLI_UPDATE_DUMP:-}" ]; then
    rm -f -- "$CLI_UPDATE_DUMP"
  fi
  if [ -z "${CLI_UPDATE_OUTCOME:-}" ]; then
    if [ "${CLI_UPDATE_SWITCHING:-0}" -eq 1 ] && [ "$status" -ne 0 ]; then
      CLI_UPDATE_OUTCOME="interrupted"
      echo "maran: the update was interrupted after the release was switched. Run: maran rollback" >&2
      [ -z "${CLI_UPDATE_DUMP:-}" ] || echo "maran: the pre-update database dump is ${CLI_UPDATE_DUMP}" >&2
    elif [ "$status" -ne 0 ]; then
      CLI_UPDATE_OUTCOME="refused"
    fi
  fi
  [ -z "${CLI_UPDATE_OUTCOME:-}" ] || cli_audit_log update "$CLI_UPDATE_OUTCOME"
}

# cli_update_require_space: refuses unless the filesystem holding $1 (or its nearest existing
# parent) has at least $2 bytes free (MARAN_UPDATE_FLOOR_BYTES when $2 is not given).
cli_update_require_space() {
  local path="$1" floor="${2:-$MARAN_UPDATE_FLOOR_BYTES}" avail
  while [ ! -d "$path" ]; do path="$(dirname -- "$path")"; done
  avail="$(df --output=avail -B1 -- "$path" | tail -n 1 | tr -d ' ')"
  [[ "$avail" =~ ^[0-9]+$ ]] || cli_fail "could not measure the free space under ${path}"
  [ "$avail" -ge "$floor" ] || cli_fail "not enough free space under ${path}: ${floor} bytes are required here"
}

# cli_update_require_update_space: the full floors an actual update needs before it asks or downloads.
# cli_dumps_path only prints (it cannot fail); the directory is created by cli_dumps_dir at dump time.
cli_update_require_update_space() {
  cli_update_require_space "$MARAN_CLI_STAGING_BASE"
  cli_update_require_space "$MARAN_LAYOUT_ROOT"
  cli_update_require_space "$(cli_dumps_path)"
}

# cli_update_parse: parses the verb's arguments into CLI_UPDATE_CHECK, CLI_UPDATE_YES,
# CLI_UPDATE_REQUESTED_CHANNEL and CLI_UPDATE_TARBALL (both empty when not given). Every refusal is a
# usage failure. No value may start with `-`, so an option is never swallowed as a value.
cli_update_parse() {
  local opt value
  CLI_UPDATE_CHECK=0
  CLI_UPDATE_YES=0
  CLI_UPDATE_REQUESTED_CHANNEL=""
  CLI_UPDATE_TARBALL=""
  while [ "$#" -gt 0 ]; do
    opt="$1"
    case "$opt" in
      --check)
        [ "$CLI_UPDATE_CHECK" -eq 0 ] || cli_update_usage_fail "--check was given twice"
        CLI_UPDATE_CHECK=1
        ;;
      --yes)
        [ "$CLI_UPDATE_YES" -eq 0 ] || cli_update_usage_fail "--yes was given twice"
        CLI_UPDATE_YES=1
        ;;
      --channel|--offline-tarball)
        [ "$#" -ge 2 ] || cli_update_usage_fail "${opt} needs a value"
        value="$2"
        [ -n "$value" ] && [[ "$value" != -* ]] \
          || cli_update_usage_fail "the value of ${opt} must not be empty or start with '-'"
        if [ "$opt" = "--channel" ]; then
          [ -z "$CLI_UPDATE_REQUESTED_CHANNEL" ] || cli_update_usage_fail "--channel was given twice"
          [[ "$value" =~ ^(stable|beta)$ ]] || cli_update_usage_fail "--channel is stable or beta"
          CLI_UPDATE_REQUESTED_CHANNEL="$value"
        else
          [ -z "$CLI_UPDATE_TARBALL" ] || cli_update_usage_fail "--offline-tarball was given twice"
          CLI_UPDATE_TARBALL="$value"
        fi
        shift
        ;;
      *) cli_update_usage_fail "unknown argument" ;;
    esac
    shift
  done
  [ -z "$CLI_UPDATE_REQUESTED_CHANNEL" ] || [ -z "$CLI_UPDATE_TARBALL" ] \
    || cli_update_usage_fail "--offline-tarball and --channel cannot be combined: the bundle names its own channel"
}

# cli_update_require_tarball_syntax: refuses unless $1 is an absolute path of plain characters (so
# it never starts with '-'). Text only: it reads no file, so it runs right after the arguments are
# parsed, before the lock and before any layout or state is read.
cli_update_require_tarball_syntax() {
  [[ "$1" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$1" != *..* ]] \
    || cli_fail "--offline-tarball must be an absolute path of letters, digits, '.', '_', '-' and '/'"
}

# cli_update_require_tarball: refuses unless $1 names a regular non-symlink file owned by root that
# neither group nor others can write. The path syntax was checked by the _syntax half above.
cli_update_require_tarball() {
  local file="$1" mode
  [ -f "$file" ] && [ ! -L "$file" ] || cli_fail "--offline-tarball is not a regular file"
  [ "$(stat -c '%u' -- "$file")" = "0" ] || cli_fail "--offline-tarball must be owned by root"
  mode="$(stat -c '%a' -- "$file")"
  [[ "${mode: -2}" =~ ^[0145]{2}$ ]] || cli_fail "--offline-tarball must not be writable by group or others"
}

# cli_update_fetch_manifest: puts a verified manifest into $2 and sets CLI_UPDATE_AVAILABLE and
# CLI_UPDATE_RESOLVED_CHANNEL. The release libraries verify the signature before any field is read and,
# from the network, require the signed channel to be present and equal to the requested one. An
# offline bundle keeps the channel it names, or the wanted one when it names none.
#   $1 = wanted channel, $2 = release work directory.
cli_update_fetch_manifest() {
  local channel="$1" dir="$2" manifest="${2}/${MARAN_UPDATE_MANIFEST_NAME}" signed
  install -d -m 0700 -o root -g root "$dir"
  if [ -n "$CLI_UPDATE_TARBALL" ]; then
    cli_update_require_tarball "$CLI_UPDATE_TARBALL"
    cli_release_verify_offline "$CLI_UPDATE_TARBALL" "$dir"
    signed="$(cli_release_manifest_field "$manifest" channel)"
    if [ -n "$signed" ]; then
      [[ "$signed" =~ ^(stable|beta)$ ]] || cli_fail "the bundle names an unknown channel"
      channel="$signed"
    fi
  else
    cli_release_fetch_manifest "$channel" "$dir"
  fi
  CLI_UPDATE_RESOLVED_CHANNEL="$channel"
  CLI_UPDATE_AVAILABLE="$(cli_release_manifest_field "$manifest" version)"
  cli_version_is_semver "$CLI_UPDATE_AVAILABLE" \
    || cli_fail "the release manifest carries a version that is not a SemVer string"
}

# cli_update_is_legacy_host: succeeds when any component under ${MARAN_LAYOUT_ROOT} is still a real
# directory, which is a host installed before the release layout existed, or one whose adoption a kill
# interrupted (`current` is set, some components are not yet links). Only such a host may have no
# `current`, and cli_layout_adopt_legacy is what completes it.
cli_update_is_legacy_host() {
  local part
  for part in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    if [ -d "${MARAN_LAYOUT_ROOT}/${part}" ] && [ ! -L "${MARAN_LAYOUT_ROOT}/${part}" ]; then
      return 0
    fi
  done
  return 1
}

# cli_update_is_newer: succeeds when release $1 is newer than the installed release $2. An empty $2
# is a legacy install that was never adopted, which is older than every SemVer release.
cli_update_is_newer() {
  [ -z "$2" ] || cli_version_gt "$1" "$2"
}

# cli_update_confirm: asks the operator unless --yes was given. A run with no terminal and no --yes
# refuses, so a pipe or a cron job can never update the panel by accident.
#   $1 = installed version, $2 = available version, $3 = channel.
cli_update_confirm() {
  local answer=""
  [ "$CLI_UPDATE_YES" -eq 0 ] || return 0
  [ -t 0 ] || cli_fail "this is not a terminal: pass --yes to update without being asked"
  echo "Update ${1} to ${2} on the ${3} channel. The panel restarts and is unavailable for a short time."
  read -r -p "Continue? [y/N] " answer || answer=""
  [[ "$answer" =~ ^([yY]|[yY][eE][sS])$ ]] || cli_fail "update declined; nothing was changed"
}

# cli_update_adopt: moves a legacy layout into releases/legacy-<UTC stamp> (cli_layout_adopt_legacy),
# BEFORE the dump so the dump's name carries the adopted release name. The library's output goes to
# stderr: stdout carries this verb's result and nothing else.
cli_update_adopt() {
  cli_update_attempt cli_update_adopt_quietly
  [ "$CLI_UPDATE_STATUS" -eq 0 ] \
    || cli_fail "the legacy layout could not be adopted; nothing was switched, check ${MARAN_LAYOUT_ROOT}"
}

# cli_update_adopt_quietly: runs cli_layout_adopt_legacy with its stdout sent to stderr.
cli_update_adopt_quietly() {
  cli_layout_adopt_legacy >&2
}

# cli_update_take_dump: writes the pre-update database dump into the root-only dump directory and
# sets CLI_UPDATE_DUMP. A failed or empty dump refuses the update before anything is switched. The
# name, like the pruning and the newest-first lookup, belongs to update-dumps.sh alone.
#   $1 = release being left (its name as installed or adopted), $2 = release being installed.
cli_update_take_dump() {
  local dir name
  # cli_dumps_dir creates the directory root:root 0700 when absent and refuses anything else; the
  # assignments are plain so a refusal ends the verb (inside an argument it would only end the $( )).
  dir="$(cli_dumps_dir)"
  # Make room BEFORE the new dump, so repeated failing updates never hold more than KEEP dumps.
  cli_update_attempt cli_dumps_prune $((MARAN_UPDATE_DUMP_KEEP - 1))
  [ "$CLI_UPDATE_STATUS" -eq 0 ] || cli_fail "could not prune the old pre-update dumps; nothing was changed"
  name="$(cli_dumps_name "$1" "$2")"
  cli_update_require_space "$dir"
  CLI_UPDATE_DUMP="${dir}/${name}"
  cli_update_attempt cli_dump_database "$CLI_UPDATE_DUMP"
  [ "$CLI_UPDATE_STATUS" -eq 0 ] && [ -s "$CLI_UPDATE_DUMP" ] && [ ! -L "$CLI_UPDATE_DUMP" ] \
    || cli_fail "the pre-update database dump failed or is empty; nothing was changed"
  chmod 0600 -- "$CLI_UPDATE_DUMP"
}

# cli_update_activate: the steps a failure rolls back, in order: switch `current`, migrate with the
# NEW binary, restart api then agent, then the health gate. Run through cli_update_attempt.
#   $1 = version being installed.
cli_update_activate() {
  cli_layout_switch "$1"
  cli_update_migrate "$1"
  cli_update_restart_services
  cli_update_wait_healthy "$MARAN_UPDATE_GATE_SECONDS"
}

# cli_update_revert: the automatic rollback, run through cli_update_attempt. Symlink only: the
# database is NEVER restored here. Agent first, then api, then the gate again.
#   $1 = version to return to.
cli_update_revert() {
  cli_layout_switch "$1"
  cli_update_restart_for_rollback
  cli_update_wait_healthy "$MARAN_UPDATE_GATE_SECONDS"
}

# cli_update_roll_back: handles a failed activation, once. Never loops and never touches the
# database; always ends the run with exit 1 and a message saying which of the two states it is in.
# The outcome is audited as rolled-back or rollback-failed from the status of the revert itself.
#   $1 = version to return to.
cli_update_roll_back() {
  local previous="$1"
  echo "maran: the update failed after the switch; returning to ${previous}" >&2
  cli_update_attempt cli_update_revert "$previous"
  if [ "$CLI_UPDATE_STATUS" -ne 0 ]; then
    CLI_UPDATE_OUTCOME="rollback-failed"
    echo "maran: the rollback to ${previous} did not become healthy either; no further attempt is made" >&2
    echo "maran: see 'journalctl -u ${MARAN_CLI_API_UNIT} -u ${MARAN_CLI_AGENT_UNIT}', then run 'maran rollback'" >&2
  else
    CLI_UPDATE_OUTCOME="rolled-back"
    echo "maran: the update did not become healthy and was rolled back: ${previous} is running again." >&2
  fi
  echo "maran: the database was NOT restored. Writes made since the update are kept." >&2
  echo "maran: the pre-update dump is ${CLI_UPDATE_DUMP}" >&2
  echo "maran: to restore it, discarding every write since, run: maran rollback --restore-database" >&2
  exit 1
}

# cli_update_prune: removes old releases and dumps after a SUCCESSFUL update. A prune that fails is a
# warning, never a failure: the new release is already running and recorded.
cli_update_prune() {
  cli_update_attempt cli_layout_prune
  [ "$CLI_UPDATE_STATUS" -eq 0 ] || echo "maran: could not prune old releases; the update itself succeeded" >&2
  cli_update_attempt cli_dumps_prune "$MARAN_UPDATE_DUMP_KEEP"
  [ "$CLI_UPDATE_STATUS" -eq 0 ] || echo "maran: could not prune old dumps; the update itself succeeded" >&2
}

# cli_update_report_check: the whole output of --check: installed, available, channel, and a plain
# sentence saying whether the available release is newer than the installed one.
#   $1 = installed version (empty: legacy), $2 = channel.
cli_update_report_check() {
  local shown="${1:-unversioned legacy install}"
  echo "current: ${shown}"
  echo "available: ${CLI_UPDATE_AVAILABLE}"
  echo "channel: ${2}"
  if [ "$CLI_UPDATE_AVAILABLE" = "$1" ]; then
    echo "No update: the installed release is already ${CLI_UPDATE_AVAILABLE}."
  elif cli_update_is_newer "$CLI_UPDATE_AVAILABLE" "$1"; then
    echo "Update available: ${CLI_UPDATE_AVAILABLE} is newer than ${shown}."
  else
    echo "No update: ${CLI_UPDATE_AVAILABLE} is not newer than ${shown}."
  fi
}

# cli_update: the verb. Arguments: [--check] [--channel stable|beta] [--offline-tarball PATH] [--yes].
cli_update() {
  cli_update_parse "$@"
  # Text-only checks come first: a hostile path is refused before the lock and before any read.
  [ -z "$CLI_UPDATE_TARBALL" ] || cli_update_require_tarball_syntax "$CLI_UPDATE_TARBALL"
  # Globals, not locals: the EXIT trap runs after this function has returned.
  CLI_UPDATE_OUTCOME=""
  CLI_UPDATE_SWITCHING=0
  CLI_UPDATE_DUMP=""
  CLI_UPDATE_AVAILABLE=""
  CLI_UPDATE_RESOLVED_CHANNEL=""
  cli_panel_open_stage cli_update_cleanup
  # Prints the facts itself (and removes stale .partial releases); here it is only a test.
  cli_layout_check_consistent || exit 1

  local installed state_channel channel work="${CLI_PANEL_STAGE}/release" legacy=0
  installed="$(cli_layout_current_version)"
  # A real component directory means adoption is still owed, even when `current` already exists
  # (a kill during an earlier adoption): the adoption below resumes it.
  if cli_update_is_legacy_host; then legacy=1; fi
  if [ -z "$installed" ] && [ "$legacy" -eq 0 ]; then
    cli_fail "the installed version is unknown: no release layout, release.state or legacy install" \
      "under ${MARAN_LAYOUT_ROOT}; refusing to update"
  fi
  state_channel="$(cli_layout_state_read CHANNEL)"
  state_channel="${state_channel:-stable}"
  channel="${CLI_UPDATE_REQUESTED_CHANNEL:-$state_channel}"
  [[ "$channel" =~ ^(stable|beta)$ ]] || cli_fail "release.state names an unknown channel"

  # A --check stores only the signed manifest: a small floor on staging alone. An offline bundle is
  # copied and unpacked there, so it asks for the full one.
  if [ -n "$CLI_UPDATE_TARBALL" ]; then
    cli_update_require_space "$MARAN_CLI_STAGING_BASE"
  else
    cli_update_require_space "$MARAN_CLI_STAGING_BASE" "$MARAN_UPDATE_MANIFEST_FLOOR_BYTES"
  fi
  cli_update_fetch_manifest "$channel" "$work"
  channel="$CLI_UPDATE_RESOLVED_CHANNEL"

  if [ "$CLI_UPDATE_CHECK" -eq 1 ]; then
    cli_update_report_check "$installed" "$channel"
    CLI_UPDATE_OUTCOME="check"
    return 0
  fi
  if [ "$CLI_UPDATE_AVAILABLE" = "$installed" ]; then
    echo "Already on ${installed}; nothing to do."
    CLI_UPDATE_OUTCOME="check"
    return 0
  fi
  cli_update_is_newer "$CLI_UPDATE_AVAILABLE" "$installed" \
    || cli_fail "the release ${CLI_UPDATE_AVAILABLE} is not newer than ${installed};" \
      "only 'maran rollback' goes backwards"
  # An update is decided: a download must never be what fills the disk, so the full floors apply now.
  cli_update_require_update_space
  cli_update_confirm "${installed:-the unversioned legacy install}" "$CLI_UPDATE_AVAILABLE" "$channel"

  # The network path downloads and checks sha256 here; an offline bundle was unpacked by its verify.
  if [ -z "$CLI_UPDATE_TARBALL" ]; then
    cli_release_fetch_artifacts "${work}/${MARAN_UPDATE_MANIFEST_NAME}" "$work"
  fi
  cli_update_require_space "$MARAN_LAYOUT_ROOT"
  if [ "$legacy" -eq 1 ]; then
    cli_update_adopt
    installed="$(cli_layout_current_version)"
    [ -n "$installed" ] || cli_fail "the legacy layout was adopted but names no release; check ${MARAN_LAYOUT_ROOT}"
  fi
  cli_update_take_dump "$installed" "$CLI_UPDATE_AVAILABLE"
  # install_release re-checks each archive's sha256 against the manifest before it unpacks; an
  # abandoned release of this version left by a failed update is replaced.
  cli_layout_install_release "$CLI_UPDATE_AVAILABLE" "$work" replace

  # PREVIOUS is durable BEFORE the switch, so a kill leaves `rollback` a target; VERSION stays the old
  # one until the gate is healthy, and that disagreement with `current` marks an unfinished update.
  # A failing state write is a refusal; only from the line below is a kill an interrupted switch.
  cli_layout_state_write "$state_channel" "$installed" "$installed"
  CLI_UPDATE_SWITCHING=1
  cli_update_attempt cli_update_activate "$CLI_UPDATE_AVAILABLE"
  # An `if`, not `|| cli_update_roll_back`: a function that runs as an `||` operand has errexit
  # disabled for its whole body, including the subshell its own attempt would open.
  if [ "$CLI_UPDATE_STATUS" -ne 0 ]; then
    cli_update_roll_back "$installed"
  fi

  cli_layout_state_write "$channel" "$CLI_UPDATE_AVAILABLE" "$installed"
  CLI_UPDATE_SWITCHING=0
  CLI_UPDATE_OUTCOME="updated"
  cli_update_prune

  echo "Panel updated: ${installed} -> ${CLI_UPDATE_AVAILABLE} (${channel}); the health gate passed."
  echo "The pre-update database dump is ${CLI_UPDATE_DUMP}."
  echo "To go back to ${installed}: maran rollback"
}
