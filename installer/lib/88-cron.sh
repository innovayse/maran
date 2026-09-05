#!/usr/bin/env bash
# Step 88: make the host able to RUN the scheduled jobs the panel writes — the cron
# daemon installed, enabled at boot, and actually running.
#
# The panel's cron feature is a per-account crontab installed by the agent through
# `/usr/bin/crontab`. That program happily accepts and stores a table on a host whose
# daemon is missing, disabled or stopped, and nothing about the panel looks broken
# afterwards: the entry appears in the UI, the account sees it, and it silently never
# runs. This step exists so that failure cannot ship — it is the same reasoning as the
# database step next door, where a server the agent cannot use is refused at install
# time rather than discovered by the first customer.
#
# What this step does NOT do, and why each omission is deliberate:
#
# - It writes no crontab, for root or for anybody else. A crontab is account-lifetime
#   state derived from a validated schedule and an entry id, and it belongs to the
#   agent, which owns every byte that reaches a cron line. The installer runs once,
#   before any account exists; it lays the ground the agent then builds on — exactly the
#   division the SFTP step draws between the group it creates and the jails it does not.
#
# - It does not configure the daemon. Cron needs no configuration to run a per-account
#   table; the environment lines the panel relies on are rendered INTO each account's
#   table by the agent, not set globally here, so that one account's environment can
#   never leak into another's job.
#
# - It has no counterpart in uninstall.sh. Cron is a stock host service that predates
#   this installation and outlives it — removing the package or disabling the unit
#   because the panel is being removed would silently stop the host's own scheduled
#   work (log rotation, package cleanup, whatever the operator wrote by hand). The
#   uninstaller removes what Maran created; cron is not that.
#
# Every action is idempotent, because installers get re-run: both package managers
# treat an already-installed package as success, and `systemctl enable --now` treats an
# already-enabled, already-running unit the same way. A second run therefore converges
# and reports, rather than failing on work that is already done.
set -euo pipefail

# The crontab program the AGENT will execute, spelled the way
# `DistroAdapter::crontab_binary()` spells it on both families. Named here so this step
# verifies the path the agent is going to run, not whatever `crontab` happens to be
# first on the installer's PATH: a `crontab` that works for the installer and a missing
# `/usr/bin/crontab` for the agent is exactly the gap this step exists to close before a
# customer meets it.
#
# Both families agree on the path — Debian's `cron` package and RHEL's `cronie` package
# install it there — which is an agreement between two packages rather than a rule, and
# is why the agent asks its distro adapter for it instead of assuming it.
readonly MARAN_CRONTAB_BINARY="/usr/bin/crontab"

# cron_packages_for_family: the package carrying the daemon and `crontab(1)`, per family.
# The one place the two families' cron packaging shows up as a name: Debian calls the
# package `cron`, RHEL calls it `cronie`, and they are the same daemon's job.
#
# Public on purpose: the polygon images install cron for their own suites, and taking the
# name from THIS function rather than from a literal of their own means a package name
# that stops being right stops both image builds instead of waiting to be discovered on a
# customer's server — the arrangement `mysql_packages_for_family` already has.
cron_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "cron" ;;
    rhel)   echo "cronie" ;;
    *)
      echo "88-cron.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# cron_service_name: the service unit, matching `DistroAdapter::cron_service()`.
#
# `cron` on the Debian family, `crond` on the RHEL family, and no alias bridges the two:
# enabling the wrong name leaves the host with no scheduler and the installer none the
# wiser, which is why the family is decided here rather than guessed once for both.
cron_service_name() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "cron" ;;
    rhel)   echo "crond" ;;
    *)
      echo "88-cron.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# verify_cron_ready: the gate. Three questions, because passing two of them is not the
# state the panel needs.
#
# 1. Can the agent reach `crontab(1)` at all? Without it every cron operation the panel
#    offers fails at spawn time, on a host that otherwise looks installed.
# 2. Is the unit enabled? A cron that runs now and does not come back after a reboot
#    hands the operator a panel whose scheduled jobs stop at the next restart — the
#    worst kind of failure, because nothing reports it and the cause is a week old.
# 3. Is the unit actually running? `systemctl enable --now` returns success for a daemon
#    that started and then exited, so the state is asked for rather than assumed. An
#    installer that enables a unit and never looks at it has verified nothing.
#
# Public on purpose, so the polygon images can run THIS function against a real cron
# instead of writing their own inline checks. An image that checks the daemon with
# assertions of its own proves something about itself; running this function proves the
# installer still refuses a host on which no scheduled job would ever run.
verify_cron_ready() {
  local service
  service="$(cron_service_name)"

  if [ ! -x "$MARAN_CRONTAB_BINARY" ]; then
    cat >&2 <<EOF
88-cron.sh: ${MARAN_CRONTAB_BINARY} is missing; the agent executes exactly this path.

Every scheduled job the panel offers is installed by running that program. Install this
family's cron package and re-run the installer:

    $(cron_packages_for_family)
EOF
    exit 1
  fi

  if ! systemctl is-enabled "$service" >/dev/null 2>&1; then
    cat >&2 <<EOF
88-cron.sh: the ${service} service is installed but not enabled at boot.

Scheduled jobs would run until the next restart of this server and then stop, with
nothing to report it. Enable it and re-run the installer:

    systemctl enable --now ${service}
EOF
    exit 1
  fi

  if ! systemctl is-active "$service" >/dev/null 2>&1; then
    cat >&2 <<EOF
88-cron.sh: the ${service} service is enabled but not running.

A stopped cron accepts every crontab the panel installs and executes none of them, so
the panel would report scheduled jobs that silently never run. Find out why it will not
start, then re-run the installer:

    systemctl status ${service}
    journalctl -u ${service} -n 50
EOF
    exit 1
  fi

  echo "cron is installed, enabled and running (unit ${service}, crontab at ${MARAN_CRONTAB_BINARY})."
}

step_cron() {
  echo "Installing the cron daemon for scheduled jobs..."

  # Both answers first, into plain assignments, so that a family neither function knows
  # stops the step here — with one message — instead of at the third call site, having
  # already run a package manager with no package names and systemd with no unit name.
  local packages service
  packages="$(cron_packages_for_family)"
  service="$(cron_service_name)"

  # Unquoted on purpose: the answer is a package LIST and the words are the packages.
  # shellcheck disable=SC2086
  pkg_install $packages

  # `enable --now` is idempotent by design: on a re-run the unit is already enabled and
  # already running, and systemd treats both as success.
  systemctl enable --now "$service"

  verify_cron_ready
  echo "Scheduled jobs will run; per-account crontabs are written by the agent, never by this installer."
}
