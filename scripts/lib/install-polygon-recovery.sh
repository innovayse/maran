#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# stage_panel_recovery: the closing criterion of issue #129, against the real systemd container
# every other stage of the install polygon uses.
#
#   "Back up a panel with accounts and logins, install fresh, restore: the panel starts with the
#    same administrators, accounts and decryptable secrets."
#
# What it does, in order:
#   1. On the installed panel, creates an administrator's second factor (a TOTP secret the panel
#      stores ENCRYPTED in its database), a hosting account and the account owner's login.
#   2. Runs the scheduled-backup half (install-polygon-schedule.sh) and a manual backup.
#   3. Takes the archive OFF the server, stops that server for good, and installs a second, fresh
#      server (a second container from the same image) whose keys are shown to DIFFER.
#   4. Refuses a restore with the wrong passphrase and shows the fresh panel unchanged; then
#      restores for real and shows the same administrator signs in with the same password AND the
#      same one-time-code secret (so the encrypted column decrypted under the restored key), the
#      account and its owner's login exist, and the fresh install's own host settings were kept.
#   5. Restores once more from the manual archive, to show that path restores too.
#
# install-polygon-recovery-guards.sh adds three observations to that round trip: the licence file
# travelling with the archive, a fresh panel that holds a schema the archive lacks, and the refusal
# of an archive from a newer release.
#
# A second container rather than uninstall-then-reinstall: it is a different server, so nothing of
# the first one's disk (a leftover system account, a database role, a key file) can leak into the
# proof, and the old server can be stopped to make "dead" mean dead.
#
# Reads these globals from install-polygon.sh: container, root, log_dir, os, arch, mode, family,
# note, recovery_expect, wait_for_panel, restart_panel_api, start_server_container,
# install_panel_into, installer_status, extra_containers.

REC_ADMIN_USER="polygonadmin"
REC_ADMIN_PASSWORD="Polygon-New-Password-2"
REC_ADMIN_EMAIL="polygon.admin@example.test"
REC_ACCOUNT_NAME="polyrecover"
REC_OWNER_PASSWORD="Polygon-Customer-Password-4"
REC_ADMIN_TOKEN=""
REC_TOTP_SECRET=""
REC_WORK=""
REC_PASSPHRASE=""
REC_S3_ENABLED="no"
REC_S3_REASON=""
REC_MANUAL_ARCHIVE=""

# The values captured on the server that dies, compared on the one that replaces it.
REC_OLD_ENCRYPTION_HASH=""
REC_OLD_SIGNING_HASH=""
REC_OLD_SETUP_HASH=""
REC_FRESH_SETUP_HASH=""
REC_FRESH_HOST_SETTINGS=""

