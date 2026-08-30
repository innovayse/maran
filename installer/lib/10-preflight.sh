#!/usr/bin/env bash
# Step 10: preflight checks. Refuses BEFORE anything else on the machine is touched.
# Sourced by installer/install.sh, which has already run detect_os/detect_arch and
# exported MARAN_OS_ID, MARAN_OS_VERSION_ID, MARAN_OS_FAMILY, MARAN_ARCH.
set -euo pipefail

# Minimum resources Maran needs to run PostgreSQL + the API + the agent + nginx
# comfortably on a small VPS. Conservative floor, not a recommendation.
readonly MARAN_MIN_RAM_MB=1024
readonly MARAN_MIN_DISK_MB=2048
# Ports the panel and its dependencies claim by default. PostgreSQL is unix-socket
# only (see 30-postgresql.sh) so it is deliberately not in this list.
readonly MARAN_REQUIRED_PORTS="8443"

# supported_os_matrix: "id:version" pairs from the design spec §4. A version prefix
# match (e.g. "22.04" matches VERSION_ID "22.04") keeps point releases working.
readonly MARAN_SUPPORTED_MATRIX="ubuntu:22.04 ubuntu:24.04 debian:12 debian:13 almalinux:9 almalinux:10 rocky:9 rocky:10"

# fail: print a uniform "what failed / what to do" message and mark preflight failed.
# Preflight collects ALL failures before exiting so the operator does not have to
# re-run the installer once per problem.
_PREFLIGHT_FAILED=0
fail() {
  local what="$1" fix="$2"
  echo "PREFLIGHT FAIL: ${what}"
  echo "  -> ${fix}"
  _PREFLIGHT_FAILED=1
}

ok() {
  echo "PREFLIGHT OK:   $1"
}

# check_os_supported: rejects any (id, version) pair not in the supported matrix.
# Distro differences are handled in the package-manager adapter (20-dependencies.sh);
# this check exists purely as a gate so an unsupported combination fails loudly here
# instead of partway through package installation.
check_os_supported() {
  local pair="${MARAN_OS_ID}:${MARAN_OS_VERSION_ID}" candidate matched=0
  for candidate in $MARAN_SUPPORTED_MATRIX; do
    local cid="${candidate%%:*}" cver="${candidate##*:}"
    if [ "$MARAN_OS_ID" = "$cid" ] && [[ "$MARAN_OS_VERSION_ID" == "${cver}"* ]]; then
      matched=1
      break
    fi
  done
  if [ "$matched" -eq 1 ]; then
    ok "OS ${MARAN_OS_ID} ${MARAN_OS_VERSION_ID} is supported"
  else
    fail "OS ${MARAN_OS_ID} ${MARAN_OS_VERSION_ID} is not a supported target" \
      "Install on one of: Ubuntu 22.04/24.04, Debian 12/13, AlmaLinux 9/10, Rocky 9/10."
  fi
}

check_arch_supported() {
  if [ "$MARAN_ARCH" = "x86_64" ] || [ "$MARAN_ARCH" = "aarch64" ]; then
    ok "architecture ${MARAN_ARCH} is supported"
  else
    fail "architecture $(uname -m) is not supported" \
      "Maran ships x86_64 and aarch64 artifacts only."
  fi
}

check_root() {
  if [ "$(id -u)" -eq 0 ]; then
    ok "running as root"
  else
    fail "not running as root" "Re-run with: sudo bash install.sh"
  fi
}

# check_ram: reads MemTotal from /proc/meminfo (kB) and compares to the floor.
check_ram() {
  local kb mb
  kb="$(awk '/^MemTotal:/ { print $2 }' /proc/meminfo)"
  mb=$(( kb / 1024 ))
  if [ "$mb" -ge "$MARAN_MIN_RAM_MB" ]; then
    ok "RAM: ${mb} MiB (>= ${MARAN_MIN_RAM_MB} MiB required)"
  else
    fail "insufficient RAM: ${mb} MiB available, ${MARAN_MIN_RAM_MB} MiB required" \
      "Upgrade the server's memory or use a larger instance size before installing."
  fi
}

