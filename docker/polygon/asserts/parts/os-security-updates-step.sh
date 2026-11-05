#!/usr/bin/env bash
# the opt-in unattended OS security updates (issue #163): the assertions that RUN step 68
#
# Part of docker/polygon/asserts/assert-installer-steps.sh. Sourced, never executed. The helpers,
# the constants and the checkers these assertions use are in os-security-updates.sh, which is
# sourced first; read its header for the four properties and for what a green run does not mean.

# os_updates_assert_nothing_written: the host as it was before step 68, observed five ways. $1 names
# the run that has just finished, for the diagnosis. Each observation is shown able to see a change
# by its caller (the flag-on assertion is their inverse control) or by the planted controls at the
# top of the refusing assertion.
os_updates_assert_nothing_written() {
  local run="$1" timer record
  if [ "$OS_UPDATES_HAD_CONFIG" -eq 1 ]; then
    cmp -s "$OS_UPDATES_SAVED_CONFIG" "$OS_UPDATES_CONFIG" \
      || fail "step 68 (${run}) changed ${OS_UPDATES_CONFIG}. Without the opt-in it must write nothing"
  else
    [ ! -e "$OS_UPDATES_CONFIG" ] && [ ! -L "$OS_UPDATES_CONFIG" ] \
      || fail "step 68 (${run}) wrote ${OS_UPDATES_CONFIG}. Without the opt-in it must write nothing"
  fi
  [ -z "$(os_updates_previous_copies)" ] \
    || fail "step 68 (${run}) kept a copy at $(os_updates_previous_copies | head -1), which means it replaced a file"
  for timer in "${OS_UPDATES_TIMERS[@]}"; do
    record="$(os_updates_timer_record "$timer")"
    [ "$record" = none ] \
      || fail "step 68 (${run}) asked the polygon's systemctl to enable ${timer} (record: ${record}). Without the opt-in no timer is enabled"
  done
  if os_updates_package_installed; then
    [ "$OS_UPDATES_HAD_PACKAGE" -eq 1 ] \
      || fail "step 68 (${run}) installed the distribution's unattended-update package. Without the opt-in it installs nothing"
  elif [ "$OS_UPDATES_HAD_PACKAGE" -eq 1 ]; then
    fail "the distribution's unattended-update package disappeared during step 68 (${run})"
  fi
  [ "$(os_updates_systemd_listing)" = "$OS_UPDATES_UNIT_LISTING" ] \
    || fail "step 68 (${run}) changed /etc/systemd/system. The step owns no unit"
}

# os_updates_assert_timers_enabled: every timer of this family was asked to be enabled, as far as
# the polygon's systemctl can say.
#
# The step enables its timers in ONE call (`systemctl enable --now a b`), and the stand-in's enable
# arm records only the first unit of a call. So a missing record for a later timer is either a step
# that did not ask or a stand-in that did not write it, and the two cannot be told apart from the
# record alone. The control runs the same call shape against two units no step ever names: when the
# stand-in records both, a missing record is a failure; when it records only the first, the later
# timers are printed as UNOBSERVED and the first timer's record is still required. Never a silent
# pass: the unobserved list is returned in OS_UPDATES_UNOBSERVED_TIMERS and printed by the caller.
OS_UPDATES_UNOBSERVED_TIMERS=""
os_updates_assert_timers_enabled() {
  local timer first="${OS_UPDATES_TIMERS[0]}" control_a="maran-polygon-control-a.timer" control_b="maran-polygon-control-b.timer"
  local records_every_unit=0 record
  OS_UPDATES_UNOBSERVED_TIMERS=""

  systemctl enable --now "$control_a" "$control_b" >/dev/null \
    || fail "the polygon's systemctl refused 'enable --now ${control_a} ${control_b}', so the multi-unit control cannot be set up"
  [ "$(os_updates_timer_record "$control_a")" = enabled ] \
    || fail "the polygon's systemctl enabled ${control_a} and recorded nothing, so no enablement record below means anything"
  [ "$(os_updates_timer_record "$control_b")" = enabled ] && records_every_unit=1
  rm -f -- "${UNIT_STATE_DIRECTORY}/${control_a}" "${UNIT_STATE_DIRECTORY}/${control_a}.enabled" \
    "${UNIT_STATE_DIRECTORY}/${control_b}" "${UNIT_STATE_DIRECTORY}/${control_b}.enabled"

  for timer in "${OS_UPDATES_TIMERS[@]}"; do
    record="$(os_updates_timer_record "$timer")"
    [ "$record" = enabled ] && continue
    if [ "$timer" != "$first" ] && [ "$records_every_unit" -eq 0 ]; then
      OS_UPDATES_UNOBSERVED_TIMERS="${OS_UPDATES_UNOBSERVED_TIMERS:+${OS_UPDATES_UNOBSERVED_TIMERS} }${timer}"
      continue
    fi
    fail "step 68 with the flag on did not ask the polygon's systemctl to enable ${timer} (record: ${record}). The distribution's timer is what runs the job"
  done
}

