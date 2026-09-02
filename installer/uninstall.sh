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
  # And the include pointing at the agent's vhost directory. It goes with the panel's own
  # vhost rather than being left behind: /etc/maran is removed further down, and an
  # include naming a directory that no longer exists makes every later `nginx -t` on this
  # host fail — including ones that have nothing to do with Maran.
  rm -f /etc/nginx/conf.d/maran-sites.conf
  systemctl reload nginx 2>/dev/null || true
}

# remove_sftp_sshd_block: takes back the one edit the installer made to a file it
# does not own. Delimited by the same markers installer/lib/86-sftp.sh writes, so
# the removal is exactly the inverse of the install and cannot eat an operator's
# own configuration around it.
#
# Validated before it replaces the live file for the same reason it was validated
# on the way in: a host left with an sshd_config that sshd refuses is a host
# nobody can log in to fix, and "I uninstalled the panel" is a bad reason to lose
# a server.
remove_sftp_sshd_block() {
  local config="/etc/ssh/sshd_config"
  if [ ! -f "$config" ] || ! grep -q "^# BEGIN Maran SFTP" "$config"; then
    return
  fi
  echo "Removing the Maran SFTP block from ${config}..."
  local candidate sshd_bin
  candidate="$(mktemp)"
  sed '/^# BEGIN Maran SFTP/,/^# END Maran SFTP$/d' "$config" > "$candidate"
  chmod --reference="$config" "$candidate" 2>/dev/null || chmod 0600 "$candidate"
  chown --reference="$config" "$candidate" 2>/dev/null || true
  sshd_bin="$(command -v sshd || echo /usr/sbin/sshd)"
  if "$sshd_bin" -t -f "$candidate" >/dev/null 2>&1; then
    mv -f "$candidate" "$config"
    systemctl reload sshd 2>/dev/null || systemctl reload ssh 2>/dev/null || true
  else
    rm -f "$candidate"
    echo "WARNING: removing the Maran SFTP block would leave an sshd_config that 'sshd -t' rejects."
    echo "         Left the file alone; remove the block between the '# BEGIN Maran SFTP' and"
    echo "         '# END Maran SFTP' markers by hand."
  fi
}

# release_sftp_jails: stops and disables the per-account bind-mount units, then
# confirms nothing is still mounted under the jail root.
#
# This runs BEFORE anything deletes /var/lib/maran, and it is the most dangerous
# thing in this script if it is skipped: each jail has the account's REAL home
# bind-mounted at <jail>/home, so an `rm -rf /var/lib/maran` over a live mount
# deletes the customer's files through it. Unmounting first — and refusing to
# delete while any mount remains — is what keeps this uninstaller's promise that
# it never touches /home.
release_sftp_jails() {
  local unit
  for unit in /etc/systemd/system/var-lib-maran-sftp-*.mount; do
    [ -e "$unit" ] || continue
    unit="$(basename "$unit")"
    echo "Stopping SFTP jail mount ${unit}..."
    systemctl disable --now "$unit" 2>/dev/null || true
    rm -f "/etc/systemd/system/${unit}"
  done
  systemctl daemon-reload 2>/dev/null || true
  # Belt and braces: a mount that systemd does not own (a hand-run `mount --bind`)
  # is still a route into a customer's home.
  local mount_point
  while read -r mount_point; do
    [ -n "$mount_point" ] || continue
    echo "Unmounting ${mount_point}..."
    umount "$mount_point" 2>/dev/null || true
  done < <(awk '$2 ~ "^/var/lib/maran/sftp/" { print $2 }' /proc/self/mounts 2>/dev/null | sort -r)
}

# sftp_jails_still_mounted: 0 when something under the jail root is still a mount
# point. Consulted by remove_var_lib, which must not recurse through one.
sftp_jails_still_mounted() {
  awk '$2 ~ "^/var/lib/maran/sftp/" { found = 1 } END { exit found ? 0 : 1 }' \
    /proc/self/mounts 2>/dev/null
}

# remove_sftp_group: the group installer/lib/86-sftp.sh created. Removed only when
# it is empty — a member left in it is an SFTP login this script did not create
# and has no business deleting, and `groupdel` on a group that is some user's
# primary group would fail anyway.
remove_sftp_group() {
  if ! getent group maran-sftp >/dev/null 2>&1; then
    return
  fi
  local members
  members="$(getent group maran-sftp | cut -d: -f4)"
  if [ -n "$members" ]; then
    echo "Keeping the 'maran-sftp' group: it still has members (${members})."
    echo "Those SFTP logins and their jails under /var/lib/maran/sftp are customer accounts;"
    echo "remove them through the panel before uninstalling, or by hand afterwards."
    return
  fi
  groupdel maran-sftp 2>/dev/null || true
  echo "'maran-sftp' group removed."
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
#
# The one exception to "everything under it is derivable": /var/lib/maran/sftp
# holds the per-account jails, and each jail has the account's real home
# bind-mounted inside it. release_sftp_jails has already unmounted them; if
# anything is somehow still mounted, this refuses rather than deleting a
# customer's files through a mount point.
remove_var_lib() {
  if sftp_jails_still_mounted; then
    echo "WARNING: something is still mounted under /var/lib/maran/sftp."
    echo "         NOT deleting /var/lib/maran: an rm -rf across a bind mount would delete the"
    echo "         customer home it points at. Unmount them and remove the directory by hand."
    return
  fi
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
  - MariaDB itself, or any database in it
  - Backups created by the Backups module

Remove those yourself if you intend to decommission the server entirely.
EOF
}

main() {
  echo "Uninstalling Maran..."
  stop_and_disable_services
  remove_systemd_units
  remove_nginx_vhost
  # SFTP first, and in this order: take back the sshd edit, then unmount the jails
  # so that nothing further down can recurse through a bind mount into a home.
  remove_sftp_sshd_block
  release_sftp_jails
  remove_binaries
  # The database question comes before /etc/maran is deleted: keeping the data while
  # silently destroying the key that decrypts it is the one unrecoverable mistake this
  # script could make on its own.
  drop_database
  remove_config_and_state
  remove_var_lib
  remove_logs
  remove_sftp_group
  remove_panel_user
  note_customer_data_untouched
  echo "Maran uninstall complete."
}

main "$@"
