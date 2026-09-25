# Development-only test container for the Rust agent on Ubuntu 24.04.
# Production never uses Docker — see spec §2.
#
# The image carries nginx, php-fpm and a Rust toolchain so that the agent's OWN
# validation can be exercised: `safe_write` renames a rendered vhost into place
# and then runs the real `nginx -t` against the real configuration tree, and
# `write_pool` does the same with the real `php-fpm -t`. Until something ran
# those binaries, the rollback protocol had never rolled anything back. The
# packages are here to be VALIDATED AGAINST, not to make the container a web
# server — nothing is started by the image, no port is published, and the agent's
# integration tests are the only thing that runs.
#
# The toolchain is in the image rather than mounted from the host because the
# two polygon families ship different glibc versions (Ubuntu 24.04 has 2.39,
# AlmaLinux 9 has 2.34): a test binary compiled on the runner runs on one of
# them and refuses to start on the other. Compiling inside each polygon is what
# makes "the agent, on that distribution" true rather than approximately true.
#
# EVERYTHING FETCHED HERE IS PINNED AND VERIFIED. This image builds the binary
# whose tests decide whether a root daemon is safe, so `rules/security.md`'s
# posture does not stop at the container boundary: the base image is a digest,
# rustup is a versioned archive checked against its sha256, and the Sury signing
# key is checked against its fingerprint rather than trusted on first sight. A
# checksum that stops matching means upstream moved the artefact — re-pin it
# deliberately, having looked at what changed; never delete the check to get a
# build green.

# Digest-pinned: `ubuntu:24.04` is a moving point release, and a polygon whose
# base changed under it is a suite whose meaning changed with no diff.
FROM ubuntu:24.04@sha256:33ceb71981b602c1a7443a53469e4dba065f7503eab3078a2d7a57a2ab987517

# Pinned so a polygon run and a `maran agent check` compile with the same
# compiler; bump it together with scripts/lib/agent.sh.
ARG RUST_VERSION=1.98.0
# rustup itself, as a versioned archive rather than `sh.rustup.rs` — that URL
# serves whatever is current, and piping it into a shell unverified is the one
# unpinned dependency that could rewrite every other one.
ARG RUSTUP_VERSION=1.28.2
ARG RUSTUP_SHA256=20a06e644b0d9bd2fbdbfd52d42540bdde820ea7df86e92e533c073da0cdd43c
# One PHP version is enough to exercise a php-fpm pool and a `fastcgi_pass`
# vhost. Sury keeps the dot in the version, which is exactly the family
# difference DebianAdapter::php_fpm_pool_directory encodes.
ARG PHP_VERSION=8.3
# Sury's repository signing key, by fingerprint. The key is fetched over TLS
# like everything else; what this pins is WHICH key, so a substituted one is
# refused instead of becoming the trusted signer for every PHP package below.
ARG SURY_FINGERPRINT=15058500A0235D97F5D10063B188E2B695BD4743

ENV DEBIAN_FRONTEND=noninteractive \
    RUSTUP_HOME=/usr/local/rustup \
    CARGO_HOME=/usr/local/cargo \
    PATH=/usr/local/cargo/bin:$PATH

# The marker the root-only integration tests require before they touch a single
# system account. Without it they refuse to run rather than skipping quietly, so
# a suite that never entered a polygon cannot be mistaken for a suite that passed
# in one.
ENV MARAN_POLYGON=ubuntu24

