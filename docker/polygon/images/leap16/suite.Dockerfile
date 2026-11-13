# Development-only test container for the Rust agent on openSUSE Leap 16.
# Production never uses Docker — see spec §2.
#
# NOTHING in this file about openSUSE was measured by us. Every package name, path and tool behaviour
# below that this family decides is UNVERIFIED: read from the distribution's documentation and
# confirmed only by the first build of this image. Where a RUN depends on such a fact it names the
# command that settles it, and a wrong name stops the build at that RUN, which is the intended
# failure. docker/polygon/images/README.md carries the matrix row and what is still owed.
#
# The image carries nginx and a Rust toolchain (no site runtime: php-fpm, Apache and Node.js are apps,
# issue #517, installed by a suite through the app route) so that the agent's OWN
# validation can be exercised: `safe_write` renames a rendered vhost into place
# and then runs the real `nginx -t` against the real configuration tree. The package
# is here to be VALIDATED AGAINST, not to make the container a web server —
# nothing is started by the image, no port is published, and the agent's
# integration tests are the only thing that runs.
#
# The toolchain is in the image rather than mounted from the host because the polygon families ship
# different glibc versions (the figure for this one is to be measured with `ldd --version` in the
# pinned image): a test binary compiled on the runner runs on one of them and refuses to start on
# another. Compiling inside each polygon is what makes "the agent, on that distribution" true rather
# than approximately true.
#
# EVERYTHING FETCHED HERE IS PINNED AND VERIFIED, with one exception stated below. This image builds
# the binary whose tests decide whether a root daemon is safe, so `rules/security.md`'s posture does
# not stop at the container boundary: every archive and installer below is checked against its sha256
# before it is used. A checksum that stops matching means upstream moved the artefact — re-pin it
# deliberately, having looked at what changed; never delete the check to get a build green.
#
# THE EXCEPTION, and the pin that is owed: the base image below is a TAG, not a digest. Nothing here
# could pull the registry to read the digest, and a digest written from memory would be an invention.
# Pin it with `docker buildx imagetools inspect registry.opensuse.org/opensuse/leap:16.1` (the
# manifest-list digest, so both architectures are covered), write the result into this FROM line and
# into leap16/base.txt together, and delete this paragraph. Until then a rebuild can change what the
# suite measures with no diff to review.

# Unpinned (see above); keep this line equal to the one in leap16/base.txt.
FROM registry.opensuse.org/opensuse/leap:16.1

# Pinned so a polygon run and a `maran agent check` compile with the same
# compiler; bump it together with scripts/lib/agent.sh.
ARG RUST_VERSION=1.98.0
# rustup itself, as a versioned archive rather than `sh.rustup.rs` — that URL
# serves whatever is current, and piping it into a shell unverified is the one
# unpinned dependency that could rewrite every other one.
ARG RUSTUP_VERSION=1.28.2
ARG RUSTUP_SHA256=20a06e644b0d9bd2fbdbfd52d42540bdde820ea7df86e92e533c073da0cdd43c
# Upstream protoc, the same pinned archive the other family images use. Whether this family's own
# protobuf package is new enough for proto3 `optional` is UNVERIFIED (`zypper info protobuf-devel`),
# and one pinned compiler on every family is the stronger arrangement in any case.
ARG PROTOC_VERSION=25.3
ARG PROTOC_SHA256=f853e691868d0557425ea290bf7ba6384eef2fa9b04c323afab49a770ba9da80

ENV RUSTUP_HOME=/usr/local/rustup \
    CARGO_HOME=/usr/local/cargo \
    PATH=/usr/local/cargo/bin:$PATH

# The marker the root-only integration tests require before they touch a single
# system account. Without it they refuse to run rather than skipping quietly, so
# a suite that never entered a polygon cannot be mistaken for a suite that passed
# in one.
ENV MARAN_POLYGON=leap16

