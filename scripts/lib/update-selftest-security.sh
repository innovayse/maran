#!/usr/bin/env bash
# update-selftest-security.sh: the unattended security-patch update's decisions (issue #383) - the
# signed `security` flag and every spelling of it that must read as "not a security release", the
# major-version comparison, the decision an unattended run takes, the memory of a release that already
# failed, the flags that make a run unattended, and the report to the panel.
#
# Sourced by update-selftest.sh after update-selftest-notes.sh; never executed on its own and it sets no
# shell option. It drives the copies of update-unattended.sh and update.sh in the temporary root. The
# shell function `cli_update` itself is NOT driven (it needs root and systemd): what is driven is every
# function it takes the unattended decision from.

# shellcheck disable=SC1090
. "${libdir}/update-unattended.sh"
# shellcheck disable=SC1090
. "${libdir}/update.sh"
for fn in cli_release_manifest_is_security cli_version_major cli_version_same_major cli_unattended_decide \
          cli_unattended_skip cli_unattended_failed_version cli_unattended_remember_failure \
          cli_unattended_clear_failure cli_unattended_report cli_update_parse cli_update_parse_unattended; do
  declare -F "$fn" >/dev/null || selftest_failed "the library function ${fn} the security part drives is not defined."
done
selftest_pass "every library function the security part drives is defined by the copies"

# The two programs the report would run: recorded instead of run. cli_dotnet is the panel binary as the
# service account; cli_audit_log writes the real journal. Both are replaced for the rest of the run,
# and this part is the last one.
SEC_DOTNET_LOG="${work}/security-dotnet.log"
SEC_AUDIT_LOG="${work}/security-audit.log"
SEC_DOTNET_STATUS=0
cli_dotnet() {
  printf '%s\n' "$*" >> "$SEC_DOTNET_LOG"
  return "$SEC_DOTNET_STATUS"
}
cli_audit_log() {
  printf '%s %s\n' "$1" "$2" >> "$SEC_AUDIT_LOG"
}

# security_case: <line or empty>. A signed copy of the pristine stable manifest with LINE inserted after
# the channel member (nothing inserted when empty); prints its directory. The copy is signed again
# because the flag has to arrive inside a manifest whose signature verifies.
security_case() {
  local dir
  dir="$(new_case_dir)"
  cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$dir/"
  if [ -n "${1:-}" ]; then
    SEC_LINE="$1" awk '{ print } /^[[:space:]]*"channel"[[:space:]]*:/ { print ENVIRON["SEC_LINE"] }' \
      "${dir}/manifest.json" > "${dir}/manifest.json.new"
    mv "${dir}/manifest.json.new" "${dir}/manifest.json"
    resign "$dir" "${keydir}/good.key"
  fi
  printf '%s' "$dir"
}

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 24: the signed security flag - only a lone literal true counts --"
# ---------------------------------------------------------------------------------------------
fresh_layout
d="$(security_case '  "security": true,')"
grep -q '"security": true' "${d}/manifest.json" || selftest_failed "fixture: the security line was not inserted."
expect_accept "a manifest carrying security true, signed by the baked key" cli_release_verify_manifest "$d"
expect_true "security true reads as a security release (positive control)" cli_release_manifest_is_security "${d}/manifest.json"

d="$(security_case '  "security": false,')"
expect_accept "a manifest carrying security false, signed by the baked key" cli_release_verify_manifest "$d"
expect_false "security false is not a security release" cli_release_manifest_is_security "${d}/manifest.json"

d="$(security_case '')"
grep -q '"security"' "${d}/manifest.json" && selftest_failed "fixture: the pristine manifest already names security, so the absent case proves nothing."
expect_false "a manifest without the member (an older release) is not a security release" \
  cli_release_manifest_is_security "${d}/manifest.json"

for spelling in '"security": "true",' '"security": "yes",' '"security": 1,' '"security": True,' '"security": TRUE,' \
                '"security": "false",' '"security": null,' '"security": [true],' '"security": truee,' '"securityx": true,' \
                '"Security": true,'; do
  d="$(security_case "  ${spelling}")"
  expect_accept "a validly signed manifest with ${spelling}" cli_release_verify_manifest "$d"
  expect_false "${spelling} is not a security release" cli_release_manifest_is_security "${d}/manifest.json"
done

