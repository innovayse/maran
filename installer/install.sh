#!/usr/bin/env bash
# Maran native production installer entry point.
#
# Usage: curl -sSL https://get.maran.com | bash
#    or: sudo bash install.sh [--offline-tarball <path>] [--channel stable|beta]
#
# This script is deliberately thin: it detects the OS/arch, verifies it is one of
# Maran's supported targets, sets up logging, then sources and runs the numbered
# steps under lib/ in order. All privileged, distro-specific and feature-specific logic
# lives in those step files, not here. Every step is idempotent (checks current state
# before acting) so the whole script is safe to re-run after an interrupted install —
# re-running simply resumes at whatever is not yet done.
set -euo pipefail

# Resolve the real directory of this script even if invoked via a symlink or a pipe
# (curl | bash runs from a FIFO, so we fall back to downloading lib/ alongside it —
# see install_from_pipe() below). BASH_SOURCE is empty when read from stdin.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-.}")" >/dev/null 2>&1 && pwd -P)"
LIB_DIR="${SCRIPT_DIR}/lib"

MARAN_LOG_DIR="/var/log/maran"
MARAN_LOG_FILE="${MARAN_LOG_DIR}/install.log"

# --- CLI arguments -----------------------------------------------------------------
# Parsed once here and exported so any step file can read them without re-parsing argv.
MARAN_CHANNEL="stable"
MARAN_OFFLINE_TARBALL=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --channel)
      MARAN_CHANNEL="${2:?--channel requires a value}"
      shift 2
      ;;
    --offline-tarball)
      MARAN_OFFLINE_TARBALL="${2:?--offline-tarball requires a path}"
      shift 2
      ;;
    *)
      echo "install.sh: unknown argument: $1" >&2
      exit 2
      ;;
  esac
done
export MARAN_CHANNEL MARAN_OFFLINE_TARBALL

# require_root: the installer performs privileged operations (package install, service
# management, user creation) from step 20 onward; refuse early rather than fail deep
# inside a step with a half-finished state.
require_root() {
  if [ "$(id -u)" -ne 0 ]; then
    echo "install.sh: must be run as root (try: sudo bash install.sh)" >&2
    exit 1
  fi
}

# setup_logging: create the log directory before anything else writes to it, then
# duplicate all stdout/stderr into the log file (via `tee`) while still showing output
# on the terminal. Uses `tee -a` so a re-run after an interrupted install appends
# rather than truncates — the log is a full history of every attempt.
setup_logging() {
  mkdir -p "$MARAN_LOG_DIR"
  chmod 750 "$MARAN_LOG_DIR"
  # Keep the original stdout on file descriptor 3 BEFORE redirecting. Anything a step
  # must show the operator without persisting it — the one-time setup token, which is
  # enough on its own to create the first administrator — is written to fd 3, so it
  # reaches the terminal and never the log file.
  exec 3>&1
  # exec through a process substitution so both stdout and stderr are captured,
  # timestamps are added by `ts`-less awk (no extra dependency), and the terminal
  # still sees everything live.
  exec > >(awk '{ print strftime("[%Y-%m-%d %H:%M:%S]"), $0; fflush() }' | tee -a "$MARAN_LOG_FILE") 2>&1
}

# detect_os: identifies distro family (debian|rhel), distro id and version from
# /etc/os-release. Exported for lib/10-preflight.sh and the package-manager adapter
# in lib/20-dependencies.sh. Unsupported values are rejected by 10-preflight.sh, not
# here — this function only detects, it never judges.
detect_os() {
  if [ ! -r /etc/os-release ]; then
    echo "install.sh: cannot read /etc/os-release; unsupported system" >&2
    exit 1
  fi
  # shellcheck disable=SC1091
  . /etc/os-release
  MARAN_OS_ID="${ID:-unknown}"
  MARAN_OS_VERSION_ID="${VERSION_ID:-unknown}"
  MARAN_OS_ID_LIKE="${ID_LIKE:-}"

  case "$MARAN_OS_ID" in
    ubuntu|debian)
      MARAN_OS_FAMILY="debian"
      ;;
    almalinux|rocky)
      MARAN_OS_FAMILY="rhel"
      ;;
    *)
      # Fall back to ID_LIKE for derivative distros that still ship compatible package
      # managers; preflight still validates the concrete (id, version) pair against the
      # supported matrix, so a permissive fallback here cannot admit an untested target.
      case "$MARAN_OS_ID_LIKE" in
        *debian*) MARAN_OS_FAMILY="debian" ;;
        *rhel*|*fedora*) MARAN_OS_FAMILY="rhel" ;;
        *) MARAN_OS_FAMILY="unknown" ;;
      esac
      ;;
  esac
  export MARAN_OS_ID MARAN_OS_VERSION_ID MARAN_OS_FAMILY
}

# detect_arch: normalizes `uname -m` to Maran's two supported artifact arches.
# Anything else is rejected by preflight with an explicit message.
detect_arch() {
  case "$(uname -m)" in
    x86_64|amd64) MARAN_ARCH="x86_64" ;;
    aarch64|arm64) MARAN_ARCH="aarch64" ;;
    *) MARAN_ARCH="unsupported" ;;
  esac
  export MARAN_ARCH
}

# run_step: sources one numbered file from lib/ and calls the function it defines
# (file `NN-name.sh` defines function `step_name`). Centralizing this makes the step
# list in main() a readable table of contents and keeps sourcing/error handling in
# one place.
run_step() {
  local file="$1" fn="$2"
  echo "==> ${file}"
  # shellcheck disable=SC1090
  . "${LIB_DIR}/${file}"
  "$fn"
}

main() {
  require_root
  setup_logging
  echo "Maran installer starting: $(date -u '+%Y-%m-%dT%H:%M:%SZ')"

  detect_os
  detect_arch
  echo "Detected: ${MARAN_OS_ID} ${MARAN_OS_VERSION_ID} (${MARAN_OS_FAMILY} family), arch ${MARAN_ARCH}"

  run_step 10-preflight.sh    step_preflight
  run_step 20-dependencies.sh step_dependencies
  run_step 30-postgresql.sh   step_postgresql
  run_step 40-user.sh         step_user
  run_step 50-artifacts.sh    step_artifacts
  run_step 60-config.sh       step_config
  run_step 70-services.sh     step_services
  run_step 80-nginx.sh        step_nginx
  run_step 90-finish.sh       step_finish

  echo "Maran installer finished: $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
}

main "$@"
