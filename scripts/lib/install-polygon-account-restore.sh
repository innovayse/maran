#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# stage_account_restore: the destroy-and-restore proof of issue #6, through the PRODUCT path, against
# the real systemd container every other stage of the install polygon uses.
#
#   "Create, restore, list and delete backups ... Restore is the one that deserves the most care."
#
# The agent's own polygon suites prove the restore operation (a real MariaDB, a real account, a
# real SIGKILL); the panel-recovery stage proves the PANEL's archive. Nothing proved the path a
# customer's data actually takes: an account with files and a database, backed up through the API,
# damaged, restored through the API, and compared. This stage is that path, and it is built so it can
# be seen to fail:
#
#   1. A FOURTH fresh server is installed (a restore must not run on a server another stage killed).
#   2. An account is seeded through the API: a static site, a database, and a tree of files of every
#      kind (a symlink, a dangling symlink, an empty directory, a setgid directory, a 0600 file,
#      binary content, a name with a space).
#   3. Snapshot A is taken (install-polygon-account-snapshot.sh), every axis of the snapshot is
#      shown a real change it must see (acr_live_controls), and the changes are reverted.
#   4. A backup is taken through POST /api/v1/backups, must be `completed` and `verified`, and its
#      sha256 is the artifact's.
#   5. The account is DAMAGED, not removed: files deleted, a mode changed, a stray file added, the
#      database dropped. Snapshot B must differ from A (the control that the damage is observable).
#   6. A restore with the wrong confirmation is refused and changes nothing.
#   7. The real restore (POST /api/v1/backups/{id}/restore, with the real ConfirmAccountUsername)
#      answers 200, the task is Completed, the audit entry exists, and snapshot C equals A on every
#      axis. A second identical restore converges to the same state.
#
# The interrupted-restore leg is install-polygon-account-restore-kill.sh.
#
# Reads these globals from install-polygon.sh: container, root, log_dir, os, arch, mode, family,
# note, recovery_expect, wait_for_panel, restart_panel_api, start_server_container, install_panel_into,
# installer_status, extra_containers, web_group; and from install-polygon-recovery.sh /
# install-polygon-panel-api.sh: rec_seed_panel, rec_call helpers, rec_psql, REC_ADMIN_*.
#
# Secrets: the database password the panel returns when it creates a database is never read into a
# variable that outlives the call and is never printed. The administrator's token travels as a header
# on `docker exec` argv, exactly as the other stages' does, for a container deleted at the end.

ACR_DOMAIN="polyrestore.test"
ACR_TOKEN=""
ACR_ACCOUNT_ID=""
ACR_SITE_ROOT=""
ACR_SITE_MARKER=""
ACR_SITE_OK="no"
ACR_BACKUP_ID=""
ACR_BACKUP_SHA=""
ACR_CODE=""
ACR_BODY=""

# acr_sign_in: a fresh administrator token (password, then the second factor the seed enrolled).
# Sets ACR_TOKEN; returns 1 when no token could be had.
acr_sign_in() {
  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  if [ -z "$REC_TOKEN" ]; then
    rec_second_factor "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD" "$REC_TOTP_SECRET"
  fi
  ACR_TOKEN="$REC_TOKEN"
  [ -n "$ACR_TOKEN" ]
}

# acr_call: one JSON request as the administrator, with a long timeout (a backup of a large account
# is synchronous) and ONE re-sign-in when the token has expired or the panel restarted. Sets ACR_CODE
# and ACR_BODY. The body is kept only for the caller to read and clear.
#   $1 = method, $2 = path, $3 = JSON body or empty.
acr_call() {
  local method="$1" path="$2" body="${3:-}" attempt out
  local -a args
  for attempt in 1 2; do
    args=(-sk --max-time 900 -X "$method" -H 'Content-Type: application/json' -w $'\n%{http_code}'
      -H "Authorization: Bearer ${ACR_TOKEN}")
    [ -z "$body" ] || args+=(--data "$body")
    out="$(docker exec "$container" curl "${args[@]}" "https://127.0.0.1:8443${path}" 2>/dev/null \
      | tr -d '\r')" || out=$'\n000'
    ACR_CODE="${out##*$'\n'}"
    ACR_BODY="${out%$'\n'*}"
    [ -n "$ACR_CODE" ] || ACR_CODE="000"
    if [ "$ACR_CODE" = "401" ] && [ "$attempt" -eq 1 ]; then
      acr_sign_in || return 0
      continue
    fi
    return 0
  done
}