# assert_the_os_security_updates_step_changes_nothing_without_the_flag: step 68 run with the flag
# off, with the variable not set at all, and with a value that is neither word, and the host looked
# at.
#
# The refusing half of a pair; its inverse control is the next assertion, where the same step with
# the flag on writes the file. A gate mutated to "skip always" passes this one and fails that one;
# a step that ignored the flag passes that one and fails this one.
#
# "Nothing" is five observations (os_updates_assert_nothing_written): no config file (a stat of the
# exact path the step writes), no kept copy, no enablement recorded for either timer (the record is
# shown to exist for an enabled timer first), the package database unchanged, and no new name under
# /etc/systemd/system (the listing is shown to see a planted file).
#
# UNOBSERVED HERE: this image boots no systemd, so "no timer enabled" is "the polygon's systemctl
# was not asked to enable one". A distribution that ships its timers enabled by default is not
# something this reads: that is the distribution's own state and not the step's.
assert_the_os_security_updates_step_changes_nothing_without_the_flag() {
  local flag output timer planted

  os_updates_select_family
  os_updates_note_prior_state

  # Positive controls for the two observations that read a record and a listing.
  timer="${OS_UPDATES_TIMERS[0]}"
  systemctl enable --now -- "$timer" >/dev/null \
    || fail "the polygon's systemctl refused 'enable --now -- ${timer}', so the enablement control below cannot be set up"
  [ "$(os_updates_timer_record "$timer")" = enabled ] \
    || fail "the polygon's systemctl enabled ${timer} and the record reader does not see it, so 'no timer was enabled' below would say nothing"
  rm -f -- "${UNIT_STATE_DIRECTORY}/${timer}" "${UNIT_STATE_DIRECTORY}/${timer}.enabled"
  planted="/etc/systemd/system/maran-polygon-os-updates-control.service"
  : > "$planted"
  [ "$(os_updates_systemd_listing)" != "$OS_UPDATES_UNIT_LISTING" ] \
    || { rm -f -- "$planted"; fail "the /etc/systemd/system listing does not see a planted unit file, so 'the step wrote no unit' below would say nothing"; }
  rm -f -- "$planted"

  for flag in unset no; do
    output="$(run_os_updates_step "$flag" 2>&1)" \
      || fail "step 68 failed with the flag ${flag}: ${output}. An opt-in step that is off must be a clean no-op, not a failure the default install would inherit"
    case "$output" in
      *"$OS_UPDATES_SKIP_SENTENCE"*) ;;
      *) fail "step 68 ran with the flag ${flag} but did not print '${OS_UPDATES_SKIP_SENTENCE}': ${output}" ;;
    esac
    os_updates_assert_nothing_written "the flag ${flag}"
  done

  # A value that is neither word is refused, and refused BEFORE anything is written: a typo in an
  # opt-in must not turn into either answer.
  if output="$(run_os_updates_step maybe 2>&1)"; then
    fail "step 68 ACCEPTED MARAN_OS_SECURITY_UPDATES=maybe. The flag is yes or no; a step that guesses can enable an unattended root job on a typo: ${output}"
  fi
  os_updates_assert_nothing_written "the flag maybe"

  os_updates_restore_prior_state
  echo "Step 68 with MARAN_OS_SECURITY_UPDATES unset and with it 'no' printed '${OS_UPDATES_SKIP_SENTENCE}', and with 'maybe' refused; none of the three changed ${OS_UPDATES_CONFIG}, kept a copy, installed the package, asked for a timer to be enabled or added a name under /etc/systemd/system."
  echo "UNOBSERVED HERE: this image boots no systemd, so 'no timer enabled' means the polygon's systemctl was never asked to enable one; whether the distribution ships its own timers enabled is its own state and is not read."
}

