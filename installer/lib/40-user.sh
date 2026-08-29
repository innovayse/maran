#!/usr/bin/env bash
# Step 40: create the unprivileged `panel` system user that maran-api runs as
# (rules/architecture.md: the API is never root), and the on-disk directory layout
# with correct ownership and modes. The agent runs as root and needs no dedicated user.
set -euo pipefail

readonly MARAN_USER="panel"
readonly MARAN_GROUP="panel"

# create_panel_user: a system account with no login shell and no home directory of its
# own under /home (it must never be confused with a customer hosting account). Idempotent:
# useradd is skipped if the user already exists.
create_panel_user() {
  if id -u "$MARAN_USER" >/dev/null 2>&1; then
    echo "User '${MARAN_USER}' already exists."
  else
    useradd --system --no-create-home --shell /usr/sbin/nologin --user-group "$MARAN_USER"
    echo "Created system user '${MARAN_USER}'."
  fi
}

# create_directory_layout: every path Maran owns, with the tightest mode that still
# lets the intended process read/write it. /etc/maran holds panel.env (written with
# its own 0640 mode by 60-config.sh); the directory itself only needs to be traversable.
create_directory_layout() {
  install -d -o root  -g root         -m 0755 /usr/local/maran
  install -d -o root  -g "$MARAN_GROUP" -m 0750 /etc/maran
  install -d -o "$MARAN_USER" -g "$MARAN_GROUP" -m 0750 /var/lib/maran
  install -d -o "$MARAN_USER" -g "$MARAN_GROUP" -m 0750 /var/log/maran
  # /run/maran is normally recreated on boot by the systemd unit's RuntimeDirectory=
  # (see installer/systemd/maran-agent.service); created here too so the directory
  # exists immediately for the rest of this install run, before services first start.
  install -d -o root -g "$MARAN_GROUP" -m 0750 /run/maran
}

step_user() {
  echo "Creating '${MARAN_USER}' system user and directory layout..."
  create_panel_user
  create_directory_layout
  echo "User and directories ready."
}