# The panel's web server first, from the family's own repositories. This family ships ONE PHP minor
# (decision 219: Leap 16 offers php 8.4 as the `php8-*` packages) as an app, installed on request, so
# no PHP package is installed here. The nginx package
# VERSIONS deliberately float: they are what the family ships on the day of the build, which is what
# the agent will meet on a customer's server, and pinning them would test a configuration nobody runs.
#
# UNVERIFIED: the package name `nginx`, and that `/usr/sbin/nginx` is where it lands. Confirm with
# `rpm -ql nginx | grep sbin` in the pinned image; `binary_paths_on_a_real_host` stats what the
# adapter declares against exactly these.
# What the BUILD and the assert script need from the base, named instead of guessed: this family's
# container base is minimal, and the other family images lean on a base that carries these. Installing
# what is already there is a no-op. `systemd` brings systemd-tmpfiles, systemd-escape (the mount
# stand-in's expectation) and systemd-analyze; `openssl` is the panel certificate step's tool; `gawk`,
# `findutils`, `tar`, `gzip` and `util-linux` are the assertions' own. UNVERIFIED: each package name; a
# wrong one stops the build at "Package ... not found", which is the intended failure.
RUN zypper --non-interactive install --no-recommends \
        ca-certificates curl systemd openssl gawk findutils tar gzip util-linux \
    && zypper --non-interactive install --no-recommends \
        nginx \
    && zypper --non-interactive clean --all

# The Rust toolchain and what cargo needs to link a test binary: `cc` for the
# linker and protoc for the agent's build script, which compiles the proto
# contract at build time (rules/proto.md).
#
# protoc comes from upstream rather than from the distribution (see the ARG above). It is the build's
# one outbound binary fetch besides rustup, so it is pinned by version AND by checksum — a GitHub
# release asset can be replaced by anyone with push rights to the release, so "pinned to a version" is
# not on its own pinned to a file.
RUN zypper --non-interactive install --no-recommends \
        gcc \
        make \
        unzip \
    && zypper --non-interactive clean --all \
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
# here (see the same block in the other family images for the defect this replaced).
#
# This family differs in one way that matters: UNVERIFIED, openSUSE's stock nginx.conf is believed to
# include `vhosts.d/*.conf` and NOT `conf.d/*.conf`, so the include the installer's step 80 makes first
# (`ensure_conf_d_include_in_nginx_conf`, which `step_nginx` calls before anything else) is run here
# too, before `install_agent_config_include`. The check afterwards asks nginx, not the file: `nginx -T`
# must list the drop-in among the files it loaded, which is the server's own answer to "do you read
# this?" and the only observation of a file in a directory nothing includes (a file there passes
# `nginx -t` and is never served). Confirm the premise with `grep -n include /etc/nginx/nginx.conf`
# right after `zypper install nginx` in the pinned image.
COPY installer/lib/80-nginx.sh /tmp/maran-installer/lib/80-nginx.sh
RUN bash -c 'set -euo pipefail; export MARAN_OS_FAMILY=suse; . /tmp/maran-installer/lib/80-nginx.sh; ensure_conf_d_include_in_nginx_conf; install_agent_config_include' \
    && test -d /etc/maran/nginx/sites \
    && test -d /etc/maran/certificates \
    && grep -q '/etc/maran/nginx/sites' /etc/nginx/conf.d/maran-sites.conf \
    && nginx -t \
    && nginx -T 2>/dev/null | grep -qF '# configuration file /etc/nginx/conf.d/maran-sites.conf:' \
    && rm -rf /tmp/maran-installer

