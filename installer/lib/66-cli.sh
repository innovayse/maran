#!/usr/bin/env bash
# 66-cli.sh: installs the `maran` break-glass command for an operator who is locked out.
#
# The wrapper goes to /usr/local/sbin/maran, 0700 root:root, its helpers to
# /usr/local/maran/cli/lib and the release-signing public key that `maran update` verifies against
# to /usr/local/maran/cli/keys/release-signing.pub (all root-owned, not writable by anyone else;
# the key is 0644 in a 0755 directory, and the CLI has no knob that points the check at another key).
# The catalogue of verbs and flags that `maran help --json` prints goes beside them, 0644 in the same
# 0755 directory; the wrapper never reads it to decide anything.
# /usr/local/sbin is on the root PATH of both families and on current RHEL-family secure_path, but
# secure_path is configurable, so the operator is also given the absolute path. Root only on purpose:
# the command mints a credential for an administrator account.
#
# Placed after 65-schema.sh and before 70-services.sh: it needs only the panel binary 50-artifacts.sh
# installed, and a step that cannot copy a file should stop the install while the operator watches.
# The library list below is the install list: a library the wrapper sources but this loop omits makes
# every verb fail on the server, so the step refuses when any listed library, or the key, is missing
# from the installer package. Re-running overwrites all three, so an update always leaves the command
# matching the installed panel.

readonly MARAN_CLI_SOURCE_DIR="${SCRIPT_DIR:?must be set by install.sh}/cli"
readonly MARAN_CLI_WRAPPER_PATH="/usr/local/sbin/maran"
readonly MARAN_CLI_LIB_INSTALL_DIR="/usr/local/maran/cli/lib"
readonly MARAN_CLI_KEY_INSTALL_DIR="/usr/local/maran/cli/keys"
# The same package file 50-artifacts.sh verifies the first install against.
readonly MARAN_CLI_KEY_SOURCE="${SCRIPT_DIR}/keys/release-signing.pub"
# The catalogue `maran help --json` prints (issue #155): data only, installed beside the libraries.
readonly MARAN_CLI_CATALOGUE_SOURCE="${MARAN_CLI_SOURCE_DIR}/commands.json"
readonly MARAN_CLI_CATALOGUE_INSTALL_PATH="/usr/local/maran/cli/commands.json"
# Every library installer/cli/maran sources, in the order it sources them after the wrapper's own.
readonly CLI_LIB_NAMES=(common.sh status.sh reset.sh panel-certificate.sh diagnostics.sh panel-archive.sh
  panel-backup-config.sh panel-backup-retention.sh panel-backup-upload.sh panel-backup.sh
  panel-backup-schedule.sh panel-restore.sh release-version.sh release-verify.sh release-unpack.sh
  release-layout.sh panel-dump.sh update-dumps.sh update-health.sh update-unattended.sh update.sh
  update-schedule.sh rollback.sh)

# step_cli: copies the wrapper, its libraries and the release-signing public key into place with
# exact modes; refuses before writing anything if the package lacks the wrapper, a library or the key.
step_cli() {
  local lib
  for lib in "${CLI_LIB_NAMES[@]}"; do
    [ -f "${MARAN_CLI_SOURCE_DIR}/lib/${lib}" ] \
      || { echo "66-cli.sh: ${MARAN_CLI_SOURCE_DIR}/lib/${lib} is missing from the installer package." >&2; exit 1; }
  done
  [ -f "$MARAN_CLI_KEY_SOURCE" ] \
    || { echo "66-cli.sh: ${MARAN_CLI_KEY_SOURCE} is missing from the installer package;" \
           "maran update could not verify a release." >&2
         exit 1; }
  [ -f "$MARAN_CLI_CATALOGUE_SOURCE" ] \
    || { echo "66-cli.sh: ${MARAN_CLI_CATALOGUE_SOURCE} is missing from the installer package." >&2; exit 1; }
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
  install -d -m 0755 -o root -g root /usr/local/maran/cli "$MARAN_CLI_LIB_INSTALL_DIR" "$MARAN_CLI_KEY_INSTALL_DIR"
  for lib in "${CLI_LIB_NAMES[@]}"; do
    install -m 0644 -o root -g root "${MARAN_CLI_SOURCE_DIR}/lib/${lib}" "${MARAN_CLI_LIB_INSTALL_DIR}/${lib}"
  done
  install -m 0644 -o root -g root "$MARAN_CLI_KEY_SOURCE" "${MARAN_CLI_KEY_INSTALL_DIR}/release-signing.pub"
  install -m 0644 -o root -g root "$MARAN_CLI_CATALOGUE_SOURCE" "$MARAN_CLI_CATALOGUE_INSTALL_PATH"
  install -m 0700 -o root -g root "${MARAN_CLI_SOURCE_DIR}/maran" "$MARAN_CLI_WRAPPER_PATH"
  echo "Installed ${MARAN_CLI_WRAPPER_PATH} (root only)."
}
