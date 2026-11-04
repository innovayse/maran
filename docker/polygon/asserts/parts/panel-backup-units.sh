#!/usr/bin/env bash
# the scheduled panel backup's units (issue #129): what step 67 installs and what the units may say
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The subject. `maran-panel-backup.service` is a root job that reads the panel's encryption key,
# archives its state and may open a connection to an address an operator wrote into a file, started
# by `maran-panel-backup.timer`. Three properties are worth a gate, because each one fails silently:
#
#   1. The service is the one fixed command, with the sandbox the design allows. A unit that gains a
#      second command, loses ProtectSystem=strict, or widens ReadWritePaths= from three named paths
#      to a parent directory still parses, still starts and still backs the panel up.
#   2. The timer carries NO trigger of its own. A timer with an OnCalendar= in the shipped file would
#      put an unattended root job on every fresh install before an operator chose a passphrase file;
#      the schedule belongs to a drop-in `maran panel-backup-schedule enable` writes. Nothing else
#      here would notice the line arriving.
#   3. Step 67 installs and enables nothing: the files land root:root 0644, the output directory
#      root:root 0700, and no enablement exists afterwards.
#
# Every checker below is shown to refuse before it is trusted: copies of the real unit with ONE
# property broken at a time must each be refused, and copies that differ only in a comment must be
# ACCEPTED, so a refusal is known to come from the content and an acceptance to mean something.
# The assertions read the units' EFFECTIVE lines (comments and blanks removed) because the shipped
# files argue at length about the directives they leave out, and a search over the raw text finds the
# explanation and calls it a defect (rules/testing.md).

# The paths step 67 installs to and creates. Literals on purpose, the arrangement the directory modes
# follow: an assertion that read them out of 67-panel-backup.sh would agree with it by construction.
readonly PANEL_BACKUP_UNIT_DIR="/etc/systemd/system"
readonly PANEL_BACKUP_SERVICE_FILE="maran-panel-backup.service"
readonly PANEL_BACKUP_TIMER_FILE="maran-panel-backup.timer"
readonly PANEL_BACKUP_OUT_DIR="/var/backups/maran-panel"
readonly PANEL_BACKUP_STAGING_DIR="/var/lib/maran-cli-staging"
readonly PANEL_BACKUP_EXAMPLE_INSTALLED="/usr/local/maran/cli/panel-backup.conf.example"
readonly PANEL_BACKUP_CONFIG_FILE="/etc/maran/panel-backup.conf"
readonly PANEL_BACKUP_DROPIN_DIR="${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_TIMER_FILE}.d"

# The two lines the design fixes, spelled out here and not read from the unit: deleting or loosening
# a line in the unit cannot also delete or loosen its check.
readonly PANEL_BACKUP_EXEC_EXPECTED="ExecStart=/usr/local/sbin/maran panel-backup --scheduled"
readonly PANEL_BACKUP_RW_EXPECTED="ReadWritePaths=/var/backups/maran-panel /var/lib/maran-cli-staging /run"

# panel_backup_effective_lines: a unit file with its comments and blank lines removed. Read through
# a variable by the checkers below, never a pipe: under pipefail a `printf | grep -q` can fail with
# SIGPIPE when grep exits first, and a checker that flakes on its own plumbing gets switched off.
panel_backup_effective_lines() {
  grep -v '^[[:space:]]*#' "$1" | grep -v '^[[:space:]]*$' || true
}

# run_panel_backup_step: step 67's own step_panel_backup, run the way install.sh runs a step (a child
# with errexit set, the shared helpers sourced, SCRIPT_DIR naming the installer package and LIB_DIR
# its lib/ directory, which the step resolves the units against).
run_panel_backup_step() {
  SCRIPT_DIR="$INSTALLER_ROOT" LIB_DIR="$INSTALLER_LIB" bash -c 'set -euo pipefail
. "$1"
. "$2"
step_panel_backup' _ "$COMMON_LIB" "$PANEL_BACKUP_STEP"
}

# panel_backup_note_prior_state: records, in globals, which of the paths step 67 creates were already
# in this image, and refuses a leftover that would make an absence check below say nothing.
#
# The units, the drop-in and the configuration must not exist: this part proves what the STEP leaves
# and what the verbs write, and a file some earlier assertion left behind would be credited to the
# wrong author. The three directories and the example may exist (an earlier assertion may have
# installed them); what this part created it removes again, and only that.
panel_backup_note_prior_state() {
  local leftover
  for leftover in \
    "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_SERVICE_FILE}" \
    "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_TIMER_FILE}" \
    "$PANEL_BACKUP_DROPIN_DIR" "$PANEL_BACKUP_CONFIG_FILE"; do
    if [ -e "$leftover" ] || [ -L "$leftover" ]; then
      fail "${leftover} is already in this image before the scheduled panel backup assertions run. Something installed or wrote it, and every 'the step left nothing enabled' and 'the verb wrote nothing' check below would be about that file's author, not about step 67"
    fi
  done
  PANEL_BACKUP_HAD_OUT_DIR=0
  PANEL_BACKUP_HAD_STAGING_DIR=0
  PANEL_BACKUP_HAD_EXAMPLE=0
  PANEL_BACKUP_HAD_ETC_MARAN=0
  [ -e "$PANEL_BACKUP_OUT_DIR" ] || [ -L "$PANEL_BACKUP_OUT_DIR" ] && PANEL_BACKUP_HAD_OUT_DIR=1
  [ -e "$PANEL_BACKUP_STAGING_DIR" ] || [ -L "$PANEL_BACKUP_STAGING_DIR" ] && PANEL_BACKUP_HAD_STAGING_DIR=1
  [ -e "$PANEL_BACKUP_EXAMPLE_INSTALLED" ] && PANEL_BACKUP_HAD_EXAMPLE=1
  [ -d /etc/maran ] && PANEL_BACKUP_HAD_ETC_MARAN=1
  return 0
}

