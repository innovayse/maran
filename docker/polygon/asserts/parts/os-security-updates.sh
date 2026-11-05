#!/usr/bin/env bash
# the opt-in unattended OS security updates (issue #163): the helpers and the checkers; the assertions
# that run step 68 are in os-security-updates-step.sh
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The subject. With `--os-security-updates` the installer installs the distribution's own
# unattended-update package, writes ONE root-owned configuration file and enables the timers the
# distribution already ships; a root job then installs security updates on a schedule with nobody
# reading the list first (docs/superpowers/notes/2026-11-04-os-security-updates-threat-note.md).
# Four properties are worth a gate, because each one fails silently:
#
#   1. Without the flag the step changes NOTHING: no file, no package, no timer, no unit. The
#      default install must not gain an unattended root job it did not have before.
#   2. With the flag the file is root:root 0644, carries exactly the keys the design fixes, and
#      the hold list is the one list (database engines, web server, PHP, FTP daemon).
#   3. The Debian family's file must WIN: the distribution's own 50unattended-upgrades allows
#      several origins, so a file that does not clear them leaves every update allowed beside ours.
#      That is a property of the EFFECTIVE configuration, so it is read with `apt-config dump`,
#      the tool that answers "what does apt think", and not from the file's text.
#   4. The distribution's timer is enabled, and the step wrote no unit of its own.
#
# Every refusal here is paired with the acceptance that proves it can say yes: the flag-off runs
# are the refusals and the flag-on run is their inverse control; the text checkers are shown to
# refuse copies of the real file with ONE property broken and to ACCEPT copies that differ only in
# a comment; the `apt-config dump` reader is shown to see the distribution's origins when the file
# is moved aside (rules/testing.md "A check must be able to observe what it reports on").
#
# What a green run here does NOT mean, said once and repeated in the output: nothing here ran a
# real security run. The image has no reachable security channel with pending updates and boots no
# systemd, so "the job applies only security updates and skips the held packages" is read from the
# package manager's documentation and observed nowhere.

# The two files the step may write. Literals on purpose, the arrangement the directory modes
# follow: an assertion that read them out of 68-os-updates.sh would agree with it by construction.
# The copy the step keeps of a file that is not its own is `<file>.maran-previous` (root, 0600), with
# a UTC stamp appended when that name is taken; see OS_UPDATES_PREVIOUS below.
readonly OS_UPDATES_DEBIAN_CONFIG="/etc/apt/apt.conf.d/52maran-os-security-updates"
readonly OS_UPDATES_RHEL_CONFIG="/etc/dnf/automatic.conf"

# The line every file the step renders carries, in the file's own comment syntax. It is how the step
# tells its own earlier render from a file it must keep a copy of before replacing.
readonly OS_UPDATES_MARKER="Maran-managed by installer/lib/68-os-updates.sh"

# The sentence the step prints when the flag is off. A literal here: the operator reads it.
readonly OS_UPDATES_SKIP_SENTENCE="skipped (opt-in: --os-security-updates)"

# The blind spot the RHEL family's step must name, because it has no offline validator.
readonly OS_UPDATES_BLIND_SPOT_SENTENCE="UNOBSERVED HERE: a real security run"

# The hold list, spelled out here and not read from MARAN_OS_UPDATE_HOLD: editing the list in the
# installer cannot also edit its check. A list that grows is a decision, and these arrays are where
# the decision is made visible. Quoted, because a bare `php*` is a glob.
#
# Two spellings of ONE list, because the two package managers read it differently. dnf takes the
# glob as written, under `exclude=`. apt's Package-Blacklist is a list of regular expressions
# matched with `re.match`, so the step anchors each pattern and turns its `*` into `.*`; a glob left
# as it is would hold every name that merely begins `ph`.
readonly -a OS_UPDATES_HOLD_EXPECTED=('mariadb*' 'mysql*' 'postgresql*' 'nginx*' 'php*' 'vsftpd*')
readonly -a OS_UPDATES_APT_BLACKLIST_EXPECTED=('^mariadb.*' '^mysql.*' '^postgresql.*' '^nginx.*' '^php.*' '^vsftpd.*')

