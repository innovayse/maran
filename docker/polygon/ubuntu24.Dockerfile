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

# The agent's own directories, and the one line of host configuration the
# installer performs on a real server: nginx includes the directory the agent
# owns, and never the distribution's sites-enabled (spec §9). Without it
# `nginx -t` parses a tree the agent's vhosts are not in and passes whatever
# they say.
RUN mkdir -p /run/maran /run/maran/php /etc/maran/nginx/sites /etc/maran/certificates \
    && chmod 755 /run/maran \
    && sed -i '0,/^http {/s//http {\n    include \/etc\/maran\/nginx\/sites\/*.conf;/' /etc/nginx/nginx.conf \
    && grep -q '/etc/maran/nginx/sites' /etc/nginx/nginx.conf \
    && nginx -t

# A container has no init system, so the reload half of the config-write protocol
# needs something to talk to. The stand-in explains itself and its limits.
COPY systemctl-stand-in.sh /usr/bin/systemctl
# And a container has no filesystem quotas, which account creation applies.
COPY setquota-stand-in.sh /usr/local/bin/setquota
RUN chmod 755 /usr/bin/systemctl /usr/local/bin/setquota

# Expected docker run invocation for the polygon suites, from the repository root:
# docker run --rm -v "$PWD:/maran" -w /maran/agent maran-polygon-ubuntu24 \
#   cargo test --test sites_on_a_real_host --test php_pools_on_a_real_host \
#     --test privileges_on_a_real_host -- --ignored --test-threads=1
#
# And for the agent itself, which is still built on the host and mounted:
# docker run --rm -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" maran-polygon-ubuntu24 maran-agent --socket /run/maran/agent.sock --allow-uid 0