# rec_seed_panel: the data the restore must bring back, created through the panel's own API: a plan,
# a hosting account, its owner's login, and a second factor on the administrator. Returns 1 when the
# panel cannot be driven at all, because everything after it would be measuring nothing.
rec_seed_panel() {
  local setup_token plan_id account_id link token secret_stored

  # The earlier stages left the sign-in limiter part-spent; a restart empties it (the account lock
  # lives in the database and is unaffected).
  restart_panel_api || { note "panel-recovery: the panel did not come back after a restart"; return 1; }

  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  if [ -z "$REC_TOKEN" ]; then
    setup_token="$(docker exec "$container" sed -n 's/^Setup__Token=//p' /etc/maran/panel.env 2>/dev/null | tr -d '\r' || true)"
    if [ "${#setup_token}" -ge 8 ]; then
      rec_call POST /api/v1/setup "{\"token\":\"${setup_token}\",\"username\":\"${REC_ADMIN_USER}\",\"email\":\"${REC_ADMIN_EMAIL}\",\"password\":\"${REC_ADMIN_PASSWORD}\"}"
      recovery_expect "creating the first administrator (no earlier stage did)" "$REC_CODE" 200
      rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
    fi
  fi
  if [ -z "$REC_TOKEN" ]; then
    note "panel-recovery: cannot sign in as ${REC_ADMIN_USER} with the password the lockout stage left, and no setup token is left to create one; nothing below was observed"
    return 1
  fi
  REC_ADMIN_TOKEN="$REC_TOKEN"

  # Account creation applies a disk quota, and the container's overlay filesystem has none to apply
  # it to, so the real `setquota` refuses. The suite images solve the same problem with this very
  # stand-in; it goes only into the server that is about to die, and the stage says so.
  docker cp "${root}/docker/polygon/stand-ins/setquota-stand-in.sh" "${container}:/usr/sbin/setquota" >/dev/null
  docker exec "$container" chmod 0755 /usr/sbin/setquota
  docker exec "$container" chown root:root /usr/sbin/setquota

  rec_call POST /api/v1/plans \
    '{"name":"Polygon recovery plan","diskQuotaMb":1024,"maxSites":2,"maxDatabases":1,"maxSftpUsers":1,"maxFtpUsers":0,"maxCronEntries":0,"maxPhpWorkersPerPool":2,"cpuQuotaPercent":{"kind":"unlimited"},"memoryMaxMb":{"kind":"unlimited"},"maxProcesses":{"kind":"unlimited"},"features":[]}' \
    "$REC_ADMIN_TOKEN"
  recovery_expect "creating a plan through the API" "${REC_CODE:0:1}xx" 2xx
  plan_id="$(rec_json_field "$REC_BODY" id)"
  [ -n "$plan_id" ] || { note "panel-recovery: the plan response carried no id: ${REC_BODY:0:200}"; return 1; }

  rec_call POST /api/v1/accounts \
    "{\"name\":\"${REC_ACCOUNT_NAME}\",\"primaryDomain\":\"${REC_ACCOUNT_NAME}.test\",\"planId\":\"${plan_id}\",\"ownerEmail\":\"owner@${REC_ACCOUNT_NAME}.test\"}" \
    "$REC_ADMIN_TOKEN"
  recovery_expect "creating a hosting account through the API" "${REC_CODE:0:1}xx" 2xx
  account_id="$(rec_json_field "$REC_BODY" id)"
  [ -n "$account_id" ] || { note "panel-recovery: the account response carried no id: ${REC_BODY:0:200}"; return 1; }
  recovery_expect "the agent created the account's system user" \
    "$(docker exec "$container" getent passwd "$REC_ACCOUNT_NAME" >/dev/null 2>&1 && echo present || echo absent)" present

  # The owner's login is made by a handler that runs after the account is stored, so the invitation
  # link is asked for until it exists.
  link=""
  for _ in $(seq 1 30); do
    rec_call POST "/api/v1/invitations/${account_id}/link" "" "$REC_ADMIN_TOKEN"
    if [ "$REC_CODE" = "200" ]; then
      link="$(rec_json_field "$REC_BODY" link)"
      break
    fi
    sleep 2
  done
  [ -n "$link" ] || { note "panel-recovery: no invitation link for the account owner (last answer ${REC_CODE})"; return 1; }
  token="${link##*token=}"
  token="$(printf '%b' "$(printf '%s' "$token" | sed 's/%\([0-9A-Fa-f][0-9A-Fa-f]\)/\\x\1/g')")"
  rec_call POST /api/v1/auth/accept-invitation "{\"token\":\"${token}\",\"newPassword\":\"${REC_OWNER_PASSWORD}\"}"
  recovery_expect "the account owner accepts the invitation" "${REC_CODE:0:1}xx" 2xx
  rec_login "$REC_ACCOUNT_NAME" "$REC_OWNER_PASSWORD"
  recovery_expect "the account owner signs in with the password they chose" "$REC_CODE" 200
  [ -n "$REC_TOKEN" ] || note "panel-recovery: the owner's sign-in carried no access token"

  # The encrypted secret: a second factor. The panel stores the TOTP secret encrypted with
  # Security__EncryptionKey; the API shows it once, here, in the clear.
  rec_call POST /api/v1/auth/two-factor/enrol "" "$REC_ADMIN_TOKEN"
  REC_TOTP_SECRET="$(rec_json_field "$REC_BODY" secret)"
  [ -n "$REC_TOTP_SECRET" ] || { note "panel-recovery: enrolling a second factor returned no secret (${REC_CODE})"; return 1; }
  rec_totp_fresh "$REC_TOTP_SECRET"
  rec_call POST /api/v1/auth/two-factor/confirm "{\"secret\":\"${REC_TOTP_SECRET}\",\"code\":\"${REC_TOTP_CODE}\"}" "$REC_ADMIN_TOKEN"
  recovery_expect "confirming the administrator's second factor" "$REC_CODE" 200

  # Encrypted at rest, observed in the column itself. The first query is the probe's own positive
  # control (it finds the row and a non-empty value), the third that `position` can find a
  # plaintext in this table at all; only then does the second one's zero mean anything.
  secret_stored="$(rec_psql "select count(*) from identity.\"Users\" where \"Username\" = '${REC_ADMIN_USER}' and length(coalesce(\"TotpSecret\", '')) > 0")"
  recovery_expect "the administrator's stored second-factor value is non-empty (probe control)" "$secret_stored" 1
  recovery_expect "the probe finds a plaintext in the same table (probe control)" \
    "$(rec_psql "select count(*) from identity.\"Users\" where position('${REC_ADMIN_USER}' in \"Username\") > 0 and \"Username\" = '${REC_ADMIN_USER}'")" 1
  recovery_expect "the second-factor secret is NOT stored in the clear" \
    "$(rec_psql "select count(*) from identity.\"Users\" where position('${REC_TOTP_SECRET}' in \"TotpSecret\") > 0")" 0

  rec_call GET /api/v1/accounts "" "$REC_ADMIN_TOKEN"
  recovery_expect "the account list shows the account (so the same check after the restore can see it)" \
    "$(case "$REC_BODY" in *"\"name\":\"${REC_ACCOUNT_NAME}\""*) echo listed ;; *) echo missing ;; esac)" listed
  return 0
}