# nftables, installed BEFORE the installer assertions below rather than beside cron further down, and
# the ordering is the whole point: those assertions run 87-firewall.sh's own include wiring, which
# invokes `nft` to check the file it is about to write.
#
# The protocol check is about how `nft` PRINTS a rule back, not about whether the ruleset loads: the
# template writes `meta l4proto 58`, which loads on a host with no protocols database at all
# (measured on the other families). It asks the C library, `getent protocols ipv6-icmp`, which is the
# lookup nft itself makes, rather than grepping /etc/protocols: UNVERIFIED whether this family keeps
# the file at /etc/protocols or under /usr/etc, and the library's answer is correct for either.
RUN zypper --non-interactive install --no-recommends nftables \
    && zypper --non-interactive clean --all \
    && nft --version \
    && getent protocols ipv6-icmp >/dev/null

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
COPY installer/nginx/maran-port80.conf /tmp/maran-installer/nginx/maran-port80.conf
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
# Step 65 applies the database migrations (issue #66). The census in assert-installer-steps.sh
# requires every step to be here, which is how its absence was found.
COPY installer/lib/65-schema.sh /tmp/maran-installer/lib/65-schema.sh
# Read, never sourced: `10-preflight.sh` defines its own `fail`, and sourcing it
# would replace the assert script's.
# The shared helpers the steps call; absent, a step exits 127 before any assertion runs.
COPY installer/lib/00-common.sh /tmp/maran-installer/lib/00-common.sh
COPY installer/lib/10-preflight.sh /tmp/maran-installer/lib/10-preflight.sh
# Read, so the documented keys can be checked against what the installer writes.
COPY installer/panel.env.example /tmp/maran-installer/panel.env.example
COPY installer/agent.env.example /tmp/maran-installer/agent.env.example
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
COPY installer/systemd/maran-host.tmpfiles.conf /tmp/maran-installer/systemd/maran-host.tmpfiles.conf
COPY installer/systemd/maran-php.tmpfiles.conf /tmp/maran-installer/systemd/maran-php.tmpfiles.conf
COPY installer/systemd/maran-agent.service /tmp/maran-installer/systemd/maran-agent.service
# Step 70's install_units renders and installs the package-install template with the units above
# (issue #93), so it must be here or the step aborts on a missing source and no suite runs.
COPY installer/systemd/maran-app-install@.service /tmp/maran-installer/systemd/maran-app-install@.service
COPY installer/systemd/maran-app-remove@.service /tmp/maran-installer/systemd/maran-app-remove@.service
COPY installer/systemd/maran-app-nodejs-install@.service /tmp/maran-installer/systemd/maran-app-nodejs-install@.service
COPY installer/systemd/maran-app-nodejs-remove@.service /tmp/maran-installer/systemd/maran-app-nodejs-remove@.service
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
# Step 66, the break-glass `maran` command, and the wrapper and libraries it installs. RUN and READ by
# docker/polygon/asserts/parts/cli-recovery.sh: the assert script drives the step's own step_cli, then
# looks at the modes it left, runs the installed wrapper as root and as nobody, and greps its text for
# the shapes that would make a root script interpret what an operator typed. The step resolves its
# payload against $SCRIPT_DIR/cli, which the assert script sets to /tmp/maran-installer; the census in
# parts/uninstaller.sh also reads the step as one of install.sh's run_step lines.
COPY installer/lib/66-cli.sh /tmp/maran-installer/lib/66-cli.sh
COPY installer/cli /tmp/maran-installer/cli
# The release signing public key: step 66 bakes it into the CLI directory and step 50 verifies a
# release against it (parts/release-layout.sh compares the installed copy with this one).
COPY installer/keys /tmp/maran-installer/keys

# Step 67, the scheduled panel backup (issue #129), and the payload it installs: the service and the
# timer (RUN through step_panel_backup and compared byte for byte with the installed copies) and the
# example configuration, which the step reads from $SCRIPT_DIR and places beside the CLI libraries.
# docker/polygon/asserts/parts/panel-backup-units.sh drives the step; absent, it aborts by name on a
# missing payload file, which is what a forgotten COPY must look like.
COPY installer/lib/67-panel-backup.sh /tmp/maran-installer/lib/67-panel-backup.sh
COPY installer/systemd/maran-panel-backup.service /tmp/maran-installer/systemd/maran-panel-backup.service
COPY installer/systemd/maran-panel-backup.timer /tmp/maran-installer/systemd/maran-panel-backup.timer
COPY installer/panel-backup.conf.example /tmp/maran-installer/panel-backup.conf.example
# The panel's update-request units (and the release-check pair, issue #516) and the step that installs them
# (issue #382). The assertions in
# docker/polygon/asserts/parts/update-request-units.sh read these files' text and render the step's
# tmpfiles snippet; absent, the part aborts by name. maran-api.service is copied above.
COPY installer/lib/69-update-request.sh /tmp/maran-installer/lib/69-update-request.sh
COPY installer/lib/69-update-schedule.sh /tmp/maran-installer/lib/69-update-schedule.sh
COPY installer/systemd/maran-update-security.service /tmp/maran-installer/systemd/maran-update-security.service
COPY installer/systemd/maran-update-security.timer /tmp/maran-installer/systemd/maran-update-security.timer
COPY installer/systemd/maran-update-request.service /tmp/maran-installer/systemd/maran-update-request.service
COPY installer/systemd/maran-update-request.path /tmp/maran-installer/systemd/maran-update-request.path
COPY installer/systemd/maran-update-check.service /tmp/maran-installer/systemd/maran-update-check.service
COPY installer/systemd/maran-update-check.path /tmp/maran-installer/systemd/maran-update-check.path
COPY installer/systemd/maran-update-request.tmpfiles.conf /tmp/maran-installer/systemd/maran-update-request.tmpfiles.conf

