#!/usr/bin/env bash
# Step 15: rename a pre-rename installation's system account, its group and its PostgreSQL role
# onto the names this product uses now, in place, before any other step names either.
#
# The product's account used to be called `panel` — the one name it put on a host that did not
# say whose it is, and, worse, a name common enough that step 40's `id -u` idempotence would
# silently ADOPT somebody else's account of that name and hand it /etc/maran, /var/lib/maran, the
# panel's TLS key, User= on the api unit and an admitted uid on the root agent's socket. It is
# `maran` now (install.sh decides both names;
# docs/superpowers/notes/2026-09-09-service-account-rename-threat-note.md carries the argument).
#
# WHY A RENAME AND NOT A NEW ACCOUNT. `usermod -l` and `groupmod -n` keep the uid and the gid, so
# every file the old account owned — /etc/maran and panel.env, the TLS key, /var/lib/maran with
# the DataProtection keys inside it, /var/log/maran and /var/log/maran/panel, /run/maran, the
# api's socket directory, and anything an operator created by hand — keeps its owner. Only the
# NAME printed for that uid changes, and not one `chown` runs. A migration that created `maran`
# and chowned the tree would have to enumerate that list correctly, would break whatever it
# missed, and would still miss what a future step adds. Measured on both polygon families: the
# rename returns 0, uid and gid are unchanged, and files follow it.
#
# WHY IT IS STEP 15 — before the packages, before PostgreSQL, before step 40. Every step after
# this one then sees exactly the state a fresh install produces, and no other step in the
# installer carries a branch for the old name. That is the whole reason for the position: the
# alternative, migrating late to shorten the outage below, needs a legacy branch in steps 30, 40
# and 60, and three branches nobody exercises are worth less than one step that runs first.
#
# WHAT IT COSTS: the panel is DOWN from here until step 70 restarts it (see stop_the_api).
#
# On a fresh host every one of the five actions below is skipped and the step prints one line.
set -euo pipefail

# The names this migration renames from and to. Decided in install.sh, read here, never
# re-spelled: the polygon asserts that the name step 40 CREATES is the name this step renames
# ONTO, so the two ends of the migration cannot drift apart.
: "${MARAN_USER:?15-identity.sh: MARAN_USER is unset; it is set by install.sh and must be in the environment}"
: "${MARAN_GROUP:?15-identity.sh: MARAN_GROUP is unset; it is set by install.sh and must be in the environment}"
: "${MARAN_LEGACY_USER:?15-identity.sh: MARAN_LEGACY_USER is unset; it is set by install.sh and must be in the environment}"
: "${MARAN_LEGACY_GROUP:?15-identity.sh: MARAN_LEGACY_GROUP is unset; it is set by install.sh and must be in the environment}"
: "${MARAN_SERVICE_ACCOUNT_MARKER:?15-identity.sh: MARAN_SERVICE_ACCOUNT_MARKER is unset; it is set by install.sh and must be in the environment}"

# The state directory whose ownership is the evidence that a legacy account is OURS, and the
# configuration file whose Database__Username line is the evidence that the panel installed here
# still authenticates as the legacy PostgreSQL role. Both are named here as literals on purpose:
# this step must be able to recognise an installation made by a RELEASE THAT NO LONGER EXISTS,
# so it reads what that release wrote, not what today's steps would write.
readonly MARAN_LEGACY_STATE_DIR="/var/lib/maran"
readonly MARAN_LEGACY_CONFIG_FILE="/etc/maran/panel.env"
readonly MARAN_LEGACY_DB_NAME="maran"

# host_is_a_legacy_installation: whether the account named by MARAN_LEGACY_USER is THIS
# PRODUCT'S account rather than somebody else's account that happens to share the name.
#
# The evidence is ownership of the state directory this product created: /var/lib/maran exists
# and is owned by exactly that uid and gid. That is an observation of the artefact the system
# actually uses, and it is the only one available — the legacy release stamped no marker, which
# is the defect this whole change is about.
#
# A foreign `panel` on a host with no Maran installation therefore answers NO here and is never
# renamed. It is not this step's business: it belongs to nobody, and nothing in this installer
# will touch it (the product's own name is `maran` now).
host_is_a_legacy_installation() {
  local uid gid
  id -u "$MARAN_LEGACY_USER" >/dev/null 2>&1 || return 1
  [ -d "$MARAN_LEGACY_STATE_DIR" ] || return 1
  [ ! -L "$MARAN_LEGACY_STATE_DIR" ] || return 1
  uid="$(stat -c '%u' "$MARAN_LEGACY_STATE_DIR")"
  gid="$(stat -c '%g' "$MARAN_LEGACY_STATE_DIR")"
  [ "$uid" = "$(id -u "$MARAN_LEGACY_USER")" ] || return 1
  [ "$gid" = "$(id -g "$MARAN_LEGACY_USER")" ] || return 1
  return 0
}