d="$(security_case '  "security": false,')"
sed -i '0,/"security": false,/s//"security": false,\n  "security": true,/' "${d}/manifest.json"
resign "$d" "${keydir}/good.key"
[ "$(grep -c '"security"' "${d}/manifest.json")" -eq 2 ] || selftest_failed "fixture: the duplicate member was not planted."
expect_false "a manifest naming security twice (false, then true) is not a security release" \
  cli_release_manifest_is_security "${d}/manifest.json"

d="$(security_case '  "security": true,')"
sed -i '0,/"security": true,/s//"security": true,\n  "security": true,/' "${d}/manifest.json"
resign "$d" "${keydir}/good.key"
expect_false "a manifest naming security true twice is not a security release either" \
  cli_release_manifest_is_security "${d}/manifest.json"

d="$(security_case '')"
sed -i '0,/^\([[:space:]]*\)"url"/s//\1"security": true,\n\1"url"/' "${d}/manifest.json"
grep -q '"security": true' "${d}/manifest.json" || selftest_failed "fixture: the nested security member was not planted."
resign "$d" "${keydir}/good.key"
expect_accept "a validly signed manifest with security true inside an artifact block" cli_release_verify_manifest "$d"
expect_false "security true inside an artifact block is not a security release" \
  cli_release_manifest_is_security "${d}/manifest.json"

d="$(security_case '  "security": true')"
expect_true "security true as a member without a trailing comma still reads as true" \
  cli_release_manifest_is_security "${d}/manifest.json"

d="$(security_case '  "security": false,')"
sed -i 's/"security": false/"security": true/' "${d}/manifest.json"
expect_refuse "a manifest whose security flag was flipped to true after signing" "signature|verif" \
  cli_release_verify_manifest "$d"
d="$(security_case '  "security": true,')"
sed -i 's/"security": true/"security": false/' "${d}/manifest.json"
expect_refuse "a manifest whose security flag was flipped to false after signing" "signature|verif" \
  cli_release_verify_manifest "$d"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 25: the major version - an unattended run never crosses one --"
# ---------------------------------------------------------------------------------------------
expect_equal "the major of 1.2.3" "1" "$(cli_version_major 1.2.3)"
expect_equal "the major of 12.0.1-beta.4" "12" "$(cli_version_major 12.0.1-beta.4)"
expect_true "1.2.3 and 1.9.0 share a major" cli_version_same_major 1.2.3 1.9.0
expect_true "1.0.0-beta.4 and 1.0.0 share a major" cli_version_same_major 1.0.0-beta.4 1.0.0
expect_true "0.9.0 and 0.9.1 share a major" cli_version_same_major 0.9.0 0.9.1
expect_false "1.9.9 and 2.0.0 do not share a major" cli_version_same_major 1.9.9 2.0.0
expect_false "10.0.0 and 1.0.0 do not share a major (not a prefix match)" cli_version_same_major 10.0.0 1.0.0
expect_false "1.0.0 and 11.0.0 do not share a major (not a prefix match)" cli_version_same_major 1.0.0 11.0.0
expect_refuse "the major of a legacy name" "not a SemVer" cli_version_major legacy-20261101T120000Z
expect_refuse "the major of a malformed version" "not a SemVer" cli_version_major "1.2"
expect_returns_refusal "same_major with a legacy installed name returns not-the-same" "not a SemVer" \
  cli_version_same_major 1.0.1 legacy-20261101T120000Z
expect_returns_refusal "same_major with an empty argument returns not-the-same" "not a SemVer" cli_version_same_major "" ""

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 26: the unattended decision and the memory of a failed release --"
# ---------------------------------------------------------------------------------------------
fresh_layout
sec_manifest="$(security_case '  "security": true,')/manifest.json"
plain_manifest="$(security_case '  "security": false,')/manifest.json"

# decide: <installed> <available> <manifest>. Runs the decision in THIS shell (its outcome is a global)
# and prints nothing; sets DECIDE_STATUS and leaves CLI_UPDATE_OUTCOME and DECIDE_OUT.
decide() {
  local out="${work}/decide.out"
  CLI_UPDATE_OUTCOME=""
  cli_unattended_decide "$@" > "$out" 2>&1 && DECIDE_STATUS=0 || DECIDE_STATUS=$?
  DECIDE_OUT="$(cat "$out")"
}

cli_unattended_clear_failure
decide 1.0.0 1.0.1 "$sec_manifest"
expect_equal "a security release of the same major is applicable (inverse control)" "0" "$DECIDE_STATUS"
expect_equal "an applicable release sets no outcome" "" "$CLI_UPDATE_OUTCOME"

