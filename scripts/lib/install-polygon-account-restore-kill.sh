#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# stage_account_restore_kill: the interrupted-restore leg of the issue #6 proof. The root agent is
# killed with a real SIGKILL (`systemctl kill -s SIGKILL maran-agent`: the whole control group, as an
# out-of-memory kill or a TimeoutStopSec overrun would end it) in the middle of a restore started
# through the panel's API, systemd restarts it, and the account is compared with the backup.
#
# What the agent promises, per window (docs/superpowers/notes/2026-09-09-restore-interruption-
# recovery-threat-note.md; ops/src/backup/recover_restores.rs, called by the agent before it binds
# its socket). Written BEFORE the code below, so the assertions are the note's and not the code's:
#
#   window       the kill lands            the account afterwards                    reconciliation
#   extraction   while the home is being   untouched: exactly the account as it     no marker exists, so the staging
#                extracted into staging    was when the restore began                tree is LEFT in place (warned);
#                                                                                    nothing swapped
#   databases    while a database is       home untouched; the database is NEITHER   no marker; the pre-restore dump is
#                being dropped and loaded  the old state nor the backup's. The       KEPT in the bulk scratch's rollback
#                                          threat note names this as not recovered   set. An operator (or the retry
#                                          automatically, an owner decision          below) finishes it
#   swap         the marker is on the      the backup's account, complete: the       FINISH FORWARD (a marker means every
#                disk: between the two     databases were already replaced, so the   database was replaced already)
#                renames or in finalising  only honest ending is the full restore
#
# In every window the retry of the same restore through the API must end in the backup's account
# (the retry removes whatever the interrupted one left behind), and the bulk scratch must hold no
# extracted dump afterwards.
#
# R2 (the kill must hit its window): the killer polls for a FACT about the window -- a staging tree
# of at least 20 MiB, a table that holds some rows but not all, the marker file -- and kills at once.
# A killer that never saw its window exits 3 and the leg FAILS; it is never read as a pass. For the
# swap window the state the kill left (home present or absent, parked home, staging) is read before
# systemd restarts the agent and printed, so which of the document's states was hit is a
# measurement. The marker must still be on the disk after the kill: a marker that is gone means
# the restore finished before the signal arrived, and that is a miss.
#
# R6 (the panel's own row): the HTTP answer of the killed restore and the task row it left are read
# and printed. The panel cannot know what the host did after the stream died, so the answer must not
# be a success, and a task row saying Completed over a restore that was killed is a finding.
#
# Reads the globals of install-polygon-account-restore.sh (acr_*, ACR_*) and of install-polygon.sh.

# The bulk the windows need to be wide enough to hit: a 128 MiB incompressible file, two thousand
# small files, and a table of 300000 rows. The stage measures an uninterrupted restore of it and
# prints the duration, so a window too narrow to hit shows up as a number rather than a flake.
ACR_BULK_BYTES=134217728
ACR_BULK_FILES=2000
ACR_BULK_ROWS=300000
ACR_KILL_TIMEOUT=300

# acr_bulk_script: writes the bulk tree, run AS THE ACCOUNT. Arguments: $1 = the home, $2 = bytes of
# the big file, $3 = how many small files.
acr_bulk_script() {
  cat <<'BULK'
set -euo pipefail
umask 022
home="$1"
mkdir -p "$home/bulk/small"
head -c "$2" /dev/urandom > "$home/bulk/big.bin"
for i in $(seq 1 "$3"); do
  printf 'small file %s\n' "$i" > "$home/bulk/small/f${i}.txt"
done
BULK
}

