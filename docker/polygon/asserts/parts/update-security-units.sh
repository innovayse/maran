#!/usr/bin/env bash
# the unattended security-patch update's units (issue #383): what step 69 installs and what the units may say
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The subject. `maran-update-security.service` is a root job that replaces the code root runs, started
# by `maran-update-security.timer` once a day with no operator watching. Four properties are worth a
# gate, because each one fails silently:
#
#   1. The service is the one fixed command `maran update --security-only --scheduled`. A unit that
#      loses `--security-only` (a one-word edit that reads as a tidy-up) would apply FEATURE releases
#      unattended and still parse, still start and still report success.
#   2. The sandbox is the one the threat note derived: ProtectSystem=strict with exactly five writable
#      paths, none of them /var/lib/maran (which the panel's service account can write) or a parent.
#   3. The timer fires once a day with a random delay and no other trigger: a boot-time or
#      unit-active trigger is the same unattended root job by another spelling.
#   4. The default is OFF, is told, and is kept (issue #550): no install, online or offline, enables
#      the timer; an operator's `maran update-schedule enable` survives a re-run of the step, and a
#      timer disabled with `maran update-schedule disable` stays off.
#
# Every checker below is shown to refuse before it is trusted: copies of the real unit with ONE
# property broken at a time must each be refused, and copies that differ only in a comment must be
# ACCEPTED, so a refusal is known to come from the content and an acceptance to mean something.
# The assertions read the units' EFFECTIVE lines (comments and blanks removed) because the shipped
# files argue at length about directives they leave out, and a search over the raw text finds the
# explanation and calls it a defect (rules/testing.md).

# The installer package's copies of the units, and the step. Literals on purpose: an assertion that
# read its expectation out of the step would agree with it by construction.
readonly UPDATE_SECURITY_STEP="${INSTALLER_LIB}/69-update-schedule.sh"
readonly UPDATE_SECURITY_SERVICE_SOURCE="${INSTALLER_ROOT}/systemd/maran-update-security.service"
readonly UPDATE_SECURITY_TIMER_SOURCE="${INSTALLER_ROOT}/systemd/maran-update-security.timer"

# Where step 69 installs to and creates, and the names of the units.
readonly UPDATE_SECURITY_UNIT_DIR="/etc/systemd/system"
readonly UPDATE_SECURITY_SERVICE_FILE="maran-update-security.service"
readonly UPDATE_SECURITY_TIMER_FILE="maran-update-security.timer"
readonly UPDATE_SECURITY_DUMP_DIR="/var/backups/maran-update"

# The lines the design fixes, spelled out here and not read from the unit: deleting or loosening a
# line in the unit cannot also delete or loosen its check.
readonly UPDATE_SECURITY_EXEC_EXPECTED="ExecStart=/usr/local/sbin/maran update --security-only --scheduled"
readonly UPDATE_SECURITY_RW_EXPECTED="ReadWritePaths=/usr/local/maran /etc/maran /var/backups/maran-update /var/lib/maran-cli-staging /run"
readonly UPDATE_SECURITY_AF_EXPECTED="RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK"

# update_security_effective_lines: a unit file with its comments and blank lines removed. Read through
# a variable by the checkers below, never a pipe: under pipefail a `printf | grep -q` can fail with
# SIGPIPE when grep exits first, and a checker that flakes on its own plumbing gets switched off.
update_security_effective_lines() {
  grep -v '^[[:space:]]*#' "$1" | grep -v '^[[:space:]]*$' || true
}

# run_update_security_step: step 69's own step_update_schedule, run the way install.sh runs a step (a
# child with errexit set, SCRIPT_DIR naming the installer package and LIB_DIR its lib/ directory, which
# the step resolves the units against). Any argument is a NAME=value environment assignment handed to
# the child, which is how an offline install is spelled.
run_update_security_step() {
  env "$@" SCRIPT_DIR="$INSTALLER_ROOT" LIB_DIR="$INSTALLER_LIB" bash -c 'set -euo pipefail
. "$1"
step_update_schedule' _ "$UPDATE_SECURITY_STEP"
}