# Step 68, the opt-in unattended OS security updates (issue #163). The step is RUN twice by
# docker/polygon/asserts/parts/os-security-updates.sh, flag off and flag on, and it installs the
# distribution's package through step 20's pkg_install (COPY'd below with step 89). The step is also in the
# census of install.sh's run_step lines, which refuses by name a step this image does not carry.
COPY installer/lib/68-os-updates.sh /tmp/maran-installer/lib/68-os-updates.sh

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
COPY installer/lib/89-dns.sh /tmp/maran-installer/lib/89-dns.sh
COPY installer/lib/89-mail.sh /tmp/maran-installer/lib/89-mail.sh
COPY installer/systemd/maran-dns.service /tmp/maran-installer/systemd/maran-dns.service
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

# The same reason for the clock: a container boots no systemd, so the real
# `timedatectl` refuses with "System has not been booted with systemd" and the
# agent's clock reading (and the dev stack's system status page) could never be
# healthy (issue #392). The stand-in is what the adapter's path resolves to.
COPY docker/polygon/stand-ins/timedatectl-stand-in.sh /usr/bin/timedatectl
RUN chmod 755 /usr/bin/timedatectl

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
COPY agent/crates/distro/src/suse/suse_services.rs /tmp/maran-agent/crates/distro/src/suse/suse_services.rs
COPY agent/crates/distro/src/suse/suse_dns_services.rs /tmp/maran-agent/crates/distro/src/suse/suse_dns_services.rs
COPY agent/crates/distro/src/suse/suse_account_programs.rs /tmp/maran-agent/crates/distro/src/suse/suse_account_programs.rs
COPY agent/crates/agent-core/src/agent_paths.rs /tmp/maran-agent/crates/agent-core/src/agent_paths.rs
COPY agent/crates/templates/templates/vsftpd/vsftpd.conf.j2 /tmp/maran-agent/crates/templates/templates/vsftpd/vsftpd.conf.j2
COPY agent/crates/ops/src/ftps/enable_ftps.rs /tmp/maran-agent/crates/ops/src/ftps/enable_ftps.rs
# NO pam-devel on this family, and that is a decision rather than an omission: the headers are only
# for compiling docker/polygon/asserts/pam-witness.c, which asks the real libpam about the FTPS
# stack, and FTPS is not offered here (decision 222: vsftpd is not packaged for Leap 16). The witness
# is COPY'd above only because the assert script's census requires the file; it is not compiled or run
# on this family, and the assert script says so in its output.

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
# Step 41 and its payload, the rotation policy for the installer's own transcript (issue #131).
# The census above reads the step because install.sh runs it; parts/log-retention.sh RUNS it and
# hands the installed policy to the real logrotate, which is why logrotate itself is installed
# here: a policy no logrotate has parsed is a file, not a retention policy.
COPY installer/lib/41-log-retention.sh /tmp/maran-installer/lib/41-log-retention.sh
COPY installer/logrotate/maran-install /tmp/maran-installer/logrotate/maran-install
# The two nginx rotation policies parts/nginx-log-rotation.sh parses with the real logrotate (issue #490).
COPY installer/logrotate/maran-sites /tmp/maran-installer/logrotate/maran-sites
RUN zypper --non-interactive install --no-recommends logrotate && zypper --non-interactive clean --all
# 88-cron.sh is COPY'd again further down for the cron suite's own reasons; it is needed HERE,
# before the assert script runs, because the census reads it too. The same duplication
# 40-user.sh and 80-nginx.sh already carry, and for the same reason: a COPY is cheap, a step the
# census cannot see is not.
COPY installer/lib/88-cron.sh /tmp/maran-installer/lib/88-cron.sh
COPY docker/polygon/asserts/assert-installer-steps.sh /tmp/maran-installer/assert-installer-steps.sh
# The assertions themselves, one file per subject. The entry point above refuses by name when a
# part is missing, so a forgotten COPY is a named failure rather than a silently shorter suite.
COPY docker/polygon/asserts/parts /tmp/maran-installer/parts
# The shadow file, on this family, is not changed. The other RHEL-family image has to put /etc/shadow
# at 0400 because that family ships it at 0000 and an AppArmor host kernel withholds the capability
# root would need to read it; the full mechanism is written out at the `chmod 0400 /etc/shadow` block
# in alma9/suite.Dockerfile. Whether it applies here depends on this family's shipped mode, which is
# UNVERIFIED (believed 0640 root:shadow, which root reads as the OWNER with no capability). So the
# build ASSERTS the premise instead of editing a file it has no reason to edit: the owner-read bit
# must be set, or this build fails and points at that block.
RUN [ "$(( 0$(stat -c %a /etc/shadow) & 0400 ))" -ne 0 ] \
    && [ "$(stat -c %U /etc/shadow)" = "root" ] \
    || { echo "/etc/shadow on this image is not owner-readable by root ($(stat -c '%a %U:%G' /etc/shadow)): on an AppArmor host no login can be verified inside a privileged container. See the chmod 0400 /etc/shadow block in alma9/suite.Dockerfile before changing anything here." >&2; exit 1; }

