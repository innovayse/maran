#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# The scheduled-backup half of the panel-recovery stage (issue #129), run on the server that is
# going to "die": the configuration file and its refusals, three scheduled runs under KEEP=2, the
# upload to the S3 stand-in and its refusals, the schedule verb, and one run started by systemd
# itself. It leaves the newest archive's name in REC_UNIT_ARCHIVE for the restore half.
#
# Each refusal is paired with an acceptance of the same machinery: a configuration refused for being
# world-readable is followed, a few lines later, by the SAME configuration at mode 0600 being run.
# A check that only ever hands the product broken input passes against a product that refuses
# everything.
#
# Reads these globals set by the stage: container, REC_WORK, REC_PASSPHRASE, REC_S3_ENABLED
# ("yes" when the stand-in is up and the container's curl can sign requests).

REC_BACKUP_CONF="/etc/maran/panel-backup.conf"
REC_OUT_DIR="/var/backups/maran-panel"
# In /etc/maran and not under /root, for the reason given beside REC_S3_KEY_ID_FILE.
REC_PASSPHRASE_FILE="/etc/maran/polygon-panel-passphrase"
REC_SHORT_PASSPHRASE_FILE="/etc/maran/polygon-short-passphrase"
REC_SCHEDULE=""
REC_STATUS=0
REC_OUT=""
REC_UNIT_ARCHIVE=""

# rec_write_config: writes /etc/maran/panel-backup.conf (root:root 0600) in the current container.
#   $1 = passphrase file, $2 = KEEP, $3 = SCHEDULE, $4 = "yes" to add the S3 keys,
#   $5 = one extra raw line, optional.
rec_write_config() {
  local text
  text="PASSPHRASE_FILE=$1"$'\n'"OUT_DIR=${REC_OUT_DIR}"$'\n'"KEEP=$2"$'\n'"SCHEDULE=$3"$'\n'
  if [ "$4" = "yes" ]; then
    text+="S3_ENDPOINT=https://${REC_S3_HOST}"$'\n'
    text+="S3_BUCKET=${REC_S3_BUCKET}"$'\n'"S3_REGION=${REC_S3_REGION}"$'\n'"S3_PREFIX=${REC_S3_PREFIX}"$'\n'
    text+="S3_ACCESS_KEY_ID_FILE=${REC_S3_KEY_ID_FILE}"$'\n'"S3_SECRET_FILE=${REC_S3_SECRET_FILE}"$'\n'
  fi
  [ -z "${5:-}" ] || text+="$5"$'\n'
  printf '%s' "$text" | rec_put_file "$container" 0600 "$REC_BACKUP_CONF"
}

# rec_run_scheduled: runs `maran panel-backup --scheduled` once. Sets REC_STATUS and REC_OUT
# (standard output and error together, which is everything an operator or a log would see).
rec_run_scheduled() {
  REC_STATUS=0
  REC_OUT="$(docker exec "$container" /usr/local/sbin/maran panel-backup --scheduled 2>&1 | tr -d '\r')" \
    || REC_STATUS=$?
}

# rec_timer_state: "enabled" or "not-enabled", as systemd answers for the backup timer.
rec_timer_state() {
  local state
  state="$(docker exec "$container" systemctl is-enabled maran-panel-backup.timer 2>/dev/null | tr -d '\r' || true)"
  if [ "$state" = "enabled" ]; then echo enabled; else echo not-enabled; fi
}

# rec_timer_calendar: the calendar expression(s) systemd itself holds for the timer. This is the
# server's own answer to "what will you run, and when", read from the manager and not from a file
# the product wrote, which is why it can disagree with the drop-in and say so.
rec_timer_calendar() {
  docker exec "$container" systemctl show -p TimersCalendar --value maran-panel-backup.timer 2>/dev/null \
    | tr -d '\r' || true
}

# rec_decoys: "intact" when the operator's own files in the backup directory are all still there, as
# they were planted, else "damaged". Asked before any run (the positive control: the probe can see
# them) and after retention (the question).
rec_decoys() {
  docker exec "$container" bash -c '
    cd "$1" \
      && [ -f polygon-keep-me.txt ] \
      && [ -f maran-panel-backup-20200101T000000Z-1.tar.gpg.part ] \
      && [ -L maran-panel-backup-20200101T000001Z-2.tar.gpg ] \
      && [ -d maran-panel-backup-20200101T000002Z-3.tar.gpg ] \
      && [ "$(cat /root/polygon-symlink-target)" = target ] \
      && echo intact || echo damaged' _ "$REC_OUT_DIR" | tr -d '\r'
}