# panel_backup_restore_prior_state: removes what step 67 and the verbs created since
# panel_backup_note_prior_state, and nothing that was there before. The directories go only when
# they are empty: a rmdir that fails is a signal that an assertion left an archive, and the next
# assertion's absence checks must not inherit it.
panel_backup_restore_prior_state() {
  rm -f -- "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_SERVICE_FILE}" "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_TIMER_FILE}"
  rm -rf -- "$PANEL_BACKUP_DROPIN_DIR"
  rm -f -- "$PANEL_BACKUP_CONFIG_FILE"
  rm -f -- "${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_TIMER_FILE}" "${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_TIMER_FILE}.enabled" \
    "${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_SERVICE_FILE}" "${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_SERVICE_FILE}.enabled"
  [ "$PANEL_BACKUP_HAD_EXAMPLE" -eq 1 ] || rm -f -- "$PANEL_BACKUP_EXAMPLE_INSTALLED"
  if [ "$PANEL_BACKUP_HAD_OUT_DIR" -eq 0 ] && [ -d "$PANEL_BACKUP_OUT_DIR" ] && [ ! -L "$PANEL_BACKUP_OUT_DIR" ]; then
    rmdir -- "$PANEL_BACKUP_OUT_DIR" \
      || fail "${PANEL_BACKUP_OUT_DIR} is not empty after the scheduled panel backup assertions, so one of them left an archive behind"
  fi
  if [ "$PANEL_BACKUP_HAD_STAGING_DIR" -eq 0 ] && [ -d "$PANEL_BACKUP_STAGING_DIR" ] && [ ! -L "$PANEL_BACKUP_STAGING_DIR" ]; then
    rmdir -- "$PANEL_BACKUP_STAGING_DIR" \
      || fail "${PANEL_BACKUP_STAGING_DIR} is not empty after the scheduled panel backup assertions, so one of them left a stage behind"
  fi
  if [ "$PANEL_BACKUP_HAD_ETC_MARAN" -eq 0 ] && [ -d /etc/maran ]; then
    rmdir -- /etc/maran 2>/dev/null || true
  fi
}

