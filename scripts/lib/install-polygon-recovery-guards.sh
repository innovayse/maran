#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# Three observations the panel-recovery stage (install-polygon-recovery.sh) makes on top of the
# round trip, each about a way a restore can quietly do less than it claims (issue #129):
#
#   1. The LICENCE half. The install holds no licence, so without a fixture the restore's licence
#      step moves nothing and nothing says so. A file that is not a signed licence is planted on the
#      dying server; after the restore it must be on the new one, byte-identical, with the owner
#      and mode the panel's writer gives it, and the panel must READ it (state "refused", where an
#      unreadable or missing file reads "absent"). Signature validity is not observed, and the stage
#      prints that.
#   2. A fresh panel that holds a SCHEMA the archive lacks (what a newer release's migrations
#      leave behind). A schema owned by the panel's own database role is planted before the restore;
#      the restore must remove it and bring the administrators back.
#   3. The RELEASE GUARD against a real archive and a real install: an archive taken by a newer
#      release than the one installed is refused before the panel is stopped, and the refusal
#      changes nothing; an archive from an older release restores; an equal one restores.
#
# Reads these globals from install-polygon.sh and the stage: container, recovery_expect, note,
# REC_ADMIN_TOKEN, REC_CODE, REC_BODY, REC_STATUS, REC_OUT, REC_PASSPHRASE, rec_* helpers.

REC_LICENCE_FILE="/var/lib/maran/licence.json"
REC_LICENCE_HASH=""
REC_LICENCE_STATE_OLD=""
REC_OLD_RELEASE=""
REC_RELEASE_GUARD="no"
REC_RELEASE_STATE_FILE="/etc/maran/release.state"
REC_RELEASE_STATE_COPY="/root/polygon-release.state.orig"
REC_RELEASE_LOWERED="0.0.0"
REC_RELEASE_RAISED="999.0.0"
REC_EXTRA_SCHEMA="polygonextra"

# rec_release_state: the value of release.state's key $1 in the current container, empty when absent.
rec_release_state() {
  docker exec "$container" sed -n "s/^$1=//p" "$REC_RELEASE_STATE_FILE" 2>/dev/null | tr -d '\r' || true
}

# rec_licence_state: the licence state the panel reports ("absent", "valid" or "refused"), lower-cased,
# read through the administrator endpoint with the bearer token $1. Empty when the call failed.
rec_licence_state() {
  rec_call GET /api/v1/licence-status "" "$1"
  [ "$REC_CODE" = "200" ] || return 0
  rec_json_field "$REC_BODY" state | tr 'A-Z' 'a-z'
}

# rec_panel_psql: one single-value query against the panel database as the panel's OWN role (the
# service account over the socket, with the connection settings panel.env gives), so an object it
# creates is owned the way the panel's own tables are. Prints the value.
#   $1 = SQL.
rec_panel_psql() {
  local host database username
  host="$(docker exec "$container" sed -n 's/^Database__Host=//p' /etc/maran/panel.env | tr -d '\r')"
  database="$(docker exec "$container" sed -n 's/^Database__Database=//p' /etc/maran/panel.env | tr -d '\r')"
  username="$(docker exec "$container" sed -n 's/^Database__Username=//p' /etc/maran/panel.env | tr -d '\r')"
  docker exec -w /tmp "$container" runuser -u maran -- psql --no-psqlrc -tA \
    --host="$host" --username="$username" --dbname="$database" -c "$1" 2>/dev/null | tr -d '\r' || true
}

# rec_guards_prepare: on the server that is about to die, plants the licence fixture and records
# the release it runs. Called after the seeding, before any backup is taken, so every archive holds
# both.
rec_guards_prepare() {
  local before after
  REC_OLD_RELEASE="$(rec_release_state VERSION)"
  recovery_expect "the dying server records its release in release.state (the release guard has something to compare)" \
    "$([ -n "$REC_OLD_RELEASE" ] && echo recorded || echo missing)" recorded
  REC_RELEASE_GUARD="no"
  [ -z "$REC_OLD_RELEASE" ] || REC_RELEASE_GUARD="yes"

  before="$(rec_licence_state "$REC_ADMIN_TOKEN")"
  recovery_expect "before the fixture the dying panel reports no licence (control: the probe can tell absent from present)" \
    "$before" absent
  printf '%s\n' '{"fixture":"maran-polygon-not-a-signed-licence"}' \
    | docker exec -i "$container" bash -c 'umask 077; cat > "$1"; chown maran:maran "$1"; chmod 0640 "$1"' _ "$REC_LICENCE_FILE"
  REC_LICENCE_HASH="$(docker exec "$container" sha256sum "$REC_LICENCE_FILE" | cut -d' ' -f1 | tr -d '\r')"
  after="$(rec_licence_state "$REC_ADMIN_TOKEN")"
  recovery_expect "with the fixture the dying panel reads the file and reports it refused (the service account can read it)" \
    "$after" refused
  REC_LICENCE_STATE_OLD="$after"
}