decide 1.0.0 1.0.1 "$plain_manifest"
expect_equal "a release the manifest does not call a security release is left alone" "1" "$DECIDE_STATUS"
expect_equal "...with the outcome skipped-not-security" "skipped-not-security" "$CLI_UPDATE_OUTCOME"
case "$DECIDE_OUT" in *"No unattended update"*"left for 'maran update'"*) selftest_pass "...and says where it went" ;; *) selftest_failed "the skip said: ${DECIDE_OUT}" ;; esac

decide 1.4.2 2.0.0 "$sec_manifest"
expect_equal "a security release of another major is left alone" "1" "$DECIDE_STATUS"
expect_equal "...with the outcome skipped-major" "skipped-major" "$CLI_UPDATE_OUTCOME"

decide legacy-20261101T120000Z 1.0.1 "$sec_manifest"
expect_equal "an unversioned legacy install is left to the operator" "1" "$DECIDE_STATUS"
expect_equal "...with the outcome skipped-unversioned" "skipped-unversioned" "$CLI_UPDATE_OUTCOME"
decide "" 1.0.1 "$sec_manifest"
expect_equal "an empty installed version is left to the operator" "skipped-unversioned" "$CLI_UPDATE_OUTCOME"

# Order: not-security is reported before a major difference, so the most common skip is the one said.
decide 1.0.0 2.0.0 "$plain_manifest"
expect_equal "a non-security release of another major is reported as not-security" "skipped-not-security" "$CLI_UPDATE_OUTCOME"

# The failed-release memory.
expect_equal "no memory at first" "" "$(cli_unattended_failed_version)"
cli_unattended_remember_failure 1.0.1
expect_equal "a recorded failed release is read back" "1.0.1" "$(cli_unattended_failed_version)"
expect_equal "the memory is root-only (0600)" "600" "$(stat -c '%a' -- "$MARAN_UNATTENDED_FAILED_FILE")"
decide 1.0.0 1.0.1 "$sec_manifest"
expect_equal "a release that already failed here is not tried again" "skipped-failed-before" "$CLI_UPDATE_OUTCOME"
decide 1.0.0 1.0.2 "$sec_manifest"
expect_equal "a newer release is tried (inverse control for the memory)" "0" "$DECIDE_STATUS"
cli_unattended_clear_failure
decide 1.0.0 1.0.1 "$sec_manifest"
expect_equal "a cleared memory lets the release be tried again" "0" "$DECIDE_STATUS"