# The family under test and what it writes, filled in by os_updates_select_family. Globals because
# a function called through `$(...)` could not set them; every caller invokes it directly.
OS_UPDATES_CONFIG=""
OS_UPDATES_PREVIOUS=""
declare -a OS_UPDATES_TIMERS=()

# How many planted controls the text checker ran, set by os_updates_prove_the_text_checker_refuses.
OS_UPDATES_PLANTED_COUNT=0

# Prior state, filled in by os_updates_note_prior_state and read by os_updates_restore_prior_state.
OS_UPDATES_HAD_CONFIG=0
OS_UPDATES_SAVED_CONFIG=""
OS_UPDATES_HAD_PACKAGE=0
OS_UPDATES_UNIT_LISTING=""

# os_updates_select_family: the config path and the distribution's timers for this image's family.
# A family this part does not know is a failure and never a skip: a part that silently asserted
# nothing on a third family would be a gate over nothing.
os_updates_select_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian)
      OS_UPDATES_CONFIG="$OS_UPDATES_DEBIAN_CONFIG"
      OS_UPDATES_PREVIOUS="${OS_UPDATES_DEBIAN_CONFIG}.maran-previous"
      OS_UPDATES_TIMERS=(apt-daily.timer apt-daily-upgrade.timer)
      ;;
    rhel)
      OS_UPDATES_CONFIG="$OS_UPDATES_RHEL_CONFIG"
      OS_UPDATES_PREVIOUS="${OS_UPDATES_RHEL_CONFIG}.maran-previous"
      OS_UPDATES_TIMERS=(dnf-automatic-install.timer)
      ;;
    *) fail "the OS security updates assertions know the families debian and rhel, and MARAN_OS_FAMILY is '${MARAN_OS_FAMILY:-unset}'. A family this part cannot name is a family it asserts nothing about" ;;
  esac
}

# run_os_updates_step: step 68's own step_os_updates, run the way install.sh runs a step (a child
# with errexit set, the shared helpers and the package-manager adapter sourced, SCRIPT_DIR and
# LIB_DIR naming the installer package). $1 is the flag's value, or the word `unset` to leave
# MARAN_OS_SECURITY_UPDATES out of the child's environment altogether, which is what the step sees
# when nothing exported the default.
run_os_updates_step() {
  local flag="$1"
  local -a environment=("SCRIPT_DIR=${INSTALLER_ROOT}" "LIB_DIR=${INSTALLER_LIB}")
  if [ "$flag" != unset ]; then
    environment+=("MARAN_OS_SECURITY_UPDATES=${flag}")
  fi
  env -u MARAN_OS_SECURITY_UPDATES "${environment[@]}" bash -c 'set -euo pipefail
. "$1"
. "$2"
. "$3"
step_os_updates' _ "$COMMON_LIB" "$DEPENDENCIES_STEP" "$OS_UPDATES_STEP"
}

# os_updates_package_installed: whether the distribution's unattended-update package is on this
# image. Asked of the package database, the one place that knows.
os_updates_package_installed() {
  local status
  case "$MARAN_OS_FAMILY" in
    debian)
      status="$(dpkg-query -W -f='${Status}' unattended-upgrades 2>/dev/null || true)"
      case "$status" in *"install ok installed"*) return 0 ;; esac
      return 1
      ;;
    rhel) rpm -q dnf-automatic >/dev/null 2>&1 ;;
  esac
}

# os_updates_timer_record: what the polygon's systemctl recorded when it was asked to enable $1, or
# `none` when it was never asked. NOT `systemctl is-enabled`: the stand-in answers `enabled` for a
# unit it has no record of (its DEFAULT_ENABLEMENT, because a container's missing init must not
# turn every unit into a failure), so is-enabled would say yes with the step never having run. The
# record file is written by `enable` and by nothing else.
os_updates_timer_record() {
  local record="${UNIT_STATE_DIRECTORY}/${1}.enabled"
  if [ -f "$record" ]; then head -1 "$record"; else printf 'none'; fi
}

# os_updates_systemd_listing: the names directly under /etc/systemd/system, sorted. The step writes
# no unit of its own, and a listing taken before and after is how that is observed.
os_updates_systemd_listing() {
  ls -A -- /etc/systemd/system 2>/dev/null | sort || true
}

