#!/usr/bin/env bash
# Step 30: install and initialise PostgreSQL, configured to accept connections over
# the unix socket ONLY (rules/architecture.md: "Only maran-api talks to PostgreSQL").
# TCP listening is disabled outright rather than firewalled, so there is no network
# attack surface to misconfigure later. Creates the panel's role and database.
set -euo pipefail

readonly MARAN_DB_NAME="maran"
# The role is named after the unprivileged OS user rather than being given a name of its own,
# so PostgreSQL's peer auth (unix-socket connections are authenticated by matching OS
# username to role name) works with no pg_ident.conf mapping to maintain. That is also why the
# account's name has no hyphen in it: this name is interpolated into CREATE ROLE, CREATE
# DATABASE ... OWNER, ALTER ROLE ... RENAME TO and DROP ROLE unquoted, and a hyphen is not a
# bare SQL identifier (installer/install.sh, "The panel's system account").
#
# A host installed before the rename reaches this step with its role ALREADY renamed: step 15
# runs first and does `ALTER ROLE panel RENAME TO maran` in place, so nothing here has a branch
# for the old name and the checks below simply find the role present.
: "${MARAN_USER:?30-postgresql.sh: MARAN_USER is unset; it is set by install.sh and must be in the environment}"
readonly MARAN_DB_ROLE="$MARAN_USER"

# The oldest PostgreSQL major this panel is known to work on, and "known" is literal: AlmaLinux 9
# ships 13 and its install polygon run is green, so the floor is the lowest version actually
# verified rather than the newest available (issue #55).
#
# It exists because EL8's DEFAULT module stream is PostgreSQL 10, on which the panel aborts at
# startup — `column c.conparentid does not exist`, a column added in PostgreSQL 11 — after the
# installer had created the database and pronounced it ready. Until this floor, the installer took
# whatever the distribution shipped and checked nothing, which README.md admitted in writing and
# this defect turned into a dead panel.
readonly MARAN_PG_MINIMUM_MAJOR=13

# pg_available_major: the major version the package manager WOULD install right now, or empty when
# it cannot be determined. Read from the package metadata rather than from a running server, because
# the decision it feeds — whether to switch module stream — has to be made BEFORE installing.
pg_available_major() {
  case "$MARAN_OS_FAMILY" in
    debian) apt-cache show postgresql 2>/dev/null | awk '/^Version:/{print $2; exit}' ;;
    rhel)   dnf info postgresql-server 2>/dev/null | awk '/^Version/{print $3; exit}' ;;
  esac | sed 's/[^0-9].*//'
}

# pg_select_modern_stream_on_rhel: on the RHEL family, move to a newer `postgresql` module stream
# when and only when the default one is below the floor.
#
# EL8's AppStream carries streams 9.6, 10 (default), 12, 13, 15 and 16, so a modern PostgreSQL comes
# from the DISTRIBUTION itself and this needs no third-party repository — which is why the fix is a
# stream switch and not a PGDG repo.
#
# Conditional on purpose. AlmaLinux 9 installs 13 and is green on it; switching it to 16 for
# tidiness would change a working system with no measured reason to. The defect is EL8's default of
# 10, so EL8's default is what changes.
pg_select_modern_stream_on_rhel() {
  [ "$MARAN_OS_FAMILY" = "rhel" ] || return 0
  command -v dnf >/dev/null 2>&1 || return 0

  local available
  available="$(pg_available_major)"
  # No opinion means no action: a package manager whose metadata could not be read is not evidence
  # that the stream is wrong, and the floor check after installation still refuses a bad version.
  [ -n "$available" ] || return 0
  [ "$available" -lt "$MARAN_PG_MINIMUM_MAJOR" ] || return 0

  # The newest stream on offer, chosen by version rather than by listing order.
  local newest
  newest="$(dnf module list postgresql 2>/dev/null \
              | awk '$1 == "postgresql" { print $2 }' \
              | grep -E '^[0-9]+(\.[0-9]+)?$' | sort -V | tail -1)"
  if [ -z "$newest" ]; then
    echo "30-postgresql.sh: this host would install PostgreSQL ${available}, below the required" >&2
    echo "  minimum of ${MARAN_PG_MINIMUM_MAJOR}, and no 'postgresql' module streams were found to" >&2
    echo "  switch to. The panel aborts at startup on PostgreSQL below ${MARAN_PG_MINIMUM_MAJOR}" >&2
    echo "  (column c.conparentid does not exist), so this install stops here instead." >&2
    exit 1
  fi

  echo "PostgreSQL ${available} is the default here and the panel needs ${MARAN_PG_MINIMUM_MAJOR}+; enabling module stream postgresql:${newest}."
  # `reset` before `enable`: a stream already enabled makes `enable` a no-op, and the default stream
  # counts as enabled, so without the reset this would silently keep installing 10.
  dnf module -y reset postgresql >/dev/null 2>&1 || true
  dnf module -y enable "postgresql:${newest}" >/dev/null 2>&1 \
    || { echo "30-postgresql.sh: could not enable module stream postgresql:${newest}" >&2; exit 1; }
}