# panel_backup_service_defect: prints the first reason the unit at $1 is not the unit the design
# allows, and prints nothing when it is.
#
# What "allows" means is the threat note's section 1: one fixed command run as root, the sandbox the
# verb can live with, three writable paths and not a parent of them, and nothing that starts it at
# boot. Each test is an exact line, because the failure this guards against is a LOOSENING that
# reads as a tidy-up (`ProtectSystem=strict` to `full`, `/var/backups/maran-panel` to `/var/backups`).
panel_backup_service_defect() {
  local file="$1" effective key token count
  effective="$(panel_backup_effective_lines "$file")"
  # eff_grep: grep over the effective lines through a here-string, never a pipe.
  eff_grep() { grep "$@" <<<"$effective"; }

  # One fixed argv, and it is the expected one. No second command of any kind: a unit that can run
  # a pre- or post-command as root runs whatever that line says.
  count="$(eff_grep -c '^ExecStart=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} ExecStart= lines, not exactly one"; return 0; }
  [ "$(eff_grep '^ExecStart=')" = "$PANEL_BACKUP_EXEC_EXPECTED" ] \
    || { echo "ExecStart= is not '${PANEL_BACKUP_EXEC_EXPECTED}'"; return 0; }
  if eff_grep -Eq '^(ExecStartPre|ExecStartPost|ExecStop|ExecStopPost|ExecReload|ExecCondition)='; then
    echo "has a second command (ExecStartPre=, ExecStartPost=, ExecStop=, ExecStopPost=, ExecReload= or ExecCondition=), and anything it names runs as root"; return 0
  fi
  if eff_grep -Eq '(^|[ =/])(sh|bash|dash)( |$)|[ ]-c[ ]'; then
    echo "runs a shell"; return 0
  fi
  if eff_grep -q '^EnvironmentFile='; then
    echo "reads an EnvironmentFile=, so a file other than the unit chooses the environment of a root job"; return 0
  fi

  # Whom it runs as, and how it ends.
  for key in "Type=oneshot" "User=root" "RemainAfterExit=no"; do
    [ "$(eff_grep -c "^${key%%=*}=" || true)" -le 1 ] || { echo "${key%%=*}= appears more than once (systemd keeps the last)"; return 0; }
  done
  eff_grep -qx 'Type=oneshot' || { echo "is not Type=oneshot, so a failed run would not show as failed"; return 0; }
  eff_grep -qx 'User=root' || { echo "does not state User=root; the verb is the wrapper's root-only command"; return 0; }
  if eff_grep -q '^RemainAfterExit=' && ! eff_grep -qx 'RemainAfterExit=no'; then
    echo "sets RemainAfterExit to something other than no, which hides the night the backup stopped working"; return 0
  fi
  count="$(eff_grep '^TimeoutStartSec=' | sed 's/^TimeoutStartSec=//' | head -1 || true)"
  case "$count" in
    ''|0|infinity) echo "has no finite TimeoutStartSec= (read: '${count}'), so a hung upload holds the backup lock for ever"; return 0 ;;
  esac

  # The sandbox: each directive exactly once, with the exact value.
  for key in "NoNewPrivileges=yes" "PrivateTmp=yes" "ProtectHome=yes" "ProtectSystem=strict"; do
    count="$(eff_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one (systemd keeps the last assignment)"; return 0; }
    eff_grep -qx "$key" || { echo "is missing '${key}'"; return 0; }
  done

  # The writable set: three named paths, and never a parent of them or a system tree.
  [ "$(eff_grep -c '^ReadWritePaths=' || true)" -eq 1 ] \
    || { echo "does not hold exactly one ReadWritePaths= line"; return 0; }
  for token in $(eff_grep '^ReadWritePaths=' | sed 's/^ReadWritePaths=//'); do
    token="${token#-}"
    case "$token" in
      /|/var|/var/lib|/var/backups|/etc|/usr|/boot|/home|/root|/opt|/srv|/lib|/bin|/sbin|/usr/*|/etc/*|/boot/*)
        echo "ReadWritePaths= names ${token}, which makes more than the three named paths writable to a root job"; return 0 ;;
    esac
  done
  [ "$(eff_grep '^ReadWritePaths=')" = "$PANEL_BACKUP_RW_EXPECTED" ] \
    || { echo "ReadWritePaths= is not '${PANEL_BACKUP_RW_EXPECTED}'"; return 0; }

  # Nothing starts it but the timer and an operator: no [Install], no second section.
  if eff_grep -Eq '^\[Install\]|^(WantedBy|RequiredBy|Alias|Also)='; then
    echo "has an [Install] section, so it can be enabled and started at boot"; return 0
  fi
  [ "$(eff_grep -c '^\[' || true)" -eq 2 ] \
    || { echo "does not have exactly the two sections [Unit] and [Service]"; return 0; }
  return 0
}

# panel_backup_timer_defect: prints the first reason the timer at $1 is not the timer the design
# allows, and prints nothing when it is.
#
# The trigger test is the one that matters: every directive that makes a timer FIRE is refused, not
# only OnCalendar=, because a monotonic trigger (OnBootSec=, OnUnitActiveSec=) on the shipped file
# is the same unattended root job by another spelling.
panel_backup_timer_defect() {
  local file="$1" effective count key
  effective="$(panel_backup_effective_lines "$file")"
  eff_grep() { grep "$@" <<<"$effective"; }

  if eff_grep -Eq '^(OnCalendar|OnActiveSec|OnBootSec|OnStartupSec|OnUnitActiveSec|OnUnitInactiveSec|OnClockChange|OnTimezoneChange)='; then
    echo "carries a trigger of its own (OnCalendar=, OnBootSec=, OnUnitActiveSec= or a sibling), so a fresh install would run an unattended root job before an operator chose a passphrase file"; return 0
  fi
  for key in "Persistent=true" "RandomizedDelaySec=15min" "Unit=maran-panel-backup.service" "WantedBy=timers.target"; do
    count="$(eff_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one"; return 0; }
    eff_grep -qx "$key" || { echo "is missing '${key}'"; return 0; }
  done
  return 0
}

# assert_the_panel_backup_service_and_timer_are_what_the_design_allows: both checkers on the shipped
# files, then the checkers shown to refuse.
#
# The shipped files are checked first and the planted ones after, so a checker that refuses
# everything fails on the real unit and one that refuses nothing fails on a planted break. The
# inverse controls (a comment appended, a commented-out directive) are copies the checker must
# ACCEPT, because a checker mutated to refuse any difference passes every test that only hands it
# broken input.
#
# UNOBSERVED HERE: this reads TEXT. A key the running systemd does not know is logged and IGNORED,
# so the text can name a directive the loaded unit does not have; systemd-analyze, where this image
# has it, is asked in assert_step_67_installs_the_panel_backup_units_with_exact_modes_and_enables_nothing.
assert_the_panel_backup_service_and_timer_are_what_the_design_allows() {
  local work defect name control planted_count=0
  work="$(mktemp -d)"

  defect="$(panel_backup_service_defect "$PANEL_BACKUP_SERVICE_SOURCE")"
  [ -z "$defect" ] \
    || fail "${PANEL_BACKUP_SERVICE_FILE} ${defect}. This unit is a root job that reads the panel's encryption key; it must stay one fixed command with the sandbox of the threat note's section 1 (docs/superpowers/notes/2026-11-04-panel-backup-schedule-threat-note.md)."
  defect="$(panel_backup_timer_defect "$PANEL_BACKUP_TIMER_SOURCE")"
  [ -z "$defect" ] \
    || fail "${PANEL_BACKUP_TIMER_FILE} ${defect}. The schedule belongs to the drop-in 'maran panel-backup-schedule enable' writes from the root-only configuration."

  # The inverse controls first: differing only in a comment, or in a commented-out directive, is not
  # a defect, and a checker that says it is has stopped reading effective lines.
  sed '$a# ProtectSystem=full' "$PANEL_BACKUP_SERVICE_SOURCE" > "${work}/commented.service"
  cmp -s "$PANEL_BACKUP_SERVICE_SOURCE" "${work}/commented.service" \
    && fail "the service inverse control changed nothing, so its acceptance proves nothing"
  [ -z "$(panel_backup_service_defect "${work}/commented.service")" ] \
    || fail "the service checker REFUSED a unit that differs from the real one only by a commented-out 'ProtectSystem=full'; it refuses what it should accept"
  sed '$a# OnCalendar=daily' "$PANEL_BACKUP_TIMER_SOURCE" > "${work}/commented.timer"
  cmp -s "$PANEL_BACKUP_TIMER_SOURCE" "${work}/commented.timer" \
    && fail "the timer inverse control changed nothing, so its acceptance proves nothing"
  [ -z "$(panel_backup_timer_defect "${work}/commented.timer")" ] \
    || fail "the timer checker REFUSED a timer that differs from the real one only by a commented-out 'OnCalendar=daily'; it refuses what it should accept (the shipped timer's own header names OnCalendar= in prose)"
  planted_count=$((planted_count + 2))

  # The refusals. Each is the real file with ONE thing broken; the name says what, the checker says why.
  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    sed "$control" "$PANEL_BACKUP_SERVICE_SOURCE" > "${work}/planted.service"
    cmp -s "$PANEL_BACKUP_SERVICE_SOURCE" "${work}/planted.service" \
      && fail "the planted service control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(panel_backup_service_defect "${work}/planted.service")"
    [ -n "$defect" ] \
      || fail "the service checker ACCEPTED the planted defect '${name}'. A checker that accepts a unit with that broken guards nothing about a root job that reads the panel's key."
    planted_count=$((planted_count + 1))
  done <<'SERVICE_CONTROLS'
an argument appended to ExecStart|s#^ExecStart=.*#& --out /root#
a shell wrapped around ExecStart|s#^ExecStart=.*#ExecStart=/bin/sh -c "/usr/local/sbin/maran panel-backup --scheduled"#
a second ExecStart|s#^ExecStart=.*#&\nExecStart=/bin/true#
an ExecStartPost|s#^User=root#User=root\nExecStartPost=/bin/true#
an ExecStartPre|s#^User=root#User=root\nExecStartPre=/bin/true#
User changed to the service account|s#^User=root#User=maran#
Type changed from oneshot|s#^Type=oneshot#Type=simple#
RemainAfterExit=yes|s#^RemainAfterExit=no#RemainAfterExit=yes#
an EnvironmentFile added|s#^UMask=0077#UMask=0077\nEnvironmentFile=/etc/maran/agent.env#
TimeoutStartSec infinity|s#^TimeoutStartSec=.*#TimeoutStartSec=infinity#
NoNewPrivileges removed|/^NoNewPrivileges=yes$/d
NoNewPrivileges=no|s#^NoNewPrivileges=yes#NoNewPrivileges=no#
PrivateTmp removed|/^PrivateTmp=yes$/d
ProtectHome=no|s#^ProtectHome=yes#ProtectHome=no#
ProtectSystem loosened to full|s#^ProtectSystem=strict#ProtectSystem=full#
ProtectSystem removed|/^ProtectSystem=strict$/d
ProtectSystem set twice, the last wins|s#^ProtectSystem=strict#ProtectSystem=strict\nProtectSystem=no#
ReadWritePaths widened to /var|s#^ReadWritePaths=.*#ReadWritePaths=/var#
ReadWritePaths widened to the parent of the output directory|s#^ReadWritePaths=.*#ReadWritePaths=/var/backups /var/lib/maran-cli-staging /run#
ReadWritePaths widened by /etc|s#^ReadWritePaths=.*#& /etc#
ReadWritePaths widened by an optional /usr/lib|s#^ReadWritePaths=.*#& -/usr/lib#
ReadWritePaths dropping the staging directory|s#^ReadWritePaths=.*#ReadWritePaths=/var/backups/maran-panel /run#
ReadWritePaths naming only the root|s#^ReadWritePaths=.*#ReadWritePaths=/#
an [Install] section|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
SERVICE_CONTROLS

  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    sed "$control" "$PANEL_BACKUP_TIMER_SOURCE" > "${work}/planted.timer"
    cmp -s "$PANEL_BACKUP_TIMER_SOURCE" "${work}/planted.timer" \
      && fail "the planted timer control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(panel_backup_timer_defect "${work}/planted.timer")"
    [ -n "$defect" ] \
      || fail "the timer checker ACCEPTED the planted defect '${name}'. A timer with its own trigger is an unattended root job on every fresh install."
    planted_count=$((planted_count + 1))
  done <<'TIMER_CONTROLS'
an OnCalendar=|s#^Persistent=true#Persistent=true\nOnCalendar=daily#
an OnBootSec=|s#^Persistent=true#Persistent=true\nOnBootSec=5min#
an OnUnitActiveSec=|s#^Persistent=true#Persistent=true\nOnUnitActiveSec=1h#
an OnActiveSec=|s#^Persistent=true#Persistent=true\nOnActiveSec=1min#
an OnStartupSec=|s#^Persistent=true#Persistent=true\nOnStartupSec=1min#
Persistent removed|/^Persistent=true$/d
RandomizedDelaySec removed|/^RandomizedDelaySec=/d
Unit= pointing elsewhere|s#^Unit=.*#Unit=maran-agent.service#
WantedBy changed|s#^WantedBy=timers.target#WantedBy=multi-user.target#
TIMER_CONTROLS

  [ "$planted_count" -eq 35 ] \
    || fail "the panel backup unit checkers ran ${planted_count} planted controls, not the 35 they carry (2 accepted, 24 service and 9 timer refusals). A control dropped from a list is a check that no longer exists."

  rm -rf -- "$work"
  echo "maran-panel-backup.service is one fixed ExecStart run as root with NoNewPrivileges, PrivateTmp, ProtectHome, ProtectSystem=strict and exactly three writable paths, a finite timeout and no [Install]; maran-panel-backup.timer carries no trigger of its own. The checkers refused all 33 planted breaks and accepted the 2 commented-out inverse controls."
  echo "UNOBSERVED HERE: this reads the unit files' TEXT. A directive the running systemd does not know is logged and ignored, and nothing here ran the service under its sandbox: that ReadWritePaths= leaves the verb what it needs (runuser, gpg, pg_dump, SELinux on AlmaLinux) is the threat note's unverified item 4 and belongs to a booted host."
}

# assert_step_67_installs_the_panel_backup_units_with_exact_modes_and_enables_nothing: step 67's own
# step_panel_backup is RUN and the files it left are looked at.
#
# Looked at, and not assumed: the two units are root:root 0644 and byte-identical to the package's
# (a unit file another uid can write is a root command line that uid can choose); the output
# directory and the verb's staging directory are root:root 0700 and real directories; the example
# configuration is installed where the step says and is NOT the live /etc/maran/panel-backup.conf;
# and the timer is not enabled. The last is the one claim a text search cannot make, so it is asked of
# the stand-in's enablement record, after a positive control shows the record exists for this timer.
#
# Then three refusals of the step itself: a symbolic link planted at the output directory (root's
# backups would be written through it), a loosened mode (put right), and a re-run over an existing
# archive and schedule (left exactly as they were).
#
# UNOBSERVED HERE: this image boots no systemd; "enabled" means "the polygon's systemctl was asked to
# enable it", the stand-in's own documented limit. The verification by systemd-analyze, where this
# image has it, is reported below with its own blind spot.
assert_step_67_installs_the_panel_backup_units_with_exact_modes_and_enables_nothing() {
  local output unit path observed work verdict aside target dropin_lines
  local state="${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_TIMER_FILE}.enabled"

  panel_backup_note_prior_state

  # The positive control for the enablement record: enabling this timer through the stand-in makes
  # the file appear. Without it, the file's absence after the step could be a stand-in that records
  # nothing for this unit.
  rm -f -- "$state"
  systemctl enable --now -- "$PANEL_BACKUP_TIMER_FILE" >/dev/null \
    || fail "the polygon's systemctl refused 'enable --now -- ${PANEL_BACKUP_TIMER_FILE}', so the enablement control below cannot be set up"
  [ -f "$state" ] \
    || fail "the polygon's systemctl enabled ${PANEL_BACKUP_TIMER_FILE} and recorded nothing at ${state}, so the file's absence after step 67 would say nothing about whether the step enabled the timer"
  rm -f -- "$state" "${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_TIMER_FILE}"

  output="$(run_panel_backup_step 2>&1)" \
    || fail "step 67's step_panel_backup failed in this image: ${output}"
  case "$output" in
    *"NOT enabled"*) ;;
    *) fail "step 67 ran but did not tell the operator the schedule is NOT enabled: ${output}" ;;
  esac

  for unit in "$PANEL_BACKUP_SERVICE_FILE" "$PANEL_BACKUP_TIMER_FILE"; do
    path="${PANEL_BACKUP_UNIT_DIR}/${unit}"
    assert_file_is "$path" "0:0:644"
    cmp -s "${INSTALLER_ROOT}/systemd/${unit}" "$path" \
      || fail "${path} differs from installer/systemd/${unit}: the step installed something other than what the package carries"
  done
  assert_file_is "$PANEL_BACKUP_EXAMPLE_INSTALLED" "0:0:644"
  cmp -s "$PANEL_BACKUP_EXAMPLE_SOURCE" "$PANEL_BACKUP_EXAMPLE_INSTALLED" \
    || fail "${PANEL_BACKUP_EXAMPLE_INSTALLED} differs from installer/panel-backup.conf.example"
  [ ! -e "$PANEL_BACKUP_CONFIG_FILE" ] && [ ! -L "$PANEL_BACKUP_CONFIG_FILE" ] \
    || fail "step 67 wrote ${PANEL_BACKUP_CONFIG_FILE}. The live configuration exists only because a human chose to create it; the step installs the documented example and nothing else"
  assert_directory_is "$PANEL_BACKUP_OUT_DIR" "0:0:700"
  assert_directory_is "$PANEL_BACKUP_STAGING_DIR" "0:0:700"

  # The units as installed, through the same checkers.
  observed="$(panel_backup_service_defect "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_SERVICE_FILE}")"
  [ -z "$observed" ] || fail "the INSTALLED ${PANEL_BACKUP_SERVICE_FILE} ${observed}"
  observed="$(panel_backup_timer_defect "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_TIMER_FILE}")"
  [ -z "$observed" ] || fail "the INSTALLED ${PANEL_BACKUP_TIMER_FILE} ${observed}"

  # Every writable path the service names exists after the step. ProtectSystem=strict makes a missing
  # ReadWritePaths= entry a start-up failure (226/NAMESPACE) at the first night the timer fires, not
  # at install time. The list is read from the INSTALLED unit, so a path added to it must be created
  # by the step or this build stops.
  for path in $(grep '^ReadWritePaths=' "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_SERVICE_FILE}" | sed 's/^ReadWritePaths=//'); do
    [ -d "$path" ] \
      || fail "${PANEL_BACKUP_SERVICE_FILE} names ${path} in ReadWritePaths= and it does not exist after step 67, so systemd would refuse to start the unit with 226/NAMESPACE"
  done

  # Not enabled: no .wants / .requires entry names either unit, and the stand-in recorded no
  # enablement. The wants search has its own positive control, in the manner of the php install unit.
  work="$(mktemp -d)"
  mkdir -p "${work}/timers.target.wants"
  ln -s "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_TIMER_FILE}" "${work}/timers.target.wants/${PANEL_BACKUP_TIMER_FILE}"
  [ -n "$(find "$work" -mindepth 2 -maxdepth 2 -name 'maran-panel-backup*' 2>/dev/null)" ] \
    || fail "the .wants search (find -mindepth 2 -maxdepth 2 -name 'maran-panel-backup*') does not find a planted link, so its empty answer over ${PANEL_BACKUP_UNIT_DIR} means nothing"
  rm -rf -- "$work"
  observed="$(find "$PANEL_BACKUP_UNIT_DIR" -mindepth 2 -maxdepth 2 -name 'maran-panel-backup*' 2>/dev/null | head -1 || true)"
  [ -z "$observed" ] \
    || fail "${observed} enables a panel backup unit. Step 67 must install both and enable neither: the operator writes the configuration and runs 'maran panel-backup-schedule enable'"
  [ ! -e "$state" ] \
    || fail "the polygon's systemctl was asked to enable ${PANEL_BACKUP_TIMER_FILE} during step 67 (${state} exists). The step must leave the timer to 'maran panel-backup-schedule enable'"
  [ ! -e "$PANEL_BACKUP_DROPIN_DIR" ] \
    || fail "step 67 created ${PANEL_BACKUP_DROPIN_DIR}. The schedule drop-in is written only by 'maran panel-backup-schedule enable', from a validated configuration"

  # A symbolic link at the output directory is refused, the link's target left alone, and the real
  # directory accepted again once the link is gone (the inverse control for the refusal).
  aside="${PANEL_BACKUP_OUT_DIR}.polygon-aside"
  target="$(mktemp -d)"
  chmod 0755 "$target"
  mv -- "$PANEL_BACKUP_OUT_DIR" "$aside"
  ln -s "$target" "$PANEL_BACKUP_OUT_DIR"
  if observed="$(run_panel_backup_step 2>&1)"; then
    rm -f -- "$PANEL_BACKUP_OUT_DIR"; mv -- "$aside" "$PANEL_BACKUP_OUT_DIR"; rm -rf -- "$target"
    fail "step 67 SUCCEEDED with ${PANEL_BACKUP_OUT_DIR} a symbolic link. Root's panel backups would be written through it to wherever it points: ${observed}"
  fi
  case "$observed" in
    *"symbolic link"*) ;;
    *) rm -f -- "$PANEL_BACKUP_OUT_DIR"; mv -- "$aside" "$PANEL_BACKUP_OUT_DIR"; rm -rf -- "$target"
       fail "step 67 refused the planted symbolic link but not for that reason: ${observed}" ;;
  esac
  [ "$(stat -c '%a %u' -- "$target")" = "755 0" ] && [ -z "$(ls -A -- "$target")" ] \
    || { rm -f -- "$PANEL_BACKUP_OUT_DIR"; mv -- "$aside" "$PANEL_BACKUP_OUT_DIR"; rm -rf -- "$target"
         fail "step 67 refused the link but still changed the mode of, or wrote into, the directory it points to"; }
  rm -f -- "$PANEL_BACKUP_OUT_DIR"
  mv -- "$aside" "$PANEL_BACKUP_OUT_DIR"
  rm -rf -- "$target"
  output="$(run_panel_backup_step 2>&1)" \
    || fail "step 67 refused the real output directory once the planted link was gone, so the refusal above may be the step refusing everything: ${output}"

  # A loosened mode is put right, and a re-run leaves an existing archive and an existing schedule
  # drop-in exactly as they were: an upgrade must not undo the operator's choice or touch a backup.
  chmod 0755 "$PANEL_BACKUP_OUT_DIR"
  printf 'a planted archive\n' > "${PANEL_BACKUP_OUT_DIR}/maran-panel-backup-20260101T000000Z-1.tar.gpg"
  install -d -m 0755 -o root -g root "$PANEL_BACKUP_DROPIN_DIR"
  printf '[Timer]\nOnCalendar=\nOnCalendar=daily\n' > "${PANEL_BACKUP_DROPIN_DIR}/schedule.conf"
  output="$(run_panel_backup_step 2>&1)" \
    || fail "step 67 failed on its second run (installers get re-run): ${output}"
  assert_directory_is "$PANEL_BACKUP_OUT_DIR" "0:0:700"
  printf 'a planted archive\n' | cmp -s - "${PANEL_BACKUP_OUT_DIR}/maran-panel-backup-20260101T000000Z-1.tar.gpg" \
    || fail "step 67's second run changed or removed an archive in ${PANEL_BACKUP_OUT_DIR}. The output directory is created and put right, never listed, moved or pruned by the installer"
  dropin_lines="$(cat "${PANEL_BACKUP_DROPIN_DIR}/schedule.conf" 2>/dev/null || true)"
  [ "$dropin_lines" = "$(printf '[Timer]\nOnCalendar=\nOnCalendar=daily')" ] \
    || fail "step 67's second run changed the operator's schedule drop-in; an upgrade must leave a schedule that was on, on"
  [ ! -e "$state" ] \
    || fail "step 67's second run enabled ${PANEL_BACKUP_TIMER_FILE}"
  rm -rf -- "$PANEL_BACKUP_DROPIN_DIR"
  rm -f -- "${PANEL_BACKUP_OUT_DIR}/maran-panel-backup-20260101T000000Z-1.tar.gpg"

  # systemd's own opinion of the files, where this image has the tool. The service is verified on its
  # own; the timer is verified WITH the drop-in the real `enable` code writes, because the shipped
  # timer has no trigger by design and a timer alone is not the unit that ever runs.
  if command -v systemd-analyze >/dev/null 2>&1; then
    # The wrapper has to exist: verify checks that ExecStart names an executable, and a unit whose
    # command is absent is refused for that and not for anything this assertion is about.
    run_cli_step >/dev/null || fail "step 66's step_cli could not install the command the service's ExecStart names"
    work="$(mktemp -d)"
    printf '[Unit]\nDescription=control\n\n[Service]\nType=oneshot\nExecStart=relative/path --scheduled\n' > "${work}/control.service"
    if systemd-analyze verify "${work}/control.service" >/dev/null 2>&1; then
      echo "UNOBSERVED HERE: systemd-analyze verify ACCEPTED a service whose ExecStart is a relative path, which systemd rejects on load, so in this image its acceptance of the two panel backup units means nothing and is not reported as a pass."
    else
      if ! verdict="$(systemd-analyze verify "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_SERVICE_FILE}" 2>&1)"; then
        rm -rf -- "$work"
        fail "systemd-analyze verify refused ${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_SERVICE_FILE}:
${verdict}"
      fi
      bash -c 'set -euo pipefail
. "$1"
. "$2"
cli_pbsched_write_dropin "$3"' _ "${CLI_LIB_SOURCE_DIR}/common.sh" "${CLI_LIB_SOURCE_DIR}/panel-backup-schedule.sh" '*-*-* 02:30:00' \
        || { rm -rf -- "$work"; fail "the real cli_pbsched_write_dropin could not write the schedule drop-in this verification needs"; }
      grep -qx 'OnCalendar=\*-\*-\* 02:30:00' "${PANEL_BACKUP_DROPIN_DIR}/schedule.conf" \
        || { rm -rf -- "$work"; fail "the drop-in the real enable code wrote does not carry the schedule it was given: $(cat "${PANEL_BACKUP_DROPIN_DIR}/schedule.conf")"; }
      if ! verdict="$(systemd-analyze verify "${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_TIMER_FILE}" 2>&1)"; then
        rm -rf -- "$work"
        fail "systemd-analyze verify refused ${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_TIMER_FILE} together with the drop-in 'enable' writes:
${verdict}"
      fi
      rm -rf -- "$PANEL_BACKUP_DROPIN_DIR"
      echo "systemd-analyze verify accepted the installed service, and the installed timer together with the drop-in the real enable code writes (it refused the planted relative-path ExecStart, so it can say no)."
    fi
    rm -rf -- "$work"
    remove_the_installed_break_glass_command
  else
    echo "UNOBSERVED HERE: no systemd-analyze in this image, so the installed units were not parsed by systemd."
  fi

  panel_backup_restore_prior_state
  echo "Step 67 installed ${PANEL_BACKUP_SERVICE_FILE} and ${PANEL_BACKUP_TIMER_FILE} as 644 root:root, the output directory and the staging directory as 700 root:root, the documented example (never a live configuration), enabled nothing, refused a symbolic link at ${PANEL_BACKUP_OUT_DIR}, put a loosened mode right and left an existing archive and schedule drop-in untouched on a re-run."
  echo "UNOBSERVED HERE: this image boots no systemd. 'Enabled' means the polygon's systemctl stand-in was asked; a timer firing at its schedule, Persistent=true catching up after downtime, and the service starting under ProtectSystem=strict are claims about a booted host (the install polygon's recovery stage invokes the service by name and says it did not wait for the clock)."
}

# panel_backup_grants_for_the_service: every file under the given directories that names the panel
# backup units, one per line. Used for the polkit and sudoers search; `-E` and the absolute grep
# because a shell here may define `grep` otherwise (rules/testing.md).
panel_backup_grants_for_the_service() {
  /usr/bin/grep -rlE 'maran-panel-backup' -- "$@" 2>/dev/null || true
}

# assert_the_panel_backup_service_cannot_be_started_by_a_non_root_user: nobody can start the job, edit
# its unit, or run its command, and nothing delegates it.
#
# Three things are observable here and one is not. Observable: the unit file is not writable by an
# unprivileged user (so its command line is not theirs to choose); the unit's ExecStart, taken
# verbatim from the INSTALLED unit and run as nobody, is refused before the verb reads anything while
# the same argv run as root gets past the identity check and fails later on the missing configuration
# (the inverse control: the refusal is about the caller and not about the command); and no polkit
# rule, sudoers entry, extra unit, socket or path unit names the job. NOT observable: that
# `systemctl start` by a non-root user is refused by polkit on a booted host, since the polygon's
# systemctl enforces no authorization.
#
# UNOBSERVED HERE: the booted half, named above. This is evidence that nothing GRANTS the job to
# anyone, which is what step 67 controls; it is not a measurement of what polkit does with a request.
assert_the_panel_backup_service_cannot_be_started_by_a_non_root_user() {
  local output status unit_path exec_line planted found
  local -a words
  panel_backup_note_prior_state

  run_cli_step >/dev/null || fail "step 66's step_cli could not install the command this assertion runs"
  run_panel_backup_step >/dev/null || fail "step 67's step_panel_backup could not install the units this assertion reads"
  unit_path="${PANEL_BACKUP_UNIT_DIR}/${PANEL_BACKUP_SERVICE_FILE}"

  # The unit file is not editable by an unprivileged user, and the test can say yes: control first.
  planted="$(mktemp)"
  chmod 0666 "$planted"
  run_as_nobody test -w "$planted" \
    || fail "'test -w' as nobody says a 0666 file is not writable, so the answer for the unit file means nothing"
  rm -f -- "$planted"
  if run_as_nobody test -w "$unit_path"; then
    fail "${unit_path} is writable by an unprivileged user; whoever can edit the unit chooses the command root runs"
  fi
  if run_as_nobody test -w "${PANEL_BACKUP_UNIT_DIR}"; then
    fail "${PANEL_BACKUP_UNIT_DIR} is writable by an unprivileged user"
  fi

  # The unit's own ExecStart, run as nobody and as root. read -a splits it into argv words; nothing is
  # evaluated, which is the point of the wrapper this exercises.
  exec_line="$(grep '^ExecStart=' "$unit_path" | head -1)"
  exec_line="${exec_line#ExecStart=}"
  read -r -a words <<<"$exec_line"
  [ "${#words[@]}" -eq 3 ] && [ "${words[2]}" = "--scheduled" ] \
    || fail "the installed ExecStart did not split into the three words of 'maran panel-backup --scheduled' (read: '${exec_line}')"

  status=0
  output="$(run_as_nobody "${words[@]}" 2>&1)" || status=$?
  [ "$status" -ne 0 ] \
    || fail "nobody ran the service's ExecStart successfully: ${output}"
  case "$output" in
    *"does not exist"*|*"Scheduled panel backup written"*) fail "nobody got as far as reading the panel backup configuration through the service's ExecStart: ${output}" ;;
  esac
  ls "${PANEL_BACKUP_OUT_DIR}"/maran-panel-backup-* >/dev/null 2>&1 \
    && fail "an archive appeared in ${PANEL_BACKUP_OUT_DIR} after nobody ran the service's ExecStart"

  status=0
  output="$("${words[@]}" 2>&1)" || status=$?
  [ "$status" -eq 1 ] \
    || fail "root running the service's ExecStart got exit ${status}, not the 1 of a missing configuration, so the refusal of nobody above may be the command refusing everyone: ${output}"
  case "$output" in
    *"${PANEL_BACKUP_CONFIG_FILE} does not exist"*) ;;
    *) fail "root running the service's ExecStart was not refused for the missing configuration: ${output}" ;;
  esac

  # Nothing delegates the job: no polkit rule or sudoers entry names it (search shown to find a
  # planted one), and exactly the two unit files exist under that name in the unit directories.
  planted="$(mktemp -d)"
  printf 'polkit.addRule(function(a, s) { return a.id == "org.freedesktop.systemd1.manage-units" && a.lookup("unit") == "maran-panel-backup.service" ? polkit.Result.YES : polkit.Result.NOT_HANDLED; });\n' > "${planted}/49-control.rules"
  [ -n "$(panel_backup_grants_for_the_service "$planted")" ] \
    || fail "the grant search (/usr/bin/grep -rlE 'maran-panel-backup') does not find a planted polkit rule, so its empty answer over polkit and sudoers means nothing"
  rm -rf -- "$planted"
  found="$(panel_backup_grants_for_the_service /etc/polkit-1 /usr/share/polkit-1 /etc/sudoers.d /etc/sudoers)"
  [ -z "$found" ] \
    || fail "a polkit rule or sudoers entry names the panel backup units (${found}); step 67 must grant the job to nobody"
  found="$(find /etc/systemd/system /usr/lib/systemd/system /lib/systemd/system -name 'maran-panel-backup*' 2>/dev/null | sed 's#.*/##' | sort -u | tr '\n' ' ')"
  [ "$found" = "maran-panel-backup.service maran-panel-backup.timer " ] \
    || fail "the unit directories hold '${found}' under the name maran-panel-backup*, not exactly the service and the timer. A .path or .socket unit, or an extra service, is another way to start the job"

  remove_the_installed_break_glass_command
  panel_backup_restore_prior_state
  echo "The panel backup service's unit file and directory are not writable by an unprivileged user, its ExecStart run as nobody is refused before the configuration is read while the same argv run as root is refused only for the missing configuration, and no polkit rule, sudoers entry or extra unit names the job."
  echo "UNOBSERVED HERE: that 'systemctl start maran-panel-backup.service' by a non-root user is refused by polkit. This image boots no systemd and the polygon's systemctl enforces no authorization; what is shown is that nothing grants the job, and that the command itself is root's alone."
}
