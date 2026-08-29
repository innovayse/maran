#!/usr/bin/env bash
# Step 30: install and initialise PostgreSQL, configured to accept connections over
# the unix socket ONLY (rules/architecture.md: "Only maran-api talks to PostgreSQL").
# TCP listening is disabled outright rather than firewalled, so there is no network
# attack surface to misconfigure later. Creates the panel role and database.
set -euo pipefail

readonly MARAN_DB_NAME="maran"
# The role is named after the unprivileged OS user ("panel") rather than the product,
# so PostgreSQL's peer auth (unix-socket connections are authenticated by matching OS
# username to role name) works with no pg_ident.conf mapping to maintain.
readonly MARAN_DB_ROLE="panel"

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
pg_conf_path() {
  case "$MARAN_OS_FAMILY" in
    debian)
      find /etc/postgresql -maxdepth 2 -name postgresql.conf 2>/dev/null | sort -V | tail -1
      ;;
    rhel)
      echo "/var/lib/pgsql/data/postgresql.conf"
      ;;
  esac
}

pg_hba_path() {
  case "$MARAN_OS_FAMILY" in
    debian)
      find /etc/postgresql -maxdepth 2 -name pg_hba.conf 2>/dev/null | sort -V | tail -1
      ;;
    rhel)
      echo "/var/lib/pgsql/data/pg_hba.conf"
      ;;
  esac
}

# pg_restrict_to_unix_socket: forces listen_addresses='' (no TCP at all) and rewrites
# pg_hba.conf to only allow local (unix socket) peer/trust-by-role connections. Written
# via a temp file + atomic mv so a crash mid-write never leaves a half-written config,
# matching the agent's own "render -> validate -> atomic rename" discipline.
pg_restrict_to_unix_socket() {
  local conf hba tmp
  conf="$(pg_conf_path)"
  hba="$(pg_hba_path)"
  if [ -z "$conf" ] || [ ! -f "$conf" ]; then
    echo "30-postgresql.sh: could not locate postgresql.conf" >&2
    exit 1
  fi

  if grep -q "^listen_addresses" "$conf"; then
    sed -i "s/^listen_addresses.*/listen_addresses = ''/" "$conf"
  else
    echo "listen_addresses = ''" >> "$conf"
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
pg_create_role_and_db() {
  local role_exists db_exists
  role_exists="$(sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='${MARAN_DB_ROLE}'")"
  if [ "$role_exists" != "1" ]; then
    sudo -u postgres psql -c "CREATE ROLE ${MARAN_DB_ROLE} LOGIN;"
  fi

  db_exists="$(sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='${MARAN_DB_NAME}'")"
  if [ "$db_exists" != "1" ]; then
    sudo -u postgres psql -c "CREATE DATABASE ${MARAN_DB_NAME} OWNER ${MARAN_DB_ROLE};"
  fi
}

step_postgresql() {
  echo "Installing and configuring PostgreSQL (unix socket only)..."
  pg_install
  pg_restrict_to_unix_socket
  systemctl enable --now "$(pg_service_name)"
  systemctl reload "$(pg_service_name)" || systemctl restart "$(pg_service_name)"
  pg_create_role_and_db
  echo "PostgreSQL ready: database '${MARAN_DB_NAME}', role '${MARAN_DB_ROLE}', unix-socket only."
}