# assert_pg_version_at_least_floor: the installed server really is at or above the floor.
#
# Separate from the stream selection above, and reading the SERVER rather than the package metadata,
# because they answer different questions: one predicts what will be installed, this one reports
# what is running. A host where an operator pinned an old PostgreSQL, or where the stream switch did
# not take, must fail HERE with a version in the message rather than in the panel's journal with an
# SQL error about a system catalogue column (rules/testing.md: a check must be able to observe what
# it reports on).
assert_pg_version_at_least_floor() {
  local version major
  version="$(su -s /bin/sh - postgres -c 'psql -tAc "SHOW server_version"' 2>/dev/null \
               | tr -d '[:space:]')"
  major="$(printf '%s' "$version" | sed 's/[^0-9].*//')"
  if [ -z "$major" ]; then
    echo "30-postgresql.sh: could not read the PostgreSQL server version, so this install cannot" >&2
    echo "  claim the panel's minimum of ${MARAN_PG_MINIMUM_MAJOR} is met (rules/testing.md)." >&2
    exit 1
  fi
  if [ "$major" -lt "$MARAN_PG_MINIMUM_MAJOR" ]; then
    echo "30-postgresql.sh: PostgreSQL ${version} is installed; the panel requires ${MARAN_PG_MINIMUM_MAJOR} or newer." >&2
    echo "  Below ${MARAN_PG_MINIMUM_MAJOR} the panel aborts at startup reading the system catalogues" >&2
    echo "  ('column c.conparentid does not exist' — pg_constraint.conparentid arrived in 11)." >&2
    echo "  Install a newer PostgreSQL for this distribution and re-run the installer (issue #55)." >&2
    exit 1
  fi
  echo "PostgreSQL ${version} meets the panel's minimum of ${MARAN_PG_MINIMUM_MAJOR}."
}

# pg_install: installs the PostgreSQL server package for the current family. RHEL-family
# distros ship PostgreSQL as modular/appstream packages needing an explicit `postgresql-setup
# --initdb` step; Debian-family packages self-initialise on install. This split is exactly
# the kind of distro difference the adapter pattern isolates.
pg_install() {
  case "$MARAN_OS_FAMILY" in
    debian)
      pkg_install postgresql
      ;;
    rhel)
      pg_select_modern_stream_on_rhel
      pkg_install postgresql-server
      # Idempotent: postgresql-setup refuses (non-fatally, we tolerate it) if the data
      # directory is already initialised, which is exactly the re-run case.
      if [ ! -s /var/lib/pgsql/data/PG_VERSION ]; then
        postgresql-setup --initdb
      fi
      ;;
    *)
      echo "30-postgresql.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

pg_service_name() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "postgresql" ;;
    rhel) echo "postgresql" ;;
  esac
}

