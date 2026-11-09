#!/usr/bin/env bash
# update-selftest-requested.sh: the panel's window onto the update (issue #382) - the `--requested`
# flag and everything it refuses, the check record the panel's screen reads (written from a verified
# manifest, with a hostile date or address written empty), the running state, and the report a requested
# run sends back to close the panel's task.
#
# Sourced by update-selftest.sh after update-selftest-security.sh, which replaced cli_dotnet and
# cli_audit_log with recorders (SEC_DOTNET_LOG, SEC_AUDIT_LOG); never executed on its own and it sets no
# shell option. It drives the copies of update-unattended.sh and update.sh in the temporary root. The
# shell function `cli_update` itself is NOT driven (it needs root and systemd).

for fn in cli_update_publish_file cli_update_record_check cli_update_publish_running cli_update_clear_running \
          cli_requested_report cli_requested_report_outcome; do
  declare -F "$fn" >/dev/null || selftest_failed "the library function ${fn} the requested part drives is not defined."
done
selftest_pass "every library function the requested part drives is defined by the copies"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 31: --requested is the panel's fixed run and takes nothing else --"
# ---------------------------------------------------------------------------------------------
CLI_UPDATE_YES=0
cli_update_parse --requested
expect_equal "--requested alone is accepted and stands in for the prompt" "1:1:0:0" \
  "${CLI_UPDATE_FROM_PANEL}:${CLI_UPDATE_YES}:${CLI_UPDATE_SECURITY_ONLY}:${CLI_UPDATE_SCHEDULED}"
cli_update_parse --yes
expect_equal "an ordinary update is not the panel's run (inverse control)" "0:1" "${CLI_UPDATE_FROM_PANEL}:${CLI_UPDATE_YES}"
for extra in "--yes" "--check" "--security-only" "--channel beta" "--offline-tarball /root/x.tar.gz"; do
  # shellcheck disable=SC2086
  expect_refuse "--requested with ${extra}" "cannot be combined" cli_update_parse --requested $extra
done
expect_refuse "--requested with --security-only --scheduled" "cannot be combined" cli_update_parse --requested --security-only --scheduled
expect_refuse "--requested twice" "twice" cli_update_parse --requested --requested
expect_refuse "--requested with a value" "unknown argument" cli_update_parse --requested stable

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 32: the check record - from a verified manifest, hostile values written empty --"
# ---------------------------------------------------------------------------------------------
install -d "${mroot}/etc/maran"
check_file="${mroot}/etc/maran/update-check.state"
expect_equal "the check record path is the one the panel reads" "${mroot}/etc/maran/update-check.state" "$MARAN_UPDATE_CHECK_STATE_FILE"
rm -f -- "$check_file"
d="$(notes_case)"
cli_update_record_check 1.0.0 "${d}/manifest.json"
[ -f "$check_file" ] || selftest_failed "the check was not recorded."
expect_equal "the record is world-readable and root's to write (0644)" "644" "$(stat -c '%a' -- "$check_file")"
grep -qE '^CHECKED_AT=[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' "$check_file" \
  || selftest_failed "CHECKED_AT is not the exact instant format the panel parses: $(cat "$check_file")"
selftest_pass "CHECKED_AT is a UTC instant in the format the panel parses"
selftest_pass "the record is read from the manifest and the panel's channel is not in it"
grep -q '^CHANNEL=' "$check_file" && selftest_failed "the record names a channel, which only release.state may do."
expect_equal "the record names the available version" "AVAILABLE=1.0.0" "$(grep '^AVAILABLE=' "$check_file")"
expect_equal "the record carries the signed release date" "RELEASED=2026-11-07" "$(grep '^RELEASED=' "$check_file")"
expect_equal "the record carries the signed notes address" "NOTES_URL=https://github.com/innovayse/maran/releases/tag/v1.0.0" "$(grep '^NOTES_URL=' "$check_file")"
expect_equal "the record has exactly four lines" "4" "$(wc -l < "$check_file" | tr -d ' ')"
[ -z "$(find "${mroot}/etc/maran" -maxdepth 1 -name '.state.*')" ] || selftest_failed "a temporary file was left beside the record."

d="$(notes_case)"
set_field "$d" notes_url "javascript:alert(1)"
set_field "$d" released "2026-02-31"
cli_update_record_check 1.0.0 "${d}/manifest.json"
expect_equal "a hostile notes address is written empty" "NOTES_URL=" "$(grep '^NOTES_URL=' "$check_file")"
expect_equal "an impossible release date is written empty" "RELEASED=" "$(grep '^RELEASED=' "$check_file")"
expect_equal "...and the version is still recorded" "AVAILABLE=1.0.0" "$(grep '^AVAILABLE=' "$check_file")"

d="$(notes_case)"
sed -i '/"notes_url"/d; /"released"/d' "${d}/manifest.json"
resign "$d" "${keydir}/good.key"
cli_update_record_check 1.0.0 "${d}/manifest.json"
expect_equal "a manifest without date and notes records both empty" "RELEASED=:NOTES_URL=" \
  "$(grep '^RELEASED=' "$check_file"):$(grep '^NOTES_URL=' "$check_file")"