# update_security_note_prior_state: records, in globals, whether the dump directory was already in this
# image, and refuses a leftover that would make an absence check below say nothing.
#
# The units must not exist: this part proves what the STEP leaves, and a file some earlier assertion
# left behind would be credited to the wrong author.
update_security_note_prior_state() {
  local leftover
  for leftover in \
    "${UPDATE_SECURITY_UNIT_DIR}/${UPDATE_SECURITY_SERVICE_FILE}" \
    "${UPDATE_SECURITY_UNIT_DIR}/${UPDATE_SECURITY_TIMER_FILE}"; do
    if [ -e "$leftover" ] || [ -L "$leftover" ]; then
      fail "${leftover} is already in this image before the security-patch update assertions run. Something installed or wrote it, and every 'the step enabled / did not enable the timer' check below would be about that file's author, not about step 69"
    fi
  done
  UPDATE_SECURITY_HAD_DUMP_DIR=0
  { [ -e "$UPDATE_SECURITY_DUMP_DIR" ] || [ -L "$UPDATE_SECURITY_DUMP_DIR" ]; } && UPDATE_SECURITY_HAD_DUMP_DIR=1
  UPDATE_SECURITY_HAD_ETC_MARAN=0
  [ -d /etc/maran ] && UPDATE_SECURITY_HAD_ETC_MARAN=1
  return 0
}

# update_security_forget_units: takes the two unit files and the stand-in's records for them away, so
# the next run of the step meets a host that has never seen the feature.
update_security_forget_units() {
  rm -f -- "${UPDATE_SECURITY_UNIT_DIR}/${UPDATE_SECURITY_SERVICE_FILE}" "${UPDATE_SECURITY_UNIT_DIR}/${UPDATE_SECURITY_TIMER_FILE}"
  rm -f -- "${UNIT_STATE_DIRECTORY}/${UPDATE_SECURITY_TIMER_FILE}" "${UNIT_STATE_DIRECTORY}/${UPDATE_SECURITY_TIMER_FILE}.enabled" \
    "${UNIT_STATE_DIRECTORY}/${UPDATE_SECURITY_SERVICE_FILE}" "${UNIT_STATE_DIRECTORY}/${UPDATE_SECURITY_SERVICE_FILE}.enabled"
}

# update_security_restore_prior_state: removes what step 69 and the verb created since
# update_security_note_prior_state, and nothing that was there before.
update_security_restore_prior_state() {
  update_security_forget_units
  if [ "$UPDATE_SECURITY_HAD_DUMP_DIR" -eq 0 ]; then
    if [ -L "$UPDATE_SECURITY_DUMP_DIR" ]; then
      rm -f -- "$UPDATE_SECURITY_DUMP_DIR"
    elif [ -d "$UPDATE_SECURITY_DUMP_DIR" ]; then
      rmdir -- "$UPDATE_SECURITY_DUMP_DIR" \
        || fail "${UPDATE_SECURITY_DUMP_DIR} is not empty after the security-patch update assertions, so one of them left a dump behind"
    fi
  fi
  if [ "$UPDATE_SECURITY_HAD_ETC_MARAN" -eq 0 ] && [ -d /etc/maran ]; then
    rmdir -- /etc/maran 2>/dev/null || true
  fi
}