# pg_data_dir: locates postgresql.conf so this step can edit it without hardcoding a
# version-numbered path (Debian embeds the major version in the path).
# THE DEPTH IS THREE, and it was two. Debian and Ubuntu put the file at
# /etc/postgresql/<major>/<cluster>/postgresql.conf — three levels below the starting point, not
# two — so `-maxdepth 2` matched nothing and this step refused with "could not locate
# postgresql.conf" on a host where the file was sitting exactly where the distribution puts it.
# Measured on Ubuntu 24.04 with PostgreSQL 16: the packages installed, initdb created the cluster,
# and the installer then could not find its own configuration.
#
# The polygon did not catch it, and the reason is worth writing down rather than fixing quietly:
# its image installs MariaDB and sshd, not PostgreSQL, and it copies this file in without ever
# executing the step. So nothing in this repository had ever run pg_conf_path against a real
# Debian-family layout. The assertion added to docker/polygon/asserts/assert-installer-steps.sh now
# exercises the resolver against that layout directly, which is cheap and catches this class
# without installing a database in the image.
pg_conf_path() {
  case "$MARAN_OS_FAMILY" in
    debian)
      find /etc/postgresql -maxdepth 3 -name postgresql.conf 2>/dev/null | sort -V | tail -1
      ;;
    rhel)
      echo "/var/lib/pgsql/data/postgresql.conf"
      ;;
  esac
}

pg_hba_path() {
  case "$MARAN_OS_FAMILY" in
    debian)
      # Same depth, same reason as pg_conf_path above: the file is a sibling of postgresql.conf.
      find /etc/postgresql -maxdepth 3 -name pg_hba.conf 2>/dev/null | sort -V | tail -1
      ;;
    rhel)
      echo "/var/lib/pgsql/data/pg_hba.conf"
      ;;
  esac
}

# pg_restrict_to_unix_socket: forces listen_addresses='' (no TCP at all) and rewrites
# pg_hba.conf to only allow local (unix socket) peer/trust-by-role connections. Written
# via a temp file + mv so a crash mid-write never leaves a half-written config. Two
# honest differences from the agent's own protocol (rules/rust.md "Config writes:
# render -> swap -> validate"), stated because a comment claiming a discipline it does
# not follow is what stops the next reader looking. First, the agent's order is
# rename-THEN-validate -- the validator has to read the file at the path the daemon
# reads, which a temp file is not -- and this step has no validator at all: what it
# writes is checked afterwards, by observation, in pg_assert_no_tcp_listener below.
# Second, `mktemp` puts the temporary file in $TMPDIR (/tmp by default), not in the
# target's directory, so this `mv` is an atomic rename only while the two share a
# filesystem and a copy otherwise.
pg_restrict_to_unix_socket() {
  local conf hba tmp
  conf="$(pg_conf_path)"
  hba="$(pg_hba_path)"
  if [ -z "$conf" ] || [ ! -f "$conf" ]; then
    # Says WHERE it looked and what is there. The first version of this message said only that
    # the file could not be located, and diagnosing the depth bug above needed a photograph of a
    # console: a refusal that does not name its own search is a refusal somebody has to reproduce
    # before they can read it.
    echo "30-postgresql.sh: could not locate postgresql.conf" >&2
    echo "  searched: find /etc/postgresql -maxdepth 3 -name postgresql.conf" >&2
    echo "  /etc/postgresql holds:" >&2
    find /etc/postgresql -maxdepth 3 2>/dev/null | sed 's/^/    /' >&2 || echo "    (the directory does not exist)" >&2
    exit 1
  fi
  # pg_hba.conf is validated exactly like postgresql.conf. Without this, an empty result
  # from pg_hba_path() reaches `mv -f "$tmp" ""` and the operator's diagnostic for "the
  # installer could not find PostgreSQL's config" is mv complaining about an empty argument.
  if [ -z "$hba" ] || [ ! -f "$hba" ]; then
    echo "30-postgresql.sh: could not locate pg_hba.conf" >&2
    exit 1
  fi

  if grep -q "^listen_addresses" "$conf"; then
    sed -i "s/^listen_addresses.*/listen_addresses = ''/" "$conf"
  else
    echo "listen_addresses = ''" >> "$conf"
  fi

  # This step REPLACES pg_hba.conf wholesale, which is what makes the unix-socket-only
  # guarantee a guarantee rather than a hope: any `host` line left in place would keep a TCP
  # grant alive. Wholesale replacement also discards anything the operator added by hand
  # (replication lines are the usual case), so the file being replaced is preserved once —
  # only if no preserved copy exists yet, so a re-run can never overwrite the true original
  # with a copy of Maran's own managed file — and the operator is told where it went. Silently
  # discarding an operator's configuration is destructive even when the outcome is idempotent.
  if [ ! -f "${hba}.pre-maran" ]; then
    cp -p "$hba" "${hba}.pre-maran"
    echo "Saved the previous pg_hba.conf to ${hba}.pre-maran."
    echo "  Maran manages pg_hba.conf: any lines you added there (replication, for example)"
    echo "  are not carried over and must be re-applied by hand after reviewing them."
  fi

  tmp="$(mktemp)"
  {
    echo "# Managed by Maran installer: unix-socket-only access."
    echo "local   all             postgres                                peer"
    echo "local   ${MARAN_DB_NAME}   ${MARAN_DB_ROLE}                            peer"
  } > "$tmp"
  chmod --reference="$hba" "$tmp" 2>/dev/null || chmod 640 "$tmp"
  chown --reference="$hba" "$tmp" 2>/dev/null || true
  mv -f "$tmp" "$hba"
}

