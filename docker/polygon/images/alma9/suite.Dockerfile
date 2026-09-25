# Development-only test container for the Rust agent on AlmaLinux 9.
# Production never uses Docker — see spec §2.
#
# The image carries nginx, php-fpm and a Rust toolchain so that the agent's OWN
# validation can be exercised: `safe_write` renames a rendered vhost into place
# and then runs the real `nginx -t` against the real configuration tree, and
# `write_pool` does the same with the real `php-fpm -t`. Until something ran
# those binaries, the rollback protocol had never rolled anything back — and the
# php-fpm binary this family's adapter named did not exist at all. The packages
# are here to be VALIDATED AGAINST, not to make the container a web server —
# nothing is started by the image, no port is published, and the agent's
# integration tests are the only thing that runs.
#
# The toolchain is in the image rather than mounted from the host because the
# two polygon families ship different glibc versions (AlmaLinux 9 has 2.34,
# Ubuntu 24.04 has 2.39): a test binary compiled on the runner runs on one of
# them and refuses to start on the other. Compiling inside each polygon is what
# makes "the agent, on that distribution" true rather than approximately true.
#
# EVERYTHING FETCHED HERE IS PINNED AND VERIFIED. This image builds the binary
# whose tests decide whether a root daemon is safe, so `rules/security.md`'s
# posture does not stop at the container boundary: the base image is a digest,
# and every rpm, archive and installer below is checked against its sha256
# before it is used. A checksum that stops matching means upstream moved the
# artefact — re-pin it deliberately, having looked at what changed; never delete
# the check to get a build green.

# Digest-pinned: `almalinux:9` is a moving point release, and a polygon whose
# base changed under it is a suite whose meaning changed with no diff.
FROM almalinux:9@sha256:d2515c769e7b73f95c4fde38c0a505336ff38f14990c0b7253b77060a049a743

# Pinned so a polygon run and a `maran agent check` compile with the same
# compiler; bump it together with scripts/lib/agent.sh.
ARG RUST_VERSION=1.98.0
# rustup itself, as a versioned archive rather than `sh.rustup.rs` — that URL
# serves whatever is current, and piping it into a shell unverified is the one
# unpinned dependency that could rewrite every other one.
ARG RUSTUP_VERSION=1.28.2
ARG RUSTUP_SHA256=20a06e644b0d9bd2fbdbfd52d42540bdde820ea7df86e92e533c073da0cdd43c
# Remi drops the dot from the version and roots its packages under
# /opt/remi/php83 — the family difference RhelAdapter encodes twice over, in the
# pool directory and in the php-fpm binary path.
ARG PHP_VERSION=83
# Upstream protoc: see the toolchain layer below for why this family does not
# use its own package.
ARG PROTOC_VERSION=25.3
ARG PROTOC_SHA256=f853e691868d0557425ea290bf7ba6384eef2fa9b04c323afab49a770ba9da80
# The two repository-release rpms, by content. Their URLs float by design —
# "latest" is the only address upstream publishes — so the checksum is what makes
# the build deliberate: a changed release rpm stops the build instead of quietly
# installing a different set of repositories and keys.
ARG EPEL_RELEASE_SHA256=b434245bffd8b40ea486157e72363d08b36e38145c8f917c5c00adfca3f2101b
ARG REMI_RELEASE_SHA256=21100d93098f5821ff21d7ed511c99d874ba895438c2b78d8c73937133158d73

ENV RUSTUP_HOME=/usr/local/rustup \
    CARGO_HOME=/usr/local/cargo \
    PATH=/usr/local/cargo/bin:$PATH

# The marker the root-only integration tests require before they touch a single
# system account. Without it they refuse to run rather than skipping quietly, so
# a suite that never entered a polygon cannot be mistaken for a suite that passed
# in one.
ENV MARAN_POLYGON=alma9