# os_updates_previous_copies: every copy the step may have kept of the target, one path per line:
# `<file>.maran-previous` and the stamped names it falls back to.
os_updates_previous_copies() {
  find "${OS_UPDATES_CONFIG%/*}" -maxdepth 1 -name "${OS_UPDATES_CONFIG##*/}.maran-previous*" 2>/dev/null | sort || true
}

# os_updates_note_prior_state: records what this image held before, and refuses a leftover that
# would make an absence check below say nothing about the step.
os_updates_note_prior_state() {
  local timer
  if [ -n "$(os_updates_previous_copies)" ]; then
    fail "$(os_updates_previous_copies | head -1) is already in this image before the OS security updates assertions run. Something wrote it, and every 'the step kept nothing' check below would be about that file's author, not about step 68"
  fi
  for timer in "${OS_UPDATES_TIMERS[@]}"; do
    [ "$(os_updates_timer_record "$timer")" = none ] \
      || fail "the polygon's systemctl already holds an enablement record for ${timer} before the OS security updates assertions run, so 'the step enabled nothing' below would be about whoever enabled it"
  done
  if [ "$MARAN_OS_FAMILY" = debian ]; then
    if [ -e "$OS_UPDATES_CONFIG" ] || [ -L "$OS_UPDATES_CONFIG" ]; then
      fail "${OS_UPDATES_CONFIG} is already in this image before the OS security updates assertions run. Something wrote it, and every 'the step wrote nothing' check below would be about that file's author"
    fi
    OS_UPDATES_HAD_CONFIG=0
  elif [ -e "$OS_UPDATES_CONFIG" ] || [ -L "$OS_UPDATES_CONFIG" ]; then
    # The package may already ship this file in the image. Its bytes are put back at the end.
    OS_UPDATES_HAD_CONFIG=1
    OS_UPDATES_SAVED_CONFIG="$(mktemp)"
    cp -p -- "$OS_UPDATES_CONFIG" "$OS_UPDATES_SAVED_CONFIG"
  else
    OS_UPDATES_HAD_CONFIG=0
  fi
  if os_updates_package_installed; then OS_UPDATES_HAD_PACKAGE=1; else OS_UPDATES_HAD_PACKAGE=0; fi
  OS_UPDATES_UNIT_LISTING="$(os_updates_systemd_listing)"
}

# os_updates_restore_prior_state: takes away what the step and these assertions wrote, and nothing
# that was there before. The distribution's package is left installed when the step installed it:
# installing it is the step's documented act, and the uninstaller keeps it on purpose.
os_updates_restore_prior_state() {
  local timer
  while IFS= read -r timer; do
    [ -z "$timer" ] || rm -f -- "$timer"
  done < <(os_updates_previous_copies)
  if [ "$MARAN_OS_FAMILY" = debian ]; then
    rm -f -- "$OS_UPDATES_CONFIG" "${OS_UPDATES_CONFIG}.disabled"
  elif [ "$OS_UPDATES_HAD_CONFIG" -eq 1 ]; then
    cp -p -- "$OS_UPDATES_SAVED_CONFIG" "$OS_UPDATES_CONFIG"
    rm -f -- "$OS_UPDATES_SAVED_CONFIG"
  else
    rm -f -- "$OS_UPDATES_CONFIG"
  fi
  for timer in "${OS_UPDATES_TIMERS[@]}"; do
    rm -f -- "${UNIT_STATE_DIRECTORY}/${timer}" "${UNIT_STATE_DIRECTORY}/${timer}.enabled"
  done
}