# rec_precheck_sign_in: the sign-in the restore must reproduce, performed on the server that still
# lives. It is the control for the one-time-code generator: if the codes this harness computes were
# wrong, the sign-in after the restore would fail the same way a failed decryption does.
rec_precheck_sign_in() {
  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  recovery_expect "before the backup: the administrator's password alone no longer signs in (second factor required)" \
    "$REC_CODE/$([ -z "$REC_TOKEN" ] && echo no-session || echo session)" "200/no-session"
  rec_second_factor "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD" "$REC_TOTP_SECRET"
  recovery_expect "before the backup: password plus the harness's own code signs in (control for the code generator)" \
    "$REC_CODE/$([ -n "$REC_TOKEN" ] && echo session || echo no-session)" "200/session"
  rec_login "$REC_ADMIN_USER" "polygon-wrong-password-9"
  recovery_expect "before the backup: a wrong password is refused" "$REC_CODE" 401
}

# rec_manual_backup: `maran panel-backup --passphrase-file ... --out /root`, the manual verb, on the
# server that still lives. Sets REC_MANUAL_ARCHIVE to the path inside the container.
rec_manual_backup() {
  local status=0 out
  out="$(docker exec "$container" /usr/local/sbin/maran panel-backup --passphrase-file "$REC_PASSPHRASE_FILE" --out /root 2>&1 \
    | tr -d '\r')" || status=$?
  recovery_expect "the manual panel-backup succeeds" "$status" 0
  REC_MANUAL_ARCHIVE="$(printf '%s\n' "$out" | sed -n 's/^Panel backup: //p')"
  recovery_expect "it reports the archive it wrote" "$([ -n "$REC_MANUAL_ARCHIVE" ] && echo reported || echo silent)" reported
  recovery_expect "the archive is 0600 root" \
    "$(docker exec "$container" stat -c '%a %U' "$REC_MANUAL_ARCHIVE" 2>/dev/null | tr -d '\r' || true)" "600 root"
  recovery_expect "the manual backup printed no secret" "$(rec_leak_count "$out")" 0
}

