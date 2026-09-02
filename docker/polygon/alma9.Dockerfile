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

# MariaDB and OpenSSH, and — the same point again, for the two areas plan 4 adds —
# the host preconditions obtained by RUNNING THE INSTALLER'S OWN STEP FILES.
#
# The package list is not written here: it comes from `mysql_packages_for_family`
# in installer/lib/85-mysql.sh, so a package name that stops being right on this
# family stops this build. What is asserted afterwards lives in
# docker/polygon/assert-installer-steps.sh, identical for both families, which
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
COPY docker/polygon/assert-installer-steps.sh /tmp/maran-installer/assert-installer-steps.sh
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=rhel; \
      . /tmp/maran-installer/lib/85-mysql.sh; \
      dnf install -y --nodocs openssh-server openssh-clients $(mysql_packages_for_family); \
      dnf clean all; \
      getent passwd mysql >/dev/null || { echo "85-mysql.sh: mysql_packages_for_family produced no MariaDB SERVER package" >&2; exit 1; }; \
      install -d -o mysql -g mysql -m 0755 /run/mysqld /var/lib/mysql; \
      mariadb-install-db --user=mysql --datadir=/var/lib/mysql >/dev/null; \
      ssh-keygen -A; \
      install -d -m 0755 /run/sshd; \
      mariadbd-safe --skip-networking --skip-syslog & \
      for _ in $(seq 1 60); do mariadb-admin ping >/dev/null 2>&1 && break; sleep 1; done; \
      MARAN_OS_FAMILY=rhel bash /tmp/maran-installer/assert-installer-steps.sh; \
      mariadb-admin shutdown' \
    && rm -rf /tmp/maran-installer

# A container has no init system, so the reload half of the config-write protocol
# needs something to talk to. The stand-in explains itself and its limits.
COPY docker/polygon/systemctl-stand-in.sh /usr/bin/systemctl
# And a container has no filesystem quotas, which account creation applies.
COPY docker/polygon/setquota-stand-in.sh /usr/sbin/setquota
RUN chmod 755 /usr/bin/systemctl /usr/sbin/setquota

# Expected docker run invocation for the polygon suites, from the repository root:
# docker run --rm -v "$PWD:/maran" -w /maran/agent maran-polygon-alma9 \
#   cargo test --test sites_on_a_real_host --test php_pools_on_a_real_host \
#     --test privileges_on_a_real_host --test databases_on_a_real_host \
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