# rec_expect_refused_run: one scheduled run that MUST be refused, with the four things a refusal
# owes: a non-zero status, no archive written (or removed), exactly one failure line in the
# journal, and no secret in what it printed.
#   $1 = what is being refused.
rec_expect_refused_run() {
  local label="$1" journal_before journal_after list_before
  journal_before="$(rec_journal_failures)"
  list_before="$(rec_archives "$REC_OUT_DIR")"
  rec_run_scheduled
  journal_after="$(rec_journal_failures)"
  recovery_expect "${label}: the scheduled backup is refused" "$(rec_refused "$REC_STATUS")" refused
  recovery_expect "${label}: the archives in the backup directory are exactly as before" \
    "$(rec_archives "$REC_OUT_DIR")" "$list_before"
  recovery_expect "${label}: exactly one failure line reaches the journal" \
    "$(( journal_after - journal_before ))" 1
  recovery_expect "${label}: nothing it printed holds a secret" "$(rec_leak_count "$REC_OUT")" 0
}

# rec_expect_good_run: one scheduled run that MUST succeed and write a new archive. Sets
# REC_NEW_ARCHIVE to its name.
#   $1 = what is being run.
rec_expect_good_run() {
  local label="$1" list_before
  list_before="$(rec_archives "$REC_OUT_DIR")"
  rec_run_scheduled
  recovery_expect "${label}: succeeds" "$REC_STATUS" 0
  REC_NEW_ARCHIVE="$(rec_archives "$REC_OUT_DIR" | tail -1)"
  recovery_expect "${label}: writes an archive that was not there before" \
    "$([ -n "$REC_NEW_ARCHIVE" ] && ! printf '%s\n' "$list_before" | grep -qxF -- "$REC_NEW_ARCHIVE" && echo new || echo not-new)" new
  recovery_expect "${label}: nothing it printed holds a secret" "$(rec_leak_count "$REC_OUT")" 0
}

# rec_schedule_refusals: the configuration the job reads is refused when it is unsafe, and the
# refusals change nothing. No archive exists yet at this point, so "no new archive" is exact.
rec_schedule_refusals() {
  local status
  # The leak counter must be able to see what it counts for: a text holding the passphrase is flagged.
  recovery_expect "the secret scanner flags a text that holds the passphrase (positive control)" \
    "$(rec_leak_count "prefix ${REC_PASSPHRASE} suffix")" 1

  rec_write_config "$REC_PASSPHRASE_FILE" 2 "$REC_SCHEDULE" no
  docker exec "$container" chmod 0644 "$REC_BACKUP_CONF"
  rec_expect_refused_run "a world-readable configuration"

  rec_write_config "$REC_PASSPHRASE_FILE" 2 "$REC_SCHEDULE" no
  docker exec "$container" bash -c 'mv "$1" "$2" && ln -s "$2" "$1"' _ "$REC_BACKUP_CONF" /root/polygon-panel-backup.conf.real
  rec_expect_refused_run "a symlinked configuration"
  docker exec "$container" bash -c 'rm -f "$1" && mv "$2" "$1"' _ "$REC_BACKUP_CONF" /root/polygon-panel-backup.conf.real

  rec_write_config "$REC_PASSPHRASE_FILE" 2 "*-*-* 02:30:00; touch /root/polygon-pwned" no
  rec_expect_refused_run "a SCHEDULE holding a shell command"
  status=0
  docker exec "$container" /usr/local/sbin/maran panel-backup-schedule enable >/dev/null 2>&1 || status=$?
  recovery_expect "enable with a bad SCHEDULE is refused" "$(rec_refused "$status")" refused
  recovery_expect "enable with a bad SCHEDULE leaves the timer disabled" "$(rec_timer_state)" not-enabled
  recovery_expect "enable with a bad SCHEDULE installs no drop-in for systemd to read" \
    "$(docker exec "$container" systemctl show -p DropInPaths --value maran-panel-backup.timer | tr -d '\r')" ""

  rec_write_config "$REC_PASSPHRASE_FILE" 2 "$REC_SCHEDULE" no "ExecStartPre=/bin/touch /root/polygon-pwned"
  rec_expect_refused_run "a configuration line that names no known key"
  recovery_expect "no refused configuration ever ran a command" \
    "$(docker exec "$container" test -e /root/polygon-pwned && echo ran || echo never-ran)" never-ran
}