# rec_guards_fresh_controls: on the fresh server, before any restore: the controls that make the
# observations after it mean something. The licence file is absent (so its presence later came from
# the restore), and the release.state is saved so it can be put back byte for byte.
rec_guards_fresh_controls() {
  recovery_expect "the fresh server holds no licence file before the restore (so its presence afterwards came from the archive)" \
    "$(docker exec "$container" test -e "$REC_LICENCE_FILE" && echo present || echo absent)" absent
  [ "$REC_RELEASE_GUARD" = "yes" ] || return 0
  docker exec "$container" cp -p -- "$REC_RELEASE_STATE_FILE" "$REC_RELEASE_STATE_COPY" \
    || { note "panel-recovery: the fresh server's release.state could not be saved, so the release guard is not observed"; REC_RELEASE_GUARD="no"; return 0; }
  recovery_expect "the fresh server's release is the dying server's (one tree installed both)" \
    "$(rec_release_state VERSION)" "$REC_OLD_RELEASE"
}

# rec_release_set: rewrites the VERSION line of the fresh server's release.state to $1, keeping the
# file root:root 0644, by rename. Test-only: nothing in the product lowers or raises its own release.
#   $1 = the release name to record.
rec_release_set() {
  docker exec "$container" bash -c '
    set -e
    tmp="$(mktemp /etc/maran/.release.state.XXXXXX)"
    sed "s/^VERSION=.*/VERSION=$1/" "$2" > "$tmp"
    chown root:root "$tmp"
    chmod 0644 "$tmp"
    mv -T -- "$tmp" "$3"' _ "$1" "$REC_RELEASE_STATE_COPY" "$REC_RELEASE_STATE_FILE"
  recovery_expect "release.state now records ${1} (control: the probe sees the rewrite)" "$(rec_release_state VERSION)" "$1"
}

# rec_release_put_back: restores the fresh server's release.state to the bytes it was installed with.
rec_release_put_back() {
  docker exec "$container" cp -p -- "$REC_RELEASE_STATE_COPY" "$REC_RELEASE_STATE_FILE"
  recovery_expect "release.state is back to the bytes the installer wrote" \
    "$(docker exec "$container" sha256sum "$REC_RELEASE_STATE_FILE" | cut -d' ' -f1 | tr -d '\r')" \
    "$(docker exec "$container" sha256sum "$REC_RELEASE_STATE_COPY" | cut -d' ' -f1 | tr -d '\r')"
}

# rec_extra_schema_plant: creates, as the panel's own database role, a schema the archive does not
# hold, with one row in it. A restore that clears nothing leaves it; one that clears too much would
# take the panel's schemas with it, which the sign-in checks after the restore would show.
rec_extra_schema_plant() {
  rec_panel_psql "create schema ${REC_EXTRA_SCHEMA}; create table ${REC_EXTRA_SCHEMA}.marker (id int); insert into ${REC_EXTRA_SCHEMA}.marker values (1)" >/dev/null
  recovery_expect "the fresh panel holds a schema the archive lacks (probe control: the planted row is found)" \
    "$(rec_panel_psql "select count(*) from ${REC_EXTRA_SCHEMA}.marker")" 1
}

# rec_extra_schema_count: how many schemas named like the planted one the database holds.
rec_extra_schema_count() {
  rec_psql "select count(*) from pg_namespace where nspname = '${REC_EXTRA_SCHEMA}'"
}