# rec_take_archives_off: copies the archive the restore will use, and the manual one, out of the
# server and into the harness's own directory, together with a record of the old server's keys. The
# restore uses the copy from the S3 stand-in when there is one: the off-site copy is the one that
# survives a dead server, and it is shown byte-identical to the local archive first.
rec_take_archives_off() {
  local local_hash remote_hash
  REC_OLD_ENCRYPTION_HASH="$(rec_env_hash Security__EncryptionKey)"
  REC_OLD_SIGNING_HASH="$(rec_env_hash Jwt__SigningKey)"
  REC_OLD_SETUP_HASH="$(rec_env_hash Setup__Token)"
  recovery_expect "the old server's encryption, signing and setup values are all present (nothing to compare would make the later difference check vacuous)" \
    "$([ "$REC_OLD_ENCRYPTION_HASH" != "$REC_EMPTY_HASH" ] && [ "$REC_OLD_SIGNING_HASH" != "$REC_EMPTY_HASH" ] && [ "$REC_OLD_SETUP_HASH" != "$REC_EMPTY_HASH" ] && echo present || echo missing)" present

  if [ -z "$REC_MANUAL_ARCHIVE" ] || [ -z "$REC_UNIT_ARCHIVE" ]; then
    note "panel-recovery: there is no manual or no scheduled archive to take off the server, so nothing can be restored"
    return 1
  fi
  docker cp "${container}:${REC_MANUAL_ARCHIVE}" "${REC_WORK}/manual.tar.gpg" >/dev/null \
    || { note "panel-recovery: could not copy the manual archive out of the server"; return 1; }
  if [ "$REC_S3_ENABLED" = "yes" ]; then
    docker cp "${REC_S3_CONTAINER}:${REC_S3_OBJECT_DIR}/${REC_UNIT_ARCHIVE}" "${REC_WORK}/scheduled.tar.gpg" >/dev/null \
      || { note "panel-recovery: the unit's archive is not in the bucket, so the off-site copy cannot be restored"; return 1; }
    local_hash="$(docker exec "$container" sha256sum "${REC_OUT_DIR}/${REC_UNIT_ARCHIVE}" | cut -d' ' -f1 | tr -d '\r')"
    remote_hash="$(sha256sum "${REC_WORK}/scheduled.tar.gpg" | cut -d' ' -f1)"
    recovery_expect "the object in the bucket is byte-identical to the archive the unit wrote" "$remote_hash" "$local_hash"
  else
    docker cp "${container}:${REC_OUT_DIR}/${REC_UNIT_ARCHIVE}" "${REC_WORK}/scheduled.tar.gpg" >/dev/null \
      || { note "panel-recovery: could not copy the scheduled archive out of the server"; return 1; }
  fi
  recovery_expect "both archives are non-empty files outside any container" \
    "$([ -s "${REC_WORK}/scheduled.tar.gpg" ] && [ -s "${REC_WORK}/manual.tar.gpg" ] && echo yes || echo no)" yes
  return 0
}

# rec_fresh_leg: the old server dies and a new one is installed. Leaves `container` pointing at the
# new one. Returns 1 when the new server cannot be made, because nothing can be restored onto it.
rec_fresh_leg() {
  local old="$container" fresh="${container}-fresh" fresh_log fresh_encryption fresh_signing unit
  fresh_log="${log_dir}/install-fresh-${os}-${arch}-${mode}.log"

  docker stop -t 10 "$old" >/dev/null 2>&1 || true
  recovery_expect "the old server is stopped for good" \
    "$(docker inspect -f '{{.State.Running}}' "$old" 2>/dev/null | tr -d '\r' || true)" false

  extra_containers+=("$fresh")
  if ! start_server_container "$fresh"; then
    note "panel-recovery: the fresh server's container did not boot, so nothing could be restored"
    return 1
  fi
  install_panel_into "$fresh" "$fresh_log"
  container="$fresh"
  recovery_expect "the fresh install exits 0 (log: ${fresh_log#"$root"/})" "$installer_status" 0
  for unit in maran-agent maran-api; do
    recovery_expect "the fresh server's ${unit} is active" \
      "$(docker exec "$container" systemctl is-active "$unit" 2>&1 | tr -d '\r' || true)" active
  done
  wait_for_panel || { note "panel-recovery: the fresh panel never reported a reachable database"; return 1; }

  # THE POSITIVE CONTROL for the whole proof: the new install has keys of its own. If it did not,
  # a restore that brought back nothing would still sign the administrator in.
  fresh_encryption="$(rec_env_hash Security__EncryptionKey)"
  fresh_signing="$(rec_env_hash Jwt__SigningKey)"
  REC_FRESH_SETUP_HASH="$(rec_env_hash Setup__Token)"
  recovery_expect "the fresh install has a different encryption key" \
    "$([ "$fresh_encryption" != "$REC_EMPTY_HASH" ] && [ "$fresh_encryption" != "$REC_OLD_ENCRYPTION_HASH" ] && echo different || echo same)" different
  recovery_expect "the fresh install has a different signing key" \
    "$([ "$fresh_signing" != "$REC_EMPTY_HASH" ] && [ "$fresh_signing" != "$REC_OLD_SIGNING_HASH" ] && echo different || echo same)" different
  recovery_expect "the fresh install has a different setup token" \
    "$([ "$REC_FRESH_SETUP_HASH" != "$REC_EMPTY_HASH" ] && [ "$REC_FRESH_SETUP_HASH" != "$REC_OLD_SETUP_HASH" ] && echo different || echo same)" different
  REC_FRESH_HOST_SETTINGS="$(docker exec "$container" grep -vE '^(Security__EncryptionKey|Jwt__SigningKey)=' /etc/maran/panel.env | tr -d '\r')"
  recovery_expect "the fresh install's host settings are readable (control for the comparison after the restore)" \
    "$(case "$REC_FRESH_HOST_SETTINGS" in *Setup__Token=*) echo readable ;; *) echo empty ;; esac)" readable

  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  recovery_expect "on the fresh server the old administrator cannot sign in yet" "$REC_CODE" 401
  rec_login "$REC_ACCOUNT_NAME" "$REC_OWNER_PASSWORD"
  recovery_expect "on the fresh server the old account owner cannot sign in yet" "$REC_CODE" 401
  return 0
}