# update_security_service_defect: prints the first reason the unit at $1 is not the unit the design
# allows, and prints nothing when it is.
#
# Each test is an exact line, because the failure this guards against is a LOOSENING that reads as a
# tidy-up (`--security-only` dropped, `ProtectSystem=strict` to `full`, `/var/backups/maran-update` to
# `/var/backups`).
update_security_service_defect() {
  local file="$1" effective key token count
  effective="$(update_security_effective_lines "$file")"
  # sec_grep: grep over the effective lines through a here-string, never a pipe.
  sec_grep() { grep "$@" <<<"$effective"; }

  # One fixed argv, and it is the expected one. No second command of any kind.
  count="$(sec_grep -c '^ExecStart=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} ExecStart= lines, not exactly one"; return 0; }
  [ "$(sec_grep '^ExecStart=')" = "$UPDATE_SECURITY_EXEC_EXPECTED" ] \
    || { echo "ExecStart= is not '${UPDATE_SECURITY_EXEC_EXPECTED}' (a unit without --security-only applies feature releases unattended)"; return 0; }
  if sec_grep -Eq '^(ExecStartPre|ExecStartPost|ExecStop|ExecStopPost|ExecReload|ExecCondition)='; then
    echo "has a second command (ExecStartPre=, ExecStartPost=, ExecStop=, ExecStopPost=, ExecReload= or ExecCondition=), and anything it names runs as root"; return 0
  fi
  if sec_grep -Eq '(^|[ =/])(sh|bash|dash)( |$)|[ ]-c[ ]'; then
    echo "runs a shell"; return 0
  fi
  if sec_grep -q '^EnvironmentFile='; then
    echo "reads an EnvironmentFile=, so a file other than the unit chooses the environment of a root job"; return 0
  fi

  # Whom it runs as, and how it ends.
  for key in "Type=oneshot" "User=root" "RemainAfterExit=no"; do
    [ "$(sec_grep -c "^${key%%=*}=" || true)" -le 1 ] || { echo "${key%%=*}= appears more than once (systemd keeps the last)"; return 0; }
  done
  sec_grep -qx 'Type=oneshot' || { echo "is not Type=oneshot, so a failed run would not show as failed"; return 0; }
  sec_grep -qx 'User=root' || { echo "does not state User=root; the verb is the wrapper's root-only command"; return 0; }
  if sec_grep -q '^RemainAfterExit=' && ! sec_grep -qx 'RemainAfterExit=no'; then
    echo "sets RemainAfterExit to something other than no, which hides the night the update stopped working"; return 0
  fi
  count="$(sec_grep '^TimeoutStartSec=' | sed 's/^TimeoutStartSec=//' | head -1 || true)"
  case "$count" in
    ''|0|infinity) echo "has no finite TimeoutStartSec= (read: '${count}'), so a hung update holds the update lock for ever"; return 0 ;;
  esac

  # The sandbox: each directive exactly once, with the exact value.
  for key in "NoNewPrivileges=yes" "PrivateTmp=yes" "ProtectHome=yes" "ProtectSystem=strict" "$UPDATE_SECURITY_AF_EXPECTED"; do
    count="$(sec_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one (systemd keeps the last assignment)"; return 0; }
    sec_grep -qxF "$key" || { echo "is missing '${key}'"; return 0; }
  done

  # The writable set: five named paths, and never a parent of them, a system tree, or the panel's own
  # state directory (which the unprivileged service account can write).
  [ "$(sec_grep -c '^ReadWritePaths=' || true)" -eq 1 ] \
    || { echo "does not hold exactly one ReadWritePaths= line"; return 0; }
  for token in $(sec_grep '^ReadWritePaths=' | sed 's/^ReadWritePaths=//'); do
    token="${token#-}"
    case "$token" in
      /|/var|/var/lib|/var/lib/maran|/var/backups|/etc|/etc/systemd|/etc/systemd/*|/usr|/usr/lib|/usr/lib/*|/boot|/boot/*|/home|/root|/opt|/srv|/lib|/bin|/sbin)
        echo "ReadWritePaths= names ${token}, which makes more than the five named paths writable to a root job"; return 0 ;;
    esac
  done
  [ "$(sec_grep '^ReadWritePaths=')" = "$UPDATE_SECURITY_RW_EXPECTED" ] \
    || { echo "ReadWritePaths= is not '${UPDATE_SECURITY_RW_EXPECTED}'"; return 0; }

  # Nothing starts it but the timer and an operator: no [Install], no second section.
  if sec_grep -Eq '^\[Install\]|^(WantedBy|RequiredBy|Alias|Also)='; then
    echo "has an [Install] section, so it can be enabled and started at boot"; return 0
  fi
  [ "$(sec_grep -c '^\[' || true)" -eq 2 ] \
    || { echo "does not have exactly the two sections [Unit] and [Service]"; return 0; }
  return 0
}

# update_security_timer_defect: prints the first reason the timer at $1 is not the timer the design
# allows, and prints nothing when it is.
#
# Every directive that makes a timer FIRE is checked, not only OnCalendar=: a monotonic trigger
# (OnBootSec=, OnUnitActiveSec=) next to the daily one is an update attempt at every boot or every hour.
update_security_timer_defect() {
  local file="$1" effective count key
  effective="$(update_security_effective_lines "$file")"
  sec_grep() { grep "$@" <<<"$effective"; }

  if sec_grep -Eq '^(OnActiveSec|OnBootSec|OnStartupSec|OnUnitActiveSec|OnUnitInactiveSec|OnClockChange|OnTimezoneChange)='; then
    echo "carries a trigger besides the daily calendar one (OnBootSec=, OnUnitActiveSec= or a sibling), so the unattended root job runs more often than once a day"; return 0
  fi
  for key in "OnCalendar=daily" "RandomizedDelaySec=4h" "Persistent=true" "Unit=maran-update-security.service" "WantedBy=timers.target"; do
    count="$(sec_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one"; return 0; }
    sec_grep -qx "$key" || { echo "is missing '${key}'"; return 0; }
  done
  [ "$(sec_grep -c '^\[' || true)" -eq 3 ] \
    || { echo "does not have exactly the three sections [Unit], [Timer] and [Install]"; return 0; }
  return 0
}

# assert_the_update_security_service_and_timer_are_what_the_design_allows: both checkers on the shipped
# files, then the checkers shown to refuse.
#
# The shipped files are checked first and the planted ones after, so a checker that refuses everything
# fails on the real unit and one that refuses nothing fails on a planted break. The inverse controls (a
# comment appended, a commented-out directive) are copies the checker must ACCEPT.
#
# UNOBSERVED HERE: this reads TEXT. A key the running systemd does not know is logged and IGNORED, so
# the text can name a directive the loaded unit does not have; the hardening SCORE of the unit is
# held by `maran units` (agent.yml, job units), not by this assertion.
assert_the_update_security_service_and_timer_are_what_the_design_allows() {
  local work defect name control planted_count=0
  require_installer_file "$UPDATE_SECURITY_STEP" installer/lib/69-update-schedule.sh
  require_installer_file "$UPDATE_SECURITY_SERVICE_SOURCE" installer/systemd/maran-update-security.service
  require_installer_file "$UPDATE_SECURITY_TIMER_SOURCE" installer/systemd/maran-update-security.timer
  work="$(mktemp -d)"

  defect="$(update_security_service_defect "$UPDATE_SECURITY_SERVICE_SOURCE")"
  [ -z "$defect" ] \
    || fail "${UPDATE_SECURITY_SERVICE_FILE} ${defect}. This unit is a root job that replaces the code root runs; it must stay one fixed command with the sandbox of the threat note (docs/superpowers/notes/2026-11-07-security-patch-updates-threat-note.md)."
  defect="$(update_security_timer_defect "$UPDATE_SECURITY_TIMER_SOURCE")"
  [ -z "$defect" ] \
    || fail "${UPDATE_SECURITY_TIMER_FILE} ${defect}. The schedule is one daily check with a random delay and nothing else."

  # The inverse controls first: differing only in a comment, or in a commented-out directive, is not a
  # defect, and a checker that says it is has stopped reading effective lines.
  sed '$a# ProtectSystem=full' "$UPDATE_SECURITY_SERVICE_SOURCE" > "${work}/commented.service"
  cmp -s "$UPDATE_SECURITY_SERVICE_SOURCE" "${work}/commented.service" \
    && fail "the service inverse control changed nothing, so its acceptance proves nothing"
  [ -z "$(update_security_service_defect "${work}/commented.service")" ] \
    || fail "the service checker REFUSED a unit that differs from the real one only by a commented-out 'ProtectSystem=full'; it refuses what it should accept"
  sed '$a# OnBootSec=5min' "$UPDATE_SECURITY_TIMER_SOURCE" > "${work}/commented.timer"
  cmp -s "$UPDATE_SECURITY_TIMER_SOURCE" "${work}/commented.timer" \
    && fail "the timer inverse control changed nothing, so its acceptance proves nothing"
  [ -z "$(update_security_timer_defect "${work}/commented.timer")" ] \
    || fail "the timer checker REFUSED a timer that differs from the real one only by a commented-out 'OnBootSec=5min'; it refuses what it should accept"
  planted_count=$((planted_count + 2))

  # The refusals. Each is the real file with ONE thing broken; the name says what, the checker says why.
  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    sed "$control" "$UPDATE_SECURITY_SERVICE_SOURCE" > "${work}/planted.service"
    cmp -s "$UPDATE_SECURITY_SERVICE_SOURCE" "${work}/planted.service" \
      && fail "the planted service control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(update_security_service_defect "${work}/planted.service")"
    [ -n "$defect" ] \
      || fail "the service checker ACCEPTED the planted defect '${name}'. A checker that accepts a unit with that broken guards nothing about an unattended root job that replaces the panel's code."
    planted_count=$((planted_count + 1))
  done <<'SERVICE_CONTROLS'
--security-only dropped, so feature releases apply unattended|s# --security-only##
--scheduled dropped|s# --scheduled$##
an argument appended to ExecStart|s#^ExecStart=.*#& --yes#
--security-only replaced by a channel switch|s#--security-only#--channel beta#
a shell wrapped around ExecStart|s#^ExecStart=.*#ExecStart=/bin/sh -c "/usr/local/sbin/maran update --security-only --scheduled"#
a second ExecStart|s#^ExecStart=.*#&\nExecStart=/bin/true#
an ExecStartPre|s#^User=root#User=root\nExecStartPre=/bin/true#
an ExecStartPost|s#^User=root#User=root\nExecStartPost=/bin/true#
User changed to the service account|s#^User=root#User=maran#
Type changed from oneshot|s#^Type=oneshot#Type=simple#
RemainAfterExit=yes|s#^RemainAfterExit=no#RemainAfterExit=yes#
an EnvironmentFile added|s#^UMask=0077#UMask=0077\nEnvironmentFile=/etc/maran/panel.env#
TimeoutStartSec infinity|s#^TimeoutStartSec=.*#TimeoutStartSec=infinity#
NoNewPrivileges removed|/^NoNewPrivileges=yes$/d
PrivateTmp removed|/^PrivateTmp=yes$/d
ProtectHome=no|s#^ProtectHome=yes#ProtectHome=no#
ProtectSystem loosened to full|s#^ProtectSystem=strict#ProtectSystem=full#
ProtectSystem removed|/^ProtectSystem=strict$/d
ProtectSystem set twice, the last wins|s#^ProtectSystem=strict#ProtectSystem=strict\nProtectSystem=no#
ReadWritePaths widened to /var|s#^ReadWritePaths=.*#ReadWritePaths=/var#
ReadWritePaths widened by the panel's own /var/lib/maran|s#^ReadWritePaths=.*#& /var/lib/maran#
ReadWritePaths widened by all of /etc|s#^ReadWritePaths=.*#& /etc#
ReadWritePaths widened by the system units under /etc|s#^ReadWritePaths=.*#& /etc/systemd/system#
ReadWritePaths dropping the dump directory|s# /var/backups/maran-update##
ReadWritePaths widened to the parent of the dump directory|s#/var/backups/maran-update#/var/backups#
ReadWritePaths widened by an optional /usr/lib|s#^ReadWritePaths=.*#& -/usr/lib#
RestrictAddressFamilies widened to raw packets|s#^RestrictAddressFamilies=.*#& AF_PACKET#
an [Install] section|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
SERVICE_CONTROLS

  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    sed "$control" "$UPDATE_SECURITY_TIMER_SOURCE" > "${work}/planted.timer"
    cmp -s "$UPDATE_SECURITY_TIMER_SOURCE" "${work}/planted.timer" \
      && fail "the planted timer control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(update_security_timer_defect "${work}/planted.timer")"
    [ -n "$defect" ] \
      || fail "the timer checker ACCEPTED the planted defect '${name}'. A timer with a second trigger or without its delay is a different unattended root job."
    planted_count=$((planted_count + 1))
  done <<'TIMER_CONTROLS'
an OnBootSec=|s#^Persistent=true#Persistent=true\nOnBootSec=5min#
an OnUnitActiveSec=|s#^Persistent=true#Persistent=true\nOnUnitActiveSec=1h#
an OnActiveSec=|s#^Persistent=true#Persistent=true\nOnActiveSec=1min#
an OnStartupSec=|s#^Persistent=true#Persistent=true\nOnStartupSec=1min#
a second OnCalendar=|s#^OnCalendar=daily#OnCalendar=daily\nOnCalendar=minutely#
OnCalendar changed to hourly|s#^OnCalendar=daily#OnCalendar=hourly#
Persistent removed|/^Persistent=true$/d
RandomizedDelaySec removed|/^RandomizedDelaySec=/d
RandomizedDelaySec set to zero|s#^RandomizedDelaySec=.*#RandomizedDelaySec=0#
Unit= pointing elsewhere|s#^Unit=.*#Unit=maran-agent.service#
WantedBy changed|s#^WantedBy=timers.target#WantedBy=multi-user.target#
TIMER_CONTROLS

  [ "$planted_count" -eq 41 ] \
    || fail "the security-patch update unit checkers ran ${planted_count} planted controls, not the 41 they carry (2 accepted, 28 service and 11 timer refusals). A control dropped from a list is a check that no longer exists."

  rm -rf -- "$work"
  echo "maran-update-security.service is the one fixed 'maran update --security-only --scheduled' run as root with NoNewPrivileges, PrivateTmp, ProtectHome, ProtectSystem=strict, exactly five writable paths, a finite timeout and no [Install]; maran-update-security.timer fires once a day with a four hour random delay and no other trigger. The checkers refused all 39 planted breaks and accepted the 2 commented-out inverse controls."
  echo "UNOBSERVED HERE: this reads the unit files' TEXT. Nothing here ran the service: this image boots no systemd, so that the update's lock, dump, restart and rollback work under ProtectSystem=strict with these five writable paths (runuser, pg_dump as the service user, systemctl restart under NoNewPrivileges) is the threat note's unverified item and belongs to a booted host."
}

# assert_step_69_installs_the_security_update_units_and_keeps_the_operators_choice: step 69's own
# step_update_schedule is RUN and what it left is looked at.
#
# Looked at, and not assumed: the two units are root:root 0644 and byte-identical to the package's; the
# dump directory is a real root:root 0700 directory (a symbolic link planted at its name is refused
# BEFORE anything is written); a first install, online or offline, installs the units and leaves the timer
# off, saying how to enable it; a timer the operator switched on with the REAL `maran update-schedule enable` stays
# enabled, and one disabled with `disable` stays disabled, through re-runs of the step. Each "not enabled" answer is asked of the stand-in after a
# positive control shows the stand-in can answer "enabled" for this very unit.
#
# UNOBSERVED HERE: this image boots no systemd; "enabled" means "the polygon's systemctl stand-in was
# asked", its own documented limit. That a disabled timer stays off across a REBOOT, and that
# Persistent=true catches up a missed run, are claims about a booted host.
assert_step_69_installs_the_security_update_units_and_keeps_the_operators_choice() {
  local output status unit path aside timer="$UPDATE_SECURITY_TIMER_FILE"

  update_security_note_prior_state

  # The positive control for the enablement record: the stand-in can say "enabled" for this unit, and
  # says "disabled" for it before anything enabled it. The second half is what makes "the offline
  # install did not enable it" observable at all, and it fails on a stale image whose stand-in still
  # reports an unknown unit as enabled.
  systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
    && fail "the polygon's systemctl reports ${timer} enabled before anything enabled it, so 'the offline install left it disabled' could not be observed. The stand-in must list it among the dormant panel units, and the image must be rebuilt (maran polygon build)"
  systemctl enable --now -- "$timer" >/dev/null \
    || fail "the polygon's systemctl refused 'enable --now -- ${timer}', so the enablement control below cannot be set up"
  systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
    || fail "the polygon's systemctl enabled ${timer} and then does not report it enabled, so its answers below mean nothing"
  update_security_forget_units

  # A symbolic link at the dump directory is refused before ANYTHING is written: root's database dumps
  # would otherwise be written through it. The inverse control follows (the real directory is accepted).
  if [ "$UPDATE_SECURITY_HAD_DUMP_DIR" -eq 0 ]; then
    aside="$(mktemp -d)"
    ln -s "$aside" "$UPDATE_SECURITY_DUMP_DIR"
    status=0
    output="$(run_update_security_step 2>&1)" || status=$?
    [ "$status" -ne 0 ] \
      || fail "step 69 accepted a symbolic link at ${UPDATE_SECURITY_DUMP_DIR}; root's database dumps would be written through it: ${output}"
    case "$output" in
      *"symbolic link"*) ;;
      *) fail "step 69 failed on a planted link at ${UPDATE_SECURITY_DUMP_DIR} but not with the refusal that names it: ${output}" ;;
    esac
    [ ! -e "${UPDATE_SECURITY_UNIT_DIR}/${UPDATE_SECURITY_SERVICE_FILE}" ] && [ ! -e "${UPDATE_SECURITY_UNIT_DIR}/${timer}" ] \
      || fail "step 69 refused the planted link but had already installed a unit; the refusal must come before any write"
    [ -z "$(ls -A "$aside")" ] \
      || fail "step 69 wrote into the directory a planted link pointed to"
    rm -f -- "$UPDATE_SECURITY_DUMP_DIR"
    rm -rf -- "$aside"
  fi

  # An online first install: the units land, the timer is NOT enabled, and the operator is told how to
  # enable it.
  output="$(run_update_security_step 2>&1)" \
    || fail "step 69's step_update_schedule failed in this image: ${output}"
  case "$output" in
    *"installed but off"*"maran update-schedule enable"*) ;;
    *) fail "step 69 ran but did not tell the operator that automatic security-patch updates are off and how to enable them: ${output}" ;;
  esac
  systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
    && fail "step 69's online first install enabled ${timer}; the feature is off until the operator enables it (issue #550)"
  for unit in "$UPDATE_SECURITY_SERVICE_FILE" "$timer"; do
    path="${UPDATE_SECURITY_UNIT_DIR}/${unit}"
    assert_file_is "$path" "0:0:644"
    cmp -s "${INSTALLER_ROOT}/systemd/${unit}" "$path" \
      || fail "${path} differs from installer/systemd/${unit}: the step installed something other than what the package carries"
  done
  assert_directory_is "$UPDATE_SECURITY_DUMP_DIR" "0:0:700"

  # A loosened dump directory is put right on a re-run.
  chmod 0755 "$UPDATE_SECURITY_DUMP_DIR"
  output="$(run_update_security_step 2>&1)" \
    || fail "step 69 failed on its second run (installers get re-run): ${output}"
  assert_directory_is "$UPDATE_SECURITY_DUMP_DIR" "0:0:700"

  # The operator turns it ON with the real verb (the inverse control of the default above). The verb
  # needs the wrapper, which step 66 installs.
  run_cli_step >/dev/null || fail "step 66's step_cli could not install the command the enable below runs"
  output="$(/usr/local/sbin/maran update-schedule enable 2>&1)" \
    || fail "'maran update-schedule enable' failed in this image: ${output}"
  systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
    || fail "'maran update-schedule enable' ran and the timer does not read enabled, so the survival check below would prove nothing"
  for status in 1 2; do
    output="$(run_update_security_step 2>&1)" \
      || fail "step 69 failed on re-run ${status} over an enabled timer: ${output}"
    systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
      || fail "step 69's re-run ${status} switched off a timer the operator enabled; an installer re-run must not change the choice"
  done

  # The operator turns it off again; a re-run keeps it off.
  output="$(/usr/local/sbin/maran update-schedule disable 2>&1)" \
    || fail "'maran update-schedule disable' failed in this image: ${output}"
  systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
    && fail "'maran update-schedule disable' ran and the timer still reads enabled, so the check below would prove nothing"
  for status in 1 2; do
    output="$(run_update_security_step 2>&1)" \
      || fail "step 69 failed on re-run ${status} over a disabled timer: ${output}"
    systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
      && fail "step 69's re-run ${status} switched a timer the operator disabled back on"
  done
  remove_the_installed_break_glass_command

  # An offline first install behaves the same: units installed, timer off, same line.
  update_security_forget_units
  output="$(run_update_security_step MARAN_OFFLINE_TARBALL=/nonexistent/bundle.tar.gz 2>&1)" \
    || fail "step 69's offline first install failed in this image: ${output}"
  case "$output" in
    *"installed but off"*"maran update-schedule enable"*) ;;
    *) fail "step 69's offline first install did not say the timer is off and how to enable it: ${output}" ;;
  esac
  systemctl is-enabled -- "$timer" >/dev/null 2>&1 \
    && fail "step 69's offline first install enabled ${timer}"
  assert_file_is "${UPDATE_SECURITY_UNIT_DIR}/${timer}" "0:0:644"

  update_security_restore_prior_state
  echo "Step 69 installed ${UPDATE_SECURITY_SERVICE_FILE} and ${timer} as 644 root:root byte for byte as packaged and the dump directory as 700 root:root, refused a symbolic link at ${UPDATE_SECURITY_DUMP_DIR} before writing anything, left the timer off on an online and an offline first install and told the operator how to enable it, and left a timer enabled (then disabled) with 'maran update-schedule' as the operator set it through two re-runs each."
  echo "UNOBSERVED HERE: this image boots no systemd. 'Enabled' means the polygon's systemctl stand-in was asked; a disabled timer staying off across a reboot, Persistent=true catching up a missed run, the timer firing at its calendar time and the service starting under ProtectSystem=strict are claims about a booted host."
}