# rec_release_guard_refusal: the archive $1 (inside the container, with passphrase file $2) is
# restored while the install claims to be older than the archive. Everything the refusal must not
# touch is measured before and compared after. Leaves the lowered release recorded; the caller puts
# it back or sets another.
#   $1 = archive in the container, $2 = passphrase file in the container.
rec_release_guard_refusal() {
  local archive="$1" passphrase="$2" manifest archived env_before started_before refusals_before
  manifest="$(docker exec "$container" bash -c '
    home="$(mktemp -d)"
    trap "rm -rf -- \"$home\"" EXIT
    gpg --homedir "$home" --batch --quiet --no-tty --pinentry-mode loopback --passphrase-file "$2" --decrypt -- "$1" \
      | tar -xOf - ./manifest.txt' _ "$archive" "$passphrase" 2>/dev/null | tr -d '\r' || true)"
  archived="$(printf '%s\n' "$manifest" | sed -n 's/^release=//p')"
  recovery_expect "the archive's manifest records the release that took it, once" \
    "$(printf '%s\n' "$manifest" | grep -c '^release=' || true)" 1
  recovery_expect "the release in the manifest is the dying server's" "$archived" "$REC_OLD_RELEASE"
  recovery_expect "the lowered release is below the archive's (so the refusal below is the guard's to make)" \
    "$([ "$archived" != "$REC_RELEASE_LOWERED" ] && echo below || echo equal)" below

  rec_release_set "$REC_RELEASE_LOWERED"
  env_before="$(docker exec "$container" sha256sum /etc/maran/panel.env | cut -d' ' -f1 | tr -d '\r')"
  started_before="$(docker exec "$container" systemctl show -p ActiveEnterTimestampMonotonic --value maran-api | tr -d '\r')"
  refusals_before="$(rec_journal_matches 'verb=panel-restore outcome=refused')"

  rec_restore "$archive" "$passphrase"
  recovery_expect "an archive from a newer release than the installed one is refused" "$(rec_refused "$REC_STATUS")" refused
  recovery_expect "the refusal names both releases" \
    "$(case "$REC_OUT" in *"release ${archived}, newer than the installed ${REC_RELEASE_LOWERED}"*) echo names-both ;; *) echo silent ;; esac)" names-both
  recovery_expect "the refusal came before the panel was stopped (the stop-and-restart notice is absent)" \
    "$(case "$REC_OUT" in *"the restore did not finish"*) echo stopped ;; *) echo untouched ;; esac)" untouched
  recovery_expect "the refusal printed no passphrase" "$(rec_leak_count "$REC_OUT")" 0
  recovery_expect "the refusal wrote one audit line" "$(( $(rec_journal_matches 'verb=panel-restore outcome=refused') - refusals_before ))" 1
  recovery_expect "after the refusal the panel service was never stopped (same start time)" \
    "$(docker exec "$container" systemctl show -p ActiveEnterTimestampMonotonic --value maran-api | tr -d '\r')" "$started_before"
  recovery_expect "after the refusal maran-api is active" \
    "$(docker exec "$container" systemctl is-active maran-api 2>&1 | tr -d '\r' || true)" active
  recovery_expect "after the refusal panel.env is byte-identical" \
    "$(docker exec "$container" sha256sum /etc/maran/panel.env | cut -d' ' -f1 | tr -d '\r')" "$env_before"
  recovery_expect "after the refusal the database is untouched (a restore would have dropped the planted schema)" \
    "$(rec_extra_schema_count)" 1
  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  recovery_expect "after the refusal the old administrator is still unknown" "$REC_CODE" 401
}

# rec_licence_assert_restored: the licence leg of a restore, asked of the current server.
#   $1 = which restore this is, for the messages, $2 = an administrator's bearer token.
rec_licence_assert_restored() {
  local label="$1" token="$2"
  recovery_expect "${label}: the licence file is byte-identical to the dying server's" \
    "$(docker exec "$container" sha256sum "$REC_LICENCE_FILE" 2>/dev/null | cut -d' ' -f1 | tr -d '\r' || true)" "$REC_LICENCE_HASH"
  recovery_expect "${label}: the licence file has the owner and mode the panel's writer gives it" \
    "$(docker exec "$container" stat -c '%U %G %a' "$REC_LICENCE_FILE" 2>/dev/null | tr -d '\r' || true)" "maran maran 640"
  recovery_expect "${label}: the panel reads the restored licence file (state ${REC_LICENCE_STATE_OLD}, not absent)" \
    "$(rec_licence_state "$token")" "$REC_LICENCE_STATE_OLD"
}

# rec_guards_blind_spots: what these three observations could not see.
rec_guards_blind_spots() {
  echo "    UNOBSERVED HERE: a VALID licence. The fixture is not signed, so the panel reports it refused; what is shown is that the file arrived intact, readable by the service account, and was read. Signature verification and the server binding are not exercised."
  echo "    UNOBSERVED HERE: a restore onto a schema left by a REAL newer release. The extra schema is planted by hand, owned by the panel's role; objects in the public schema, extensions and roles the archive lacks are not cleared and not tried."
  if [ "$REC_RELEASE_GUARD" = "yes" ]; then
    echo "    release guard: one real archive refused against an install rewritten to ${REC_RELEASE_LOWERED}, restored against ${REC_RELEASE_RAISED} (older archive) and against its own release (equal). A missing or malformed release= line is exercised in docker/polygon/asserts/parts/cli-recovery.sh, not here."
  else
    echo "    UNOBSERVED HERE: the release guard. The dying server recorded no release in release.state, so there was nothing to compare."
  fi
}