# assert_the_os_security_updates_step_writes_the_exact_configuration_when_asked: step 68 run with
# the flag on, and what it left looked at; the accepting counterpart of the assertion above.
#
# What is looked at, each on the artefact the system reads under the name it reads it: the file is
# root:root 0644 and a regular file; the text checker passes on it and is shown to refuse planted
# breaks; on the Debian family apt's own merged answer (`apt-config dump`) passes and is shown to
# fail without the file; the timers were asked to be enabled; the directory holds no temporary
# leftover beside the target; a second run converges on the same bytes; a file of the step's own
# that somebody edited is replaced without a copy, and a file that is NOT the step's is replaced
# only after a root-only copy is kept, never over the first copy.
#
# UNOBSERVED HERE: a real security run. The image has no reachable security channel with pending
# updates, so that the job applies only security updates and skips the held packages is read from
# the package manager's documentation and seen nowhere. "Enabled" is "the polygon's systemctl was
# asked", the stand-in's documented limit: nothing here reboots or starts a timer. On the RHEL
# family nothing parses the file but this script's reader: dnf-automatic has no offline validator.
assert_the_os_security_updates_step_writes_the_exact_configuration_when_asked() {
  local output first work leftover edited defect foreign_one foreign_two copy

  os_updates_select_family
  os_updates_note_prior_state
  first="$(mktemp)"
  work="$(mktemp -d)"

  output="$(run_os_updates_step yes 2>&1)" \
    || fail "step 68's step_os_updates failed with the flag on in this image: ${output}"
  case "$output" in
    *"$OS_UPDATES_BLIND_SPOT_SENTENCE"*) ;;
    *) fail "step 68 ran with the flag on but did not tell the operator '${OS_UPDATES_BLIND_SPOT_SENTENCE}'. A step that implies a check it did not make is the shape rules/testing.md names: ${output}" ;;
  esac

  assert_file_is "$OS_UPDATES_CONFIG" "0:0:644"
  cp -p -- "$OS_UPDATES_CONFIG" "$first"
  grep -qF -- "$OS_UPDATES_MARKER" "$first" \
    || fail "${OS_UPDATES_CONFIG} does not carry the line '${OS_UPDATES_MARKER}', which is how a later run tells its own render from a file it must keep a copy of"
  # uninstall.sh spells the marker itself (it sources nothing from the installer) and removes a file
  # only when it carries it, so the two spellings must agree or the uninstaller leaves the file.
  grep -qF -- "local marker=\"${OS_UPDATES_MARKER}\"" "$UNINSTALLER" \
    || fail "${UNINSTALLER} does not spell the marker '${OS_UPDATES_MARKER}' that step 68 writes, so it would never remove the file the step wrote"
  os_updates_package_installed \
    || fail "step 68 ran with the flag on and the distribution's unattended-update package is not installed"

  os_updates_prove_the_text_checker_refuses
  if [ "$MARAN_OS_FAMILY" = debian ]; then
    defect="$(os_updates_debian_dump_defect)"
    [ -z "$defect" ] \
      || fail "the effective apt configuration after step 68 is wrong: ${defect}. The file is present and reads right as text, and apt does not agree: a later or earlier file in /etc/apt/apt.conf.d wins"
    os_updates_prove_the_dump_reader_can_see
  fi
  os_updates_assert_timers_enabled

  # Nothing of the step's own beside the target: no temporary file left by an interrupted or
  # careless write, and no unit. The find is shown to see a planted leftover first.
  leftover="${OS_UPDATES_CONFIG}.tmp-polygon-control"
  : > "$leftover"
  [ -n "$(find "${OS_UPDATES_CONFIG%/*}" -maxdepth 1 -name "${OS_UPDATES_CONFIG##*/}*" -name '*tmp-polygon-control' 2>/dev/null)" ] \
    || { rm -f -- "$leftover"; fail "the leftover search does not find a planted file beside ${OS_UPDATES_CONFIG}, so its empty answer below means nothing"; }
  rm -f -- "$leftover"
  leftover="$(find "${OS_UPDATES_CONFIG%/*}" -maxdepth 1 -name "${OS_UPDATES_CONFIG##*/}*" ! -name "${OS_UPDATES_CONFIG##*/}" ! -name '*.maran-previous*' 2>/dev/null | head -1 || true)"
  [ -z "$leftover" ] \
    || fail "${leftover} was left beside ${OS_UPDATES_CONFIG} by step 68. The write is a temporary file renamed over the target, and nothing may remain of it"
  [ "$(os_updates_systemd_listing)" = "$OS_UPDATES_UNIT_LISTING" ] \
    || fail "step 68 changed /etc/systemd/system. It owns no unit: the distribution's timers are the only thing it enables"

  # A second run converges on the same bytes and the same owner and mode.
  output="$(run_os_updates_step yes 2>&1)" \
    || fail "step 68 failed on its second run (installers get re-run): ${output}"
  assert_file_is "$OS_UPDATES_CONFIG" "0:0:644"
  cmp -s "$first" "$OS_UPDATES_CONFIG" \
    || fail "step 68's second run left ${OS_UPDATES_CONFIG} with different bytes than its first. The file is rendered whole every time, never appended to"

  # A file of the step's own that somebody edited (the marker line is still in it) is replaced by the
  # render, whole, with no copy kept: it is the step's file, and an append is not preserved. Any copy
  # the first run kept of a file the package shipped is cleared first, so a copy found afterwards is
  # this run's.
  while IFS= read -r copy; do
    [ -z "$copy" ] || rm -f -- "$copy"
  done < <(os_updates_previous_copies)
  edited="${work}/edited"
  sed '$a# polygon: a line a hand added' "$OS_UPDATES_CONFIG" > "$edited"
  cp -p -- "$edited" "$OS_UPDATES_CONFIG"
  output="$(run_os_updates_step yes 2>&1)" \
    || fail "step 68 failed over an edited ${OS_UPDATES_CONFIG}: ${output}"
  assert_file_is "$OS_UPDATES_CONFIG" "0:0:644"
  cmp -s "$first" "$OS_UPDATES_CONFIG" \
    || fail "step 68 over an edited ${OS_UPDATES_CONFIG} did not return it to the render: the step appended to, or merged with, the file"
  [ -z "$(os_updates_previous_copies)" ] \
    || fail "step 68 kept a copy of its own earlier render ($(os_updates_previous_copies | head -1)). A copy is for a file that is NOT the step's"

  # A file that is NOT the step's (no marker) is replaced only after a root-only copy is kept, and
  # the step says where. This is the inverse control of the assertion just above: that one requires
  # no copy for the step's own file, this one requires a copy for somebody else's.
  foreign_one="${work}/foreign-one"
  foreign_two="${work}/foreign-two"
  printf '// not written by Maran: first\nUnattended-Upgrade::Automatic-Reboot "true";\n' > "$foreign_one"
  printf '// not written by Maran: second\nUnattended-Upgrade::Automatic-Reboot "true";\n' > "$foreign_two"
  cp -- "$foreign_one" "$OS_UPDATES_CONFIG"
  output="$(run_os_updates_step yes 2>&1)" \
    || fail "step 68 failed over a file it did not write: ${output}"
  case "$output" in
    *"maran-previous"*) ;;
    *) fail "step 68 replaced ${OS_UPDATES_CONFIG}, which it did not write, without saying where the previous bytes were kept: ${output}" ;;
  esac
  assert_file_is "$OS_UPDATES_PREVIOUS" "0:0:600"
  cmp -s "$foreign_one" "$OS_UPDATES_PREVIOUS" \
    || fail "${OS_UPDATES_PREVIOUS} does not hold the bytes the step replaced"
  assert_file_is "$OS_UPDATES_CONFIG" "0:0:644"
  cmp -s "$first" "$OS_UPDATES_CONFIG" \
    || fail "step 68 over a file it did not write did not leave the render at ${OS_UPDATES_CONFIG}"
  # And the first copy survives a second foreign file: a copy is never overwritten, the later one is
  # stamped, so the first file the operator had outlives every re-run.
  cp -- "$foreign_two" "$OS_UPDATES_CONFIG"
  output="$(run_os_updates_step yes 2>&1)" \
    || fail "step 68 failed over a second file it did not write: ${output}"
  cmp -s "$foreign_one" "$OS_UPDATES_PREVIOUS" \
    || fail "${OS_UPDATES_PREVIOUS} no longer holds the FIRST file the operator had: a later copy overwrote it"
  [ "$(os_updates_previous_copies | grep -c . || true)" -eq 2 ] \
    || fail "step 68 kept $(os_updates_previous_copies | grep -c . || true) copies after two foreign files, not two: $(os_updates_previous_copies | tr '\n' ' ')"
  cmp -s "$first" "$OS_UPDATES_CONFIG" \
    || fail "step 68 over a second foreign file did not leave the render at ${OS_UPDATES_CONFIG}"

  rm -rf -- "$work"
  rm -f -- "$first"
  os_updates_restore_prior_state
  echo "Step 68 with the flag on left ${OS_UPDATES_CONFIG} root:root 0644 and a regular file, passing the family's text checker (refused all $((OS_UPDATES_PLANTED_COUNT - 2)) planted breaks and accepted the 2 commented-out inverse controls), with no leftover beside it and no new name under /etc/systemd/system. A second run and a run over an edited render converged on identical bytes with no copy kept; a file that was not the step's was kept at ${OS_UPDATES_PREVIOUS} (root, 0600) before it was replaced, and a second such file did not overwrite the first copy."
  if [ "$MARAN_OS_FAMILY" = debian ]; then
    echo "apt-config dump agrees: no Allowed-Origins entry, exactly one Origins-Pattern, a Package-Blacklist equal to the hold list, Automatic-Reboot \"false\"; and with the file moved aside the same reader sees the distribution's own origins, so its answer is not a blind one."
  fi
  echo "Timers asked to be enabled through the polygon's systemctl: ${OS_UPDATES_TIMERS[*]}."
  if [ -n "$OS_UPDATES_UNOBSERVED_TIMERS" ]; then
    echo "UNOBSERVED HERE: ${OS_UPDATES_UNOBSERVED_TIMERS}. The step enables its timers in one call and the polygon's systemctl records only the first unit of a call (a control on two units no step names shows it), so a record for a later timer does not exist to be read."
  fi
  echo "${OS_UPDATES_BLIND_SPOT_SENTENCE}. The image has no reachable security channel with pending updates, so that the unattended job applies only security updates and skips the held packages is read from the package manager's documentation and seen nowhere here."
  echo "UNOBSERVED HERE: this image boots no systemd; 'enabled' means the polygon's systemctl was asked to enable the unit, not that systemd would start it. The hold list is a policy about WHEN a package changes, never a mitigation; and on the RHEL family nothing here read the file with the tool that consumes it."
}