# pg_create_role_and_db: idempotent role/database creation using `IF NOT EXISTS`-style
# checks via psql, run as the postgres OS user over the unix socket (peer auth).
#
# `runuser`, not `sudo`. The installer is already root (install.sh refuses to start otherwise),
# so nothing here needs sudo's privilege escalation — only its uid switch. `sudo` is a package
# that a minimal Debian netinst or a minimal RHEL image does not install, and this step never
# adds it to the dependency set, so `sudo -u postgres` was a dependency the installer assumed
# and never declared. `runuser` ships in util-linux, which is part of the base system on both
# supported families and cannot be absent on a host that boots.
pg_create_role_and_db() {
  local role_exists db_exists
  role_exists="$(runuser -u postgres -- psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='${MARAN_DB_ROLE}'")"
  if [ "$role_exists" != "1" ]; then
    runuser -u postgres -- psql -c "CREATE ROLE ${MARAN_DB_ROLE} LOGIN;"
  fi

  db_exists="$(runuser -u postgres -- psql -tAc "SELECT 1 FROM pg_database WHERE datname='${MARAN_DB_NAME}'")"
  if [ "$db_exists" != "1" ]; then
    runuser -u postgres -- psql -c "CREATE DATABASE ${MARAN_DB_NAME} OWNER ${MARAN_DB_ROLE};"
  fi
}

# pg_assert_no_tcp_listener: the observation behind the message. listen_addresses is a
# `postmaster`-context GUC, so it is applied by a full restart and by nothing else; asking the
# running server what it ended up with is the only way this step can claim "unix-socket only"
# and be telling the truth. An empty listen_addresses means no TCP socket was opened at all.
pg_assert_no_tcp_listener() {
  local listen
  listen="$(runuser -u postgres -- psql -tAc "SHOW listen_addresses" | tr -d '[:space:]')"
  if [ -n "$listen" ]; then
    echo "30-postgresql.sh: PostgreSQL is still listening on TCP (listen_addresses = '${listen}')." >&2
    echo "  The unix-socket-only configuration was written but the running server did not adopt" >&2
    echo "  it. Check that $(pg_conf_path) is the config file the running server loaded" >&2
    echo "  (SHOW config_file) and that the service actually restarted. Aborting." >&2
    exit 1
  fi
}

step_postgresql() {
  echo "Installing and configuring PostgreSQL (unix socket only)..."
  pg_install
  pg_restrict_to_unix_socket
  systemctl enable --now "$(pg_service_name)"
  # RESTART, not reload. listen_addresses has GUC context `postmaster`: a SIGHUP reload
  # re-reads the file and keeps the listener the postmaster opened at startup. `reload ||
  # restart` was worse than a no-op — the reload SUCCEEDS on both families (Debian's meta
  # unit is `ExecReload=/bin/true`; RHEL's is `kill -HUP $MAINPID`), which short-circuits the
  # `||` so the restart never ran, and the package-started server kept its TCP listener on
  # 127.0.0.1:5432 while this step printed "unix-socket only".
  systemctl restart "$(pg_service_name)"
  # After the server is up and before the panel is ever asked to talk to it: the floor is checked
  # where a failure still means "no changes to the panel" rather than a crash loop in step 70.
  assert_pg_version_at_least_floor
  pg_create_role_and_db
  pg_assert_no_tcp_listener
  echo "PostgreSQL ready: database '${MARAN_DB_NAME}', role '${MARAN_DB_ROLE}', unix-socket only."
}
