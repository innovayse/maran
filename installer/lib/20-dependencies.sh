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
    suse)
      # UNVERIFIED: read from the zypper documentation (`man zypper`, "refresh"). Confirm with
      # `zypper --non-interactive refresh; echo $?` in the pinned Leap image (expect 0 with the
      # distribution's own repositories). This family adds NO third-party repository here: the
      # only sources are the ones the image ships.
      zypper --non-interactive refresh
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
    suse)
      # --no-recommends keeps the install to what is named, the way apt's --no-install-recommends
      # does. UNVERIFIED: read from `man zypper` ("install", "--no-recommends"). Confirm with
      # `zypper --non-interactive install --no-recommends nginx; echo $?` in the pinned Leap image;
      # zypper exits 0 for "already installed" (idempotent) and 104 for an unknown package name.
      zypper --non-interactive install --no-recommends "$@"
      ;;
    *)
      echo "20-dependencies.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# icu_runtime_package: the package carrying the ICU runtime the PANEL PROCESS needs, per family.
#
# The two families disagree in a way no single literal can bridge. The RHEL family ships one
# unversioned `libicu`. The Debian family ships **one versioned package per release** and no
# unversioned name at all, so a hardcoded `libicu` is not a no-op there but a hard failure:
#
#     E: Unable to locate package libicu        (apt-get exits 100)
#
# Measured across the supported matrix, which is why this is resolved and not listed:
#
#     Ubuntu 22.04  libicu70      Debian 12  libicu72
#     Ubuntu 24.04  libicu74      Debian 13  libicu76
#
# So on the Debian family the name is READ from the package index rather than predicted — the newest
# `libicu<N>` the host's own sources offer. That also means a future release needs no edit here,
# which is the point: the previous arrangement would have broken on Debian 14 with the same error.
#
# Called after `pkg_update`, and that ordering is load-bearing: `apt-cache search` reads the package
# lists, so on a host whose lists were never fetched it would find nothing and this function would
# refuse a host that is perfectly fine.
icu_runtime_package() {
  case "$MARAN_OS_FAMILY" in
    rhel)
      echo "libicu"
      ;;
    suse)
      # Versioned like the Debian family: the runtime library is `libicu<N>` (the unversioned
      # `libicu` is not the runtime there). The name is READ from the host's own repository cache,
      # never predicted, and a miss is a refusal for the reason the Debian arm gives (issue #40).
      # Called after `pkg_update`, which fills the cache `--no-refresh` reads.
      #
      # UNVERIFIED: that the runtime package is named `libicu<N>` and that `zypper search` prints a
      # `S | Name | Summary | Type` table whose second column is the name. Confirm with
      # `zypper --non-interactive --no-refresh search -t package 'libicu*'` in the pinned Leap image;
      # if the table has another shape the awk below prints nothing and the step refuses loudly
      # instead of installing a guess.
      local suse_found
      suse_found="$(zypper --non-interactive --no-refresh search -t package 'libicu*' 2>/dev/null \
                      | awk -F'|' '{ gsub(/[[:space:]]/, "", $2); if ($2 ~ /^libicu[0-9]+$/) print $2 }' \
                      | sort -V | tail -1)"
      [ -n "$suse_found" ] || {
        echo "20-dependencies.sh: no libicu<N> package in this host's zypper repositories." >&2
        echo "  The panel is a self-contained .NET build and refuses to start without ICU." >&2
        echo "  Searched: zypper --non-interactive --no-refresh search -t package 'libicu*'" >&2
        exit 1
      }
      echo "$suse_found"
      ;;
    debian)
      local found
      found="$(apt-cache --names-only search '^libicu[0-9]+$' 2>/dev/null \
                 | awk '{print $1}' | sort -V | tail -1)"
      # A refusal rather than an empty word silently dropped from the package list. The panel does
      # not start without ICU ("Couldn't find a valid ICU package installed on the system") and it
      # says so in the journal and nowhere the installer looks, so an install that quietly skipped
      # this would end with a dead panel and a clean log — the signature of issue #40.
      [ -n "$found" ] || {
        echo "20-dependencies.sh: no libicu<N> package in this host's apt sources." >&2
        echo "  The panel is a self-contained .NET build and refuses to start without ICU." >&2
        echo "  Searched: apt-cache --names-only search '^libicu[0-9]+\$'" >&2
        exit 1
      }
      echo "$found"
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
#
# `libicu` is here because the PANEL PROCESS itself needs it, not because any step configures it
# (issue #51). A self-contained .NET publish refuses to start without ICU unless it was built in
# invariant mode, and Maran is not — it formats and compares text for real tenants. Measured on a
# bare AlmaLinux 8 image: "Process terminated. Couldn't find a valid ICU package installed on the
# system", and the same binary starts normally once the package is present.
#
# Until this line it arrived transitively behind PostgreSQL or nginx on every host that happened to
# be tested, which is the same assumption this file already confesses for the shadow suite below:
# it held on the images this was developed against and is not a promise any minimal install makes.
# The two families do NOT spell it the same, and the Debian family does not spell it at all —
# `icu_runtime_package` above resolves the name and says why.
base_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian)
      echo "ca-certificates curl gnupg $(icu_runtime_package) nginx openssl"
      ;;
    rhel)
      # `curl` is NOT named unconditionally here, and the omission is load-bearing (issue #46).
      # RHEL 9 and its rebuilds ship `curl-minimal`, which PROVIDES /usr/bin/curl and declares a
      # CONFLICT with the full `curl` package, so `dnf install curl` on such a host is not a no-op
      # that reports "already installed" — it is a hard failure that ends the install at step 20:
      #
      #     Problem: problem with installed package curl-minimal-7.76.1-40.el9.x86_64
      #       - package curl-minimal ... conflicts with curl provided by curl-7.76.1-...
      #       - cannot install the best candidate for the job
      #
      # That is the RHEL 9 minimal install, the UBI images and much of the cloud fleet — measured on
      # AlmaLinux 9, where it made the entire family uninstallable. The host already has a working
      # curl; it is how the operator ran the documented one-liner to get here.
      #
      # What this installer needs is the BINARY, so the binary is what decides. Asking for the full
      # package only when /usr/bin/curl is absent keeps the host that has neither working, which
      # simply dropping the name would not.
      local rhel_packages="ca-certificates gnupg2 $(icu_runtime_package) nginx openssl policycoreutils-python-utils"
      [ -x /usr/bin/curl ] || rhel_packages="${rhel_packages} curl"
      echo "$rhel_packages"
      ;;
    suse)
      # No repository is added: every name below comes from the distribution's own repositories.
      # `curl` follows the RHEL arm's rule (install the package only when the BINARY is missing),
      # because a minimal image may carry a curl flavour that conflicts with the full package.
      # `policycoreutils-python-utils` supplies `semanage`, which a later step needs on a host whose
      # SELinux is enforcing (the default on Leap 16, same as the RHEL family).
      #
      # UNVERIFIED: every name here is read from the distribution documentation, none was measured.
      # Confirm with `zypper --non-interactive --no-refresh info ca-certificates gpg2 nginx openssl
      # policycoreutils-python-utils curl` in the pinned Leap image (exit 104 names a missing one).
      local suse_packages="ca-certificates gpg2 $(icu_runtime_package) nginx openssl policycoreutils-python-utils"
      [ -x /usr/bin/curl ] || suse_packages="${suse_packages} curl"
      echo "$suse_packages"
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
      echo "passwd quota tar gzip $(signalling_packages_for_family) $(ssh_client_packages_for_family)"
      ;;
    rhel)
      echo "shadow-utils passwd quota tar gzip $(signalling_packages_for_family) $(ssh_client_packages_for_family)"
      ;;
    suse)
      # UNVERIFIED: on SUSE the shadow programs (useradd, userdel, usermod, chpasswd, passwd) are
      # believed to ship in the single package `shadow`, with `quota`, `tar`, `gzip` named as on the
      # other families. Confirm with `rpm -qf /usr/sbin/useradd /usr/bin/passwd /usr/sbin/setquota
      # /usr/bin/tar /usr/bin/gzip` in the pinned Leap image, and fix the names here if any differs.
      echo "shadow quota tar gzip $(signalling_packages_for_family) $(ssh_client_packages_for_family)"
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
    suse)
      # UNVERIFIED: the package is believed to be `procps` (providing /usr/bin/pkill and
      # /usr/bin/pgrep). Confirm with `rpm -qf /usr/bin/pkill` in the pinned Leap image.
      echo "procps"
      ;;
    *)
      echo "20-dependencies.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# ssh_client_packages_for_family: the packages supplying the OpenSSH CLIENT programs the