# The web server and PHP first, from the family's own repositories: nginx from
# Ubuntu, php-fpm from Sury, which is the repository DebianAdapter's package and
# service names are written against.
#
# The nginx and php package VERSIONS deliberately float: they are what the
# family ships on the day of the build, which is what the agent will meet on a
# customer's server, and pinning them would test a configuration nobody runs.
# Everything else here is pinned.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg \
        nginx \
    && curl -fsSL https://packages.sury.org/php/apt.gpg -o /tmp/sury.gpg \
    && gpg --show-keys --with-colons --with-fingerprint /tmp/sury.gpg \
        | grep -qx "fpr:::::::::${SURY_FINGERPRINT}:" \
    && mv /tmp/sury.gpg /usr/share/keyrings/sury-php.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/sury-php.gpg] https://packages.sury.org/php/ noble main" \
        > /etc/apt/sources.list.d/sury-php.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        "php${PHP_VERSION}-fpm" \
        "php${PHP_VERSION}-cli" \
    && rm -rf /var/lib/apt/lists/*

# The Rust toolchain and what cargo needs to link a test binary: `cc` for the
# linker and protoc for the agent's build script, which compiles the proto
# contract at build time (rules/proto.md). Ubuntu's own protobuf-compiler is new
# enough to accept proto3 `optional`; AlmaLinux's is not, which is why only that
# family downloads one.
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential \
        pkg-config \
        protobuf-compiler \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL -o /tmp/rustup-init \
        "https://static.rust-lang.org/rustup/archive/${RUSTUP_VERSION}/x86_64-unknown-linux-gnu/rustup-init" \
    && echo "${RUSTUP_SHA256}  /tmp/rustup-init" | sha256sum -c - \
    && chmod 755 /tmp/rustup-init \
    && /tmp/rustup-init -y --profile minimal --default-toolchain "${RUST_VERSION}" \
    && rm /tmp/rustup-init \
    # Owned by root, not writable by everybody. The privilege-drop suite forks
    # into real unprivileged accounts in this image while root goes on to execute
    # cargo and rustc from these directories; a world-writable toolchain in the
    # one image whose job is to prove privileges are dropped properly is the
    # wrong default even in a disposable container.
    && chown -R root:root "${CARGO_HOME}" "${RUSTUP_HOME}" \
    && chmod -R go-w "${CARGO_HOME}" "${RUSTUP_HOME}"

# The agent's own runtime directories, and — this is the point — the nginx include
# obtained by RUNNING THE INSTALLER'S OWN STEP FILE rather than by repeating its edit
# here. The image used to `sed` the include into nginx.conf itself, which made every
# site test assert a precondition the image had manufactured: the installer never
# created /etc/maran/nginx/sites and never added the include, so the first CreateSite
# on a real server failed, and 1381 tests could not see it because the only ones that
# touch nginx ran in here. Now, if installer/lib/80-nginx.sh stops doing it, this build
# fails and no polygon suite runs at all (rules/testing.md).
COPY installer/lib/80-nginx.sh /tmp/maran-installer/lib/80-nginx.sh
RUN mkdir -p /run/maran /run/maran/php \
    && chmod 755 /run/maran \
    && bash -c 'set -euo pipefail; . /tmp/maran-installer/lib/80-nginx.sh; install_agent_config_include' \
    && test -d /etc/maran/nginx/sites \
    && test -d /etc/maran/certificates \
    && grep -q '/etc/maran/nginx/sites' /etc/nginx/conf.d/maran-sites.conf \
    && nginx -t \
    && rm -rf /tmp/maran-installer

# nftables, installed BEFORE the installer assertions below rather than beside
# cron further down, and the ordering is the whole point: those assertions run
# 87-firewall.sh's own include wiring, which invokes `nft` to check the file it
# is about to write. With the package installed later the wiring found no `nft`,
# refused the candidate it had just rendered, and failed the build — the step
# behaving correctly against an image that was not ready for it.
#
# `netbase` comes with it, and only decides how `nft` PRINTS a rule back — the
# ruleset itself loads without /etc/protocols, measured on a base image that has
# none. Same ruleset, two images:
#
#   without /etc/protocols:  meta l4proto 58 accept
#   with it:                 meta l4proto ipv6-icmp accept
#
# The firewall suite asserts the name form, because that is what an operator
# reading `nft list` on a real server sees — and a real Ubuntu server has
# netbase, which is priority `important`. The `grep` fails the build if the file
# stops being there, which is what stops that assertion turning into a puzzle.
RUN apt-get update && apt-get install -y --no-install-recommends netbase nftables \
    && rm -rf /var/lib/apt/lists/* \
    && nft --version \
    && grep -q '^ipv6-icmp' /etc/protocols

# MariaDB and OpenSSH, and — the same point again, for the two areas plan 4 adds —
# the host preconditions obtained by RUNNING THE INSTALLER'S OWN STEP FILES.
#
# The package list is not written here: it comes from `mysql_packages_for_family`
# in installer/lib/85-mysql.sh, so a package name that stops being right on this
# family stops this build. What is asserted afterwards lives in
# docker/polygon/asserts/assert-installer-steps.sh, identical for both families, which
# runs the installer's own functions and checks what they left behind — including
# the two loud refusals (a root password, a passwordless root) that no positive
# test can reach.
#
# The ssh CLIENT is installed beside the server because the SFTP suite proves the
# chroot by BEING REFUSED in a real session rather than by reading a config file,
# and being refused requires logging in.
#
# Nothing is baked in running: MariaDB is started for the length of this one RUN
# and shut down at the end of it, so the image ships a data directory rather than
# a daemon and the suites start it in their own fixture. `ssh-keygen -A` and
# /run/sshd are image setup too — sshd -t refuses to validate anything on a host
# with no host keys and no privilege separation directory, and a container has
# neither, both coming from tmpfiles and first-boot units no container runs.
COPY installer/lib/85-mysql.sh /tmp/maran-installer/lib/85-mysql.sh
COPY installer/lib/86-sftp.sh /tmp/maran-installer/lib/86-sftp.sh
# SOURCED by the assert script for its functions, never run: `step_firewall`
# ends at a gate that asks whether `table inet maran` is loaded, and nothing
# loads it here because the stand-in starts no unit. That gate failing in a
# container is the gate working, not a regression — so the assertions exercise
# the step's parts (its render invocations, its include wiring, its one-flag-
# per-port argv) rather than the step end to end.
COPY installer/lib/87-firewall.sh /tmp/maran-installer/lib/87-firewall.sh
# Files the assert script needs beside the two step files above. The first two
# it READS rather than runs: the installer's entry point, which is the single
# authority for the panel port, and the panel vhost whose every `listen` must
# name it. A port that used to be a literal in four places is one number now,
# and this is what keeps it one.
# The uninstaller, SOURCED IN A CHILD by the assert script and never run whole: it is the
# other copy of the marker state machine, and until it was copied in here nothing in this
# repository exercised it at all — the `sed '/BEGIN/,/END/d'` that once deleted an
# operator's own table could be put back in it and every gate would stay green.
COPY installer/uninstall.sh /tmp/maran-installer/uninstall.sh
COPY installer/install.sh /tmp/maran-installer/install.sh
COPY installer/nginx/maran.conf /tmp/maran-installer/nginx/maran.conf
# The rotation policy for the PANEL'S OWN two nginx logs, which step 80 installs and which it
# ABORTS BY NAME when the payload is missing. It is here because the assertions below run that
# step for real: without this line step 80 refuses, every vhost assertion goes red for the wrong
# reason, and the image build stops — which is the correct failure for a forgotten payload file,
# and better than the alternative the step could have taken of skipping the policy quietly and
# leaving the panel's access and error logs growing forever on a real host.
COPY installer/logrotate/maran-panel /tmp/maran-installer/logrotate/maran-panel
# `60-config.sh` is SOURCED: its SSH port detection is RUN against this image's
# real sshd_config rather than described. That matters here more than anywhere
# else, because these images ship the `Include /etc/ssh/sshd_config.d/*.conf`
# shape that defeated the single-file parser — so the assertion is the
# regression itself, not an account of one.
COPY installer/lib/60-config.sh /tmp/maran-installer/lib/60-config.sh
# Read, never sourced: `10-preflight.sh` defines its own `fail`, and sourcing it
# would replace the assert script's.
COPY installer/lib/10-preflight.sh /tmp/maran-installer/lib/10-preflight.sh
# Read, so the documented keys can be checked against what the installer writes.
COPY installer/panel.env.example /tmp/maran-installer/panel.env.example
# The api's unit, the tmpfiles snippet that builds the directory holding its listening socket,
# the agent unit step 70 installs beside it, and step 70 itself. The step is SOURCED and RUN:
# assert_the_panel_socket_directory_is_built_and_then_looked_at calls its own install_units,
# build_api_socket_directory and assert_api_socket_directory, so the panel's trust boundary is
# BUILT by this family's real systemd-tmpfiles and then stat'ed, rather than grepped for in a
# unit file. The two greps that used to stand in for that passed while the directory came out
# group-owned by the service account's own group on both families and nginx could not open
# the socket at all.
#
# A check whose subject is not in the image does not skip, it fails with
# `grep: ... No such file or directory` and then blames the unit. Every file that script reads or
# runs needs its COPY here; these are those four.
COPY installer/systemd/maran-api.service /tmp/maran-installer/systemd/maran-api.service
COPY installer/systemd/maran-api.tmpfiles.conf /tmp/maran-installer/systemd/maran-api.tmpfiles.conf
COPY installer/systemd/maran-agent.service /tmp/maran-installer/systemd/maran-agent.service
COPY installer/lib/70-services.sh /tmp/maran-installer/lib/70-services.sh
# Step 80 again — the earlier COPY was consumed by the `rm -rf /tmp/maran-installer` that ends
# the include block above — and step 40 beside it. Both are RUN by the assert script, not read:
# it drives the real step_nginx against this family's real nginx to prove that the vhost the
# installer validates is the vhost nginx serves, and it takes the service group step 80 needs
# from step 40's own create_service_user rather than making one of its own.
COPY installer/lib/80-nginx.sh /tmp/maran-installer/lib/80-nginx.sh
COPY installer/lib/40-user.sh /tmp/maran-installer/lib/40-user.sh
# Step 15, the identity migration: assert_the_legacy_service_account_is_migrated_in_place
# drives its rename functions against throwaway accounts and then looks at the uids.
COPY installer/lib/15-identity.sh /tmp/maran-installer/lib/15-identity.sh
# Step 50 beside them, for the same reason and RUN the same way: the assert script calls its
# prepare_staging_dir directly. Step 50 as a whole downloads release artifacts over HTTPS and
# this build spends no trust on that, but the directory that function builds is where an
# archive is checksummed and extracted from, and the file that comes out of it is the binary
# maran-agent.service starts as root — so the directory is asserted here even though the
# download is not.
COPY installer/lib/50-artifacts.sh /tmp/maran-installer/lib/50-artifacts.sh
# Step 90, the install's last message, RUN and never read: the assert script renders it at two
# different panel ports and holds what it says about the certificate paths against step 80's own
# variables. Until this COPY existed nothing could gate that message at all, so the two absolute
# paths it tells an operator to put their own certificate at, and the port in the url it sends
# them to, were literals no check could see drift in.
COPY installer/lib/90-finish.sh /tmp/maran-installer/lib/90-finish.sh

# Step 89 and what it needs, all four RUN or READ by the assert script and none of them
# repeated by this image. The step is the FTPS installer step: it masks the distribution's
# vsftpd.service, installs the package from its OWN `vsftpd_packages_for_family`, creates the
# maran-ftps group, the jail base and the config directory, writes the PAM stack, and installs
# Maran's unit and logrotate policy — all of which the assert script drives function by
# function. So the vsftpd package arrives in this image through the installer's list and
# through nothing else, and a package name that stops being right on this family stops this
# build rather than a customer's install.
#
# 20-dependencies.sh is here because `install_vsftpd_package` calls its `pkg_install`; the
# step refuses by name when it is undefined, which is what a forgotten COPY must look like.
# The unit and the logrotate policy are the step's PAYLOAD: it resolves them against
# $SCRIPT_DIR, which the assert script sets to /tmp/maran-installer.
COPY installer/lib/89-ftps.sh /tmp/maran-installer/lib/89-ftps.sh
COPY installer/lib/20-dependencies.sh /tmp/maran-installer/lib/20-dependencies.sh
COPY installer/systemd/maran-ftps.service /tmp/maran-installer/systemd/maran-ftps.service
COPY installer/logrotate/maran-ftps /tmp/maran-installer/logrotate/maran-ftps
# A container has no init system, so the reload half of the config-write protocol
# needs something to talk to. The stand-in explains itself and its limits.
#
# BEFORE the assertions below, not after them, and that ordering is a fix rather
# than a tidy-up. `disable_firewalld` is the one part of the firewall step whose
# subject is a unit rather than a file, and the only firewalld a container can
# have — or a query about one that fails to ANSWER — is the state this stand-in
# records. Copied after the suite had run, those four cases met this image's REAL
# systemctl, which needs no booted manager to read unit files off the disk and so
# answers `0 unit files listed.` honestly for every one of them: the case that
# puts the host into "the query broke" was told "No firewalld unit on this host",
# and the build failed on a fixture that had never been installed. The suite
# refuses to start without it now (require_systemctl_stand_in), so this cannot
# drift back into a check that cannot fail for the reason it names.
COPY docker/polygon/stand-ins/systemctl-stand-in.sh /usr/bin/systemctl
RUN chmod 755 /usr/bin/systemctl

# The PAM development headers, and the agent's own copies of the three FTPS names. Both are for
# assertions in docker/polygon/asserts/assert-installer-steps.sh, and both exist because a green gate over
# an authentication bypass is worse than no gate.
#
# The headers: the assert script COMPILES docker/polygon/asserts/pam-witness.c and asks the real libpam
# what /etc/pam.d/maran-ftps answers for a member, for a non-member holding a CORRECT password,
# and for a non-member holding a wrong one. Grepping that file for its lines could not tell the
# shipped stack from one with `auth sufficient pam_permit.so` prepended — same lines, every check
# green, every account on the host admitted without a password. Only the library sees the
# difference, and the library needs a program to call it. The witness is compiled IN the image,
# like the agent itself, because the two families ship different libpam and different glibc.
#
# The five agent sources are READ, never compiled: the group name, the jail root, the PAM
# service name and the transfer log path are each spelled on both sides of this feature, and
# until these were copied in, each copy was asserted only against itself. If they drift, every FTPS login on every install
# fails with every gate green — which is exactly what installer/lib/89-ftps.sh's comment used to
# claim these images prevented.
COPY docker/polygon/asserts/pam-witness.c /tmp/maran-polygon/pam-witness.c
COPY agent/crates/distro/src/debian/debian_services.rs /tmp/maran-agent/crates/distro/src/debian/debian_services.rs
COPY agent/crates/distro/src/rhel/rhel_services.rs /tmp/maran-agent/crates/distro/src/rhel/rhel_services.rs
COPY agent/crates/agent-core/src/agent_paths.rs /tmp/maran-agent/crates/agent-core/src/agent_paths.rs
COPY agent/crates/templates/templates/vsftpd/vsftpd.conf.j2 /tmp/maran-agent/crates/templates/templates/vsftpd/vsftpd.conf.j2
COPY agent/crates/ops/src/ftps/enable_ftps.rs /tmp/maran-agent/crates/ops/src/ftps/enable_ftps.rs
RUN apt-get update && apt-get install -y --no-install-recommends libpam0g-dev \
    && rm -rf /var/lib/apt/lists/* \
    && test -f /usr/include/security/pam_appl.h

# The package index is dropped AFTER the assert script rather than before it, and that is a
# fix rather than a tidy-up: the script installs vsftpd through step 89's own
# `install_vsftpd_package`, and a package manager whose index has just been deleted cannot
# resolve the name — the step would abort with "Unable to locate package vsftpd" and the family
# would look like it had lost the package rather than the metadata.
# The last two steps installer/install.sh runs that nothing else in this image needed. They are
# here for assert_the_uninstaller_removes_every_drop_in_the_installer_creates, which derives its
# step list from install.sh's own run_step lines and refuses — naming the COPY — when a step it
# must read is not in the image. A census with an invisible member is the failure that check exists
# for: 81-site-logs.sh is the step that installs /etc/logrotate.d/maran-sites, one of the three
# rotation policies whose count is the whole subject.
COPY installer/lib/30-postgresql.sh /tmp/maran-installer/lib/30-postgresql.sh
COPY installer/lib/81-site-logs.sh /tmp/maran-installer/lib/81-site-logs.sh
# 88-cron.sh is COPY'd again further down for the cron suite's own reasons; it is needed HERE,
# before the assert script runs, because the census reads it too. The same duplication
# 40-user.sh and 80-nginx.sh already carry, and for the same reason: a COPY is cheap, a step the
# census cannot see is not.
COPY installer/lib/88-cron.sh /tmp/maran-installer/lib/88-cron.sh
COPY docker/polygon/asserts/assert-installer-steps.sh /tmp/maran-installer/assert-installer-steps.sh
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=debian DEBIAN_FRONTEND=noninteractive; \
      . /tmp/maran-installer/lib/85-mysql.sh; \
      apt-get update; \
      apt-get install -y --no-install-recommends openssh-server openssh-client $(mysql_packages_for_family); \
      getent passwd mysql >/dev/null || { echo "85-mysql.sh: mysql_packages_for_family produced no MariaDB SERVER package" >&2; exit 1; }; \
      install -d -o mysql -g mysql -m 0755 /run/mysqld; \
      ssh-keygen -A; \
      install -d -m 0755 /run/sshd; \
      mariadbd-safe --skip-networking --skip-syslog & \
      for _ in $(seq 1 60); do mariadb-admin ping >/dev/null 2>&1 && break; sleep 1; done; \
      MARAN_OS_FAMILY=debian bash /tmp/maran-installer/assert-installer-steps.sh; \
      rm -rf /var/lib/apt/lists/* /tmp/maran-agent /tmp/maran-polygon; \
      mariadb-admin shutdown'

# The backup root, made by the installer's OWN step rather than by a literal
# here. `ops::backup` refuses to write into a directory that is not root-owned
# and root-only, and it refuses one that does not exist — `LocalBackupRoot`
# answers a question about a STRING and the inode check answers a separate one
# about the directory — so a polygon without this directory fails every backup
# case with `BackupRootUnsafe` and tells nobody why.
#
# `create_directory_layout` is sourced and called instead of `install -d
# /var/backups/maran` being written out again, so the mode the suites measure
# is the mode a real install produces; a second copy of the path or the mode in
# this file is a copy that stops matching. The one thing put back afterwards is
# /run/maran's mode: that function sets 0750 root:<service group>, and this
# image sets 0755 on purpose for the php-pool suites (see the assert-script
# comment that says so).
#
# The account's name is not written here either: step 40 reads MARAN_USER,
# MARAN_GROUP and MARAN_SERVICE_ACCOUNT_MARKER out of the environment, the way
# install.sh gives them to it, and they are lifted from install.sh — the one
# place that decides them — rather than spelled again in this file. A step file
# that cannot see them aborts by name instead of expanding an empty string into
# `install -o`.
COPY installer/lib/40-user.sh /tmp/maran-installer/lib/40-user.sh
RUN bash -c 'set -euo pipefail; \
      MARAN_USER="$(sed -n "s/^MARAN_USER=//p" /tmp/maran-installer/install.sh)"; \
      MARAN_GROUP="$(sed -n "s/^MARAN_GROUP=//p" /tmp/maran-installer/install.sh)"; \
      MARAN_SERVICE_ACCOUNT_MARKER="$(sed -n "s/^MARAN_SERVICE_ACCOUNT_MARKER=//p" /tmp/maran-installer/install.sh | tr -d \"\\\"\")"; \
      export MARAN_USER MARAN_GROUP MARAN_SERVICE_ACCOUNT_MARKER; \
      test -n "$MARAN_USER" && test -n "$MARAN_GROUP" && test -n "$MARAN_SERVICE_ACCOUNT_MARKER"; \
      . /tmp/maran-installer/lib/40-user.sh; \
      create_service_user >/dev/null; \
      create_directory_layout; \
      chmod 0755 /run/maran; \
      test -d /var/backups/maran' \
    && rm -rf /tmp/maran-installer/lib/40-user.sh

# cron, for the scheduling half of plan 5. It is here to be RUN against rather
# than to make the container a scheduler: a real `crontab(1)` to accept or refuse
# the rendered table, and a real daemon to RUN an entry — the part no unit test
# reaches, since `%` and `#` are the two characters that killed two earlier
# designs and only a daemon executing the installed line proves they survive.
#
# nftables moved to its own block above, before the installer assertions that
# need it; this one is cron alone.
#
# The package name is NOT written here: it comes from `cron_packages_for_family`
# in installer/lib/88-cron.sh, the same arrangement 85-mysql.sh's block uses, so
# a package name that stops being right on this family stops this build instead
# of waiting to be found on a customer's server.
COPY installer/lib/88-cron.sh /tmp/maran-installer/lib/88-cron.sh
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=debian DEBIAN_FRONTEND=noninteractive; \
      . /tmp/maran-installer/lib/88-cron.sh; \
      apt-get update; \
      apt-get install -y --no-install-recommends $(cron_packages_for_family); \
      rm -rf /var/lib/apt/lists/*; \
      test -x /usr/sbin/cron'

# The quota tools. The agent execs BOTH halves by absolute path — `/usr/sbin/setquota`
# to apply an account's limit and `/usr/bin/quota` to read its usage back for
# GetAccountUsage — and until this block existed the image had neither, so the
# read path's binary was declared by the distro adapter, asserted by a test that
# only compared one string to another, and present on no host anybody ran.
# `binary_paths_on_a_real_host.rs` now stats what the adapter declares, and it
# needs the real package to have something to stat.
RUN apt-get update && apt-get install -y --no-install-recommends quota \
    && rm -rf /var/lib/apt/lists/* \
    && test -x /usr/bin/quota

# ...and then the administrative half is replaced by a stand-in, AFTER the
# package installed the real one, so the stand-in is what wins. A container has
# no filesystem quotas, which account creation applies. The reading half is left
# real: `quota` on a filesystem without quotas prints no limit and that is a
# state the parse must handle anyway.
COPY docker/polygon/stand-ins/setquota-stand-in.sh /usr/sbin/setquota
RUN chmod 755 /usr/sbin/setquota

# 88-cron.sh's own gate, run against this image — and then run against a cron
# that is DOWN, so the green half is not the only half anybody has seen.
#
# Exactly one of its three checks is fully real here: `/usr/bin/crontab` is the
# path the agent executes, and the package installed above either put it there
# or did not. The other two ask the service manager, which in a container is the
# stand-in copied above — so on their own they would be a check that cannot
# fail, which is the shape of defect this repository keeps finding. The mutation
# closes that: the stand-in is told cron is inactive, the gate MUST refuse and
# MUST say why, the state is put back, and then the SAME thing is done to the
# enablement half. Both, because passing one of the gate's questions is not the
# state the panel needs, and a gate that had stopped asking either would look
# identical to one that passed.
#
# What is still not covered anywhere automated: whether the unit really comes
# back after a reboot. A container has no reboot, so "enabled" here means the
# stand-in was told to enable it — never that systemd would start it at boot.
# Only a real host settles that.
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=debian; \
      . /tmp/maran-installer/lib/88-cron.sh; \
      verify_cron_ready; \
      mkdir -p /run/polygon-units; \
      printf "inactive\n" > /run/polygon-units/cron; \
      if ( verify_cron_ready ) >/dev/null 2>/tmp/refusal; then \
        echo "88-cron.sh accepted a host whose cron is not running" >&2; exit 1; \
      fi; \
      grep -q "not running" /tmp/refusal \
        || { echo "88-cron.sh refused, but not for the reason it should have:" >&2; cat /tmp/refusal >&2; exit 1; }; \
      rm -f /run/polygon-units/cron; \
      systemctl disable cron; \
      if ( verify_cron_ready ) >/dev/null 2>/tmp/refusal; then \
        echo "88-cron.sh accepted a host whose cron is not enabled at boot" >&2; exit 1; \
      fi; \
      grep -q "not enabled at boot" /tmp/refusal \
        || { echo "88-cron.sh refused, but not for the reason it should have:" >&2; cat /tmp/refusal >&2; exit 1; }; \
      systemctl enable cron; \
      rm -f /tmp/refusal; \
      verify_cron_ready' \
    && rm -rf /tmp/maran-installer

# Expected docker run invocation for the polygon suites, from the repository root:
# docker run --rm -v "$PWD:/maran" -w /maran/agent maran-polygon-ubuntu24 \
#   cargo test --test sites_on_a_real_host --test php_pools_on_a_real_host \
#     --test privileges_on_a_real_host --test databases_on_a_real_host \
#     --test binary_paths_on_a_real_host \
#     -- --ignored --test-threads=1
#
# The SFTP suite needs one thing more, and it is worth saying why rather than
# hiding it in a flag: it makes a REAL bind mount, which a container cannot do
# without CAP_SYS_ADMIN and an unrestricted seccomp profile. That mount is the
# point — without it the account's home is not inside the jail, and the suite
# could not tell a working jail from an empty one. So:
# docker run --rm --privileged -v "$PWD:/maran" -w /maran/agent maran-polygon-ubuntu24 \
#   cargo test --test sftp_on_a_real_host -- --ignored --test-threads=1
#
# Run without --privileged it does not skip: the mount fails, create_sftp_user
# returns JailFailed, and the suite goes red.
#
# And for the agent itself, which is still built on the host and mounted:
# docker run --rm -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" maran-polygon-ubuntu24 maran-agent --socket /run/maran/agent.sock --allow-uid 0