# os_updates_effective_lines: a configuration file with its comments and blank lines removed and
# its whitespace squeezed, `#clear` lines KEPT. They look like comments and are not: apt reads
# `#clear <list>;` as the instruction that empties a list, so stripping every `#` line would delete
# the one line this file exists to carry. A `//` or `;` line is a comment; a `#` line is one unless
# it is `#clear`. Read through a variable by the checkers below, never a pipe: under pipefail a
# `printf | grep -q` can fail with SIGPIPE when grep exits first.
os_updates_effective_lines() {
  awk '
    /^[[:space:]]*$/ { next }
    /^[[:space:]]*(#|\/\/|;)/ && !/^[[:space:]]*#clear[[:space:]]/ { next }
    { gsub(/^[[:space:]]+|[[:space:]]+$/, ""); gsub(/[[:space:]]+/, " "); print }
  ' "$1"
}

# os_updates_ini_values: every value the given key has in the given section of the effective lines
# in $1, one per line. Every value and not the last, so a key written twice reads as two answers
# and fails a "exactly one" test instead of hiding the first.
os_updates_ini_values() {
  local effective="$1" section="$2" key="$3"
  awk -v S="$section" -v K="$key" '
    /^\[.*\]$/ { sec = substr($0, 2, length($0) - 2); next }
    sec == S {
      i = index($0, "=")
      if (i == 0) next
      k = substr($0, 1, i - 1); v = substr($0, i + 1)
      gsub(/^ +| +$/, "", k); gsub(/^ +| +$/, "", v)
      if (k == K) print v
    }' <<<"$effective"
}

# os_updates_accepted_origins: the Origins-Pattern values the effective configuration may hold, one
# per line, for THIS image's release, spelled out. Ubuntu names its security archive in the Suite
# field of its Release files, so the pattern is `archive=<codename>-security`; `codename=` would
# match nothing there and the unattended run would apply NOTHING, a safe failure and a silent one.
# A release this part has no spelling for accepts nothing, which fails the check by name rather
# than waving the value through. A value that matches a wider set of origins is the defect this
# is here for.
os_updates_accepted_origins() {
  case "${MARAN_POLYGON:-}" in
    ubuntu24) printf '%s\n' 'origin=Ubuntu,archive=noble-security' ;;
  esac
}

# os_updates_hold_sorted: the expected hold list as the family spells it, one entry per line, sorted.
os_updates_hold_sorted() {
  case "$MARAN_OS_FAMILY" in
    debian) printf '%s\n' "${OS_UPDATES_APT_BLACKLIST_EXPECTED[@]}" | sort ;;
    rhel) printf '%s\n' "${OS_UPDATES_HOLD_EXPECTED[@]}" | sort ;;
  esac
}

# os_updates_debian_defect: prints the first reason the Debian family's file at $1 is not the file
# the design allows, and prints nothing when it is. TEXT only: that this text WINS over the
# distribution's own configuration is os_updates_debian_dump_defect's question.
os_updates_debian_defect() {
  local effective rest count line pattern
  effective="$(os_updates_effective_lines "$1")"
  for line in '#clear Unattended-Upgrade::Allowed-Origins;' '#clear Unattended-Upgrade::Origins-Pattern;'; do
    count="$(grep -Fxc -- "$line" <<<"$effective" || true)"
    [ "$count" -eq 1 ] \
      || { echo "does not carry '${line}' exactly once (found ${count}), so the distribution's own origin list stays allowed beside ours"; return 0; }
  done
  rest="$(grep -v '^#clear ' <<<"$effective" || true)"
  if grep -q 'Allowed-Origins' <<<"$rest"; then
    echo "names Unattended-Upgrade::Allowed-Origins outside its #clear line, so an origin other than the security one is allowed"; return 0
  fi
  count="$(grep -o 'origin=' <<<"$rest" | wc -l || true)"
  [ "$count" -eq 1 ] \
    || { echo "names ${count} origin= patterns, not exactly one, so more than the security channel is allowed"; return 0; }
  count="$(grep -Ec '^Unattended-Upgrade::Automatic-Reboot "' <<<"$rest" || true)"
  [ "$count" -eq 1 ] \
    || { echo "has ${count} Unattended-Upgrade::Automatic-Reboot lines, not exactly one"; return 0; }
  grep -Fxq 'Unattended-Upgrade::Automatic-Reboot "false";' <<<"$rest" \
    || { echo "does not say Unattended-Upgrade::Automatic-Reboot \"false\"; so a later change of the package's default could reboot a host with customers on it"; return 0; }
  for line in 'APT::Periodic::Update-Package-Lists "1";' 'APT::Periodic::Unattended-Upgrade "1";'; do
    count="$(grep -Fxc -- "$line" <<<"$rest" || true)"
    [ "$count" -eq 1 ] || { echo "does not carry '${line}' exactly once (found ${count})"; return 0; }
  done
  if grep -Eq 'AllowUnauthenticated|AllowInsecureRepositories|AllowDowngradeToInsecureRepositories|Verify-Peer|Verify-Host' <<<"$rest"; then
    echo "turns a package or transport signature check off, which this file must never do"; return 0
  fi
  for pattern in "${OS_UPDATES_APT_BLACKLIST_EXPECTED[@]}"; do
    count="$(grep -oF -- "\"${pattern}\"" <<<"$rest" | wc -l || true)"
    [ "$count" -eq 1 ] \
      || { echo "does not hold the pattern \"${pattern}\" exactly once (found ${count}) in Unattended-Upgrade::Package-Blacklist"; return 0; }
  done
  return 0
}

