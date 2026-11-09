#!/usr/bin/env bash
# 89-mail.sh's uninstall half: remove_mail stops the one key-value unit Maran recorded and no other.
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The property: step 89-mail.sh records the key-value server unit it took over in
# /etc/maran/mail/key-value-unit, and installer/uninstall.sh `remove_mail` stops and disables
# exactly that unit. A redis or valkey unit the operator runs under another name is not Maran's, and
# a host with no record (installed before the record existed) has none of them touched. Guessing
# the unit from the OS family stopped an operator's own cache; the record is what replaced the guess.
#
# What it RUNS: the real remove_mail out of the real uninstall.sh, in a child with errexit of its
# own (see run_uninstaller), against the polygon's systemctl stand-in, which records the state a
# stop or a disable asks for. What it observes is therefore the stand-in's answer to is-active and
# is-enabled for each unit, plus the NOTE line remove_mail prints.
#
# UNOBSERVED HERE, stated in the output too: that a real systemd would stop a real daemon. This
# image boots no systemd and starts no mail or key-value server; "stopped" means the stand-in was
# asked to stop and disable that unit, and "survived" means it was not asked.
#
# Checks that must be able to fail: the verdict function below is run against the shipped
# uninstaller (it must ACCEPT, four times, over four record states) and against two planted
# mutants of it (it must REFUSE each), so a verdict that accepts everything cannot pass.

# The three key-value unit names remove_mail may be handed, and a unit it must never touch.
readonly MAIL_KV_UNITS="redis-server redis valkey"
readonly MAIL_OWN_UNITS="postfix dovecot rspamd"
readonly MAIL_UNRELATED_UNIT="nginx"
readonly MAIL_RECORD_DIRECTORY="/etc/maran/mail"
readonly MAIL_RECORD_FILE="${MAIL_RECORD_DIRECTORY}/key-value-unit"
readonly MAIL_NO_RECORD_NOTE="no record of a key-value server Maran took over"

# mail_unit_state_clear <unit>...: forget what the stand-in recorded for these units, so each reads
# its default again. The state directory is the stand-in's (UNIT_STATE_DIRECTORY).
mail_unit_state_clear() {
  local unit
  for unit in "$@"; do
    rm -f -- "${UNIT_STATE_DIRECTORY:?}/${unit:?}.service" "${UNIT_STATE_DIRECTORY:?}/${unit:?}.service.enabled"
  done
}

# mail_unit_run <unit>: put a unit in the state of a daemon somebody started and enabled.
mail_unit_run() {
  systemctl enable --now "${1}.service" >/dev/null 2>&1
}

# mail_unit_is_running <unit>: the stand-in's answer that it is both active and enabled.
mail_unit_is_running() {
  local active enabled
  active="$(systemctl is-active "${1}.service" 2>/dev/null || true)"
  enabled="$(systemctl is-enabled "${1}.service" 2>/dev/null || true)"
  [ "$active" = active ] && [ "$enabled" = enabled ]
}

# mail_unit_is_stopped <unit>: neither active nor enabled any more.
mail_unit_is_stopped() {
  local active enabled
  active="$(systemctl is-active "${1}.service" 2>/dev/null || true)"
  enabled="$(systemctl is-enabled "${1}.service" 2>/dev/null || true)"
  [ "$active" != active ] && [ "$enabled" != enabled ]
}