# os_updates_run_function: one function of step 68 in a child, with the shared helpers and the step
# sourced, and its arguments. $4 onwards are the function and its arguments.
os_updates_run_function() {
  bash -c 'set -euo pipefail
. "$1"
. "$2"
. "$3"
shift 3
"$@"' _ "$COMMON_LIB" "$DEPENDENCIES_STEP" "$OS_UPDATES_STEP" "$@"
}

# os_updates_run_hold_patterns: os_updates_hold_patterns with MARAN_OS_UPDATE_HOLD set to $1 AFTER
# 00-common.sh has set it, which is the only order in which an override can take.
os_updates_run_hold_patterns() {
  bash -c 'set -euo pipefail
. "$1"
. "$2"
. "$3"
MARAN_OS_UPDATE_HOLD="$4"
os_updates_hold_patterns' _ "$COMMON_LIB" "$DEPENDENCIES_STEP" "$OS_UPDATES_STEP" "$1"
}

# assert_the_os_updates_step_refuses_a_codename_or_a_hold_pattern_that_could_inject_a_line: the two
# values that reach a rendered file and are not literals, each shown to refuse what could break out
# of its line and to accept what is real.
#
# The codename is read from the host's own /etc/os-release on the Debian family, and the hold list
# is a constant in 00-common.sh, so neither is caller-supplied (rules/security.md item 4). Both are
# still checked against a positive shape, and this proves the shape: a newline, a quote, a
# semicolon, a space, a capital or nothing at all refused; `noble`, `bookworm` and `trixie` and the
# real hold list accepted, the inverse control without which a refuse-everything shape would pass.
# The real hold list is also compared with this part's own copy of it, so that 00-common.sh's list
# cannot grow or shrink without a reviewed change here.
#
# UNOBSERVED HERE: the render itself, fed a hostile /etc/os-release. The step reads that file by its
# absolute path, and rewriting it in an image is not something this build does; what is proved is
# the function the render asks, not the render's use of the answer.
assert_the_os_updates_step_refuses_a_codename_or_a_hold_pattern_that_could_inject_a_line() {
  local value refused=0 accepted=0 output expected

  for value in $'noble\nUnattended-Upgrade::Automatic-Reboot "true";' 'no"ble' 'noble;' 'no ble' 'Noble' 'noble-security' ''; do
    if os_updates_run_function os_updates_codename_is_valid "$value" >/dev/null 2>&1; then
      fail "os_updates_codename_is_valid ACCEPTED $(printf '%q' "$value"), which would reach a configuration file as part of a line"
    fi
    refused=$((refused + 1))
  done
  for value in noble bookworm trixie jammy; do
    os_updates_run_function os_updates_codename_is_valid "$value" >/dev/null 2>&1 \
      || fail "os_updates_codename_is_valid REFUSED the real codename '${value}', so its refusals above come from a shape that refuses everything"
    accepted=$((accepted + 1))
  done

  for value in 'php*;' 'Php*' '*' 'a"b*' 'php**' 'nginx* php*;x' '-php' ''; do
    if os_updates_run_hold_patterns "$value" >/dev/null 2>&1; then
      fail "os_updates_hold_patterns ACCEPTED the hold list $(printf '%q' "$value"), which would reach a configuration file"
    fi
    refused=$((refused + 1))
  done
  expected="$(printf '%s\n' "${OS_UPDATES_HOLD_EXPECTED[@]}")"
  output="$(os_updates_run_hold_patterns "${OS_UPDATES_HOLD_EXPECTED[*]}" 2>&1)" \
    || fail "os_updates_hold_patterns REFUSED this part's own hold list: ${output}"
  [ "$output" = "$expected" ] \
    || fail "os_updates_hold_patterns answered [$(tr '\n' ' ' <<<"$output")] for the hold list [$(tr '\n' ' ' <<<"$expected")]"
  output="$(os_updates_run_function bash -c '. "$1"; printf "%s\n" "$MARAN_OS_UPDATE_HOLD"' _ "$COMMON_LIB" 2>&1)" \
    || fail "could not read MARAN_OS_UPDATE_HOLD out of ${COMMON_LIB}: ${output}"
  [ "$output" = "${OS_UPDATES_HOLD_EXPECTED[*]}" ] \
    || fail "MARAN_OS_UPDATE_HOLD in 00-common.sh is '${output}', not '${OS_UPDATES_HOLD_EXPECTED[*]}'. The hold list changed without this part's copy of it: edit both, in one reviewed change"
  accepted=$((accepted + 1))

  echo "The codename shape refused ${refused} values that could break out of a line (a newline plus a directive, a quote, a semicolon, a space, a capital, a hyphen, nothing) and accepted 4 real codenames; the hold-list shape refused the hostile lists above and accepted the real one, which equals this part's own copy of 00-common.sh's MARAN_OS_UPDATE_HOLD (${accepted} acceptances in all)."
  echo "UNOBSERVED HERE: the render fed a hostile /etc/os-release. The step reads that file by its absolute path and this build does not rewrite it; what is proved is the shape function the render asks."
}