# rec_schedule_runs: the accepted path. The same configuration that was refused for its mode is now
# 0600 and runs; KEEP=2 across three runs keeps the newest two and nothing else of the operator's;
# a failed backup deletes nothing; and, when the stand-in is up, the upload and its refusals.
rec_schedule_runs() {
  local -a names=()
  local n

  rec_write_config "$REC_PASSPHRASE_FILE" 2 "$REC_SCHEDULE" "$REC_S3_ENABLED"

  if [ "$REC_S3_ENABLED" = "yes" ]; then
    rec_s3_write_credentials "$REC_S3_KEY_ID" "$REC_S3_SECRET"
    # Certificate verification is on: before the stand-in's certificate is trusted, the upload
    # must fail at the handshake, leave the backup in place, and send the stand-in nothing.
    rec_expect_good_run_but_upload_refused
    sleep 2
    rec_s3_trust
    recovery_expect "once its certificate is trusted, the stand-in answers over TLS (also the control for the refusal above)" \
      "$(rec_s3_reachable && echo reachable || echo unreachable)" reachable
  fi

  for n in 1 2 3; do
    rec_expect_good_run "scheduled run ${n}"
    names[n]="$REC_NEW_ARCHIVE"
    sleep 2
  done
  recovery_expect "the three runs wrote three differently named archives" \
    "$(printf '%s\n' "${names[1]}" "${names[2]}" "${names[3]}" | sort -u | grep -c . || true)" 3
  recovery_expect "KEEP=2 across three runs leaves exactly the newest two archives" \
    "$(rec_archives "$REC_OUT_DIR")" "$(printf '%s\n%s' "${names[2]}" "${names[3]}")"
  recovery_expect "retention left the operator's own file, the partial file, the link and the folder alone" \
    "$(rec_decoys)" intact

  # A failed backup deletes nothing. KEEP=1 with two archives present is what a retention that ran
  # BEFORE (or regardless of) the backup would act on; a passphrase too short for the verb to
  # accept makes the backup fail. The runs above are the inverse control: the same retention does
  # delete when the backup succeeds.
  rec_write_config "$REC_SHORT_PASSPHRASE_FILE" 1 "$REC_SCHEDULE" no
  rec_expect_refused_run "a backup that fails (passphrase too short) under KEEP=1 with two archives present"
  rec_write_config "$REC_PASSPHRASE_FILE" 2 "$REC_SCHEDULE" "$REC_S3_ENABLED"

  if [ "$REC_S3_ENABLED" = "yes" ]; then
    rec_schedule_uploads "${names[@]:1:3}"
  fi
}

# rec_expect_good_run_but_upload_refused: a run whose backup works and whose upload cannot, because
# the certificate is not trusted: refused, the archive kept, one journal line, nothing sent.
rec_expect_good_run_but_upload_refused() {
  local journal_before journal_after
  journal_before="$(rec_journal_failures)"
  rec_run_scheduled
  journal_after="$(rec_journal_failures)"
  recovery_expect "an upload to an untrusted certificate is refused (the job fails)" "$(rec_refused "$REC_STATUS")" refused
  recovery_expect "the archive of a run whose upload failed is kept" \
    "$(rec_lines "$(rec_archives "$REC_OUT_DIR")")" 1
  recovery_expect "an untrusted certificate sends the stand-in no request at all" "$(rec_s3_requests '^PUT ')" 0
  recovery_expect "the journal records the refused upload with no HTTP status (it never got that far)" \
    "$(rec_journal_matches 'verb=panel-backup-upload outcome=rejected-000')" 1
  recovery_expect "exactly one failure line reaches the journal for the refused upload" \
    "$(( journal_after - journal_before ))" 1
  recovery_expect "nothing the refused upload printed holds a secret" "$(rec_leak_count "$REC_OUT")" 0
}

