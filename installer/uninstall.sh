#!/usr/bin/env bash
# Maran uninstaller. Stops and removes everything the installer created —
# services, binaries, units, the nginx vhost, the system user — but never deletes
# customer data (the PostgreSQL database, /home/* hosting accounts, backups) without
# an explicit, separate confirmation for each category. "Honest" here means: default
# to keeping data, ask before destroying anything an operator cannot get back.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "uninstall.sh: must be run as root (try: sudo bash uninstall.sh)" >&2
  exit 1
fi

MARAN_ASSUME_YES=0
for arg in "$@"; do
  case "$arg" in
    --yes|-y) MARAN_ASSUME_YES=1 ;;
  esac
done

# confirm: interactive yes/no gate for destructive, data-loss actions. --yes on the
# command line is an explicit opt-out for scripted/CI teardown of disposable test
# hosts; it is never the default.
confirm() {
  local prompt="$1"
  if [ "$MARAN_ASSUME_YES" -eq 1 ]; then
    return 0
  fi
  local reply
  read -r -p "${prompt} [y/N] " reply </dev/tty || reply="n"
  [[ "$reply" =~ ^[Yy]$ ]]
}

stop_and_disable_services() {
  echo "Stopping Maran services..."
  systemctl stop maran-api.service maran-agent.service 2>/dev/null || true
  systemctl disable maran-api.service maran-agent.service 2>/dev/null || true
}

remove_systemd_units() {
  echo "Removing systemd units..."
  rm -f /etc/systemd/system/maran-api.service /etc/systemd/system/maran-agent.service
  systemctl daemon-reload
}

remove_nginx_vhost() {
  echo "Removing nginx vhost..."
  rm -f /etc/nginx/conf.d/maran.conf
  systemctl reload nginx 2>/dev/null || true
}

remove_binaries() {
  echo "Removing Maran binaries..."
  rm -rf /usr/local/maran
}

# remove_config_and_state: /etc/maran holds panel.env (the encryption key), agent.env and
# the panel's TLS key/cert; /run/maran is the agent's socket directory.
#
# Runs AFTER drop_database on purpose: the encryption key in panel.env is the only thing
# that can decrypt the secrets stored in the database, so an operator who chose to KEEP
# the database must be told, before the choice is irreversible, that the key is about to
# go with this directory.
remove_config_and_state() {
  if [ "$MARAN_DATABASE_KEPT" -eq 1 ]; then
    echo "WARNING: you kept the Maran database, but /etc/maran/panel.env holds the encryption key"
    echo "         for every secret stored in it. Back up /etc/maran/panel.env NOW if you intend to"
    echo "         reattach that database to a future install; without the key its secrets are lost."
    if ! confirm "Delete /etc/maran anyway (including the encryption key)?"; then
      echo "Keeping /etc/maran. Remove it yourself once the key is backed up."
      rm -rf /run/maran
      return
    fi
  fi
  echo "Removing config and runtime state (/etc/maran, /run/maran)..."
  rm -rf /etc/maran /run/maran
}

# remove_var_lib: the api's own state directory, created by installer/lib/40-user.sh.
# Everything under it is derivable and rebuildable (rules/architecture.md: "Truth lives in
# PostgreSQL"), so it is removed unconditionally like the binaries — leaving it behind is
# what made a previous uninstall incomplete.
remove_var_lib() {
  echo "Removing api state directory (/var/lib/maran)..."
  rm -rf /var/lib/maran
}

remove_logs() {
  if confirm "Delete install and application logs under /var/log/maran?"; then
    rm -rf /var/log/maran
    echo "Logs removed."
  else
    echo "Keeping /var/log/maran."
  fi
}

# drop_database: the panel's own PostgreSQL database and role. Asked separately from
# everything else because it is the one thing that cannot be re-downloaded — it is the
# customer's actual data (rules/architecture.md: "Truth lives in PostgreSQL").
# MARAN_DATABASE_KEPT: 1 once the operator has declined to drop the database, so
# remove_config_and_state knows the encryption key still has data to protect. Defaults to
# "kept" because a psql-less host reaches neither branch below and never lost the data.
MARAN_DATABASE_KEPT=1
drop_database() {
  if ! command -v psql >/dev/null 2>&1; then
    return
  fi
  if confirm "DROP the Maran PostgreSQL database and role? This deletes all panel data permanently."; then
    sudo -u postgres psql -c "DROP DATABASE IF EXISTS maran;" || true
    sudo -u postgres psql -c "DROP ROLE IF EXISTS panel;" || true
    MARAN_DATABASE_KEPT=0
    echo "Database and role dropped."
  else
    echo "Keeping the Maran PostgreSQL database and role."
  fi
}

# remove_panel_user: the system account Maran created for the api. Never touches
# customer hosting accounts under /home — those are a separate, unrelated namespace
# this uninstaller does not enumerate or manage.
remove_panel_user() {
  if ! id -u panel >/dev/null 2>&1; then
    return
  fi
  if confirm "Remove the 'panel' system user (its own home/state, not customer accounts)?"; then
    userdel panel 2>/dev/null || true
    echo "'panel' system user removed."
  else
    echo "Keeping the 'panel' system user."
  fi
}

# note_customer_data_untouched: explicit statement of what this script never touches,
# printed unconditionally so an operator never has to guess.
note_customer_data_untouched() {
  cat <<'EOF'

This uninstaller never touches:
  - Customer hosting accounts under /home/*
  - Customer sites, files, or per-account databases
  - Backups created by the Backups module

Remove those yourself if you intend to decommission the server entirely.
EOF
}

main() {
  echo "Uninstalling Maran..."
  stop_and_disable_services
  remove_systemd_units
  remove_nginx_vhost
  remove_binaries
  # The database question comes before /etc/maran is deleted: keeping the data while
  # silently destroying the key that decrypts it is the one unrecoverable mistake this
  # script could make on its own.
  drop_database
  remove_config_and_state
  remove_var_lib
  remove_logs
  remove_panel_user
  note_customer_data_untouched
  echo "Maran uninstall complete."
}

main "$@"