# mail_removal_defects <uninstaller> <record text or empty> <unit expected stopped or empty>:
# plant a host, run remove_mail from <uninstaller>, and print one line per way the outcome is wrong.
# Prints nothing when the outcome is exactly the expected one.
#
# The host: every key-value unit, every mail unit and an unrelated unit are running and enabled; the
# directory /etc/maran/mail exists (without it remove_mail does nothing at all); the record file
# holds <record text> when that is not empty. Expected: the mail units and <expected unit> are
# stopped and disabled, every other key-value unit and the unrelated unit are untouched, the
# directory is gone, and the NOTE line is printed exactly when no unit is expected.
mail_removal_defects() {
  local uninstaller="$1" record="$2" expected="$3"
  local unit output status=0

  mail_unit_state_clear $MAIL_KV_UNITS $MAIL_OWN_UNITS "$MAIL_UNRELATED_UNIT"
  rm -rf -- "${MAIL_RECORD_DIRECTORY:?}"
  mkdir -p "$MAIL_RECORD_DIRECTORY"
  if [ -n "$record" ]; then
    printf '%s\n' "$record" > "$MAIL_RECORD_FILE"
  fi
  for unit in $MAIL_KV_UNITS $MAIL_OWN_UNITS "$MAIL_UNRELATED_UNIT"; do
    mail_unit_run "$unit"
    # The plant must have taken, or "survived" below would be true of a unit that never ran.
    mail_unit_is_running "$unit" || echo "setup: the stand-in does not report ${unit} as running after being told to run it"
  done

  output="$(bash -c 'set -euo pipefail
. "$1"
remove_mail' _ "$uninstaller" 2>&1)" || status=$?
  [ "$status" -eq 0 ] || echo "remove_mail exited ${status}"

  for unit in $MAIL_OWN_UNITS; do
    mail_unit_is_stopped "$unit" || echo "${unit} was not stopped and disabled (so remove_mail did not do its work at all)"
  done
  for unit in $MAIL_KV_UNITS; do
    if [ "$unit" = "$expected" ]; then
      mail_unit_is_stopped "$unit" || echo "the recorded key-value unit ${unit} was not stopped and disabled"
    else
      mail_unit_is_running "$unit" || echo "the key-value unit ${unit} is not Maran's to stop and was stopped or disabled"
    fi
  done
  mail_unit_is_running "$MAIL_UNRELATED_UNIT" || echo "${MAIL_UNRELATED_UNIT} is not a mail unit and was stopped or disabled"
  [ ! -e "$MAIL_RECORD_DIRECTORY" ] || echo "${MAIL_RECORD_DIRECTORY} was not removed"

  if [ -z "$expected" ]; then
    case "$output" in
      *"$MAIL_NO_RECORD_NOTE"*) ;;
      *) echo "no key-value unit was expected, and the NOTE saying so was not printed" ;;
    esac
  else
    case "$output" in
      *"$MAIL_NO_RECORD_NOTE"*) echo "a key-value unit was recorded, and the no-record NOTE was printed anyway" ;;
    esac
  fi
}

# mail_mutant <out file> <original text fragment> <replacement>: write a copy of uninstall.sh with
# one fragment replaced, and refuse unless the fragment occurred exactly once and the copy differs.
# Replacement by shell parameter expansion on a quoted pattern, never sed: the fragments contain
# `$` and quotes, and a regex that silently matched nothing would hand back an unmutated copy that
# the verdict then rightly accepted, "proving" a refusal that never happened.
mail_mutant() {
  local out="$1" fragment="$2" replacement="$3" text count
  text="$(cat -- "$UNINSTALLER")"
  count="$(grep -cF -- "$fragment" "$UNINSTALLER" || true)"
  [ "$count" = 1 ] \
    || fail "the fragment '${fragment}' occurs ${count} times in uninstall.sh, not once; the planted break below would not be the one it claims to be. remove_mail changed shape; update ${BASH_SOURCE[0]}."
  printf '%s\n' "${text/"$fragment"/"$replacement"}" > "$out"
  cmp -s -- "$UNINSTALLER" "$out" && fail "the planted break for '${fragment}' did not change uninstall.sh"
  return 0
}