# rec_stage_archive: puts the archive $1 (a path on the host) and the passphrase $2 onto the fresh
# server as root-only files, where `maran panel-restore` accepts them.
#   $3 = the archive's name inside the container, $4 = the passphrase file's.
rec_stage_archive() {
  docker cp "$1" "${container}:$3" >/dev/null
  docker exec "$container" chown root:root "$3"
  docker exec "$container" chmod 0600 "$3"
  printf '%s\n' "$2" | rec_put_file "$container" 0600 "$4"
}

# rec_restore: `maran panel-restore` on the current server. Sets REC_STATUS and REC_OUT.
#   $1 = archive inside the container, $2 = passphrase file inside the container.
rec_restore() {
  REC_STATUS=0
  REC_OUT="$(docker exec "$container" /usr/local/sbin/maran panel-restore --passphrase-file "$2" \
    --replace-this-panel "$1" 2>&1 | tr -d '\r')" || REC_STATUS=$?
}

# rec_assert_restored: everything the issue's closing criterion names, asked of the current server.
#   $1 = which restore this is, for the messages.
rec_assert_restored() {
  local label="$1" setup_token wrong_status
  wait_for_panel || note "panel-recovery: ${label}: the panel did not report a reachable database after the restore"
  recovery_expect "${label}: maran-api is active" \
    "$(docker exec "$container" systemctl is-active maran-api 2>&1 | tr -d '\r' || true)" active
  recovery_expect "${label}: the restored encryption key is the old server's" "$(rec_env_hash Security__EncryptionKey)" "$REC_OLD_ENCRYPTION_HASH"
  recovery_expect "${label}: the restored signing key is the old server's" "$(rec_env_hash Jwt__SigningKey)" "$REC_OLD_SIGNING_HASH"
  recovery_expect "${label}: the fresh install's other settings were kept, line for line" \
    "$(docker exec "$container" grep -vE '^(Security__EncryptionKey|Jwt__SigningKey)=' /etc/maran/panel.env | tr -d '\r')" "$REC_FRESH_HOST_SETTINGS"
  recovery_expect "${label}: the setup token is still the fresh install's, not the old server's" "$(rec_env_hash Setup__Token)" "$REC_FRESH_SETUP_HASH"
  recovery_expect "${label}: panel.env is still 0640 root:maran" \
    "$(docker exec "$container" stat -c '%a %U %G' /etc/maran/panel.env | tr -d '\r')" "640 root maran"

  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  recovery_expect "${label}: the old administrator's password is accepted and asks for the second factor" \
    "$REC_CODE/$([ -z "$REC_TOKEN" ] && echo no-session || echo session)" "200/no-session"
  rec_totp_wrong "$REC_TOTP_SECRET"
  rec_call POST "/api/v1/auth/two-factor?username=${REC_ADMIN_USER}" \
    "{\"username\":\"${REC_ADMIN_USER}\",\"password\":\"${REC_ADMIN_PASSWORD}\",\"code\":\"${REC_TOTP_CODE}\"}"
  recovery_expect "${label}: a wrong one-time code is refused (inverse control)" "$REC_CODE" 401
  rec_second_factor "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD" "$REC_TOTP_SECRET"
  recovery_expect "${label}: the code from the ORIGINAL secret signs the administrator in, so the encrypted secret decrypted to the same value" \
    "$REC_CODE/$([ -n "$REC_TOKEN" ] && echo session || echo no-session)" "200/session"
  rec_call GET /api/v1/accounts "" "$REC_TOKEN"
  recovery_expect "${label}: the administrator sees the account" \
    "$(case "$REC_BODY" in *"\"name\":\"${REC_ACCOUNT_NAME}\""*) echo listed ;; *) echo missing ;; esac)" listed
  rec_licence_assert_restored "$label" "$REC_TOKEN"

  rec_login "$REC_ACCOUNT_NAME" "$REC_OWNER_PASSWORD"
  recovery_expect "${label}: the account owner signs in with their own password" \
    "$REC_CODE/$([ -n "$REC_TOKEN" ] && echo session || echo no-session)" "200/session"
  rec_login "$REC_ACCOUNT_NAME" "polygon-wrong-password-9"
  recovery_expect "${label}: a wrong password for the account owner is refused (inverse control)" "$REC_CODE" 401

  setup_token="$(docker exec "$container" sed -n 's/^Setup__Token=//p' /etc/maran/panel.env | tr -d '\r' || true)"
  rec_call POST /api/v1/setup "{\"token\":\"${setup_token}\",\"username\":\"polysecond\",\"email\":\"second@example.test\",\"password\":\"Polygon-Second-Password-5\"}"
  wrong_status="$REC_CODE"
  recovery_expect "${label}: first-administrator setup is closed (the restored panel already has administrators)" \
    "$([ "$wrong_status" != "200" ] && echo closed || echo open)" closed
}