# check_disk: free space on the filesystem that will host /usr/local/maran and
# PostgreSQL's data directory (both under /). A dedicated data volume is a valid
# production choice but out of scope for this floor check.
check_disk() {
  local avail_mb
  avail_mb="$(df -Pm / | awk 'NR==2 { print $4 }')"
  if [ "${avail_mb:-0}" -ge "$MARAN_MIN_DISK_MB" ]; then
    ok "disk free on /: ${avail_mb} MiB (>= ${MARAN_MIN_DISK_MB} MiB required)"
  else
    fail "insufficient disk space on /: ${avail_mb:-0} MiB free, ${MARAN_MIN_DISK_MB} MiB required" \
      "Free up space or attach a larger disk before installing."
  fi
}

# check_ports_free: refuses if anything is already listening on a port Maran needs.
# Uses `ss` (present on all supported distros via iproute2) rather than netstat.
check_ports_free() {
  local port busy
  for port in $MARAN_REQUIRED_PORTS; do
    busy="$(ss -Htln "( sport = :${port} )" 2>/dev/null || true)"
    if [ -z "$busy" ]; then
      ok "port ${port} is free"
    elif [ "$_MARAN_ALREADY_INSTALLED" -eq 1 ]; then
      # On a re-run our own nginx vhost is already listening on this port. Treating that
      # as a conflict would make the installer refuse to repair or resume any install
      # that got as far as step 80, which contradicts the idempotency promise.
      ok "port ${port} is held by this existing Maran install (re-run)"
    else
      fail "port ${port} is already in use" \
        "Stop whatever is listening on port ${port} (check 'ss -tlnp | grep :${port}'), or reconfigure it before installing Maran."
    fi
  done
}

# check_no_conflicting_panel: refuses if a well-known competing control-panel install
# footprint is detected, so Maran never fights another panel for nginx/PHP-FPM
# ownership or the same ports. Detection is by filesystem footprint only, never by
# naming the other product in output.
check_no_conflicting_panel() {
  local marker
  for marker in /usr/local/cpanel /usr/local/directadmin /usr/local/psa /etc/webmin /usr/local/lsws; do
    if [ -e "$marker" ]; then
      fail "an existing control panel appears to be installed (found ${marker})" \
        "Maran must be installed on a clean server; remove the other panel first or provision a fresh host."
      return
    fi
  done
  ok "no conflicting control panel detected"
}

# check_not_already_installed: an install that already completed is not a failure —
# it makes this run a no-op resume, consistent with idempotency. We only warn here;
# each later step decides for itself whether its own work is already done.
# _MARAN_ALREADY_INSTALLED: set by check_existing_install_state, read by checks that must
# judge "someone else is already using this" differently from "we are already here".
_MARAN_ALREADY_INSTALLED=0
check_existing_install_state() {
  if [ -f /etc/maran/panel.env ]; then
    _MARAN_ALREADY_INSTALLED=1
    echo "PREFLIGHT NOTE: /etc/maran/panel.env already exists; this run will resume/repair an existing install rather than starting fresh."
  fi
}

step_preflight() {
  echo "Running preflight checks..."
  check_root
  check_os_supported
  check_arch_supported
  check_ram
  check_disk
  # Before check_ports_free: it needs to know whether the port is held by a previous
  # Maran install (a resume) or by an unrelated service (a real conflict).
  check_existing_install_state
  check_ports_free
  check_no_conflicting_panel

  if [ "$_PREFLIGHT_FAILED" -ne 0 ]; then
    echo "Preflight failed. Fix the items above and re-run the installer. No changes were made."
    exit 1
  fi
  echo "Preflight passed."
}