# agent execs for server migration (issue #197): `ssh` (the connection to a source the
# administrator registered and every read-only command run over it), `ssh-keyscan` (the host
# keys a source offers, shown for confirmation) and `ssh-keygen` (the per-source key pair).
# The agent declares all three by absolute path in `DistroAdapter` and a missing one is
# `SpawnFailed` on the first migration step that reaches it.
#
# The two spellings differ, and the RHEL split is the same shape as the `passwd` row above:
#
#   Debian family: `openssh-client` ships ssh, ssh-keyscan and ssh-keygen. The `openssh-server`
#   package step 86 installs depends on it, so it is usually present already; it is named here
#   because an install that stops depending on it must not lose the migration door silently.
#   RHEL family: `openssh-clients` ships ssh and ssh-keyscan, and ssh-keygen is in `openssh`, a
#   different package. `openssh-server` pulls `openssh` in; a minimal host is not promised
#   `openssh-clients`, which is the one that is a real gap there.
#
# `rsync` (issue #197, phase 2) is listed beside them on every family: it is the program the
# agent runs, as the receiving account, to copy a registered source's home, and it uses the
# `ssh` above as its transport. The package is `rsync` on all three families and puts the
# program at /usr/bin/rsync, where the `DistroAdapter` says it is.
ssh_client_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian)
      echo "openssh-client rsync"
      ;;
    rhel)
      echo "openssh openssh-clients rsync"
      ;;
    suse)
      # UNVERIFIED: the client programs are believed to ship in `openssh-clients` (ssh,
      # ssh-keyscan) and `openssh-common` (ssh-keygen). Confirm with `rpm -qf /usr/bin/ssh
      # /usr/bin/ssh-keyscan /usr/bin/ssh-keygen` in the pinned Leap image, and fix the names
      # here if any differs.
      echo "openssh-clients openssh-common rsync"
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
                /usr/bin/gzip /usr/bin/getent /usr/bin/pkill /usr/bin/pgrep \
                /usr/bin/ssh /usr/bin/ssh-keyscan /usr/bin/ssh-keygen /usr/bin/rsync; do
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