# rec_restore_leg: the negative, the restore, and the second restore.
rec_restore_leg() {
  local env_before wrong_passphrase

  rec_stage_archive "${REC_WORK}/scheduled.tar.gpg" "$REC_PASSPHRASE" /root/polygon-restore-scheduled.tar.gpg /root/polygon-panel-passphrase
  wrong_passphrase="$(openssl rand -base64 33 | tr -d '/+=\n')-wrong"
  printf '%s\n' "$wrong_passphrase" | rec_put_file "$container" 0600 /root/polygon-wrong-passphrase

  # The negative. A wrong passphrase is refused before anything changes: same exit contract, panel
  # still running, panel.env byte-identical, the old administrator still unknown to the panel.
  env_before="$(docker exec "$container" sha256sum /etc/maran/panel.env | cut -d' ' -f1 | tr -d '\r')"
  rec_restore /root/polygon-restore-scheduled.tar.gpg /root/polygon-wrong-passphrase
  recovery_expect "a restore with the wrong passphrase is refused" "$(rec_refused "$REC_STATUS")" refused
  recovery_expect "the refusal says the archive could not be opened" \
    "$(case "$REC_OUT" in *decrypt*) echo says-so ;; *) echo silent ;; esac)" says-so
  recovery_expect "the refusal printed neither passphrase" \
    "$(case "$REC_OUT" in *"$wrong_passphrase"*|*"$REC_PASSPHRASE"*) echo leaked ;; *) echo clean ;; esac)" clean
  recovery_expect "after the refused restore panel.env is byte-identical" \
    "$(docker exec "$container" sha256sum /etc/maran/panel.env | cut -d' ' -f1 | tr -d '\r')" "$env_before"
  recovery_expect "after the refused restore the panel is still running" \
    "$(docker exec "$container" systemctl is-active maran-api 2>&1 | tr -d '\r' || true)" active
  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  recovery_expect "after the refused restore the old administrator is still unknown" "$REC_CODE" 401

  # A fresh panel that holds a schema the archive lacks, and an install that claims to be OLDER than
  # the archive: the restore is refused before it changes anything, the planted schema proving it.
  rec_guards_fresh_controls
  rec_extra_schema_plant
  if [ "$REC_RELEASE_GUARD" = "yes" ]; then
    rec_release_guard_refusal /root/polygon-restore-scheduled.tar.gpg /root/polygon-panel-passphrase
    # The inverse control: the same archive against an install NEWER than it restores.
    rec_release_set "$REC_RELEASE_RAISED"
  fi

  # The restore itself: the same archive, the right passphrase, the inverse of the refusals above.
  rec_restore /root/polygon-restore-scheduled.tar.gpg /root/polygon-panel-passphrase
  recovery_expect "panel-restore with the right passphrase succeeds" "$REC_STATUS" 0
  recovery_expect "the restore printed no secret" "$(rec_leak_count "$REC_OUT")" 0
  [ "$REC_STATUS" -eq 0 ] || printf '%s\n' "$REC_OUT" | sed 's/^/      restore: /' >&2
  rec_assert_restored "restore from the scheduled archive"
  recovery_expect "the restore removed the schema the archive lacks" "$(rec_extra_schema_count)" 0

  # The manual archive restores too, against an install of exactly its own release. The panel is a
  # restored one now, so this also shows a second restore onto a panel that is already in use, and
  # onto a database with no extra schema.
  [ "$REC_RELEASE_GUARD" != "yes" ] || rec_release_put_back
  rec_stage_archive "${REC_WORK}/manual.tar.gpg" "$REC_PASSPHRASE" /root/polygon-restore-manual.tar.gpg /root/polygon-panel-passphrase
  rec_restore /root/polygon-restore-manual.tar.gpg /root/polygon-panel-passphrase
  recovery_expect "panel-restore from the manual archive succeeds" "$REC_STATUS" 0
  rec_assert_restored "restore from the manual archive"
}

