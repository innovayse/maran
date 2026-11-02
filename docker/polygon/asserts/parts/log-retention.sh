# Retention for /var/log/maran (issue #131): step 41 installs the rotation policy for the
# installer's own transcript, and the real logrotate reads it.
#
# Three things are checked, each on the artefact the system uses:
#   * the installed file at the path logrotate reads, root:root 0644 and not a link, byte for byte
#     the shipped payload;
#   * the one path its single stanza rotates is the path install.sh writes its transcript to —
#     compared against install.sh's own MARAN_LOG_DIR/MARAN_LOG_FILE, not against a literal here;
#   * `logrotate --debug` accepts the installed file, with an inverse control: the same file with a
#     broken directive must be REFUSED, or the acceptance above proves nothing about parsing.

# The policy the step installs, and the payload it installs it from.
readonly INSTALL_LOGROTATE_DEST="/etc/logrotate.d/maran-install"
readonly INSTALL_LOGROTATE_SOURCE="${INSTALLER_ROOT}/logrotate/maran-install"

assert_the_install_log_rotation_policy_is_installed_and_parsed() {
  [ -r "${INSTALLER_LIB}/41-log-retention.sh" ] \
    || fail "installer/lib/41-log-retention.sh is not in this image; the suite Dockerfile must COPY it"
  [ -r "$INSTALL_LOGROTATE_SOURCE" ] \
    || fail "installer/logrotate/maran-install is not in this image; the suite Dockerfile must COPY it"
  command -v logrotate >/dev/null 2>&1 \
    || fail "logrotate is not installed in this image, so nothing here can say whether the policy parses; the suite Dockerfile installs it"

  # A child with errexit of its own, the way install.sh runs a step (see run_installer_step).
  bash -c 'set -euo pipefail
LIB_DIR="$1"
. "$1/41-log-retention.sh"
step_log_retention' _ "$INSTALLER_LIB" >/dev/null \
    || fail "step 41 (step_log_retention) failed"

  assert_file_is "$INSTALL_LOGROTATE_DEST" "0:0:644"
  cmp -s "$INSTALL_LOGROTATE_SOURCE" "$INSTALL_LOGROTATE_DEST" \
    || fail "${INSTALL_LOGROTATE_DEST} differs from the shipped installer/logrotate/maran-install"

  # The transcript's path as install.sh spells it: MARAN_LOG_FILE="${MARAN_LOG_DIR}/install.log".
  local log_dir log_file stanza_lines stanza_count stanza_path
  log_dir="$(sed -n 's/^MARAN_LOG_DIR="\(.*\)"$/\1/p' "${INSTALLER_ROOT}/install.sh")"
  log_file="$(sed -n 's/^MARAN_LOG_FILE="\${MARAN_LOG_DIR}\(.*\)"$/\1/p' "${INSTALLER_ROOT}/install.sh")"
  [ -n "$log_dir" ] && [ -n "$log_file" ] \
    || fail "could not read MARAN_LOG_DIR and MARAN_LOG_FILE out of install.sh; the comparison below would be against nothing"
  stanza_lines="$( { grep -E '^[[:space:]]*/[^[:space:]]+[[:space:]]*\{[[:space:]]*$' "$INSTALL_LOGROTATE_DEST" || true; } )"
  stanza_count="$(printf '%s\n' "$stanza_lines" | grep -c . || true)"
  [ "$stanza_count" -eq 1 ] \
    || fail "${INSTALL_LOGROTATE_DEST} has ${stanza_count} rotation stanzas, not the 1 this comparison is written for"
  stanza_path="$(printf '%s' "$stanza_lines" | sed -E 's/^[[:space:]]*([^[:space:]]+)[[:space:]]*\{.*$/\1/')"
  [ "$stanza_path" = "${log_dir}${log_file}" ] \
    || fail "the install log policy rotates ${stanza_path}, but install.sh writes its transcript to ${log_dir}${log_file}; the transcript would grow unbounded"

  # The installed policy, parsed by the real logrotate. --debug changes nothing on disk.
  # A state path in a fresh directory, never written in --debug mode and absent on purpose: an
  # absent state file is the first-run case logrotate handles on every new host.
  local state_dir state output
  state_dir="$(mktemp -d)"
  state="${state_dir}/logrotate.status"
  if ! output="$(logrotate --debug --state "$state" "$INSTALL_LOGROTATE_DEST" 2>&1)"; then
    rm -rf "$state_dir"
    fail "logrotate refused ${INSTALL_LOGROTATE_DEST}: ${output}"
  fi

  # Inverse control: the same policy with a directive logrotate cannot read must be refused, or the
  # acceptance above would be the answer of a parse that never looked.
  local broken
  broken="$(mktemp)"
  sed 's/^\([[:space:]]*\)rotate 12$/\1rotate twelve/' "$INSTALL_LOGROTATE_DEST" > "$broken"
  cmp -s "$broken" "$INSTALL_LOGROTATE_DEST" \
    && fail "the inverse control did not change the policy; the 'rotate 12' line it breaks is gone"
  if logrotate --debug --state "$state" "$broken" >/dev/null 2>&1; then
    rm -rf "$state_dir"
    rm -f "$broken"
    fail "logrotate ACCEPTED a policy reading 'rotate twelve', so its acceptance of the real policy proves nothing"
  fi
  rm -rf "$state_dir"
  rm -f "$broken"

  echo "Step 41's rotation policy for ${log_dir}${log_file} is installed root:root 0644, matches its payload, rotates the file install.sh writes, and is parsed by logrotate (which refuses a broken copy)."
  echo "UNOBSERVED HERE: an actual rotation over time — this image runs no cron or timer — and whether the distribution's logrotate timer is enabled on a real host."
}