# The web server and PHP first, from the family's own repositories: nginx from
# AlmaLinux, php-fpm from Remi, which is the repository RhelAdapter's package and
# service names are written against. Remi requires EPEL, so both release rpms are
# downloaded, checked and only then installed.
#
# The nginx and php package VERSIONS deliberately float: they are what the family
# ships on the day of the build, which is what the agent will meet on a
# customer's server, and pinning them would test a configuration nobody runs.
# Everything else here is pinned.
RUN dnf install -y --nodocs ca-certificates \
    && curl -fsSL -o /tmp/epel-release.rpm \
        https://dl.fedoraproject.org/pub/epel/epel-release-latest-9.noarch.rpm \
    && curl -fsSL -o /tmp/remi-release.rpm \
        https://rpms.remirepo.net/enterprise/remi-release-9.rpm \
    && echo "${EPEL_RELEASE_SHA256}  /tmp/epel-release.rpm" | sha256sum -c - \
    && echo "${REMI_RELEASE_SHA256}  /tmp/remi-release.rpm" | sha256sum -c - \
    && dnf install -y --nodocs /tmp/epel-release.rpm /tmp/remi-release.rpm \
    && rm /tmp/epel-release.rpm /tmp/remi-release.rpm \
    && dnf install -y --nodocs \
        nginx \
        "php${PHP_VERSION}-php-fpm" \
        "php${PHP_VERSION}-php-cli" \
    && dnf clean all

# The Rust toolchain and what cargo needs to link a test binary: `cc` for the
# linker and protoc for the agent's build script, which compiles the proto
# contract at build time (rules/proto.md).
#
# protoc comes from upstream rather than from the distribution, and only on this
# family: AlmaLinux 9 ships protobuf 3.14, which predates proto3 `optional` and
# refuses `php.proto` outright. It is the build's one outbound binary fetch, so
# it is pinned by version AND by checksum — a GitHub release asset can be
# replaced by anyone with push rights to the release, so "pinned to a version" is
# not on its own pinned to a file.
RUN dnf install -y --nodocs --enablerepo=crb \
        gcc \
        make \
        unzip \
    && dnf clean all \
    && curl -fsSL -o /tmp/protoc.zip \
        "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-x86_64.zip" \
    && echo "${PROTOC_SHA256}  /tmp/protoc.zip" | sha256sum -c - \
    && unzip -q -o /tmp/protoc.zip -d /usr/local bin/protoc 'include/*' \
    && rm /tmp/protoc.zip \
    && chmod 755 /usr/local/bin/protoc \
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
# The `grep` is about how `nft` PRINTS a rule back, not about whether the
# ruleset loads: the template writes `meta l4proto 58`, which loads on a host
# with no /etc/protocols at all (measured). This family's base image ships the
# file — the Debian family's does not, and its Dockerfile installs `netbase` —
# so this asserts rather than installs, and fails the build the day that stops
# being true.
RUN dnf install -y --nodocs nftables \
    && dnf clean all \
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
# neither, both coming from tmpfiles and first-boot units no container runs. So
# is `mariadb-install-db`: unlike the Debian family, this one's package leaves the
# data directory to its systemd unit's mariadb-prepare-db-dir, which no container
# runs either.
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
RUN dnf install -y --nodocs pam-devel \
    && dnf clean all \
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
      export MARAN_OS_FAMILY=rhel; \
      . /tmp/maran-installer/lib/85-mysql.sh; \
      dnf install -y --nodocs openssh-server openssh-clients $(mysql_packages_for_family); \
      getent passwd mysql >/dev/null || { echo "85-mysql.sh: mysql_packages_for_family produced no MariaDB SERVER package" >&2; exit 1; }; \
      install -d -o mysql -g mysql -m 0755 /run/mysqld /var/lib/mysql; \
      mariadb-install-db --user=mysql --datadir=/var/lib/mysql >/dev/null; \
      ssh-keygen -A; \
      install -d -m 0755 /run/sshd; \
      mariadbd-safe --skip-networking --skip-syslog & \
      for _ in $(seq 1 60); do mariadb-admin ping >/dev/null 2>&1 && break; sleep 1; done; \
      MARAN_OS_FAMILY=rhel bash /tmp/maran-installer/assert-installer-steps.sh; \
      dnf clean all; \
      rm -rf /tmp/maran-agent /tmp/maran-polygon; \
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
    && rm -f /tmp/maran-installer/lib/40-user.sh

