#!/usr/bin/env bash
# The script `maran deploy-local` ships to a development host and runs there as root, through
# `sudo bash deploy-local-remote.sh <stage dir>` (issue #410). It is NOT sourced and is never run on
# a workstation. It does one thing: unpack the installer copy into a 0700 root-owned directory and
# run the installer's offline path against the bundle, then report the installer's exit status.
#
# The stage directory is created by the login user (mktemp -d) and so is writable by that user; the
# installer therefore never runs from it. Everything is copied or unpacked into a fresh directory
# under /root first, which only root can enter (the offline-tarball directory rule). No password is
# read, stored or passed anywhere in this file: sudo's own prompt, if any, happened before it started.
set -euo pipefail

# fail: <message>. Prints the message to stderr and exits 1; used for every refusal below.
fail() {
  echo "deploy-local: $1" >&2
  exit 1
}

# main: <stage dir>. Re-validates the stage path (the exact pattern the workstation applied), refuses
# symlinked inputs, stages into a private directory, runs the installer and exits with its status.
main() {
  local stage="${1:-}" work status=0 pattern='^/tmp/maran-deploy-stage\.[A-Za-z0-9]{10}$'
  if [[ ! "$stage" =~ $pattern ]]; then
    fail "refused - the stage directory is not /tmp/maran-deploy-stage.<10 characters>"
  fi
  [[ -d "$stage" && ! -L "$stage" ]] || fail "refused - the stage directory is missing or a symlink"
  local f
  for f in bundle.tar.gz installer.tar.gz; do
    [[ -f "$stage/$f" && ! -L "$stage/$f" ]] || fail "refused - $stage/$f is missing or not a regular file"
  done

  work="$(mktemp -d /root/maran-deploy.XXXXXX)"
  trap 'rm -rf -- "$work"' EXIT
  chmod 0700 "$work"
  install -m 0600 -o root -g root "$stage/bundle.tar.gz" "$work/bundle.tar.gz"
  tar -xzf "$stage/installer.tar.gz" --no-same-owner -C "$work"
  [[ -f "$work/installer/install.sh" ]] || fail "refused - the installer copy has no install.sh"

  (cd "$work/installer" && bash install.sh --offline-tarball "$work/bundle.tar.gz") || status=$?
  echo "deploy-local: installer exited $status"
  exit "$status"
}

main "$@"