# rec_schedule_uploads: what the stand-in holds after three good runs, and the two ways it refuses.
#   $1..$3 = the names of the three archives, oldest first.
rec_schedule_uploads() {
  local list_before remote_before forbidden_before newest_before local_size remote_size
  local journal_before journal_after variant
  recovery_expect "the stand-in holds all three archives, including the one retention removed here" \
    "$(rec_s3_objects)" "$(printf '%s\n%s\n%s' "$1" "$2" "$3")"
  local_size="$(docker exec "$container" stat -c %s "${REC_OUT_DIR}/$3" | tr -d '\r')"
  remote_size="$(docker exec "$REC_S3_CONTAINER" stat -c %s "${REC_S3_OBJECT_DIR}/$3" | tr -d '\r')"
  recovery_expect "the stored object is exactly the local archive's size" "$remote_size" "$local_size"
  recovery_expect "no half-written object is left in the bucket" \
    "$(docker exec "$REC_S3_CONTAINER" find "$REC_S3_STORE" -name '*.partial' | wc -l | tr -d '\r ')" 0

  # Refused credentials: a wrong key id and a wrong secret. The second is the one that shows the
  # stand-in checks signatures and not only names.
  for variant in wrong-key-id wrong-secret; do
    list_before="$(rec_archives "$REC_OUT_DIR")"
    newest_before="$(printf '%s\n' "$list_before" | tail -1)"
    remote_before="$(rec_s3_objects)"
    forbidden_before="$(rec_s3_requests '^PUT .* 403$')"
    journal_before="$(rec_journal_failures)"
    case "$variant" in
      wrong-key-id) rec_s3_write_credentials "POLYWRONGKEYID00" "$REC_S3_SECRET" ;;
      wrong-secret) rec_s3_write_credentials "$REC_S3_KEY_ID" "polygon-wrong-secret-value" ;;
    esac
    rec_run_scheduled
    journal_after="$(rec_journal_failures)"
    recovery_expect "${variant}: the upload is refused and the job fails" "$(rec_refused "$REC_STATUS")" refused
    recovery_expect "${variant}: the stand-in answered 403 to exactly one PUT" \
      "$(( $(rec_s3_requests '^PUT .* 403$') - forbidden_before ))" 1
    recovery_expect "${variant}: no object was stored" "$(rec_s3_objects)" "$remote_before"
    recovery_expect "${variant}: the backup itself was kept locally" \
      "$([ "$(rec_archives "$REC_OUT_DIR" | tail -1)" != "$newest_before" ] && echo kept || echo missing)" kept
    recovery_expect "${variant}: exactly one failure line reaches the journal" "$(( journal_after - journal_before ))" 1
    recovery_expect "${variant}: nothing it printed holds a secret" "$(rec_leak_count "$REC_OUT")" 0
    sleep 2
  done
  rec_s3_write_credentials "$REC_S3_KEY_ID" "$REC_S3_SECRET"
  recovery_expect "the journal records the two refused uploads with the storage's 403" \
    "$(rec_journal_matches 'verb=panel-backup-upload outcome=rejected-403')" 2
  recovery_expect "the journal records the three verified uploads" \
    "$(rec_journal_matches 'verb=panel-backup-upload outcome=uploaded')" 3
}

