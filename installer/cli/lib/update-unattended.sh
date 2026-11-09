#!/usr/bin/env bash
# update-unattended.sh: what the update verb does for a run nobody types, and for the panel that shows
# it. Two groups and nothing else.
#
# 1. What `maran update --security-only` and `--scheduled` add to the operator's update (issue #383):
#    the DECISION whether an unattended run may apply the release the signed manifest names, the memory
#    of a release that already failed here, and the REPORT of a scheduled run's outcome to the panel.
#    The argument is docs/superpowers/notes/2026-11-07-security-patch-updates-threat-note.md.
# 2. The panel's window onto the update (issue #382): the two root-owned facts the panel's "Panel
#    updates" screen reads (the last signed manifest this host fetched, and whether an update holds the
#    lock) and the report a run the panel asked for (`--requested`) sends back to close its task. The
#    argument is docs/superpowers/notes/2026-11-07-panel-update-from-panel-threat-note.md.
#
# Every step that changes the server stays in update.sh, one body for the operator's run, the timer's
# and the panel's, so an unattended run takes the same steps and not fewer.
#
# Sourced by the `maran` wrapper after release-verify.sh and release-version.sh and before update.sh;
# never executed on its own.

# The file that remembers the release an unattended run applied, rolled back and must not try again.
# One line: the SemVer version. Root-owned in the root-only configuration directory; the panel's
# service account cannot write it, so neither can the process an attacker would reach through the
# panel. A newer release, or a successful update by anyone, ends the pause.
readonly MARAN_UNATTENDED_FAILED_FILE="/etc/maran/security-update-failed"

# cli_unattended_skip OUTCOME MESSAGE...: ends an unattended run's decision without an update. Sets the
# outcome word the verb's journal line carries, tells the terminal and the unit's journal why in one
# sentence, and returns 0: the caller then returns 1 from the decision so the run ends successfully,
# because a release that is not for this server is a decision, not a failure.
cli_unattended_skip() {
  CLI_UPDATE_OUTCOME="$1"
  shift
  echo "No unattended update: $*"
}

# cli_unattended_decide INSTALLED AVAILABLE MANIFEST: succeeds when an unattended run may apply the
# release AVAILABLE over INSTALLED, and otherwise reports why not (cli_unattended_skip) and fails.
# MANIFEST is the work copy cli_update_fetch_manifest verified, so the `security` flag read is the
# signed one. The conditions, each of which must hold:
#   - the release is a security release (the signed flag is exactly true);
#   - the installed release is a SemVer version, because a legacy, unversioned install has no major
#     number to compare and is moved by an operator's first `maran update`;
#   - the major version is unchanged: a major release is where breaking changes are allowed, and an
#     unattended jump is where one would land with nobody to read the release notes;
#   - this release has not already failed here (cli_unattended_remember_failure).
cli_unattended_decide() {
  local installed="$1" available="$2" manifest="$3"
  if ! cli_release_manifest_is_security "$manifest"; then
    cli_unattended_skip skipped-not-security \
      "${available} is not marked as a security release; it is left for 'maran update'."
    return 1
  fi
  if ! cli_version_is_semver "$installed"; then
    cli_unattended_skip skipped-unversioned \
      "this server has no versioned release to compare ${available} with; run 'maran update' once by hand."
    return 1
  fi
  if ! cli_version_same_major "$available" "$installed"; then
    cli_unattended_skip skipped-major \
      "${available} is a different major version than ${installed}; run 'maran update' by hand."
    return 1
  fi
  if [ "$(cli_unattended_failed_version)" = "$available" ]; then
    cli_unattended_skip skipped-failed-before \
      "${available} already failed here and was rolled back; it is not tried again until a newer release or 'maran update'."
    return 1
  fi
  return 0
}