# cronie, this family's cron, for the scheduling half of plan 5. It is here to
# be RUN against rather than to make the container a scheduler: a real
# `crontab(1)` to accept or refuse the rendered table, and a real daemon to RUN
# an entry — the part no unit test reaches, since `%` and `#` are the two
# characters that killed two earlier designs and only a daemon executing the
# installed line proves they survive.
#
# nftables moved to its own block above, before the installer assertions that
# need it; this one is cron alone.
#
# The package name is NOT written here: it comes from `cron_packages_for_family`
# in installer/lib/88-cron.sh, the same arrangement 85-mysql.sh's block uses, so
# a package name that stops being right on this family stops this build instead
# of waiting to be found on a customer's server.
#
# The `broken_shadow` edit below is a CONTAINER ACCOMMODATION, in the same class
# as `ssh-keygen -A` and `mariadb-install-db` above, and it is stated rather than
# hidden because it switches a check off. Measured in this image: cronie's
# `/etc/pam.d/crond` includes `account required pam_unix.so`, that module answers
# PAM_AUTHINFO_UNAVAIL for every account, and cronie refuses to run a single job
# — `crond -x proc` logs "FAILED to authorize user with PAM" and no command is
# executed. It is the container and not the account: `/usr/sbin/unix_chkpwd
# <user> chkexpiry`, the helper that module reads shadow information through,
# exits 9 here for a freshly created account even when run as root, with
# /etc/shadow present and the helper setuid. `broken_shadow` is pam_unix's OWN
# documented option for "ignore errors reading shadow information in account
# management", so this narrows one module's failure mode rather than replacing
# the stack with pam_permit.
#
# What it costs, exactly: cronie's account-expiry check does not run in this
# image, so nothing here covers what cron does for an expired or locked account.
# Nothing the agent writes depends on that check — it installs tables and command
# files, and the suite asserts those — but the gap is real and belongs in the
# record rather than in a passing suite nobody questioned. Debian's cron needs no
# such edit; its stack authorises these accounts as is.
COPY installer/lib/88-cron.sh /tmp/maran-installer/lib/88-cron.sh
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=rhel; \
      . /tmp/maran-installer/lib/88-cron.sh; \
      dnf install -y --nodocs $(cron_packages_for_family); \
      dnf clean all; \
      test -x /usr/sbin/crond; \
      grep -q "^ipv6-icmp" /etc/protocols; \
      sed -i "s/^account    include    system-auth$/account    required   pam_unix.so broken_shadow/" \
        /etc/pam.d/crond; \
      grep -q broken_shadow /etc/pam.d/crond'

# `passwd`, which on this family is its OWN package and not part of
# `shadow-utils` — the package that supplies `useradd`, `usermod` and `userdel`.
# On the Debian family one package supplies all four, which is exactly why the
# gap survived: the agent's suspension attestation runs `/usr/bin/passwd -S` to
# read whether a login is locked, it worked on ubuntu24 from the first run, and
# on this image the program was not there at all. Everything that asks the host
# whether an account is suspended failed at exec time on the whole RHEL family,
# and the polygon suite that would have caught it did not list this path.
#
# `test -x` and not a bare install, for the reason every other block here gives:
# an install that succeeded while putting the program somewhere else is the
# failure this stops.
RUN dnf install -y --nodocs passwd \
    && dnf clean all \
    && test -x /usr/bin/passwd

# The quota tools. The agent execs BOTH halves by absolute path — `/usr/sbin/setquota`
# to apply an account's limit and `/usr/bin/quota` to read its usage back for
# GetAccountUsage — and until this block existed the image had neither, so the
# read path's binary was declared by the distro adapter, asserted by a test that
# only compared one string to another, and present on no host anybody ran.
# `binary_paths_on_a_real_host.rs` now stats what the adapter declares, and it
# needs the real package to have something to stat.
RUN dnf install -y --nodocs quota \
    && dnf clean all \
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
      export MARAN_OS_FAMILY=rhel; \
      . /tmp/maran-installer/lib/88-cron.sh; \
      verify_cron_ready; \
      mkdir -p /run/polygon-units; \
      printf "inactive\n" > /run/polygon-units/crond; \
      if ( verify_cron_ready ) >/dev/null 2>/tmp/refusal; then \
        echo "88-cron.sh accepted a host whose cron is not running" >&2; exit 1; \
      fi; \
      grep -q "not running" /tmp/refusal \
        || { echo "88-cron.sh refused, but not for the reason it should have:" >&2; cat /tmp/refusal >&2; exit 1; }; \
      rm -f /run/polygon-units/crond; \
      systemctl disable crond; \
      if ( verify_cron_ready ) >/dev/null 2>/tmp/refusal; then \
        echo "88-cron.sh accepted a host whose cron is not enabled at boot" >&2; exit 1; \
      fi; \
      grep -q "not enabled at boot" /tmp/refusal \
        || { echo "88-cron.sh refused, but not for the reason it should have:" >&2; cat /tmp/refusal >&2; exit 1; }; \
      systemctl enable crond; \
      rm -f /tmp/refusal; \
      verify_cron_ready' \
    && rm -rf /tmp/maran-installer