# A write that cannot happen is a warning, never a failure of the update.
rm -f -- "$check_file"
chmod 0555 "${mroot}/etc/maran"
if [ -w "${mroot}/etc/maran" ]; then
  echo "note: running as a user that ignores directory modes; the unwritable-directory control is skipped"
else
  run_capture cli_update_record_check 1.0.0 "${d}/manifest.json"
  expect_equal "an unwritable state directory does not fail the run" "0" "$RUN_STATUS"
  case "$RUN_OUT" in *"could not record this check"*) selftest_pass "...and says so" ;; *) selftest_failed "the failed record said nothing: ${RUN_OUT}" ;; esac
  [ ! -e "$check_file" ] || selftest_failed "a record exists although the directory was not writable."
fi
chmod 0755 "${mroot}/etc/maran"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 33: the running state - present while an update holds the lock, gone after --"
# ---------------------------------------------------------------------------------------------
running_file="${mroot}/etc/maran/update-running.state"
expect_equal "the running state path is the one the panel reads" "${mroot}/etc/maran/update-running.state" "$MARAN_UPDATE_RUNNING_STATE_FILE"
rm -f -- "$running_file"
cli_update_publish_running
grep -qE '^STARTED_AT=[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' "$running_file" \
  || selftest_failed "STARTED_AT is not the exact instant format the panel parses: $(cat "$running_file" 2>/dev/null)"
expect_equal "the running state is one line" "1" "$(wc -l < "$running_file" | tr -d ' ')"
expect_equal "the running state is world-readable and root's to write (0644)" "644" "$(stat -c '%a' -- "$running_file")"
cli_update_clear_running
[ ! -e "$running_file" ] || selftest_failed "the running state survived cli_update_clear_running."
selftest_pass "the running state is published and then taken back"
cli_update_clear_running
selftest_pass "taking back an absent running state is not an error (the exit trap runs for every exit)"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 34: the report a requested run sends back --"
# ---------------------------------------------------------------------------------------------
: > "$SEC_DOTNET_LOG"
: > "$SEC_AUDIT_LOG"
SEC_DOTNET_STATUS=0
cli_requested_report installing 1.0.0 1.1.0
expect_equal "the install beginning is reported with both releases" "update-request-report installing 1.0.0 1.1.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_requested_report_outcome updated 1.0.0 1.1.0
expect_equal "a finished update is reported as applied" "update-request-report applied 1.0.0 1.1.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_requested_report_outcome rolled-back 1.0.0 1.1.0
expect_equal "a rolled-back update is reported" "update-request-report rolled-back 1.0.0 1.1.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_requested_report_outcome rollback-failed 1.0.0 1.1.0
expect_equal "a failed rollback is reported" "update-request-report rollback-failed 1.0.0 1.1.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_requested_report_outcome interrupted 1.0.0 1.1.0
expect_equal "an interrupted update is reported" "update-request-report interrupted 1.0.0 1.1.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_requested_report_outcome check 1.0.0 1.0.0
expect_equal "a run with nothing newer is reported as unchanged" "update-request-report unchanged 1.0.0 1.0.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_requested_report_outcome refused 1.0.0 ""
expect_equal "a refusal before any release is known is reported without a target" "update-request-report refused 1.0.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_requested_report_outcome refused "" ""
expect_equal "a refusal on a host with no known release names it unversioned" "update-request-report refused unversioned" "$(tail -n 1 "$SEC_DOTNET_LOG")"
expect_equal "eight reports made eight calls" "8" "$(wc -l < "$SEC_DOTNET_LOG" | tr -d ' ')"
for outcome in "" skipped-not-security skipped-major bogus; do
  cli_requested_report_outcome "$outcome" 1.0.0 1.1.0
done
expect_equal "an outcome with no word for the panel is not reported (four outcomes, still eight calls)" "8" "$(wc -l < "$SEC_DOTNET_LOG" | tr -d ' ')"
calls_before="$(wc -l < "$SEC_DOTNET_LOG" | tr -d ' ')"
cli_requested_report_outcome updated "1.0.0;x" 1.1.0
expect_equal "a release name that is not a plain name is never sent" "$calls_before" "$(wc -l < "$SEC_DOTNET_LOG" | tr -d ' ')"
cli_requested_report_outcome updated 1.0.0 "1.1.0;x"
expect_equal "a target that is not a version is dropped and the report is still sent" "update-request-report applied 1.0.0" "$(tail -n 1 "$SEC_DOTNET_LOG")"
SEC_DOTNET_STATUS=1
cli_requested_report_outcome updated 1.0.0 1.1.0 && report_status=0 || report_status=$?
expect_equal "a report the panel cannot take never fails the run" "0" "$report_status"
expect_equal "...and leaves one fixed journal line" "update report-failed" "$(tail -n 1 "$SEC_AUDIT_LOG")"
SEC_DOTNET_STATUS=0

echo "UNOBSERVED HERE: cli_update's own order in --requested mode (the running state after the lock, the installing report after the decision, the end report from the exit trap) and the units that start it - they need root and systemd."