# cli_unattended_failed_version: prints the version the failure file names, or nothing when there is no
# such file, it is a link, or its first line is not a SemVer version. An unreadable memory is no memory:
# the next run then tries again, which the rollback bounds.
cli_unattended_failed_version() {
  local line=""
  if [ -f "$MARAN_UNATTENDED_FAILED_FILE" ] && [ ! -L "$MARAN_UNATTENDED_FAILED_FILE" ]; then
    IFS= read -r line < "$MARAN_UNATTENDED_FAILED_FILE" || true
  fi
  if cli_version_is_semver "$line"; then printf '%s\n' "$line"; fi
}

# cli_unattended_remember_failure VERSION: records that an unattended run rolled VERSION back. Written
# beside the target and renamed in, root 0600. A failure to write it is a warning and never a failure:
# the run is already ending in its own rollback message, and a lost memory only means one more try.
cli_unattended_remember_failure() {
  local version="$1" dir tmp
  cli_version_is_semver "$version" || return 0
  dir="$(dirname -- "$MARAN_UNATTENDED_FAILED_FILE")"
  if tmp="$(mktemp "${dir}/.security-update-failed.XXXXXX" 2>/dev/null)" \
    && printf '%s\n' "$version" > "$tmp" \
    && chmod 0600 -- "$tmp" \
    && mv -f -- "$tmp" "$MARAN_UNATTENDED_FAILED_FILE"; then
    return 0
  fi
  [ -z "${tmp:-}" ] || rm -f -- "$tmp"
  echo "maran: could not record the failed release; an unattended run may try ${version} again" >&2
  return 0
}

# cli_unattended_clear_failure: forgets the failed release after any successful update.
cli_unattended_clear_failure() {
  rm -f -- "$MARAN_UNATTENDED_FAILED_FILE"
}

# cli_unattended_report OUTCOME FROM TO: tells the panel how a scheduled run ended, so the failure is
# in the panel's audit journal and in the operator's mail and not only in a journal nobody reads. The
# outcome word is the verb's own; only a run that applied a release or failed after switching is
# reported, because a skipped or refused run changed nothing. Best effort and never fatal: it runs from
# the exit trap, possibly with the panel down or running the previous release, and the unit's own
# failed state and the journal line are the record that remains. The panel binary is asked with fixed
# words and two release names that passed the version check, nothing else, and its output is
# discarded: a failed report writes one more fixed journal line.
#   $1 = outcome (the update verb's), $2 = release left, $3 = release installed or tried.
cli_unattended_report() {
  local outcome="$1" from="$2" to="$3" word
  case "$outcome" in
    updated) word="applied" ;;
    rolled-back | rollback-failed | interrupted) word="$outcome" ;;
    *) return 0 ;;
  esac
  cli_version_is_semver "$from" && cli_version_is_semver "$to" || return 0
  if ! ( cli_dotnet report-update "$word" "$from" "$to" ) >/dev/null 2>&1; then
    cli_audit_log update report-failed
  fi
  return 0
}

# --- The panel's window onto the update (issue #382) -----------------------------------------------

# The last signed manifest this host fetched from the release host, as the panel's screen shows it.
# Root-owned (0644) in the root-only configuration directory, so the panel's service account can read
# it and cannot forge it. KEY=value lines: CHECKED_AT, AVAILABLE, RELEASED (empty when the manifest
# carries none), NOTES_URL (empty likewise). The channel is not in it: it is the panel's own, from
# release.state, and a check is recorded only for that channel. Every value came out of a manifest whose
# signature verified and passed the same validators `--check` prints with.
readonly MARAN_UPDATE_CHECK_STATE_FILE="/etc/maran/update-check.state"
# Present while an update verb holds the lock and is changing this server: one line, STARTED_AT. Root-
# owned like the file above. A kill -9 can leave it behind; the panel ignores one older than four hours
# and the next run replaces it.
readonly MARAN_UPDATE_RUNNING_STATE_FILE="/etc/maran/update-running.state"

# cli_update_publish_file FILE CONTENT: writes FILE root:root 0644 by writing beside it and renaming,
# so the panel never reads half a file. Fails (status 1, nothing left behind) when it cannot; the
# callers treat that as a warning, because the panel's screen is not worth an update.
cli_update_publish_file() {
  local file="$1" content="$2" dir tmp=""
  dir="$(dirname -- "$file")"
  if tmp="$(mktemp "${dir}/.state.XXXXXX" 2>/dev/null)" \
    && printf '%s\n' "$content" > "$tmp" \
    && chmod 0644 -- "$tmp" \
    && mv -f -- "$tmp" "$file"; then
    return 0
  fi
  [ -z "$tmp" ] || rm -f -- "$tmp"
  return 1
}