# acr_files_script: the program that writes the account's proof tree, run AS THE ACCOUNT (a file the
# root user owns inside a home is not a state the product creates). Arguments: $1 = the home.
acr_files_script() {
  cat <<'FILES'
set -euo pipefail
umask 022
home="$1"
mkdir -p "$home/proof/bin" "$home/proof/nested/deep/empty" "$home/proof/shared"
printf 'alpha\n' > "$home/proof/alpha.txt"
printf 'a name with a space\n' > "$home/proof/with space.txt"
printf '#!/bin/sh\necho tool\n' > "$home/proof/bin/tool.sh"
chmod 0750 "$home/proof/bin/tool.sh"
printf 'top secret\n' > "$home/proof/secret.txt"
chmod 0600 "$home/proof/secret.txt"
head -c 65536 /dev/urandom > "$home/proof/binary.dat"
: > "$home/proof/zero-length.txt"
chmod 2750 "$home/proof/shared"
printf 'in the shared directory\n' > "$home/proof/shared/note.txt"
ln -s ../alpha.txt "$home/proof/nested/link-to-alpha"
ln -s nowhere "$home/proof/dangling"
FILES
}

# acr_seed: the account the proof destroys and restores: rec_seed_panel's plan, account, owner and
# second factor, then a database with rows, a static site with a file it serves, and the proof tree.
# Returns 1 when the panel cannot be driven at all, because everything after it would measure nothing.
acr_seed() {
  local databases
  rec_seed_panel || return 1
  ACR_TOKEN="$REC_ADMIN_TOKEN"
  ACR_ACCOUNT_ID="$(rec_psql "select \"Id\" from accounts.\"Accounts\" where \"Name\" = '${ACR_ACCOUNT}'")"
  acr_expect "the seeded account has an id the database, the site and the backup can name" \
    "$([ -n "$ACR_ACCOUNT_ID" ] && echo found || echo missing)" found
  [ -n "$ACR_ACCOUNT_ID" ] || return 1

  # The database goes through the panel (it records the database, and the restore replaces exactly
  # the databases the panel names). The response carries the one-time password of the database user;
  # it is cleared before anything else can print it.
  acr_call POST /api/v1/databases "{\"accountId\":\"${ACR_ACCOUNT_ID}\",\"name\":\"proof\",\"dbUserName\":\"proofuser\"}"
  ACR_BODY=""
  acr_expect "a database is created through the API" "${ACR_CODE:0:1}xx" 2xx
  databases="$(acr_sql "show databases like '${ACR_ACCOUNT}\\_%'")"
  acr_expect "the account has exactly one database on the host" "$(rec_lines "$databases")" 1
  ACR_DB="$(printf '%s\n' "$databases" | head -1)"
  [ -n "$ACR_DB" ] || return 1
  acr_sql "create table \`${ACR_DB}\`.notes (id int primary key, body text, qty int, payload blob) engine=InnoDB;
    insert into \`${ACR_DB}\`.notes values (1, 'first note', 10, unhex('00FF0A5C27')),
      (2, 'second, with \"quotes\" and apostrophes', 20, NULL), (3, '', 30, unhex('DEADBEEF'));
    create table \`${ACR_DB}\`.tags (name varchar(40) primary key, note_id int) engine=InnoDB;
    insert into \`${ACR_DB}\`.tags values ('red', 1), ('blue', 2);
    create table \`${ACR_DB}\`.empty_table (id int primary key) engine=InnoDB;" >/dev/null
  acr_expect "the seeded database holds its rows (control for every later database comparison)" \
    "$(acr_sql "select count(*) from \`${ACR_DB}\`.notes")" 3

  acr_files_script | docker exec -i "$container" runuser -u "$ACR_ACCOUNT" -- bash -s -- "/home/${ACR_ACCOUNT}" \
    || { note "account-restore: the proof tree could not be written into /home/${ACR_ACCOUNT} as the account"; return 1; }

  acr_seed_site
  return 0
}

# acr_seed_site: a static site, with a file it serves, so the restore is also observed from outside
# the files (a vhost whose root disappeared answers differently). Sets ACR_SITE_OK.
acr_seed_site() {
  local root_dir
  acr_call POST /api/v1/sites "{\"accountId\":\"${ACR_ACCOUNT_ID}\",\"domain\":\"${ACR_DOMAIN}\",\"backendType\":1}"
  acr_expect "a static site is created through the API" "${ACR_CODE:0:1}xx" 2xx
  root_dir="$(docker exec "$container" nginx -T 2>/dev/null | tr -d '\r' | awk -v d="$ACR_DOMAIN" '
    $1 == "server_name" { for (i = 2; i <= NF; i++) { n = $i; sub(/;$/, "", n); if (n == d) found = 1 } }
    found && $1 == "root" { r = $2; sub(/;$/, "", r); print r; exit }
    /^}/ { found = 0 }' || true)"
  if [ -z "$root_dir" ]; then
    note "account-restore: nginx -T names no document root for ${ACR_DOMAIN}, so the site cannot be observed through the restore"
    return 0
  fi
  ACR_SITE_ROOT="$root_dir"
  ACR_SITE_MARKER="polygon-restore-$(openssl rand -hex 8)"
  printf '%s\n' "$ACR_SITE_MARKER" \
    | docker exec -i "$container" runuser -u "$ACR_ACCOUNT" -- bash -c 'umask 022; cat > "$1"' _ "${root_dir}/proof-site.txt" \
    || note "account-restore: could not write the site's file under ${root_dir} as the account"
  if [ "$(acr_site_body)" = "$ACR_SITE_MARKER" ]; then ACR_SITE_OK="yes"; fi
  acr_expect "the site serves its file before the backup (control for the site checks after the restore)" "$ACR_SITE_OK" yes
}

# acr_site_body: what the static site answers for /proof-site.txt.
acr_site_body() {
  acr_x curl -s --max-time 10 -H "Host: ${ACR_DOMAIN}" http://127.0.0.1/proof-site.txt
}

# acr_audit_count: how many SUCCESSFUL restore entries the panel's audit journal holds.
acr_audit_count() {
  rec_psql "select count(*) from identity.\"AuditEvents\" where \"Action\" like '%BackupRestored' and \"Succeeded\""
}

# acr_backup: takes a backup of the account through the API and records its id and sha256. Scored:
# 201, completed, a sha256, then an explicit verification that must conclude `verified`. Returns 1
# when there is no backup to restore from.
acr_backup() {
  local status artifact_paths artifact_count
  acr_call POST /api/v1/backups "{\"accountId\":\"${ACR_ACCOUNT_ID}\"}"
  acr_expect "creating a backup through the API answers 201" "$ACR_CODE" 201
  ACR_BACKUP_ID="$(rec_json_field "$ACR_BODY" id)"
  ACR_BACKUP_SHA="$(rec_json_field "$ACR_BODY" sha256)"
  status="$(rec_json_field "$ACR_BODY" status)"
  acr_expect "the backup is completed" "$status" completed
  acr_expect "the backup carries a sha256" \
    "$([[ "$ACR_BACKUP_SHA" =~ ^[0-9a-f]{64}$ ]] && echo yes || echo no)" yes
  acr_expect "the backup knows it holds the account's database" \
    "$(printf '%s' "$ACR_BODY" | grep -o '"databaseCount":[0-9]*' | head -1 | cut -d: -f2)" 1
  [ "$status" = completed ] && [ -n "$ACR_BACKUP_ID" ] || { printf '%s\n' "${ACR_BODY:0:300}" | sed 's/^/      backup: /' >&2; return 1; }

  acr_call POST "/api/v1/backups/${ACR_BACKUP_ID}/verify"
  acr_expect "verifying the backup concludes (200)" "$ACR_CODE" 200
  acr_expect "the backup is verified" "$(rec_json_field "$ACR_BODY" verificationState)" verified

  # The artifact's own hash, from the file on the server: the sha256 the panel recorded is the
  # artifact's, not just a 64-hex string. Skipped, and said so, when the file is not found.
  artifact_paths="$(acr_x find /var -xdev -type f -name "*${ACR_BACKUP_ID}*" \( -name '*.tar' -o -name '*.tar.*' -o -name '*.tgz' \))"
  artifact_count="$(rec_lines "$artifact_paths")"
  if [ "$artifact_count" -eq 1 ]; then
    acr_expect "the recorded sha256 is the artifact file's own" \
      "$(acr_x sha256sum "$artifact_paths" | cut -d' ' -f1)" "$ACR_BACKUP_SHA"
  else
    echo "    UNOBSERVED HERE: the artifact file was not found as exactly one *.tar* file under /var (found ${artifact_count}), so the recorded sha256 was not compared with the file's."
  fi
  echo "    account-restore: backup ${ACR_BACKUP_ID} sha256 ${ACR_BACKUP_SHA}"
  return 0
}

# acr_destroy: DAMAGES the account without removing it. `drop` removes the database; `damage` empties
# the tables instead (the kill leg needs the database to exist). Both delete files, change a mode and
# leave a stray file the restore must not keep.
#   $1 = drop or damage.
acr_destroy() {
  local home="/home/${ACR_ACCOUNT}" uid gid
  uid="$(acr_x stat -c %u "$home")"
  gid="$(acr_x stat -c %g "$home")"
  docker exec "$container" bash -c '
    set -e
    home="$1"; uid="$2"; gid="$3"; site="$4"
    rm -rf -- "$home/proof/nested" "$home/proof/bin"
    chmod 0777 "$home/proof/secret.txt"
    printf "stray\n" > "$home/proof/stray.txt"
    chown "$uid:$gid" "$home/proof/stray.txt"
    [ -z "$site" ] || rm -f -- "$site/proof-site.txt"
  ' _ "$home" "$uid" "$gid" "$ACR_SITE_ROOT" >/dev/null 2>&1 \
    || note "account-restore: the account could not be damaged, so the restore has nothing to undo"
  case "$1" in
    drop) acr_sql "drop database \`${ACR_DB}\`" >/dev/null ;;
    damage) acr_sql "delete from \`${ACR_DB}\`.notes; delete from \`${ACR_DB}\`.tags; truncate table \`${ACR_DB}\`.big" >/dev/null ;;
  esac
}

# acr_restore: POST /api/v1/backups/{id}/restore. Sets ACR_CODE and ACR_BODY. The confirmation is the
# account's own user name unless $2 says otherwise: the stage never skips the product's guard.
#   $1 = backup id, $2 = the typed confirmation (default: the account's name).
acr_restore() {
  acr_call POST "/api/v1/backups/$1/restore" "{\"confirmAccountUsername\":\"${2-$ACR_ACCOUNT}\"}"
}

# acr_restart_for_restore: the restore endpoint allows three requests an hour per caller from an
# in-memory limiter; a restart of the panel API empties it. The limit is the product's, left in
# force, and this is the same separation the lockout stage uses.
acr_restart_for_restore() {
  restart_panel_api || { note "account-restore: the panel did not come back after a restart"; return 1; }
}

# stage_account_restore_whole: steps 3 to 7 of the header, on the seeded account.
stage_account_restore_whole() {
  local audit_before audit_after task

  acr_snapshot A || return 0
  acr_expect "snapshot A observes the proof tree, the site and the database (at least 12 entries, 3 tables)" \
    "$([ "$(acr_entries A files)" -ge 12 ] && [ "$(acr_entries A db)" -ge 4 ] && echo observed || echo "$(acr_entries A files)/$(acr_entries A db)")" observed
  acr_live_controls A
  acr_backup || return 0

  acr_destroy drop
  acr_snapshot B || return 0
  acr_differs "the damaged account differs from the backed-up one (control: the damage is observable)" \
    A B "removed added mode"
  acr_expect "the database is gone before the restore" "$(acr_sql "show databases like '${ACR_DB}'")" ""

  # The guard: a wrong confirmation, an empty one, and another account's name are all refused, and
  # NOTHING changes. Three requests, then a restart, then the real ones.
  acr_restart_for_restore || return 0
  acr_restore "$ACR_BACKUP_ID" "not-the-account"
  acr_expect "a restore typed with the wrong account name is refused (4xx)" "${ACR_CODE:0:1}xx" 4xx
  acr_restore "$ACR_BACKUP_ID" ""
  acr_expect "a restore with an empty confirmation is refused (4xx)" "${ACR_CODE:0:1}xx" 4xx
  acr_snapshot B_refused || return 0
  acr_same "after the refused restores the account is byte-for-byte as damaged" B B_refused

  acr_restart_for_restore || return 0
  audit_before="$(acr_audit_count)"
  acr_restore "$ACR_BACKUP_ID"
  acr_expect "the restore answers 200" "$ACR_CODE" 200
  [ "$ACR_CODE" = 200 ] || printf '%s\n' "${ACR_BODY:0:300}" | sed 's/^/      restore: /' >&2
  acr_expect "the restore reports the one database it replaced" \
    "$(printf '%s' "$ACR_BODY" | grep -o '"databasesRestored":[0-9]*' | head -1 | cut -d: -f2)" 1
  task="$(rec_psql "select \"Status\" || ':' || \"Percent\" from tasks.\"PanelTasks\" where \"Kind\" = 'BackupRestore' order by \"StartedAt\" desc limit 1")"
  acr_expect "the restore's task row is Completed at 100 percent" "$task" "Completed:100"
  audit_after="$(acr_audit_count)"
  acr_expect "one successful restore entry was added to the audit journal" "$((audit_after - audit_before))" 1

  acr_snapshot C || return 0
  acr_same "THE PROOF: after the restore the account is identical to the backed-up one on every axis" A C
  acr_expect "the restored database exists again" "$(acr_sql "show databases like '${ACR_DB}'")" "$ACR_DB"
  if [ "$ACR_SITE_OK" = yes ]; then
    acr_expect "the site's file is served again (the site root came back with the home)" "$(acr_site_body)" "$ACR_SITE_MARKER"
  fi
  acr_expect "nothing is left in the restore staging root" "$(acr_staging_entries)" 0
  acr_expect "nothing but (at most) rollback sets is left in the bulk scratch" "$(acr_scratch_litter)" 0

  # A second, identical restore converges to the same state: the operation is idempotent.
  acr_restart_for_restore || return 0
  acr_restore "$ACR_BACKUP_ID"
  acr_expect "the same restore, repeated, answers 200" "$ACR_CODE" 200
  acr_snapshot D || return 0
  acr_same "after the repeated restore the account is still identical to the backed-up one" A D
  acr_expect "the repeated restore was audited too" "$(( $(acr_audit_count) - audit_before ))" 2
  echo "    account-restore: whole-account leg done: damaged (files deleted, mode changed, file added, database dropped), restored through the API, identical on every axis, restored again"
}

# acr_staging_entries: how many entries the restore staging root holds.
acr_staging_entries() {
  acr_x find /home/.maran-restore -mindepth 1 -maxdepth 1 | grep -c . || true
}

# acr_scratch_litter: how many entries under the bulk scratch are neither the scratch's own two top
# levels (backup/ and backup/<id>/) nor part of a kept rollback set. Those two levels are what the
# agent leaves behind on purpose; everything else is litter.
acr_scratch_litter() {
  acr_x find /var/lib/maran-scratch -mindepth 1 \( -name rollback -o -path '*/rollback/*' \) -prune -o -print \
    | awk -F/ 'NF > 6 { n++ } END { print n + 0 }'
}

# acr_print_blind_spots: what this stage could not see, in its own output (rules/testing.md).
acr_print_blind_spots() {
  echo "    UNOBSERVED HERE: a power cut. The kernel is this machine's; the agent is killed with SIGKILL, which stops its process and not the machine, so what reached the disk by fsync is not tested."
  echo "    UNOBSERVED HERE: disk quota enforcement (the account was created with the setquota stand-in), SELinux enforcing, and a real filesystem with real inode pressure."
  echo "    UNOBSERVED HERE: a remote destination, an encrypted backup, and a partial restore. The backup here is local and whole."
  echo "    UNOBSERVED HERE: one account, one database, one site, one family per run (${os}, ${family}); the other families are proved by running this stage against them."
  echo "    UNOBSERVED HERE: restore under a concurrent customer workload (the site was idle, the database had no client)."
  acr_print_snapshot_blind_spots
}

# stage_account_restore: the stage, as the install polygon calls it. All failures are counted through
# `note`; the function itself never fails the harness, so a stage that stopped early is caught by the
# minimum-checks guard at the call site.
stage_account_restore() {
  local origin="$container" fresh="${container}-restore" fresh_log
  fresh_log="${log_dir}/install-restore-${os}-${arch}-${mode}.log"
  ACR_WORK="$(mktemp -d)"
  trap 'rm -rf -- "$ACR_WORK"' EXIT
  ACR_SITE_OK="no"

  # The comparison is proved before any server is touched: a snapshot that cannot see a change
  # certifies a restore that lost it.
  acr_selftest_comparison

  extra_containers+=("$fresh")
  if start_server_container "$fresh"; then
    install_panel_into "$fresh" "$fresh_log"
    container="$fresh"
    acr_expect "the fourth server's install exits 0 (log: ${fresh_log#"$root"/})" "$installer_status" 0
    if wait_for_panel; then
      if acr_seed; then
        stage_account_restore_whole
        stage_account_restore_kill
      fi
    else
      note "account-restore: the fourth server's panel never reported a reachable database; nothing below was observed (see ${fresh_log#"$root"/})"
    fi
    acr_print_blind_spots
  else
    note "account-restore: the fourth server's container did not boot, so nothing could be restored"
  fi

  container="$origin"
  rm -rf -- "$ACR_WORK"
  trap - EXIT
  return 0
}