# acr_seed_bulk: adds the bulk to the seeded account. Returns 1 when it cannot be written, because a
# window that cannot be hit proves nothing.
acr_seed_bulk() {
  acr_bulk_script | docker exec -i "$container" runuser -u "$ACR_ACCOUNT" -- bash -s -- \
    "/home/${ACR_ACCOUNT}" "$ACR_BULK_BYTES" "$ACR_BULK_FILES" \
    || { note "account-restore: the bulk tree could not be written as the account, so the kill windows would be too narrow to hit"; return 1; }
  acr_sql "create table \`${ACR_DB}\`.big (id int primary key, payload varchar(255)) engine=InnoDB;
    insert into \`${ACR_DB}\`.big select seq, concat(md5(seq), repeat('z', 190)) from seq_1_to_${ACR_BULK_ROWS};" >/dev/null
  acr_expect "(kill) the bulk table holds its ${ACR_BULK_ROWS} rows" "$(acr_sql "select count(*) from \`${ACR_DB}\`.big")" "$ACR_BULK_ROWS"
  acr_expect "(kill) the big file has its size" "$(acr_x stat -c %s "/home/${ACR_ACCOUNT}/bulk/big.bin")" "$ACR_BULK_BYTES"
}

# acr_killer_script: the program that waits for a window and kills the agent, run in the container so
# the polling is not slowed by a round trip. Arguments: $1 = window (extraction, databases, swap),
# $2 = account, $3 = database, $4 = rows the table holds when complete, $5 = seconds to wait.
# Prints key=value lines: result=killed|missed and the state the kill left. Exits 3 on a miss.
acr_killer_script() {
  cat <<'KILLER'
set -u
window="$1"; account="$2"; db="$3"; full="$4"; limit="$5"
staging=/home/.maran-restore
q() { /usr/bin/mysql --protocol=socket --user=root --batch --skip-column-names "$@" 2>/dev/null; }
hit() {
  local entry size rows
  case "$window" in
    extraction)
      for entry in "$staging/${account}."*; do
        case "$entry" in *.swap|*.swap.partial|*".previous."*) continue ;; esac
        [ -d "$entry" ] || continue
        size="$(du -sb "$entry" 2>/dev/null | cut -f1)"
        [ "${size:-0}" -gt 20971520 ] && return 0
      done
      return 1 ;;
    databases)
      rows="$(q -e "select count(*) from \`${db}\`.big")"
      [ -n "$rows" ] && [ "$rows" -gt 0 ] && [ "$rows" -lt "$full" ] ;;
    swap) compgen -G "$staging/*.swap" >/dev/null ;;
    *) return 1 ;;
  esac
}
end=$((SECONDS + limit))
while ! hit; do
  if [ "$SECONDS" -ge "$end" ]; then echo "result=missed"; exit 3; fi
done
systemctl kill -s SIGKILL maran-agent || { echo "result=kill-failed"; exit 4; }
# The state the kill left, read before systemd's restart (RestartSec=2) can touch it.
echo "result=killed"
echo "home=$([ -d "/home/${account}" ] && echo dir || echo absent)"
echo "marker=$(compgen -G "$staging/*.swap" >/dev/null && echo present || echo absent)"
echo "previous=$(compgen -G "$staging/*.previous.*" >/dev/null && echo present || echo absent)"
echo "staging_entries=$(find "$staging" -mindepth 1 -maxdepth 1 2>/dev/null | wc -l)"
echo "rollback_sets=$(find /var/lib/maran-scratch -type d -name rollback 2>/dev/null | wc -l)"
echo "db_rows=$(q -e "select count(*) from \`${db}\`.big")"
KILLER
}

# acr_field: the value of key $1 in the key=value text $2, or nothing.
acr_field() { printf '%s\n' "$2" | sed -n "s/^$1=//p" | head -1; }

# acr_agent_back: 0 once the agent is active and the panel reports it connected, 1 after ~2 minutes.
acr_agent_back() {
  local active health
  for _ in $(seq 1 60); do
    active="$(acr_x systemctl is-active maran-agent)"
    health="$(acr_x curl -sk --max-time 5 https://127.0.0.1:8443/health)"
    if [ "$active" = active ]; then
      case "$health" in *'"agent":"connected"'*) return 0 ;; esac
    fi
    sleep 2
  done
  return 1
}

# acr_recovery_lines: the agent's start-up reconciliation lines in its journal, colour removed, one
# per line. The line is the one server::serve logs after recover_restores.
acr_recovery_lines() {
  acr_x journalctl --sync >/dev/null
  acr_x journalctl -u maran-agent --no-pager -o cat \
    | sed 's/\x1b\[[0-9;]*m//g' | grep 'interrupted restores reconciled' || true
}

# acr_tally: the counter $2 out of the reconciliation line $1, or 0 when the line does not carry it.
acr_tally() {
  local value
  value="$(printf '%s\n' "$1" | sed -n "s/.*[[:space:]]$2=\([0-9][0-9]*\).*/\1/p" | head -1)"
  echo "${value:-0}"
}

# acr_await_recovery: 0 once the journal holds more reconciliation lines than $1, 1 after ~1 minute.
acr_await_recovery() {
  for _ in $(seq 1 30); do
    [ "$(rec_lines "$(acr_recovery_lines)")" -gt "$1" ] && return 0
    sleep 2
  done
  return 1
}

# acr_kill_window: one window of the leg. Damages the account, starts a restore through the API,
# kills the agent when the window is reached, and checks the end state against the table in the
# header. Always ends by retrying the restore, so the next window starts from the backup's account.
#   $1 = extraction, databases or swap.
acr_kill_window() {
  local window="$1" tag="kill_$1" killer_out killer_pid restore_pid
  local state result before line http task retry_task kept dumps partial_rows
  killer_out="${ACR_WORK}/${tag}.killer"

  acr_destroy damage
  acr_snapshot "${tag}_pre" || return 0
  acr_differs "(${window}) the account differs from the backup before the restore (control)" K "${tag}_pre" "removed added mode"
  acr_expect "(${window}) the restore staging root is empty before the restore (a stale entry would trip the window early)" \
    "$(acr_staging_entries)" 0
  before="$(rec_lines "$(acr_recovery_lines)")"

  acr_restart_for_restore || return 0
  acr_sign_in || { note "account-restore: (${window}) no administrator token, so the restore could not be started"; return 0; }

  # The killer is armed first, then the restore starts: the killer waits for a window, it does not
  # race for one.
  acr_killer_script | docker exec -i "$container" bash -s -- \
    "$window" "$ACR_ACCOUNT" "$ACR_DB" "$ACR_BULK_ROWS" "$ACR_KILL_TIMEOUT" >"$killer_out" 2>&1 &
  killer_pid=$!
  ( acr_restore "$ACR_BACKUP_ID"; printf '%s' "$ACR_CODE" > "${ACR_WORK}/${tag}.code" ) &
  restore_pid=$!
  wait "$killer_pid" || true
  wait "$restore_pid" || true

  state="$(cat "$killer_out")"
  result="$(acr_field result "$state")"
  acr_expect "(${window}) the kill landed in its window (a miss fails the leg)" "$result" killed
  if [ "$result" != killed ]; then
    printf '%s\n' "$state" | sed 's/^/      killer: /' >&2
    acr_agent_back || true
    return 0
  fi
  echo "    account-restore: (${window}) killed the agent; state at the kill: home=$(acr_field home "$state") marker=$(acr_field marker "$state") parked-home=$(acr_field previous "$state") staging-entries=$(acr_field staging_entries "$state") rollback-sets=$(acr_field rollback_sets "$state") table-rows=$(acr_field db_rows "$state")"

  # R6: what the panel says about the restore whose stream died.
  http="$(cat "${ACR_WORK}/${tag}.code" 2>/dev/null || true)"
  task="$(rec_psql "select \"Status\" || ':' || \"Percent\" from tasks.\"PanelTasks\" where \"Kind\" = 'BackupRestore' order by \"StartedAt\" desc limit 1")"
  acr_expect "(${window}) the panel does not answer success for a restore whose agent was killed" \
    "$([[ "$http" =~ ^2 ]] && echo "success-${http}" || echo refused)" refused
  acr_expect "(${window}) the panel's task row does not say Completed for it" \
    "$([[ "$task" == Completed* ]] && echo "$task" || echo not-completed)" not-completed
  echo "    account-restore: (${window}) the panel's account of the killed restore: HTTP ${http:-none}, task row ${task:-none}"

  acr_expect "(${window}) systemd restarted the agent and the panel sees it" "$(acr_agent_back && echo back || echo gone)" back
  acr_expect "(${window}) the agent logged a start-up reconciliation of interrupted restores" \
    "$(acr_await_recovery "$before" && echo logged || echo silent)" logged
  line="$(acr_recovery_lines | tail -1)"
  echo "    account-restore: (${window}) reconciliation: $(printf '%s' "$line" | grep -oE '(swaps_[a-z_]+|refused|unmarked_left|rollback_sets_kept|scratch_entries_removed)=[0-9]+' | tr '\n' ' ')"
  acr_snapshot "${tag}_post" || return 0

  case "$window" in
    extraction)
      acr_same "(extraction) the account is exactly as it was before the restore began (nothing was swapped)" "${tag}_pre" "${tag}_post"
      acr_expect "(extraction) no swap was completed, rolled back or abandoned" \
        "$(( $(acr_tally "$line" swaps_completed) + $(acr_tally "$line" swaps_rolled_back) + $(acr_tally "$line" swaps_abandoned) ))" 0
      acr_expect "(extraction) the unmarked staging tree was left in place for an operator, as documented" \
        "$([ "$(acr_tally "$line" unmarked_left)" -ge 1 ] && echo left || echo "$(acr_tally "$line" unmarked_left)")" left
      ;;
    databases)
      acr_same "(databases) the home is untouched" "${tag}_pre" "${tag}_post" files
      partial_rows="$(awk -F'\t' -v t="${ACR_DB}.big" '$1 == t { print $2 }' "${ACR_WORK}/${tag}_post.db")"
      acr_expect "(databases) the bulk table is PARTLY loaded: neither empty as damaged nor complete as backed up (the documented, unrecovered third state; ${partial_rows:-no} of ${ACR_BULK_ROWS} rows)" \
        "$([[ "$partial_rows" =~ ^[0-9]+$ ]] && [ "$partial_rows" -gt 0 ] && [ "$partial_rows" -lt "$ACR_BULK_ROWS" ] && echo partial || echo "${partial_rows:-absent}")" partial
      kept="$(acr_x find /var/lib/maran-scratch -type d -name rollback | grep -c . || true)"
      dumps="$(acr_x find /var/lib/maran-scratch -path '*/rollback/*' -name '*.sql' -type f | grep -c . || true)"
      acr_expect "(databases) the pre-restore dump survived the agent's restart in a rollback set" \
        "$([ "$kept" -ge 1 ] && [ "$dumps" -ge 1 ] && echo kept || echo "sets=${kept} dumps=${dumps}")" kept
      acr_expect "(databases) the unmarked staging tree was left in place for an operator, as documented" \
        "$([ "$(acr_tally "$line" unmarked_left)" -ge 1 ] && echo left || echo "$(acr_tally "$line" unmarked_left)")" left
      ;;
    swap)
      acr_expect "(swap) the marker was still on the disk after the kill (otherwise the restore had finished: a miss)" \
        "$(acr_field marker "$state")" present
      acr_same "(swap) the agent finished the restore FORWARD: the account is the backup's, on every axis" K "${tag}_post"
      acr_expect "(swap) the reconciliation completed or recognised the swap and abandoned nothing" \
        "$([ $(( $(acr_tally "$line" swaps_completed) + $(acr_tally "$line" swaps_already_done) )) -ge 1 ] \
           && [ "$(acr_tally "$line" swaps_abandoned)" = 0 ] && [ "$(acr_tally "$line" refused)" = 0 ] && echo finished || echo "$line")" finished
      acr_expect "(swap) the marker and the staging root are empty after the reconciliation" "$(acr_staging_entries)" 0
      ;;
  esac
  acr_expect "(${window}) the bulk scratch holds nothing but kept rollback sets after the restart" "$(acr_scratch_litter)" 0

  # The retry: the same restore, through the API, converges to the backup's account whatever the
  # interrupted one left behind.
  acr_restart_for_restore || return 0
  acr_sign_in || return 0
  acr_restore "$ACR_BACKUP_ID"
  acr_expect "(${window}) the retry of the same restore answers 200" "$ACR_CODE" 200
  [ "$ACR_CODE" = 200 ] || printf '%s\n' "${ACR_BODY:0:300}" | sed 's/^/      retry: /' >&2
  retry_task="$(rec_psql "select \"Status\" || ':' || \"Percent\" from tasks.\"PanelTasks\" where \"Kind\" = 'BackupRestore' order by \"StartedAt\" desc limit 1")"
  acr_expect "(${window}) the retry's task row is Completed" "$retry_task" "Completed:100"
  acr_snapshot "${tag}_retry" || return 0
  acr_same "(${window}) after the retry the account is identical to the backup on every axis" K "${tag}_retry"
  acr_expect "(${window}) the retry left the restore staging root empty" "$(acr_staging_entries)" 0
  acr_expect "(${window}) the retry left no rollback set behind" \
    "$(acr_x find /var/lib/maran-scratch -type d -name rollback | grep -c . || true)" 0
}

# stage_account_restore_kill: seeds the bulk, backs the account up again, measures an uninterrupted
# restore, then kills the agent in each window.
stage_account_restore_kill() {
  local started seconds
  acr_seed_bulk || return 0
  acr_snapshot K || return 0
  acr_backup || return 0

  # An uninterrupted restore of the bulk first: it proves the bulk restores at all, so a kill window
  # that fails afterwards fails because of the kill, and it prints how long the restore lasts.
  acr_destroy damage
  acr_restart_for_restore || return 0
  started="$(date +%s)"
  acr_restore "$ACR_BACKUP_ID"
  seconds=$(( $(date +%s) - started ))
  acr_expect "(kill) an uninterrupted restore of the bulk account answers 200" "$ACR_CODE" 200
  acr_snapshot M || return 0
  acr_same "(kill) the uninterrupted restore of the bulk account is identical to its backup" K M
  echo "    account-restore: (kill) an uninterrupted restore of ${ACR_BULK_BYTES} bytes in ${ACR_BULK_FILES} + 1 files and ${ACR_BULK_ROWS} rows took about ${seconds}s"

  acr_kill_window extraction
  acr_kill_window databases
  acr_kill_window swap
  echo "    account-restore: interrupted-restore leg done: the root agent was killed with SIGKILL while a restore was extracting, replacing databases and swapping; each end state was compared with the backup"
  echo "    UNOBSERVED HERE: SIGKILL of the agent is not a power cut. Whether the marker and the renames reached the disk is the filesystem's fsync claim, reasoned in the restore-interruption note and not measured here."
  echo "    UNOBSERVED HERE: which of the swap window's states the kill hit is printed above for this run only; a different run may hit another, and the reconciliation's table covers all of them (agent/crates/agent/tests/restore_recovery_on_a_real_host.rs aims at the window between the two renames)."
}
