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

step_dependencies() {
  echo "Installing base OS packages for ${MARAN_OS_FAMILY} family..."
  pkg_update
  # shellcheck disable=SC2046
  pkg_install $(base_packages_for_family)
  echo "Base packages installed."
}