# rec_schedule_unit: the schedule verb and the unit systemd itself runs. `enable` makes systemd
# hold the configured calendar; the service started BY NAME proves the unit's sandbox leaves the
# verb everything it needs (gpg, runuser to the service account, the staging directory, the
# network for the upload); `disable` takes the timer away and leaves the archives. The clock is
# not waited for: the output says so.
rec_schedule_unit() {
  local status out list_before journal_before journal_after remote_before wanted
  wanted="${REC_SCHEDULE#\*-\*-\* }"
  list_before="$(rec_archives "$REC_OUT_DIR")"

  status=0
  out="$(docker exec "$container" /usr/local/sbin/maran panel-backup-schedule enable 2>&1 | tr -d '\r')" || status=$?
  recovery_expect "panel-backup-schedule enable succeeds on a sound configuration" "$status" 0
  recovery_expect "the timer is enabled" "$(rec_timer_state)" enabled
  recovery_expect "the timer is armed" \
    "$(docker exec "$container" systemctl is-active maran-panel-backup.timer | tr -d '\r' || true)" active
  recovery_expect "systemd holds the calendar the configuration named" \
    "$(case "$(rec_timer_calendar)" in *"$wanted"*) echo yes ;; *) echo no ;; esac)" yes
  status=0
  docker exec "$container" /usr/local/sbin/maran panel-backup-schedule status >/dev/null 2>&1 || status=$?
  recovery_expect "panel-backup-schedule status exits 0 while the timer is enabled" "$status" 0
  status=0
  out="$(docker exec "$container" systemd-analyze verify maran-panel-backup.service maran-panel-backup.timer 2>&1 \
    | tr -d '\r')" || status=$?
  recovery_expect "systemd-analyze verify accepts the service and the timer" "$status" 0
  [ "$status" -eq 0 ] || printf '%s\n' "$out" | sed 's/^/      verify: /' >&2

  journal_before="$(rec_journal_failures)"
  remote_before="$(rec_lines "$(rec_s3_objects_or_none)")"
  status=0
  docker exec "$container" systemctl start maran-panel-backup.service >/dev/null 2>&1 || status=$?
  journal_after="$(rec_journal_failures)"
  recovery_expect "the service started by name runs the backup to success" "$status" 0
  recovery_expect "systemd records the run's result as success" \
    "$(docker exec "$container" systemctl show -p Result --value maran-panel-backup.service | tr -d '\r')" success
  REC_UNIT_ARCHIVE="$(rec_archives "$REC_OUT_DIR" | tail -1)"
  recovery_expect "the unit's run wrote a new archive" \
    "$(printf '%s\n' "$list_before" | grep -qxF -- "$REC_UNIT_ARCHIVE" && echo not-new || echo new)" new
  recovery_expect "the unit's run wrote no failure line" "$(( journal_after - journal_before ))" 0
  if [ "$REC_S3_ENABLED" = "yes" ]; then
    recovery_expect "the unit's run uploaded its archive" \
      "$(rec_lines "$(rec_s3_objects)")" "$(( remote_before + 1 ))"
  fi

  list_before="$(rec_archives "$REC_OUT_DIR")"
  status=0
  docker exec "$container" /usr/local/sbin/maran panel-backup-schedule disable >/dev/null 2>&1 || status=$?
  recovery_expect "panel-backup-schedule disable succeeds" "$status" 0
  recovery_expect "the timer is no longer enabled" "$(rec_timer_state)" not-enabled
  recovery_expect "systemd no longer holds the configured calendar" \
    "$(case "$(rec_timer_calendar)" in *"$wanted"*) echo yes ;; *) echo no ;; esac)" no
  recovery_expect "disable leaves every archive in place" "$(rec_archives "$REC_OUT_DIR")" "$list_before"
}

# rec_s3_objects_or_none: the stand-in's object names, or nothing when there is no stand-in, so the
# object count can be taken without asking whether one exists.
rec_s3_objects_or_none() {
  if [ "$REC_S3_ENABLED" = "yes" ]; then rec_s3_objects; fi
}

# rec_schedule_leg: the whole scheduled half, in order.
rec_schedule_leg() {
  REC_SCHEDULE="*-*-* $(date -u -d '+12 hours' '+%H:%M'):00"
  recovery_expect "the installer made the backup directory root-only" \
    "$(docker exec "$container" stat -c '%a %U %G' "$REC_OUT_DIR" 2>/dev/null | tr -d '\r' || true)" "700 root root"
  recovery_expect "the installer shipped no backup configuration" \
    "$(docker exec "$container" test -e "$REC_BACKUP_CONF" && echo present || echo absent)" absent
  recovery_expect "the installer did not enable the backup timer" "$(rec_timer_state)" not-enabled

  printf '%s\n' "$REC_PASSPHRASE" | rec_put_file "$container" 0600 "$REC_PASSPHRASE_FILE"
  printf 'too-short-1\n' | rec_put_file "$container" 0600 "$REC_SHORT_PASSPHRASE_FILE"
  docker exec "$container" bash -c '
    cd "$1"
    : > maran-panel-backup-20200101T000000Z-1.tar.gpg.part
    echo keep > polygon-keep-me.txt
    echo target > /root/polygon-symlink-target
    ln -s /root/polygon-symlink-target maran-panel-backup-20200101T000001Z-2.tar.gpg
    mkdir maran-panel-backup-20200101T000002Z-3.tar.gpg' _ "$REC_OUT_DIR" \
    || note "panel-recovery: could not plant the operator's own files in ${REC_OUT_DIR}"
  recovery_expect "the operator's own files are planted in the backup directory (positive control for the check after retention)" \
    "$(rec_decoys)" intact

  rec_schedule_refusals
  rec_schedule_runs
  rec_schedule_unit
}