# MariaDB and OpenSSH. The package list is the installer's own (`mysql_packages_for_family`), so a
# package name that stops being right on this family stops this build. UNVERIFIED on this family:
# that the `mariadb` package alone supplies `mariadb-install-db` and `mariadbd-safe` (confirm with
# `rpm -ql mariadb | grep -E 'bin/(mariadb-install-db|mariadbd-safe)'`), that the package creates the
# `mysql` user, and the openssh package names `openssh-server` and `openssh-clients`. Also
# UNVERIFIED: that `/etc/ssh/sshd_config` exists at all on this family (it may ship its default under
# /usr/etc/ssh and leave /etc/ssh/sshd_config to be created); the assertions read /etc/ssh/sshd_config
# and fail by name if it is not there, which would make that a finding for the installer's SFTP step.
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=suse; \
      . /tmp/maran-installer/lib/85-mysql.sh; \
      zypper --non-interactive install --no-recommends openssh-server openssh-clients $(mysql_packages_for_family); \
      getent passwd mysql >/dev/null || { echo "85-mysql.sh: mysql_packages_for_family produced no MariaDB SERVER package" >&2; exit 1; }; \
      install -d -o mysql -g mysql -m 0755 /run/mysqld /var/lib/mysql; \
      install -d -o mysql -g mysql -m 0750 /var/lib/mysql-files; \
      mariadb-install-db --user=mysql --datadir=/var/lib/mysql >/dev/null; \
      ssh-keygen -A; \
      install -d -m 0755 /run/sshd; \
      mariadbd-safe --skip-networking --skip-syslog & \
      for _ in $(seq 1 60); do mariadb-admin ping >/dev/null 2>&1 && break; sleep 1; done; \
      MARAN_OS_FAMILY=suse bash /tmp/maran-installer/assert-installer-steps.sh; \
      zypper --non-interactive clean --all; \
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
# this file is a copy that stops matching. /run/maran keeps the 0750
# root:<service group> that function sets: nothing here loosens it, because the PHP
# pool sockets live in /run/maran-php instead.
#
# The account's name is not written here either: step 40 reads MARAN_USER,
# MARAN_GROUP and MARAN_SERVICE_ACCOUNT_MARKER out of the environment, the way
# install.sh gives them to it, and they are lifted from install.sh — the one
# place that decides them — rather than spelled again in this file. A step file
# that cannot see them aborts by name instead of expanding an empty string into
# `install -o`.
COPY installer/lib/40-user.sh /tmp/maran-installer/lib/40-user.sh
# The PHP pool socket directory, built by systemd-tmpfiles from the REAL snippet the installer
# ships, not by a mkdir invented here. It is a sibling of /run/maran (root:root 0755) so the web
# server's user can traverse it while /run/maran stays the 0750 directory step 40 makes; this image
# used to chmod /run/maran to 0755 instead, which hid that the real mode locks nginx out of every
# pool socket. It is applied in the step below, where systemd-tmpfiles is already known to be
# present (the panel socket assertion runs it from this same image).
RUN bash -c 'set -euo pipefail; \
      MARAN_USER="$(sed -n "s/^MARAN_USER=//p" /tmp/maran-installer/install.sh)"; \
      MARAN_GROUP="$(sed -n "s/^MARAN_GROUP=//p" /tmp/maran-installer/install.sh)"; \
      MARAN_SERVICE_ACCOUNT_MARKER="$(sed -n "s/^MARAN_SERVICE_ACCOUNT_MARKER=//p" /tmp/maran-installer/install.sh | tr -d \"\\\"\")"; \
      export MARAN_USER MARAN_GROUP MARAN_SERVICE_ACCOUNT_MARKER; \
      test -n "$MARAN_USER" && test -n "$MARAN_GROUP" && test -n "$MARAN_SERVICE_ACCOUNT_MARKER"; \
      . /tmp/maran-installer/lib/40-user.sh; \
      create_service_user >/dev/null; \
      create_directory_layout; \
      systemd-tmpfiles --create /tmp/maran-installer/systemd/maran-php.tmpfiles.conf; \
      test "$(stat -c "%a %U %G" /run/maran-php)" = "755 root root"; \
      test -d /var/backups/maran' \
    && rm -f /tmp/maran-installer/lib/40-user.sh