# cli_update_record_check AVAILABLE MANIFEST: remembers what the verified manifest in MANIFEST said, for
# the panel's screen. Called for every manifest of the panel's own channel fetched over the network, by
# `--check`, an update, and the timer alike: "the last check" is the last time this host asked the release host. The
# date and the notes address are re-validated here and written empty when they fail, so the file never
# holds a value the verb would not have printed. Never fatal.
#   $1 = the manifest's (already validated) version, $2 = the manifest.
cli_update_record_check() {
  local available="$1" manifest="$2" released notes_url
  released="$(cli_release_manifest_field "$manifest" released)"
  notes_url="$(cli_release_manifest_field "$manifest" notes_url)"
  cli_release_date_valid "$released" || released=""
  cli_release_notes_url_valid "$notes_url" || notes_url=""
  cli_update_publish_file "$MARAN_UPDATE_CHECK_STATE_FILE" \
    "$(printf 'CHECKED_AT=%s\nAVAILABLE=%s\nRELEASED=%s\nNOTES_URL=%s\n' \
      "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$available" "$released" "$notes_url")" \
    || echo "maran: could not record this check for the panel; the update itself is unaffected" >&2
  return 0
}

# cli_update_publish_running: says, for the panel, that an update holds the lock. Never fatal.
cli_update_publish_running() {
  cli_update_publish_file "$MARAN_UPDATE_RUNNING_STATE_FILE" \
    "$(printf 'STARTED_AT=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)")" \
    || echo "maran: could not publish the running state for the panel; the update itself is unaffected" >&2
  return 0
}

# cli_update_clear_running: takes the running state back. Runs from the exit trap, so it must not fail.
cli_update_clear_running() {
  rm -f -- "$MARAN_UPDATE_RUNNING_STATE_FILE" 2>/dev/null || true
}

# cli_requested_report WORD FROM [TO]: tells the panel how a run it asked for (`--requested`) is going
# or how it ended, so the task it opened closes with the true result and not as a lost process. WORD is
# one of the closed set the panel's verb accepts: installing, applied, unchanged, refused, rolled-back,
# rollback-failed, interrupted. The release names are passed only when they passed the version check;
# an install with no version yet (a legacy host before its adoption) is named "unversioned". Best
# effort and never fatal, the way cli_unattended_report is: the panel may be down or running the
# previous release, and the task is then closed by the panel's own reaper. The panel binary is asked
# with fixed words and release names, and its output is discarded.
cli_requested_report() {
  local word="$1" from="${2:-}" to="${3:-}"
  if [ -z "$from" ]; then
    from="unversioned"
  else
    cli_version_is_release_name "$from" || return 0
  fi
  if cli_version_is_semver "$to"; then
    ( cli_dotnet update-request-report "$word" "$from" "$to" ) >/dev/null 2>&1 \
      || cli_audit_log update report-failed
  else
    ( cli_dotnet update-request-report "$word" "$from" ) >/dev/null 2>&1 \
      || cli_audit_log update report-failed
  fi
  return 0
}

# cli_requested_report_outcome OUTCOME FROM TO: the end-of-run report of a `--requested` run, mapping
# the verb's own outcome word to the panel's. An outcome the panel has no word for (the run changed
# nothing and is not a failure the panel should show) is not reported.
#   $1 = outcome (the update verb's), $2 = release left, $3 = release installed or tried.
cli_requested_report_outcome() {
  local outcome="$1" from="$2" to="$3" word
  case "$outcome" in
    updated) word="applied" ;;
    check) word="unchanged" ;;
    refused | rolled-back | rollback-failed | interrupted) word="$outcome" ;;
    *) return 0 ;;
  esac
  cli_requested_report "$word" "$from" "$to"
}