# stop_the_api: take maran-api.service down for the rest of this install, and say so.
#
# THE OUTAGE IS DELIBERATE AND IT IS THE COST OF THIS STEP. The running api read
# `Database__Username=panel` at its own start-up and holds it; the moment the OS account is
# renamed, its next PostgreSQL connection fails peer authentication (the role name must equal the
# OS user name), and moments later pg_hba.conf stops naming its role at all. There is no ordering
# that avoids this: a running old api stops being able to reach its database at the rename,
# whenever the rename happens.
#
# So the choice is between a panel that is `active (running)` and answers 500 to every request,
# and one that is `inactive (dead)` with a reason printed. The second is the one an operator can
# diagnose, and it is the one an interrupted install leaves in a state that says what happened.
#
# It is NOT required by usermod. An earlier draft of the threat note claimed usermod refuses to
# rename an account with running processes; measured on both polygon families with a live process
# owned by the account, `usermod -l` returns 0. The correction is recorded rather than removed,
# because a reason invented for a decision already taken is what stops the next reader looking.
#
# `systemctl stop` on a unit that does not exist is not an error worth stopping an install for —
# on a host whose unit file was deleted by hand there is nothing to stop — so its failure is
# tolerated and reported rather than fatal.
stop_the_api() {
  if ! command -v systemctl >/dev/null 2>&1; then
    return 0
  fi
  if ! systemctl list-unit-files maran-api.service >/dev/null 2>&1; then
    return 0
  fi
  echo "  Stopping maran-api.service. THE PANEL IS DOWN from now until this install finishes:"
  echo "  the account it runs as is about to be renamed, and only step 70 rewrites the unit and"
  echo "  restarts it. If this run is interrupted, re-run installer/install.sh — the migration"
  echo "  resumes from wherever it stopped and the panel stays down until it completes."
  systemctl stop maran-api.service || echo "  (maran-api.service could not be stopped; continuing.)"
}

# rename_the_group: groupmod -n, guarded by the state it changes. The gid is preserved, so every
# file group-owned by it — /etc/maran, panel.env, the TLS key, /var/log/maran, /run/maran — keeps
# its group without a chgrp.
rename_the_group() {
  if ! getent group "$MARAN_LEGACY_GROUP" >/dev/null 2>&1; then
    return 0
  fi
  if getent group "$MARAN_GROUP" >/dev/null 2>&1; then
    echo "15-identity.sh: this host has a legacy '${MARAN_LEGACY_GROUP}' group to rename and a" >&2
    echo "  '${MARAN_GROUP}' group already exists, so the rename would collide. Rename or remove" >&2
    echo "  the '${MARAN_GROUP}' group by hand and re-run. Aborting." >&2
    exit 1
  fi
  groupmod -n "$MARAN_GROUP" "$MARAN_LEGACY_GROUP"
  echo "  Group '${MARAN_LEGACY_GROUP}' renamed to '${MARAN_GROUP}' (gid unchanged, so no file changed group)."
}

# rename_the_user: usermod -l, plus the GECOS marker that makes step 40 adopt the account as its
# own instead of refusing it. The uid is preserved, so every file it owns keeps its owner.
rename_the_user() {
  if ! id -u "$MARAN_LEGACY_USER" >/dev/null 2>&1; then
    return 0
  fi
  if id -u "$MARAN_USER" >/dev/null 2>&1; then
    echo "15-identity.sh: this host has a legacy '${MARAN_LEGACY_USER}' account to rename and an" >&2
    echo "  account named '${MARAN_USER}' already exists (uid $(id -u "$MARAN_USER")), so the" >&2
    echo "  rename would collide. Rename or remove that account by hand and re-run. Aborting." >&2
    exit 1
  fi
  usermod -l "$MARAN_USER" -c "$MARAN_SERVICE_ACCOUNT_MARKER" "$MARAN_LEGACY_USER"
  echo "  Account '${MARAN_LEGACY_USER}' renamed to '${MARAN_USER}' (uid $(id -u "$MARAN_USER") unchanged, so no file changed owner)."
}

# postgresql_role_exists: whether `$1` is a role in this host's PostgreSQL. Answered by psql as
# the postgres OS user over the unix socket, the same way step 30 answers it.
postgresql_role_exists() {
  [ "$(runuser -u postgres -- psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='${1}'" 2>/dev/null)" = "1" ]
}