# os_updates_rhel_defect: prints the first reason the RHEL family's file at $1 is not the file the
# design allows, and prints nothing when it is. It reads the file with a small INI reader of this
# script, not with dnf-automatic: there is no offline validator for that file, which is the
# step's own stated blind spot.
os_updates_rhel_defect() {
  local effective values count spec section key wanted actual expected value
  effective="$(os_updates_effective_lines "$1")"
  for spec in 'commands upgrade_type security' 'commands apply_updates yes' 'commands random_sleep 3600'; do
    read -r section key wanted <<<"$spec"
    values="$(os_updates_ini_values "$effective" "$section" "$key")"
    count="$(grep -c . <<<"$values" || true)"
    [ "$count" -eq 1 ] || { echo "has ${count} values for ${key} in [${section}], not exactly one"; return 0; }
    [ "$values" = "$wanted" ] || { echo "has ${key} = ${values} in [${section}], not ${wanted}"; return 0; }
  done
  values="$(os_updates_ini_values "$effective" base exclude)"
  count="$(grep -c . <<<"$values" || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} exclude= lines in [base], not exactly one"; return 0; }
  actual="$(tr ', ' '\n\n' <<<"$values" | grep . | sort || true)"
  expected="$(os_updates_hold_sorted)"
  [ "$actual" = "$expected" ] \
    || { echo "holds [$(tr '\n' ' ' <<<"$actual")] in [base] exclude=, not the hold list [$(tr '\n' ' ' <<<"$expected")]"; return 0; }
  while IFS= read -r value; do
    [ -n "$value" ] || continue
    case "$(tr -d ' ' <<<"${value#*=}")" in
      never) ;;
      *) echo "sets reboot = ${value#*=}; the panel shows reboot-required and rebooting stays the operator's decision"; return 0 ;;
    esac
  done < <(grep -E '^reboot ?=' <<<"$effective" || true)
  if grep -Eq '^(gpgcheck|repo_gpgcheck|localpkg_gpgcheck|sslverify) ?=' <<<"$effective"; then
    echo "sets a signature or TLS verification key, which this file must never do"; return 0
  fi
  return 0
}

# os_updates_text_defect: the family's text checker on the file at $1.
os_updates_text_defect() {
  case "$MARAN_OS_FAMILY" in
    debian) os_updates_debian_defect "$1" ;;
    rhel) os_updates_rhel_defect "$1" ;;
  esac
}

# os_updates_dump_entries: the values of the list $2 in an `apt-config dump` text $1, one per line.
# A list prints as `Key "";` and then one `Key:: "value";` line per entry; only the second shape
# is an entry.
os_updates_dump_entries() {
  { grep -E "^${2}::[[:space:]]" <<<"$1" || true; } | sed -E 's/^[^"]*"(.*)";?$/\1/'
}