# rec_print_blind_spots: what this stage could not see, printed in its own output as every gate in
# this repository must (rules/testing.md).
rec_print_blind_spots() {
  echo "    panel-recovery: ${recovery_checks} checks so far (this stage: administrator second factor, account and owner login, scheduled runs under KEEP=2, refusals, restore onto a second server)"
  echo "    UNOBSERVED HERE: the timer FIRING on its schedule, and Persistent=true catching up after downtime. The service was started by name; the clock was not waited for."
  echo "    UNOBSERVED HERE: disk quota. The account was created with the suite images' setquota stand-in in place because the container's filesystem has no quota accounting."
  rec_guards_blind_spots
  echo "    UNOBSERVED HERE: the account's files, sites and databases. A panel backup holds the panel's records of them; on the second server the account exists in the panel with no system user behind it, as the documentation says."
  echo "    UNOBSERVED HERE: one PostgreSQL major and one family per run. The stage proves ${os} (${family} family) only; the other majors and families are proved by running it against them."
  echo "    UNOBSERVED HERE: SELinux enforcing. Containers on this host do not run it, so the labels restorecon sets after a restore are unexercised."
}

# rec_print_s3_blind_spots: the same for the upload leg, which differs by whether it ran.
rec_print_s3_blind_spots() {
  if [ "$REC_S3_ENABLED" = "yes" ]; then
    echo "    s3: uploaded to a stand-in over TLS (certificate trusted via the system store, verification on), HEAD size equal, wrong key id and wrong secret refused, an untrusted certificate refused."
    echo "    UNOBSERVED HERE: any real storage provider. The stand-in recomputes Signature Version 4 for one bucket, path-style; it does not model clock skew, multipart, ACLs or a provider's own quirks."
    echo "    UNOBSERVED HERE: curl's argv and environment DURING the upload. The process lives for well under a second and was not sampled; only what the verb PRINTED was searched for the credentials."
  else
    echo "    UNOBSERVED HERE: the S3 upload, its HEAD size check and its refusals. They did not run: ${REC_S3_REASON}"
  fi
}

# stage_panel_recovery: the stage, as the install polygon calls it. All failures are counted through
# `note`; the function itself never fails the harness, so a stage that stopped early is caught by the
# minimum-checks guard at the call site instead of by a half-printed verdict.
stage_panel_recovery() {
  local origin="$container"
  REC_S3_REASON=""
  REC_S3_ENABLED="no"
  REC_WORK="$(mktemp -d)"
  trap 'rm -rf -- "$REC_WORK"' EXIT
  REC_PASSPHRASE="$(openssl rand -base64 33 | tr -d '/+=\n')-polygon"

  if rec_seed_panel; then
    rec_guards_prepare
    if rec_curl_supports_sigv4; then
      if rec_s3_start "${origin}-s3"; then
        REC_S3_ENABLED="yes"
      else
        REC_S3_REASON="the stand-in could not be brought up (see the failure above)."
      fi
    else
      REC_S3_REASON="the curl in this system lacks --aws-sigv4 (it needs curl 7.75 or later)."
    fi
    rec_schedule_leg
    rec_precheck_sign_in
    rec_manual_backup
    if rec_take_archives_off && rec_fresh_leg; then
      rec_restore_leg
    fi
    rec_print_blind_spots
    rec_print_s3_blind_spots
  fi

  container="$origin"
  rm -rf -- "$REC_WORK"
  trap - EXIT
  return 0
}