# cronie, this family's cron. The package name is the installer's own (`cron_packages_for_family`)
# and the unit is `cron_service_name` — `cron`, not the `crond` of the RHEL family, and no alias
# bridges them. Measured: the daemon lands at /usr/sbin/cron, not /usr/sbin/crond (confirm with
# `rpm -ql cronie | grep sbin`).
#
# NO `broken_shadow` edit to the PAM stack here. The other RHEL-family image needs one because
# `pam_unix.so` answers PAM_AUTHINFO_UNAVAIL for every account inside that image; whether this family
# shows the same defect is UNVERIFIED, and an edit that switches an account check off must not be
# copied to a family where nobody has seen the need. If the cron suite goes red here on "FAILED to
# authorize user with PAM", that is the finding, and the accommodation is then a reviewed change with
# its measurement written beside it.
COPY installer/lib/88-cron.sh /tmp/maran-installer/lib/88-cron.sh
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=suse; \
      . /tmp/maran-installer/lib/88-cron.sh; \
      zypper --non-interactive install --no-recommends $(cron_packages_for_family); \
      create_cron_spool; \
      zypper --non-interactive clean --all; \
      test -x /usr/sbin/cron; \
      getent protocols ipv6-icmp >/dev/null'

# `/usr/bin/passwd`, which the agent's suspension attestation runs (`passwd -S`). On the RHEL family it
# is its OWN package and was once missing; on this family it is believed to come with `shadow`, which
# the base image carries (UNVERIFIED; confirm with `rpm -qf /usr/bin/passwd`). Nothing is installed:
# the build ASSERTS the program is there, so a base that lacks it fails here and not at exec time in a
# suite.
RUN test -x /usr/bin/passwd

# at(1), for the account-deletion leftovers suite (see the other family images for why).
# The spool directories are declared in the package's tmpfiles snippet and created by the next boot,
# which a container never has: without applying the snippet here, `atq` answers "Cannot change to
# /var/spool/atjobs" and exits 1, which the account deletion (it lists an account's at jobs) rightly
# reads as a refusal. It is applied exactly as a booted host applies it.
RUN zypper --non-interactive install --no-recommends at \
    && zypper --non-interactive clean --all \
    && systemd-tmpfiles --create /usr/lib/tmpfiles.d/at.conf \
    && test -d /var/spool/atjobs \
    && test -x /usr/bin/at && test -x /usr/bin/atq && test -x /usr/bin/atrm

# The mail spool directory, which the account-deletion suites plant a customer's mailbox in. The base
# image has no mail package, and on this family it is a mail package that brings the directory: with
# none installed neither `/var/spool/mail` (the real directory, which is what the adapter answers) nor
# `/var/mail` exists. A host that runs mail has both, `/var/mail` a link to the real one, and that is
# the layout made here so the suites meet the host they are about. UNVERIFIED: the mode 1777 is
# the one a mail package gives it; confirm with `rpm -ql postfix | grep spool/mail` in the pinned image.
RUN install -d -m 1777 /var/spool/mail \
    && ln -s spool/mail /var/mail \
    && test "$(readlink -f /var/mail)" = /var/spool/mail