printf 'not a version\n' > "$MARAN_UNATTENDED_FAILED_FILE"
expect_equal "a garbled memory is no memory" "" "$(cli_unattended_failed_version)"
rm -f -- "$MARAN_UNATTENDED_FAILED_FILE"
printf '1.0.1\n' > "${work}/failed-target"
ln -s "${work}/failed-target" "$MARAN_UNATTENDED_FAILED_FILE"
expect_equal "a memory that is a symbolic link is ignored" "" "$(cli_unattended_failed_version)"
rm -f -- "$MARAN_UNATTENDED_FAILED_FILE"
cli_unattended_remember_failure "1.0.1;reboot"
[ ! -e "$MARAN_UNATTENDED_FAILED_FILE" ] || selftest_failed "a version that is not SemVer was recorded."
selftest_pass "a version that is not SemVer is not recorded"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 27: the flags that make a run unattended --"
# ---------------------------------------------------------------------------------------------
CLI_UPDATE_YES=0
cli_update_parse --security-only
expect_equal "--security-only alone is accepted and prompts" "1:0:0" "${CLI_UPDATE_SECURITY_ONLY}:${CLI_UPDATE_SCHEDULED}:${CLI_UPDATE_YES}"
cli_update_parse --security-only --yes
expect_equal "--security-only --yes is accepted" "1:0:1" "${CLI_UPDATE_SECURITY_ONLY}:${CLI_UPDATE_SCHEDULED}:${CLI_UPDATE_YES}"
cli_update_parse --security-only --scheduled
expect_equal "--security-only --scheduled is the timer's run and stands in for the prompt" "1:1:1" "${CLI_UPDATE_SECURITY_ONLY}:${CLI_UPDATE_SCHEDULED}:${CLI_UPDATE_YES}"
cli_update_parse --yes
expect_equal "an ordinary update stays interactive and unscheduled" "0:0:1" "${CLI_UPDATE_SECURITY_ONLY}:${CLI_UPDATE_SCHEDULED}:${CLI_UPDATE_YES}"
cli_update_parse --channel beta --security-only
expect_equal "--security-only takes a channel when run by hand" "beta" "$CLI_UPDATE_REQUESTED_CHANNEL"
expect_refuse "--scheduled without --security-only" "needs --security-only" cli_update_parse --scheduled
expect_refuse "--security-only with an offline bundle" "offline-tarball" cli_update_parse --security-only --offline-tarball /root/x.tar.gz
expect_refuse "--security-only with --check" "check" cli_update_parse --security-only --check
expect_refuse "--scheduled with --yes" "yes" cli_update_parse --security-only --scheduled --yes
expect_refuse "--scheduled with --channel" "channel" cli_update_parse --security-only --scheduled --channel beta
expect_refuse "--security-only twice" "twice" cli_update_parse --security-only --security-only
expect_refuse "--scheduled twice" "twice" cli_update_parse --security-only --scheduled --scheduled
expect_refuse "an unknown flag still ends in usage" "unknown argument" cli_update_parse --security-only --bogus

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 28: the report to the panel --"
# ---------------------------------------------------------------------------------------------
: > "$SEC_DOTNET_LOG"
: > "$SEC_AUDIT_LOG"
cli_unattended_report updated 1.0.0 1.0.1
expect_equal "an applied update is reported with its two releases" "report-update applied 1.0.0 1.0.1" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_unattended_report rolled-back 1.0.0 1.0.1
expect_equal "a rolled-back update is reported" "report-update rolled-back 1.0.0 1.0.1" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_unattended_report rollback-failed 1.0.0 1.0.1
expect_equal "a failed rollback is reported" "report-update rollback-failed 1.0.0 1.0.1" "$(tail -n 1 "$SEC_DOTNET_LOG")"
cli_unattended_report interrupted 1.0.0 1.0.1
expect_equal "an interrupted update is reported" "report-update interrupted 1.0.0 1.0.1" "$(tail -n 1 "$SEC_DOTNET_LOG")"
expect_equal "four reports made four calls" "4" "$(wc -l < "$SEC_DOTNET_LOG" | tr -d ' ')"
for outcome in "" refused check skipped-not-security skipped-major skipped-unversioned skipped-failed-before bogus; do
  cli_unattended_report "$outcome" 1.0.0 1.0.1
done
expect_equal "a run that changed nothing is not reported (eight outcomes, still four calls)" "4" "$(wc -l < "$SEC_DOTNET_LOG" | tr -d ' ')"
cli_unattended_report updated "" 1.0.1
cli_unattended_report updated 1.0.0 "1.0.1;x"
cli_unattended_report updated legacy-20261101T120000Z 1.0.1
expect_equal "a report whose releases are not SemVer is not sent" "4" "$(wc -l < "$SEC_DOTNET_LOG" | tr -d ' ')"
SEC_DOTNET_STATUS=1
cli_unattended_report rolled-back 1.0.0 1.0.1 && report_status=0 || report_status=$?
expect_equal "a report the panel cannot take never fails the run" "0" "$report_status"
expect_equal "...and leaves one fixed journal line" "update report-failed" "$(tail -n 1 "$SEC_AUDIT_LOG")"
SEC_DOTNET_STATUS=0

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 29: the shipped units --"
# ---------------------------------------------------------------------------------------------
unit_dir="${root}/installer/systemd"
service="${unit_dir}/maran-update-security.service"
timer="${unit_dir}/maran-update-security.timer"
[ -f "$service" ] && [ -f "$timer" ] || selftest_failed "the unit files are missing under installer/systemd"
expect_equal "the service runs one fixed argv" "ExecStart=/usr/local/sbin/maran update --security-only --scheduled" \
  "$(grep -E '^ExecStart=' "$service")"
expect_equal "the service has exactly one ExecStart" "1" "$(grep -cE '^ExecStart' "$service")"
expect_equal "the timer ships its own interval" "OnCalendar=daily" "$(grep -E '^OnCalendar=' "$timer")"
grep -qE '^RandomizedDelaySec=[0-9]+h$' "$timer" || selftest_failed "the timer has no randomized delay."
selftest_pass "the timer spreads a fleet with a randomized delay"
grep -qE '^Persistent=true$' "$timer" || selftest_failed "the timer does not catch up a missed run."
selftest_pass "the timer catches up a run a powered-off machine missed"
# The writable set must not hold anything the panel's service account can write.
writable="$(grep -E '^ReadWritePaths=' "$service" | sed 's/^ReadWritePaths=//')"
for path in $writable; do
  case "$path" in
    /var/lib/maran|/var/lib/maran/*|/var/log/maran*|/run/maran*) selftest_failed "the unit's ReadWritePaths names ${path}, which the panel's service account can write." ;;
  esac
done
for path in /usr/local/maran /etc/maran /var/backups/maran-update /var/lib/maran-cli-staging; do
  case " $writable " in
    *" ${path} "*) ;;
    *) selftest_failed "the unit's ReadWritePaths lacks ${path}, which the verb writes: ${writable}" ;;
  esac
done
selftest_pass "the unit's writable set holds the release tree, the state, the dumps and the staging area"
# A search that found nothing owes a positive control: the same scan on the backup unit finds its path.
grep -qE '^ReadWritePaths=.*/var/backups/maran-panel' "${unit_dir}/maran-panel-backup.service" \
  || selftest_failed "the control scan found no ReadWritePaths in the backup unit, so the scan above cannot hit."