# os_updates_debian_dump_defect: prints the first reason the EFFECTIVE apt configuration is not the
# one the design allows, and prints nothing when it is. This is the observation that matters on the
# Debian family: it asks apt, which merges every file in apt.conf.d in order and honours `#clear`,
# so a later file that adds an origin back, or an earlier one this file failed to clear, shows here
# and in no grep of our own file.
os_updates_debian_dump_defect() {
  local dump entries count actual expected accepted
  dump="$(apt-config dump 2>&1)" \
    || { echo "apt-config dump failed, so the effective configuration could not be read"; return 0; }
  entries="$(os_updates_dump_entries "$dump" Unattended-Upgrade::Allowed-Origins)"
  [ -z "$entries" ] \
    || { echo "the effective configuration still allows origins through Unattended-Upgrade::Allowed-Origins ($(tr '\n' ' ' <<<"$entries")), so the #clear did not win"; return 0; }
  entries="$(os_updates_dump_entries "$dump" Unattended-Upgrade::Origins-Pattern)"
  count="$(grep -c . <<<"$entries" || true)"
  [ "$count" -eq 1 ] \
    || { echo "the effective Unattended-Upgrade::Origins-Pattern holds ${count} entries, not exactly one ($(tr '\n' ' ' <<<"$entries"))"; return 0; }
  accepted="$(os_updates_accepted_origins)"
  grep -Fxq -- "$entries" <<<"$accepted" \
    || { echo "the effective Origins-Pattern is '${entries}', which is not the security origin of this release"; return 0; }
  actual="$(os_updates_dump_entries "$dump" Unattended-Upgrade::Package-Blacklist | sort)"
  expected="$(os_updates_hold_sorted)"
  [ "$actual" = "$expected" ] \
    || { echo "the effective Package-Blacklist is [$(tr '\n' ' ' <<<"$actual")], not the hold list [$(tr '\n' ' ' <<<"$expected")]"; return 0; }
  grep -Fxq 'Unattended-Upgrade::Automatic-Reboot "false";' <<<"$dump" \
    || { echo "the effective Unattended-Upgrade::Automatic-Reboot is not \"false\""; return 0; }
  return 0
}

# os_updates_prove_the_dump_reader_can_see: the dump reader shown to see the distribution's own
# origins when this file is moved aside, then shown to accept the configuration again once it is
# back. A reader that answered "clean" over any tree, or that could not parse the dump, would pass
# the real check above with nothing behind it.
#
# `.disabled` is the suffix apt ignores SILENTLY (Dir::Ignore-Files-Silently), so the moved file is
# out of the configuration without a warning landing in the output being parsed.
os_updates_prove_the_dump_reader_can_see() {
  local aside="${OS_UPDATES_CONFIG}.disabled" dump default_origins blacklist after
  mv -- "$OS_UPDATES_CONFIG" "$aside"
  dump="$(apt-config dump 2>&1 || true)"
  mv -- "$aside" "$OS_UPDATES_CONFIG"
  default_origins="$( { os_updates_dump_entries "$dump" Unattended-Upgrade::Allowed-Origins
                        os_updates_dump_entries "$dump" Unattended-Upgrade::Origins-Pattern; } | grep -c . || true)"
  blacklist="$(os_updates_dump_entries "$dump" Unattended-Upgrade::Package-Blacklist | grep -c . || true)"
  [ "$default_origins" -gt 1 ] \
    || fail "with ${OS_UPDATES_CONFIG} moved aside the apt-config dump reader found ${default_origins} allowed origin(s), and the distribution's own 50unattended-upgrades allows several. The reader cannot see an origin list, so its empty answer for the real file means nothing"
  [ "$blacklist" -eq 0 ] \
    || fail "with ${OS_UPDATES_CONFIG} moved aside the effective Package-Blacklist still held ${blacklist} entries, so something other than the step's file writes the hold list and the check above credits the step with it"
  after="$(os_updates_debian_dump_defect)"
  [ -z "$after" ] \
    || fail "the effective apt configuration is wrong again after ${OS_UPDATES_CONFIG} was put back: ${after}"
}