# The one file mode this image changes, and the whole reason it has to.
#
# MEASURED on 2026-09-09, inside this image, both ways in one command: under
# `docker run --privileged` `pam_unix.so` answers
# `9 Authentication service cannot retrieve authentication info` for EVERY
# login on this family — a correct password and a wrong one alike — while the
# same image unprivileged answers `0 Success` and `7 Authentication failure`.
# Debian is unaffected. That is what left `ftps_on_a_real_host` unable to
# authenticate anybody here, because `scripts/lib/polygon.sh` runs a whole
# family in ONE `--privileged` container.
#
# The mechanism, narrowed to rather than guessed at:
#
#   * `pam_unix.so` on this family verifies a shadow password through the setuid
#     helper `/usr/sbin/unix_chkpwd`, and `strace` shows that helper running as
#     uid 0 and getting `openat("/etc/shadow") = -1 EACCES`.
#   * An AppArmor host — the developer machines and CI runners this repository is
#     built on — ships `/etc/apparmor.d/unix-chkpwd`, a profile attached BY PATH
#     to `/{,usr/}{,s}bin/unix_chkpwd`. It grants `/etc/shadow r` and
#     `capability audit_write`, and it does NOT grant `capability dac_override`.
#   * `--privileged` implies `apparmor=unconfined`, and a container with no
#     docker profile of its own is where a host path profile attaches. Under
#     `--security-opt apparmor=docker-default` the helper answers correctly even
#     with `--privileged`; with `--cap-add=ALL` alone, or `seccomp=unconfined`
#     alone, it also answers correctly. AppArmor confinement is the switch — not
#     the capability set and not seccomp.
#   * AlmaLinux ships `/etc/shadow` as `0000 root:root`, so even root needs
#     CAP_DAC_OVERRIDE to read it, and that is the capability the profile
#     withholds. Ubuntu ships it `0640 root:shadow`, which root reads as the
#     OWNER with no capability at all — which is exactly why the Debian image
#     never showed this.
#
# So the smallest change that lets the process which must read the shadow
# database read it is to let its OWNER read it: `0400 root:root`. Nobody gains
# access — the file stays unreadable to every uid but root, which is all `0000`
# ever bought on a host where root holds DAC_OVERRIDE anyway — and the mode
# survives `useradd`, `chpasswd` and `usermod --lock`, which rewrite the file
# through a temp copy and restore its mode (verified in this image).
#
# What it COSTS, stated rather than buried: this image no longer matches a stock
# AlmaLinux host in the mode of one file. Nothing in the product reads
# `/etc/shadow` directly — the agent asks `getent shadow`, which is not path
# confined — so no product behaviour is papered over; what is worked around is
# the interaction between an Ubuntu host kernel's AppArmor policy and a RHEL
# container image, which is a property of the harness. The alternatives were
# worse: running the polygon unprivileged is not available (`sftp_on_a_real_host`
# makes a real bind mount), and `--privileged --security-opt
# apparmor=docker-default` was measured refusing that mount outright
# ("mount: /mnt/t: cannot mount none read-only").
#
# The `stat` below is the vacuity guard: if a base image ever ships the shadow
# file at another path, or a later step resets the mode, this build fails here
# instead of producing an image on which no login can be verified. The runtime
# half — that the answer really is available to PAM inside a PRIVILEGED
# container, which no build layer can observe because a build layer is never
# privileged — is asserted by `ftps_on_a_real_host.rs`, in the test
# `the_shadow_database_can_be_read_for_authentication_in_this_container`.
RUN chmod 0400 /etc/shadow \
    && [ "$(stat -c %a /etc/shadow)" = "400" ] \
    && [ "$(stat -c %U:%G /etc/shadow)" = "root:root" ] \
    || { echo "/etc/shadow is not 0400 root:root after this step: on an AppArmor host no login can be verified inside a privileged container built from this image" >&2; exit 1; }


# Expected docker run invocation for the polygon suites, from the repository root:
# docker run --rm -v "$PWD:/maran" -w /maran/agent maran-polygon-alma9 \
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
# docker run --rm --privileged -v "$PWD:/maran" -w /maran/agent maran-polygon-alma9 \
#   cargo test --test sftp_on_a_real_host -- --ignored --test-threads=1
#
# Run without --privileged it does not skip: the mount fails, create_sftp_user
# returns JailFailed, and the suite goes red.
#
# And for the agent itself, which is still built on the host and mounted:
# docker run --rm -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" maran-polygon-alma9 maran-agent --socket /run/maran/agent.sock --allow-uid 0
