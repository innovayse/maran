#!/usr/bin/env bash
# Step 86: lay the three host-level things an SFTP login needs before the agent
# can create one — the group OpenSSH matches on, the base directory the per-account
# jails live under, and the one `Match Group` block that makes membership of that
# group mean "chrooted SFTP, and nothing else".
#
# What this step does NOT do, and why each omission is deliberate:
#
# - It does not create a per-account jail, its `home` mount point, or the systemd
#   bind-mount unit that fills it. Those are account-lifetime resources and belong
#   to the agent's `create_sftp_user`, which derives every path from a validated
#   `AccountName`. The installer runs once, before any account exists; it lays the
#   ground the agent then builds on.
#
# - It does not touch `/home/<account>` ownership. An account's home is
#   `<account>:<web server group> 0750` and every site, vhost and php-fpm pool
#   depends on that. OpenSSH refuses to chroot into a directory that is not
#   root-owned and not group-writable-free, which is precisely why the chroot is
#   the JAIL (`/var/lib/maran/sftp/<account>`, set as the login's passwd home, so
#   `ChrootDirectory %h` resolves to it) with the real home bind-mounted inside.
#   The jail exists so the home never has to change. Changing the home here would
#   undo the design and break sites in the same move.
#
# - It does not install openssh-server. A server Maran is being installed on is a
#   server the operator reached over SSH.
#
# Every action is idempotent, because installers get re-run: the group is created
# only if absent, `install -d` is happy with a directory that exists, and the sshd
# block is delimited by markers and REPLACED rather than appended, so a re-run
# leaves exactly one block no matter how many times it happens.
set -euo pipefail

# The group name `DistroAdapter::sftp_group()` returns on every family. It is one
# string in two places by necessity — the agent adds logins to it, the installer
# creates it — and the polygon images assert them equal.
readonly MARAN_SFTP_GROUP="maran-sftp"

# The base directory holding one root-owned jail per account, matching
# `AgentPaths::SFTP_JAIL_ROOT`. root:root 0755: it is the parent of every chroot
# on the host, so a login that reaches it must not be able to write in it, and
# OpenSSH walks it on every chroot.
readonly MARAN_SFTP_JAIL_ROOT="/var/lib/maran/sftp"

readonly MARAN_SSHD_CONFIG="/etc/ssh/sshd_config"

# The marker comments delimiting our block. They are the whole idempotency
# mechanism: the block between them is deleted and rewritten on every run, so the
# file converges on one current block whether it had none, one, or an older one.
readonly MARAN_SSHD_BEGIN_MARKER="# BEGIN Maran SFTP — managed by installer/lib/86-sftp.sh, do not edit between markers"
readonly MARAN_SSHD_END_MARKER="# END Maran SFTP"

# sshd_service_name: Debian names the unit `ssh`, RHEL names it `sshd`.
sshd_service_name() {
  case "${MARAN_OS_FAMILY:-}" in
    rhel) echo "sshd" ;;
    *)    echo "ssh" ;;
  esac
}

# sshd_binary: the daemon, used only to VALIDATE a candidate config before it
# becomes the live one. Not on root's PATH on every family, hence the fallback.
sshd_binary() {
  if command -v sshd >/dev/null 2>&1; then
    command -v sshd
  else
    echo "/usr/sbin/sshd"
  fi
}

# ensure_sftp_group: creates the group if it is not already there.
#
# A system group (no login, low gid range): it names a capability, never a person.
ensure_sftp_group() {
  if getent group "$MARAN_SFTP_GROUP" >/dev/null 2>&1; then
    return 0
  fi
  groupadd --system "$MARAN_SFTP_GROUP"
}

# ensure_jail_root: the base directory, root:root 0755.
#
# The parent (`/var/lib/maran`) is created plainly rather than with `install -d`'s
# ownership flags, because on a real install it already exists and belongs to
# step 40 — this step states an opinion about ITS directory only.
ensure_jail_root() {
  mkdir -p "$(dirname "$MARAN_SFTP_JAIL_ROOT")"
  install -d -o root -g root -m 0755 "$MARAN_SFTP_JAIL_ROOT"
}