# The tmpfiles snippet that builds /run/maran-host, the mount points the agent unit binds the host's
# shared temporary directories onto. The directory-layout assertion applies THIS file with the
# family's real systemd-tmpfiles and stats the result, not a mkdir invented here.

# The quota tools. The agent execs BOTH halves by absolute path — `/usr/sbin/setquota`
# to apply an account's limit and `/usr/bin/quota` to read its usage back for
# GetAccountUsage — and until this block existed the image had neither, so the
# read path's binary was declared by the distro adapter, asserted by a test that
# only compared one string to another, and present on no host anybody ran.
# `binary_paths_on_a_real_host.rs` now stats what the adapter declares, and it
# needs the real package to have something to stat.
RUN zypper --non-interactive install --no-recommends quota \
    && zypper --non-interactive clean --all \
    && test -x /usr/bin/quota

# ...and then the administrative half is replaced by a stand-in, AFTER the
# package installed the real one, so the stand-in is what wins. A container has
# no filesystem quotas, which account creation applies. The reading half is left
# real: `quota` on a filesystem without quotas prints no limit and that is a
# state the parse must handle anyway.
COPY docker/polygon/stand-ins/setquota-stand-in.sh /usr/sbin/setquota
RUN chmod 755 /usr/sbin/setquota

# BIND, from the installer's own package list (`dns_packages_for_family`, public on purpose); the
# reasoning is the same as in the other family images. UNVERIFIED: the package `bind` and the
# checker's path (measured: the checkers are in /usr/bin).
RUN bash -c 'set -euo pipefail; \
      export MARAN_OS_FAMILY=suse; export MARAN_OS_VERSION_ID=16.1; \
      . /tmp/maran-installer/lib/89-dns.sh; \
      zypper --non-interactive install --no-recommends $(dns_packages_for_family); \
      zypper --non-interactive clean --all; \
      test -x /usr/bin/named-checkzone'

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
      export MARAN_OS_FAMILY=suse; \
      . /tmp/maran-installer/lib/88-cron.sh; \
      unit="$(cron_service_name)"; \
      verify_cron_ready; \
      mkdir -p /run/polygon-units; \
      printf "inactive\n" > "/run/polygon-units/${unit}"; \
      if ( verify_cron_ready ) >/dev/null 2>/tmp/refusal; then \
        echo "88-cron.sh accepted a host whose cron is not running" >&2; exit 1; \
      fi; \
      grep -q "not running" /tmp/refusal \
        || { echo "88-cron.sh refused, but not for the reason it should have:" >&2; cat /tmp/refusal >&2; exit 1; }; \
      rm -f "/run/polygon-units/${unit}"; \
      systemctl disable "$unit"; \
      if ( verify_cron_ready ) >/dev/null 2>/tmp/refusal; then \
        echo "88-cron.sh accepted a host whose cron is not enabled at boot" >&2; exit 1; \
      fi; \
      grep -q "not enabled at boot" /tmp/refusal \
        || { echo "88-cron.sh refused, but not for the reason it should have:" >&2; cat /tmp/refusal >&2; exit 1; }; \
      systemctl enable "$unit"; \
      rm -f /tmp/refusal; \
      verify_cron_ready' \
    && rm -rf /tmp/maran-installer


# Expected docker run invocation for the polygon suites, from the repository root (the same shape the
# other family images document; the suite list is discovered by `maran polygon verify`, not written here):
# docker run --rm -v "$PWD:/maran" -w /maran/agent maran-polygon-leap16 \
#   cargo test --test sites_on_a_real_host --test php_pools_on_a_real_host \
#     --test privileges_on_a_real_host --test databases_on_a_real_host \
#     --test binary_paths_on_a_real_host \
#     -- --ignored --test-threads=1
#
# The suites that mount (SFTP, account deletion, backup, files browse) need --privileged, exactly as
# on the other families; `firewall_on_a_real_host` needs it for NET_ADMIN. `ftps_on_a_real_host` has
# nothing to run against on this family (no vsftpd), which is the agent STRUCTURE owner's question and
# is not decided in this file.
#
# And for the agent itself, which is still built on the host and mounted:
# docker run --rm -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" maran-polygon-leap16 maran-agent --socket /run/maran/agent.sock --allow-uid 0