# rename_the_database_role: ALTER ROLE ... RENAME TO, so peer authentication keeps working — the
# role name must equal the OS user name, which is why step 30 names the role after the account in
# the first place.
#
# Guarded three ways, because renaming a role is not a thing to do on a guess: the legacy role
# must exist, the new one must not, and the legacy role must OWN this product's database. The
# ownership check is what keeps this away from an unrelated `panel` role on a shared PostgreSQL.
#
# `ALTER ROLE ... RENAME TO` clears an md5-stored password. This role has none: it authenticates
# by peer over the unix socket and step 30 creates it with `CREATE ROLE ... LOGIN` and no password.
#
# When PostgreSQL cannot be reached at all while a rename is owed, this ABORTS rather than
# continuing: step 30 would otherwise create a fresh, empty `maran` role beside a `panel` role
# that still owns the database, and the panel would come up authenticated as a role with no
# rights to its own data.
rename_the_database_role() {
  local owner
  if ! command -v psql >/dev/null 2>&1; then
    return 0
  fi
  if ! runuser -u postgres -- psql -tAc 'SELECT 1' >/dev/null 2>&1; then
    if postgresql_rename_is_owed; then
      echo "15-identity.sh: this host still authenticates to PostgreSQL as the legacy role" >&2
      echo "  '${MARAN_LEGACY_USER}', and PostgreSQL is not answering on its unix socket, so the" >&2
      echo "  role cannot be renamed. Continuing would create a second, empty role and leave the" >&2
      echo "  panel without rights to its own data. Start PostgreSQL and re-run. Aborting." >&2
      exit 1
    fi
    return 0
  fi
  if ! postgresql_role_exists "$MARAN_LEGACY_USER"; then
    return 0
  fi
  if postgresql_role_exists "$MARAN_USER"; then
    echo "15-identity.sh: PostgreSQL has both a legacy role '${MARAN_LEGACY_USER}' and a role" >&2
    echo "  '${MARAN_USER}'. Which of them owns this panel's data is not a thing this installer" >&2
    echo "  may guess. Resolve it by hand and re-run. Aborting." >&2
    exit 1
  fi
  owner="$(runuser -u postgres -- psql -tAc "SELECT pg_catalog.pg_get_userbyid(datdba) FROM pg_database WHERE datname='${MARAN_LEGACY_DB_NAME}'" 2>/dev/null || true)"
  if [ "$owner" != "$MARAN_LEGACY_USER" ]; then
    return 0
  fi
  runuser -u postgres -- psql -c "ALTER ROLE ${MARAN_LEGACY_USER} RENAME TO ${MARAN_USER};"
  echo "  PostgreSQL role '${MARAN_LEGACY_USER}' renamed to '${MARAN_USER}'."
}

# postgresql_rename_is_owed: whether this host's installed configuration still names the legacy
# role. Read out of the file the panel actually reads, so the answer is about this installation
# and not about a role that happens to exist on a shared server.
postgresql_rename_is_owed() {
  [ -r "$MARAN_LEGACY_CONFIG_FILE" ] || return 1
  grep -q "^Database__Username=${MARAN_LEGACY_USER}\$" "$MARAN_LEGACY_CONFIG_FILE"
}

# rewrite_the_configured_role: the connection string in panel.env, which is the only place the
# renamed role is spelled that a rename does not follow.
#
# Written through a temporary file INSIDE /etc/maran and renamed over the original, for the two
# reasons step 60 does the same: a rename is atomic only within one filesystem, so the panel
# never reads a half-written configuration, and the file never leaves the directory whose 0750
# root:<group> is what protects it. Owner and mode are set on the temporary file BEFORE the
# rename, never after, so there is no instant at which the real path is readable by anyone else.
rewrite_the_configured_role() {
  local tmp
  postgresql_rename_is_owed || return 0
  tmp="$(mktemp "${MARAN_LEGACY_CONFIG_FILE}.XXXXXX")"
  sed "s/^Database__Username=${MARAN_LEGACY_USER}\$/Database__Username=${MARAN_USER}/" \
    "$MARAN_LEGACY_CONFIG_FILE" > "$tmp"
  chown "root:${MARAN_GROUP}" "$tmp"
  chmod 0640 "$tmp"
  mv -f "$tmp" "$MARAN_LEGACY_CONFIG_FILE"
  echo "  ${MARAN_LEGACY_CONFIG_FILE}: Database__Username is now '${MARAN_USER}'."
}

# migrate_service_identity: the five actions, in order, each guarded by the state IT changes.
#
# There is no "migration done" flag on purpose, and that is what makes an interrupted run safe:
# every action is individually idempotent, so the set converges from any prefix. Interrupted
# after the groupmod, a re-run finds no legacy group and renames the user; interrupted after the
# usermod, a re-run finds no legacy account and renames the role; and so on. In every one of
# those states the panel is down and every file is intact, because nothing here deletes, chowns
# or recurses. The recovery from all of them is the same: run install.sh again.
migrate_service_identity() {
  stop_the_api
  rename_the_group
  rename_the_user
  rename_the_database_role
  rewrite_the_configured_role
}

step_identity() {
  if host_is_a_legacy_installation || postgresql_rename_is_owed; then
    echo "Migrating this host's service identity from '${MARAN_LEGACY_USER}' to '${MARAN_USER}'..."
    migrate_service_identity
    echo "Service identity migrated."
  else
    echo "Service identity: nothing to migrate."
  fi
}