# render_sshd_block: the block itself, on stdout.
#
# `ChrootDirectory %h` rather than a literal path: %h is the login's passwd home,
# which the agent sets to that account's jail, so this one block serves every
# account and there is no per-account sshd edit and no config rewrite on account
# creation. `internal-sftp` is the in-process subsystem, so a chroot with no shell
# and no binaries in it still works. Forwarding is off because an SFTP login is a
# file transfer credential, and a customer who can forward ports through it has a
# tunnel into the server's private network — including the panel's own
# unix-socket-only PostgreSQL.
render_sshd_block() {
  cat <<EOF
${MARAN_SSHD_BEGIN_MARKER}
Match Group ${MARAN_SFTP_GROUP}
    ChrootDirectory %h
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
${MARAN_SSHD_END_MARKER}
EOF
}

# install_sshd_match_block: puts exactly one current block at the END of sshd_config.
#
# At the end, and in the main file rather than a drop-in under sshd_config.d,
# because a `Match` block is terminated only by the next `Match` or by end of file:
# everything after it is conditional on it. Both families `Include
# sshd_config.d/*.conf` from the TOP of sshd_config, so a Match block dropped in
# there would silently make the entire rest of the distribution's configuration
# apply only to our group. Appending to the end is the one placement that means
# what it reads as.
#
# Render, validate, atomically replace — the same discipline 80-nginx.sh uses and
# the agent uses for customer configs. A candidate that `sshd -t` rejects never
# reaches the live file, because a broken sshd_config on a remote server is the
# one installer failure an operator cannot log in to fix.
#
# Public on purpose: the polygon images call THIS function and then assert the
# result, instead of writing the block themselves. An image that manufactures the
# precondition it asserts proves nothing about the installer.
install_sshd_match_block() {
  if [ ! -f "$MARAN_SSHD_CONFIG" ]; then
    echo "86-sftp.sh: ${MARAN_SSHD_CONFIG} not found; is openssh-server installed?" >&2
    exit 1
  fi

  local candidate
  candidate="$(mktemp)"
  # Drop any block we wrote before (including one from an older version of this
  # step), then append the current one. Deleting first is what makes a re-run
  # leave one block rather than two, and what makes an edit to the block above
  # actually reach a host that was installed last month.
  sed "/^${MARAN_SSHD_BEGIN_MARKER//\//\\/}\$/,/^${MARAN_SSHD_END_MARKER}\$/d" \
    "$MARAN_SSHD_CONFIG" > "$candidate"
  render_sshd_block >> "$candidate"

  chmod --reference="$MARAN_SSHD_CONFIG" "$candidate" 2>/dev/null || chmod 0600 "$candidate"
  chown --reference="$MARAN_SSHD_CONFIG" "$candidate" 2>/dev/null || true

  if ! "$(sshd_binary)" -t -f "$candidate" 2>&1; then
    rm -f "$candidate"
    echo "86-sftp.sh: the candidate sshd_config failed 'sshd -t'; the live config was not touched." >&2
    exit 1
  fi

  mv -f "$candidate" "$MARAN_SSHD_CONFIG"
}

# install_sftp_prerequisites: everything this step lays down, with no service
# management — so it is callable anywhere sshd is a file rather than a daemon,
# which is what lets the polygon images run the real thing.
install_sftp_prerequisites() {
  ensure_sftp_group
  ensure_jail_root
  install_sshd_match_block
}

step_sftp() {
  echo "Preparing chrooted SFTP (group ${MARAN_SFTP_GROUP}, jails under ${MARAN_SFTP_JAIL_ROOT})..."
  install_sftp_prerequisites
  # Reload rather than restart: existing SSH sessions — including the operator's
  # own, the one running this installer — survive a reload and do not survive a
  # restart on every distribution.
  systemctl reload "$(sshd_service_name)" 2>/dev/null || systemctl restart "$(sshd_service_name)"
  echo "SFTP prerequisites installed; per-account jails are created by the agent on first SFTP user."
}