selftest_pass "the ReadWritePaths scan hits the backup unit (the scan is capable of a hit)"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 30: the installer step - off after a first install, and the operator's choice stands --"
# ---------------------------------------------------------------------------------------------
# Step 69 is run from a COPY whose absolute paths point into the temporary root (the same rewrite the
# libraries get), with a recording systemctl in front of PATH. The step is the real file; only where it
# writes and what it asks systemd are replaced.
step_unit_dir="${mroot}/etc/systemd/system"
step_dump_dir="${mroot}/var/backups/maran-update"
step_copy="${work}/69-update-schedule.sh"
sed -e "s|/etc/systemd/system|${step_unit_dir}|g" -e "s|/var/backups/maran-update|${step_dump_dir}|g" \
  "${root}/installer/lib/69-update-schedule.sh" > "$step_copy"
grep -q "$step_unit_dir" "$step_copy" && grep -q "$step_dump_dir" "$step_copy" \
  || selftest_failed "the step copy was not rewritten into the temporary root."
grep -qE '(^|[^A-Za-z0-9._/-])(/etc/systemd/system|/var/backups/maran-update)' "$step_copy" \
  && selftest_failed "the step copy still names a real path."
step_shims="${work}/step-shims"
install -d "$step_shims"
STEP_SYSTEMCTL_LOG="${work}/step-systemctl.log"
cat > "${step_shims}/systemctl" <<SHIM
#!/usr/bin/env bash
printf '%s\n' "\$*" >> "${STEP_SYSTEMCTL_LOG}"
exit 0
SHIM
chmod 0755 "${step_shims}/systemctl"

# run_step_69: [VAR=value...]. Runs step_update_schedule in a child the way install.sh runs a step.
run_step_69() {
  run_capture env PATH="${step_shims}:${PATH}" SCRIPT_DIR="${root}/installer" LIB_DIR="${root}/installer/lib" "$@" \
    bash -c 'set -euo pipefail; . "$1"; step_update_schedule' _ "$step_copy"
}

rm -rf -- "$step_unit_dir" "$step_dump_dir"
install -d "$step_unit_dir"
: > "$STEP_SYSTEMCTL_LOG"
# Control: the shim records an enable call, so the empty answer below means the step made none.
PATH="${step_shims}:${PATH}" systemctl enable --now maran-update-security.timer
grep -qx 'enable --now maran-update-security.timer' "$STEP_SYSTEMCTL_LOG" || selftest_failed "the systemctl shim does not record an enable call, so 'not enabled' below proves nothing."
: > "$STEP_SYSTEMCTL_LOG"
run_step_69 MARAN_OFFLINE_TARBALL=
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "the step failed on a first online install: ${RUN_OUT}"
[ -f "${step_unit_dir}/maran-update-security.service" ] && [ -f "${step_unit_dir}/maran-update-security.timer" ] \
  || selftest_failed "the step did not install both units."
cmp -s "${root}/installer/systemd/maran-update-security.timer" "${step_unit_dir}/maran-update-security.timer" \
  || selftest_failed "the installed timer differs from the shipped one."
expect_equal "the units are installed as they ship, mode 0644" "644" "$(stat -c '%a' -- "${step_unit_dir}/maran-update-security.timer")"
expect_equal "the dump directory is created root-only" "700" "$(stat -c '%a' -- "$step_dump_dir")"
grep -q 'enable' "$STEP_SYSTEMCTL_LOG" && selftest_failed "a first online install enabled the timer; it must be off until the operator enables it: $(cat "$STEP_SYSTEMCTL_LOG")"
grep -qx 'daemon-reload' "$STEP_SYSTEMCTL_LOG" || selftest_failed "the first install did not reload systemd after installing the units."
selftest_pass "a first online install installs the units and does not enable the timer (off by default)"
case "$RUN_OUT" in *"installed but off"*"maran update-schedule enable"*) selftest_pass "...and says it is off and how to enable it" ;; *) selftest_failed "the first install did not tell the operator: ${RUN_OUT}" ;; esac

