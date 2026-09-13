#!/usr/bin/env bash
# Step 20: install OS packages Maran needs (nginx, PostgreSQL server, basic tooling)
# via a small package-manager adapter. Adding a distro to the supported matrix is a data
# change (a new case arm below and in 10-preflight.sh's matrix) not a code change to the
# steps that call this adapter — mirrors the agent's DistroAdapter split (rules/architecture.md).
set -euo pipefail

# pkg_update: refreshes the package manager's index. Idempotent by nature (re-running
# an index refresh is always safe).
pkg_update() {
  case "$MARAN_OS_FAMILY" in
    debian)
      DEBIAN_FRONTEND=noninteractive apt-get update -y
      ;;
    rhel)
      dnf -y makecache
      ;;
    *)
      echo "20-dependencies.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# pkg_install: installs the given package names using the family's native manager.
# Both managers are idempotent when a package is already present, so this function is
# safe to call on every install re-run without checking state first.
pkg_install() {
  case "$MARAN_OS_FAMILY" in
    debian)
      DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "$@"
      ;;
    rhel)
      dnf -y install "$@"
      ;;
    *)
      echo "20-dependencies.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# base_packages: the set common to every supported distro. PostgreSQL itself is
# installed and initialised in 30-postgresql.sh (it needs family-specific
# initdb/service steps beyond a plain package install), so it is not listed here.
base_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian)
      echo "ca-certificates curl gnupg nginx openssl"
      ;;
    rhel)
      echo "ca-certificates curl gnupg2 nginx openssl policycoreutils-python-utils"
      ;;
    *)
      echo "20-dependencies.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# agent_tooling_packages_for_family: the packages that supply the programs the AGENT
# execs by absolute path. Every one of them is a path a `DistroAdapter` accessor
# declares, and a missing one is not a degraded feature — it is `SpawnFailed { code: -1 }`
# on a customer's server, in whichever operation reaches it first.
#
# These were installed by nothing before this step named them: the base set above covers
# what the PANEL needs, and the shadow suite, the quota tools and the archive tools were
# simply assumed to be on every host. The assumption held on the images this was developed
# against and is not a promise any minimal install makes.
#
# Package ownership, measured on both families rather than read off one of them
# (`rpm -qf` / `dpkg -S` over every declared path):
#
#   binary                                   Debian package    RHEL package
#   useradd userdel usermod chpasswd         passwd            shadow-utils
#   passwd                                   passwd            passwd      <- the split
#   setquota quota                           quota             quota
#   tar / gzip                               tar / gzip        tar / gzip
#   pkill                                    procps            procps-ng   <- the second split
#   id chmod chgrp                           coreutils         coreutils-single
#
# The `passwd` row is the one that has already cost this project a bug. On the Debian
# family ONE package (`passwd`) supplies all five shadow programs, so nothing there could
# ever notice; on the RHEL family `/usr/bin/passwd` is its own package, separate from the
# `shadow-utils` that supplies the other four. A host without it runs `useradd` fine and
# fails only when the agent asks `passwd -S` whether a login is locked — which is the
# suspension attestation, i.e. billing. It was missing from a polygon image for exactly
# that reason and made every suspension on that family fail.
#
# The `pkill` row is the second such split, and unlike the first it is invisible on the
# family that has it for free. `signalling_packages_for_family` below is where it is named,
# and its own comment says what it is for and what a missing one costs.
#
# `crontab`, `nft` and the MariaDB client are deliberately NOT here: 88-cron.sh, 87-firewall.sh
# and 85-mysql.sh each install their family's package beside the configuration only they know
# how to write. `getent` is not here either, and that is not an oversight — it belongs to the
# C library (`libc-bin` / `glibc-common`) and cannot be absent from a host that boots.
agent_tooling_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian)
      echo "passwd quota tar gzip $(signalling_packages_for_family)"
      ;;
    rhel)
      echo "shadow-utils passwd quota tar gzip $(signalling_packages_for_family)"
      ;;
    *)
      echo "20-dependencies.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# signalling_packages_for_family: the package supplying `pkill`, which is the ONE program
# this installer names for the sake of a security operation rather than a feature.
#
# What execs it: suspending a hosting account locks its logins and then ends the sessions
# already open, because a lock the customer's connected SFTP or FTPS client never notices is
# a suspension in name only — `ops::logins::end_account_sessions` spawns the path
# `DistroAdapter::pkill_binary()` declares, `/usr/bin/pkill` on both families, and never a
# literal of its own (rules/architecture.md: no platform fact outside the `distro` crate).
# Without the program the cull is `LoginsError::SpawnFailed`, which fails the whole
# suspension: an account the panel says is suspended, whose open transfers keep running.
#
# It is its own function, and not four more words in the arm above, for two reasons. The
# arms above are one package list per family; this is the one fact in the file with a
# per-family SPELLING that differs while the binary path does not, so a census can read the
# arms of one small function and say which family is missing one. And keeping it out of the
# arms above means an image can install exactly this — the polygon does — without
# reinstalling the quota tools over the stand-in it puts in their place.
#
# The two spellings, measured 2026-09-12 in the pinned polygon base images rather than read
# off one of them:
#
#   Debian family (ubuntu:24.04@sha256:33ceb719…): `dpkg -S /usr/bin/pkill` -> procps, a
#   Priority: required package, so /usr/bin/pkill (a symlink to pgrep) is already there.
#   RHEL family (almalinux:9@sha256:d2515c76…): /usr/bin/pkill DOES NOT EXIST, procps-ng is
#   not installed, and `dnf repoquery --whatprovides /usr/bin/pkill` answers procps-ng.
#   Installing nginx, openssh-server, cronie, quota, passwd, shadow-utils, vsftpd, tar and
#   gzip in that image pulls it in through no dependency of any of them.
#
# So this is a real gap on one family and free on the other, which is exactly the shape the
# `passwd` row above already cost this project once: the family that has it for free can
# never notice, and the failure on the other one is at exec time, in billing.
signalling_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian)
      echo "procps"
      ;;
    rhel)
      echo "procps-ng"
      ;;
    *)
      echo "20-dependencies.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# assert_agent_tooling: proves the packages above actually put the programs where the
# agent's DistroAdapter says they are, on THIS host, rather than trusting that the install
# succeeded. A package manager reporting success and a path existing are different facts,
# and the agent execs the path.
#
# The list is the subset of declared paths this step is responsible for; the ones other
# steps install are checked by those steps.
assert_agent_tooling() {
  local missing=""
  local binary
  for binary in /usr/sbin/useradd /usr/sbin/userdel /usr/sbin/usermod /usr/sbin/chpasswd \
                /usr/bin/passwd /usr/sbin/setquota /usr/bin/quota /usr/bin/tar \
                /usr/bin/gzip /usr/bin/getent /usr/bin/pkill; do
    [ -x "$binary" ] || missing="${missing} ${binary}"
  done
  if [ -n "$missing" ]; then
    echo "20-dependencies.sh: the agent execs these by absolute path and this host has" >&2
    echo "  nothing executable there:${missing}" >&2
    echo "  Install the packages named in agent_tooling_packages_for_family and re-run." >&2
    exit 1
  fi
}

step_dependencies() {
  echo "Installing base OS packages for ${MARAN_OS_FAMILY} family..."
  pkg_update
  # shellcheck disable=SC2046
  pkg_install $(base_packages_for_family)
  echo "Installing the tooling the agent execs..."
  # shellcheck disable=SC2046
  pkg_install $(agent_tooling_packages_for_family)
  assert_agent_tooling
  echo "Base packages installed."
}