# assert_remove_mail_stops_only_the_recorded_key_value_unit: the whole subject of this part.
assert_remove_mail_stops_only_the_recorded_key_value_unit() {
  [ -r "$UNINSTALLER" ] || fail "${UNINSTALLER} is not in this image; the Dockerfile must COPY installer/uninstall.sh"
  [ ! -e "$MAIL_RECORD_DIRECTORY" ] \
    || fail "${MAIL_RECORD_DIRECTORY} already exists; this assertion removes it as remove_mail does and will not destroy a directory it did not create"

  local created_parent=no defects scratch mutant case_name record expected
  [ -d /etc/maran ] || { mkdir -p /etc/maran; created_parent=yes; }
  scratch="$(mktemp -d)"

  # The shipped uninstaller must ACCEPT each of four record states. Name, record text, expected unit.
  local cases=(
    "a recorded valkey beside an operator's redis-server and redis|valkey|valkey"
    "a recorded redis-server beside an operator's redis and valkey|redis-server|redis-server"
    "a record with a second line stops only the first line's unit|$(printf 'valkey\nredis-server')|valkey"
    "a record naming a unit that is not a key-value server stops none of them|nginx|"
    "no record at all stops none of them and says so||"
  )
  for case_name in "${cases[@]}"; do
    expected="${case_name##*|}"
    record="${case_name#*|}"
    record="${record%|*}"
    case_name="${case_name%%|*}"
    defects="$(mail_removal_defects "$UNINSTALLER" "$record" "$expected")"
    if [ -n "$defects" ]; then
      mail_unit_state_clear $MAIL_KV_UNITS $MAIL_OWN_UNITS "$MAIL_UNRELATED_UNIT"
      rm -rf -- "${MAIL_RECORD_DIRECTORY:?}" "$scratch"
      [ "$created_parent" = no ] || rmdir /etc/maran 2>/dev/null || true
      fail "remove_mail went wrong for ${case_name}:
${defects}"
    fi
  done

  # Planted break 1: the old behaviour, a guessed list. Every key-value unit joins the stop list
  # whatever the record says, so an operator's unit is stopped. The verdict must REFUSE it.
  mutant="${scratch}/uninstall-stops-every-key-value-unit.sh"
  mail_mutant "$mutant" 'units="postfix dovecot rspamd"' 'units="postfix dovecot rspamd redis-server redis valkey"'
  defects="$(mail_removal_defects "$mutant" "valkey" "valkey")"
  if [ -z "$defects" ]; then
    mail_unit_state_clear $MAIL_KV_UNITS $MAIL_OWN_UNITS "$MAIL_UNRELATED_UNIT"
    rm -rf -- "${MAIL_RECORD_DIRECTORY:?}" "$scratch"
    [ "$created_parent" = no ] || rmdir /etc/maran 2>/dev/null || true
    fail "the verdict accepted a remove_mail that stops every key-value unit; it cannot see an operator's unit being stopped, so the acceptances above prove nothing (rules/testing.md)"
  fi
  # The same break on a host with no record: nothing may be touched, and the verdict must refuse that too.
  defects="$(mail_removal_defects "$mutant" "" "")"
  if [ -z "$defects" ]; then
    mail_unit_state_clear $MAIL_KV_UNITS $MAIL_OWN_UNITS "$MAIL_UNRELATED_UNIT"
    rm -rf -- "${MAIL_RECORD_DIRECTORY:?}" "$scratch"
    [ "$created_parent" = no ] || rmdir /etc/maran 2>/dev/null || true
    fail "the verdict accepted a remove_mail that stops every key-value unit on a host with no record"
  fi

  # Planted break 2: the recorded unit is never added, so the unit Maran took over keeps running.
  mutant="${scratch}/uninstall-never-stops-the-recorded-unit.sh"
  mail_mutant "$mutant" 'redis-server|redis|valkey) units="$units $kv_unit" ;;' 'redis-server|redis|valkey) : ;;'
  defects="$(mail_removal_defects "$mutant" "valkey" "valkey")"
  if [ -z "$defects" ]; then
    mail_unit_state_clear $MAIL_KV_UNITS $MAIL_OWN_UNITS "$MAIL_UNRELATED_UNIT"
    rm -rf -- "${MAIL_RECORD_DIRECTORY:?}" "$scratch"
    [ "$created_parent" = no ] || rmdir /etc/maran 2>/dev/null || true
    fail "the verdict accepted a remove_mail that never stops the recorded unit; it cannot see the unit Maran took over surviving"
  fi

  mail_unit_state_clear $MAIL_KV_UNITS $MAIL_OWN_UNITS "$MAIL_UNRELATED_UNIT"
  rm -rf -- "${MAIL_RECORD_DIRECTORY:?}" "$scratch"
  [ "$created_parent" = no ] || rmdir /etc/maran 2>/dev/null || true
  echo "remove_mail stops the key-value unit 89-mail.sh recorded and none other: ${#cases[@]} record states accepted (a recorded valkey, a recorded redis-server, a second line ignored, an unlisted name, no record with its NOTE), and two planted breaks refused (every key-value unit guessed, the recorded unit never stopped). UNOBSERVED HERE: that a real systemd stops a real daemon — this image boots no systemd and the stand-in only records what it was asked."
}
