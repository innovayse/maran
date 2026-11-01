#!/usr/bin/env bash
# Step 41: the rotation policy for the installer's own transcript (issue #131).
#
# /var/log/maran had a rotation policy for every file in it except one: install.log, which this
# installer appends a whole transcript to on every run. The other policies are installed by the step
# that makes their logs exist (80 for the panel vhost, 81 for the site logs, 89 for FTPS); this file
# is written by install.sh itself from its first line, so its policy has no such step and gets this
# one, run right after step 40 has made the directory what it must be.
#
# The policy's retention and security choices are argued inside the shipped file
# (installer/logrotate/maran-install), because that file is what an operator reads and edits and
# this script is not.
set -euo pipefail

# Where the policy lands. Removed by installer/uninstall.sh by this same literal.
readonly MARAN_INSTALL_LOGROTATE_DEST="/etc/logrotate.d/maran-install"

# install_install_log_logrotate_policy: install /etc/logrotate.d/maran-install, root:root 0644.
#
# The payload is reached as `${LIB_DIR}/../logrotate/...`, the way step 80 reaches its own: LIB_DIR is
# set wherever a step is sourced, and SCRIPT_DIR only by install.sh, so a step that read SCRIPT_DIR
# would abort under `set -u` when the polygon drives it on its own.
install_install_log_logrotate_policy() {
  local source="${LIB_DIR}/../logrotate/maran-install"
  if [ ! -r "$source" ]; then
    echo "41-log-retention.sh: ${source} is missing from the installer payload. Aborting." >&2
    exit 1
  fi
  install -D -o root -g root -m 0644 "$source" "$MARAN_INSTALL_LOGROTATE_DEST"
}

step_log_retention() {
  echo "Installing the rotation policy for the install log..."
  install_install_log_logrotate_policy
}