# os_updates_prove_the_text_checker_refuses: the family's text checker on the INSTALLED file, then
# shown to refuse copies with one property broken and to accept copies that differ only in a
# comment. Refusals are checked after the real file passes and the inverse controls before them, so
# a checker that refuses everything fails on the real file and one that refuses nothing fails on a
# planted break.
os_updates_prove_the_text_checker_refuses() {
  local work defect name control planted_count=0 expected_count
  local -a refusals=()
  work="$(mktemp -d)"

  defect="$(os_updates_text_defect "$OS_UPDATES_CONFIG")"
  [ -z "$defect" ] \
    || fail "the INSTALLED ${OS_UPDATES_CONFIG} ${defect}. This file configures a root job that installs packages with nobody reading the list; it must stay what the threat note's section 1 and 3 describe (docs/superpowers/notes/2026-11-04-os-security-updates-threat-note.md)."

  if [ "$MARAN_OS_FAMILY" = debian ]; then
    expected_count=13
    # Two inverse controls: a comment that names a dangerous directive. Neither is one.
    sed '$a// Unattended-Upgrade::Allowed-Origins { "*:*"; };' "$OS_UPDATES_CONFIG" > "${work}/commented-slash"
    sed '$a# Unattended-Upgrade::Automatic-Reboot "true";' "$OS_UPDATES_CONFIG" > "${work}/commented-hash"
    refusals=(
      'the Allowed-Origins clear removed|/^#clear Unattended-Upgrade::Allowed-Origins;/d'
      'the Origins-Pattern clear removed|/^#clear Unattended-Upgrade::Origins-Pattern;/d'
      'a second origin appended|$aUnattended-Upgrade::Origins-Pattern { "origin=Ubuntu"; };'
      'an Allowed-Origins block appended|$aUnattended-Upgrade::Allowed-Origins { "*:*"; };'
      'Automatic-Reboot true|s/Automatic-Reboot "false"/Automatic-Reboot "true"/'
      'Automatic-Reboot removed|/Unattended-Upgrade::Automatic-Reboot "/d'
      'Unattended-Upgrade periodic switched to 0|s/\(APT::Periodic::Unattended-Upgrade[[:space:]]*\)"1"/\1"0"/'
      'Update-Package-Lists removed|/APT::Periodic::Update-Package-Lists/d'
      'nginx dropped from the hold list|s/\^nginx/^ngin/'
      'php dropped from the hold list|s/\^php/^ph/'
      'AllowUnauthenticated appended|$aAPT::Get::AllowUnauthenticated "true";'
    )
  else
    expected_count=10
    sed '$a ; reboot = when-needed' "$OS_UPDATES_CONFIG" > "${work}/commented-semicolon"
    sed '$a# gpgcheck = 0' "$OS_UPDATES_CONFIG" > "${work}/commented-hash"
    refusals=(
      'upgrade_type changed from security|s/\(upgrade_type[[:space:]]*=[[:space:]]*\)security/\1default/'
      'upgrade_type removed|/upgrade_type/d'
      'apply_updates switched off|s/\(apply_updates[[:space:]]*=[[:space:]]*\)yes/\1no/'
      'random_sleep changed|s/\(random_sleep[[:space:]]*=[[:space:]]*\)3600/\1 0/'
      'nginx dropped from the hold list|s/nginx\*/nginxx/'
      'vsftpd dropped from the hold list|s/vsftpd\*/vsftpdx/'
      'a reboot key appended|$areboot = when-needed'
      'gpgcheck switched off|s/^\[base\]/[base]\ngpgcheck = 0/'
    )
  fi

  for control in "${work}"/commented-*; do
    cmp -s "$OS_UPDATES_CONFIG" "$control" \
      && fail "the inverse control ${control##*/} changed nothing, so its acceptance proves nothing"
    [ -z "$(os_updates_text_defect "$control")" ] \
      || fail "the text checker REFUSED a copy of the real file that differs only by a commented-out directive (${control##*/}); it refuses what it should accept"
    planted_count=$((planted_count + 1))
  done

  for control in "${refusals[@]}"; do
    name="${control%%|*}"
    sed "${control#*|}" "$OS_UPDATES_CONFIG" > "${work}/planted"
    cmp -s "$OS_UPDATES_CONFIG" "${work}/planted" \
      && fail "the planted control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(os_updates_text_defect "${work}/planted")"
    [ -n "$defect" ] \
      || fail "the text checker ACCEPTED the planted defect '${name}'. A checker that accepts a file with that broken guards nothing about an unattended root job."
    planted_count=$((planted_count + 1))
  done

  [ "$planted_count" -eq "$expected_count" ] \
    || fail "the OS security updates text checker ran ${planted_count} planted controls, not the ${expected_count} this family carries (2 accepted and the rest refused). A control dropped from a list is a check that no longer exists."
  rm -rf -- "$work"
  OS_UPDATES_PLANTED_COUNT="$planted_count"
}
