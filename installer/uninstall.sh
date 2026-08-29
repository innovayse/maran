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

remove_config_and_state() {
  echo "Removing config and runtime state (/etc/maran, /run/maran)..."
  rm -rf /etc/maran /run/maran
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
drop_database() {
  if ! command -v psql >/dev/null 2>&1; then
    return
  fi
  if confirm "DROP the Maran PostgreSQL database and role? This deletes all panel data permanently."; then
    sudo -u postgres psql -c "DROP DATABASE IF EXISTS maran;" || true
    sudo -u postgres psql -c "DROP ROLE IF EXISTS panel;" || true
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
  remove_config_and_state
  remove_logs
  drop_database
  remove_panel_user
  note_customer_data_untouched
  echo "Maran uninstall complete."
}

main "$@"
