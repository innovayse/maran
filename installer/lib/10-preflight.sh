#!/usr/bin/env bash
# Step 10: preflight checks. Refuses BEFORE anything else on the machine is touched.
# Sourced by installer/install.sh, which has already run detect_os/detect_arch and
# exported MARAN_OS_ID, MARAN_OS_VERSION_ID, MARAN_OS_FAMILY, MARAN_ARCH.
set -euo pipefail

# Minimum resources Maran needs to run PostgreSQL + the API + the agent + nginx
# comfortably on a small VPS. Conservative floor, not a recommendation.
readonly MARAN_MIN_RAM_MB=1024
readonly MARAN_MIN_DISK_MB=2048
# The floor the BACKUP root's filesystem is measured against, and the path it is measured
# at. Separate from MARAN_MIN_DISK_MB above, and much larger, because they answer different
# questions: 2 GiB is what the product's own files need, while a backup is a full copy of an
# account's home plus its databases and every retained generation of both (the plan's
# Decision 1: every backup is a full backup, retention 7 by default). A host that can install
# Maran comfortably can still be a host on which the first backup of a 20 GiB account fills
# the disk.
#
# A WARNING and never a refusal — see check_backup_space. The number is a floor for one
# unremarkable account's first archive, not a capacity plan, and it is deliberately not
# derived from the per-archive ceiling in the agent (64 GiB): a floor nobody can meet is a
# floor everybody learns to ignore.
readonly MARAN_MIN_BACKUP_MB=10240
readonly MARAN_BACKUP_ROOT=/var/backups/maran
# Ports the panel and its dependencies claim by default. PostgreSQL is unix-socket
# only (see 30-postgresql.sh) so it is deliberately not in this list.
#
# Derived from the authority in install.sh rather than repeated here: preflight's job is
# to refuse when something else already holds a port Maran is about to take, and a
# preflight that guards a DIFFERENT port from the one nginx will bind guards nothing. The
# expansion is safe where it sits because install.sh sets and exports MARAN_PANEL_PORT
# before main() runs, and run_step sources this file from inside main().
#
# `:?` rather than a default: a default would be the literal all over again, and it would
# make a step file sourced without the installer around it silently check the wrong port
# instead of saying so.
readonly MARAN_REQUIRED_PORTS="${MARAN_PANEL_PORT:?must be set by install.sh before this step is sourced}"

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

# warn: something an operator should see and decide about, which does NOT stop the install.
# It does not touch _PREFLIGHT_FAILED, and that is the whole distinction between it and fail:
# a preflight that refuses an install over a condition the operator can fix afterwards teaches
# operators to skip preflight, and this one has exactly one such condition (check_backup_space).
warn() {
  local what="$1" advice="$2"
  echo "PREFLIGHT WARN: ${what}"
  echo "  -> ${advice}"
}

# nearest_existing_ancestor: the closest path at or above <path> that exists.
#
# Needed because this step runs BEFORE step 40 creates /var/backups/maran, so the backup root
# is normally absent here and `df` on an absent path answers nothing at all. The filesystem
# holding the nearest existing ancestor is the filesystem the directory will be created on,
# unless the operator later mounts a volume in between — which check_backup_space says out
# loud rather than leaving the reader to assume the number is about the final layout.
#
# Terminates: the loop stops at "/", which exists on every host this runs on.
nearest_existing_ancestor() {
  local path="$1"
  while [ ! -e "$path" ] && [ "$path" != "/" ]; do
    path="$(dirname "$path")"
  done
  printf '%s' "$path"
}

# check_backup_space: warns when the filesystem that will hold the backup root has less free
# space than MARAN_MIN_BACKUP_MB, and says the number it measured and the path it measured it
# at. Never fails the install.
#
# What this check can observe, stated because a check that cannot see its subject is worse
# than none (rules/testing.md): it reads `df` on a real path on this host, so a low number is
# a real number. What it CANNOT observe is the future: an operator who mounts a volume at
# /var/backups after installing gets a warning about the root filesystem, which is why the
# message names the path that was measured instead of implying it measured the backup root.
check_backup_space() {
  local measured avail_mb
  measured="$(nearest_existing_ancestor "$MARAN_BACKUP_ROOT")"
  avail_mb="$(df -Pm "$measured" | awk 'NR==2 { print $4 }')"
  if [ "${avail_mb:-0}" -ge "$MARAN_MIN_BACKUP_MB" ]; then
    ok "backup space at ${measured}: ${avail_mb} MiB free (>= ${MARAN_MIN_BACKUP_MB} MiB suggested for ${MARAN_BACKUP_ROOT})"
  else
    warn "only ${avail_mb:-0} MiB free on the filesystem holding ${measured}, where backups will be written (${MARAN_BACKUP_ROOT}); ${MARAN_MIN_BACKUP_MB} MiB is suggested" \
      "Installing anyway. Every backup is a full backup and the default retention keeps seven, so plan for several times the size of one account — or mount a larger volume at ${MARAN_BACKUP_ROOT} and it will be used from the next backup onward."
  fi
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
  check_backup_space
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
