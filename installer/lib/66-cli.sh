#!/usr/bin/env bash
# 66-cli.sh: installs the `maran` break-glass command for an operator who is locked out.
#
# The wrapper goes to /usr/local/sbin/maran, 0700 root:root, and its helpers to
# /usr/local/maran/cli/lib (root-owned, not writable by anyone else). /usr/local/sbin is on the
# root PATH of both families and on current RHEL-family secure_path, but secure_path is
# configurable, so the operator is also given the absolute path. Root only on purpose: the command mints a credential for an administrator account.
#
# Placed after 65-schema.sh and before 70-services.sh: it needs only the panel binary 50-artifacts.sh
# installed, and a step that cannot copy a file should stop the install while the operator watches.
# Re-running overwrites both, so an update always leaves the command matching the installed panel.

readonly MARAN_CLI_SOURCE_DIR="${SCRIPT_DIR:?must be set by install.sh}/cli"
readonly MARAN_CLI_WRAPPER_PATH="/usr/local/sbin/maran"
readonly MARAN_CLI_LIB_INSTALL_DIR="/usr/local/maran/cli/lib"

# step_cli: copies the wrapper and its libraries into place with exact modes.
step_cli() {
  local lib
  [ -f "${MARAN_CLI_SOURCE_DIR}/maran" ] \
    || { echo "66-cli.sh: ${MARAN_CLI_SOURCE_DIR}/maran is missing from the installer package." >&2; exit 1; }

  # /usr/local/sbin belongs to the distribution, not to us: `install -d` would re-mode it (Debian
  # ships it 2775 root:staff), so it is created only when absent and then OBSERVED. A directory any
  # user can write into lets that user replace the wrapper root runs, so that is a refusal; a
  # group-writable one (Debian's staff) is the distribution's own choice and is reported.
  [ -d /usr/local/sbin ] || install -d -m 0755 -o root -g root /usr/local/sbin
  local sbin_mode
  sbin_mode="$(stat -c '%a' /usr/local/sbin)"
  if [ "$(stat -c '%u' /usr/local/sbin)" -ne 0 ] || [ $(( 8#${sbin_mode: -1} & 2 )) -ne 0 ]; then
    echo "66-cli.sh: /usr/local/sbin is not root-owned or is writable by every user; refusing to put a root-only command there." >&2
    exit 1
  fi
  if [ $(( 8#${sbin_mode: -2:1} & 2 )) -ne 0 ]; then
    echo "NOTE: /usr/local/sbin is group-writable (mode ${sbin_mode}); members of that group could replace /usr/local/sbin/maran."
  fi
  install -d -m 0755 -o root -g root /usr/local/maran/cli "$MARAN_CLI_LIB_INSTALL_DIR"
  for lib in common.sh status.sh reset.sh diagnostics.sh panel-archive.sh panel-backup.sh panel-restore.sh; do
    install -m 0644 -o root -g root "${MARAN_CLI_SOURCE_DIR}/lib/${lib}" "${MARAN_CLI_LIB_INSTALL_DIR}/${lib}"
  done
  install -m 0700 -o root -g root "${MARAN_CLI_SOURCE_DIR}/maran" "$MARAN_CLI_WRAPPER_PATH"
  echo "Installed ${MARAN_CLI_WRAPPER_PATH} (root only)."
}