# A re-run with the unit file already present changes nothing about the enablement, in either state.
: > "$STEP_SYSTEMCTL_LOG"
run_step_69 MARAN_OFFLINE_TARBALL=
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "the step failed on a re-run: ${RUN_OUT}"
grep -q 'enable\|disable\|start\|stop' "$STEP_SYSTEMCTL_LOG" && selftest_failed "a re-run touched the timer's enablement, undoing the operator's choice: $(cat "$STEP_SYSTEMCTL_LOG")"
grep -qx 'daemon-reload' "$STEP_SYSTEMCTL_LOG" || selftest_failed "the re-run did not reload systemd after replacing the units."
selftest_pass "a re-run replaces the units and never touches the timer's enablement (an enable or a disable survives an installer run)"

# The operator has enabled the timer (what `systemctl enable` leaves: the wants link) and the installer is
# re-run: the link is still there and the step asked systemd for nothing but the reload. The inverse of
# the first install above, where nothing is enabled, so a step that enables or disables would fail one of
# the two.
wants_dir="${step_unit_dir}/timers.target.wants"
install -d "$wants_dir"
ln -sf "${step_unit_dir}/maran-update-security.timer" "${wants_dir}/maran-update-security.timer"
: > "$STEP_SYSTEMCTL_LOG"
run_step_69 MARAN_OFFLINE_TARBALL=
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "the step failed on a re-run over an enabled timer: ${RUN_OUT}"
[ -L "${wants_dir}/maran-update-security.timer" ] \
  || selftest_failed "a re-run over an enabled timer removed the operator's enablement."
grep -q 'enable\|disable\|start\|stop' "$STEP_SYSTEMCTL_LOG" \
  && selftest_failed "a re-run over an enabled timer touched its enablement: $(cat "$STEP_SYSTEMCTL_LOG")"
grep -qx 'daemon-reload' "$STEP_SYSTEMCTL_LOG" || selftest_failed "the re-run over an enabled timer did not reload systemd."
selftest_pass "an operator-enabled timer stays enabled when the installer is re-run"
rm -rf -- "$wants_dir"

# An offline install behaves the same as an online one: fresh units, no enablement, the same line.
rm -rf -- "$step_unit_dir"
install -d "$step_unit_dir"
: > "$STEP_SYSTEMCTL_LOG"
run_step_69 MARAN_OFFLINE_TARBALL=/root/maran-offline.tar.gz
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "the step failed on an offline install: ${RUN_OUT}"
grep -q 'enable' "$STEP_SYSTEMCTL_LOG" && selftest_failed "an offline install enabled a timer that cannot reach the release host."
[ -f "${step_unit_dir}/maran-update-security.timer" ] || selftest_failed "an offline install did not install the units."
case "$RUN_OUT" in *"installed but off"*"update-schedule enable"*) selftest_pass "an offline install installs the units and leaves the timer off, saying how to enable it" ;; *) selftest_failed "the offline install did not say so: ${RUN_OUT}" ;; esac

# A dump directory that is a symbolic link is refused before anything is written (root's dumps must not follow it).
rm -rf -- "$step_unit_dir" "$step_dump_dir"
install -d "$step_unit_dir"
ln -s "${work}" "$step_dump_dir"
: > "$STEP_SYSTEMCTL_LOG"
run_step_69 MARAN_OFFLINE_TARBALL=
[ "$RUN_STATUS" -ne 0 ] || selftest_failed "the step accepted a symbolic link as the dump directory."
case "$RUN_OUT" in *"symbolic link"*) selftest_pass "a symbolic link in place of the dump directory is refused" ;; *) selftest_failed "the link was refused for another reason: ${RUN_OUT}" ;; esac
[ ! -e "${step_unit_dir}/maran-update-security.timer" ] || selftest_failed "units were installed before the refusal."
rm -f -- "$step_dump_dir"

echo "UNOBSERVED HERE: cli_update's own order in --security-only mode (the decision before the floors, the prompt and the download) and the report from its exit trap - they need root and systemd."
echo "UNOBSERVED HERE: the timer firing, Persistent catch-up and the unit under its sandbox on a real host."
