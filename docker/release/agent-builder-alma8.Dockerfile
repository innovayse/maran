# Builds the RELEASE agent binary against the OLDEST glibc in Maran's supported matrix, so that one
# artifact runs on every distribution the installer's preflight accepts.
#
# Why this image exists. `cargo build --release` links glibc dynamically, and a dynamically linked
# glibc binary is forward compatible and never backward compatible: built on Ubuntu 24.04 (glibc
# 2.39) it refuses to start on anything older. Measured on AlmaLinux 9 (issue #48):
#
#     maran-agent: /lib64/libc.so.6: version `GLIBC_2.38' not found
#     maran-agent: /lib64/libc.so.6: version `GLIBC_2.39' not found
#
# installer/lib/10-preflight.sh accepts eight distributions; four of them carry a glibc older than
# the release host's, so the shipped agent could not execute on half the matrix. The panel is not
# affected — its self-contained .NET publish was measured starting on glibc 2.34 in the same
# container — so this image is about the Rust artifact alone.
#
# AlmaLinux 8 is the floor because it and Rocky 8 carry glibc 2.28, the oldest in the matrix:
#
#     AlmaLinux 8 / Rocky 8   2.28   <- built here
#     AlmaLinux 9 / Rocky 9   2.34
#     Ubuntu 22.04            2.35
#     Debian 12               2.36
#     Ubuntu 24.04 / Alma 10  2.39
#     Debian 13               2.41
#
# The floor was 2.34 when this file was written and moved down to 2.28 when the matrix grew to the
# EL8 rebuilds, which are what much of the hosting fleet still runs. It was measured that the panel
# itself is fine there — a self-contained .NET 9 publish loads on glibc 2.28 — so the agent was the
# only artifact standing between Maran and that half of the fleet. CentOS 7 (glibc 2.17) is NOT in
# the matrix and cannot be: the panel fails there on libstdc++, not on glibc
# (`GLIBCXX_3.4.20 not found`), and it is EOL.
#
# Why NOT a static musl build, which would sidestep glibc entirely:
# agent/crates/agent-core/src/privs/account_ids.rs calls `getpwnam_r`, deliberately, instead of
# parsing /etc/passwd or running `id`. glibc's NSS cannot be linked statically, so a musl-static
# agent would stop resolving accounts on any host that answers through SSSD or LDAP — the normal
# arrangement on RHEL in an enterprise. That trades a loud failure on four distributions for a
# silent one on an unknown subset of all eight.
#
# Digest-pinned to the same AlmaLinux 8 image the polygon uses (docker/polygon/images/alma8/base.txt),
# so the distribution this builds against and the one the install polygon tests on cannot drift apart.
FROM almalinux:8@sha256:9f355ae942d6a6c0561f0771dc053a2cfae9580fc45fa4252756db7c7e80c09f

# gcc and the kernel headers: the linker Cargo invokes, and what the `cc` crate needs. `--nodocs`
# for the same reason as the polygon images — nothing here is ever read by a person.
RUN dnf install -y --nodocs gcc make findutils unzip && dnf clean all

# protoc, because agent/crates/agent/build.rs compiles the shared contract from proto/ at build time
# (rules/proto.md: the generated code is never committed, so every build needs the compiler).
#
# Fetched from the official release and VERIFIED by checksum rather than taken from a distribution
# repository, which is the posture every other pinned image in this tree holds: a dnf package would
# make the contract compiler a function of AlmaLinux's repository state on the day of the release,
# and EPEL would add a whole repository to trust for one binary.
# Two architectures, each with its OWN checksum, selected by the architecture the build is running
# on. The release's file names do not match `uname -m` on ARM — protobuf spells it `aarch_64` — so
# the mapping is explicit rather than interpolated, and an architecture this image has no checksum
# for fails the build instead of downloading something unverified.
ARG PROTOC_VERSION=36.0
ARG PROTOC_SHA256_X86_64=bc8211ce760bd43ee21ddc145d6d9dbaeeabae205267a79d9054a240e367d4b4
ARG PROTOC_SHA256_AARCH64=4a00ec5e256d20a3deadd9e77d56da0ac04c72367c3c959f6d08e110a368400a
RUN set -eu; \
    case "$(uname -m)" in \
      x86_64)  protoc_arch=x86_64;   protoc_sha="${PROTOC_SHA256_X86_64}" ;; \
      aarch64) protoc_arch=aarch_64; protoc_sha="${PROTOC_SHA256_AARCH64}" ;; \
      *) echo "agent-builder: no pinned protoc for $(uname -m)" >&2; exit 1 ;; \
    esac; \
    curl --proto '=https' --tlsv1.2 -sSLo /tmp/protoc.zip \
      "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-${protoc_arch}.zip"; \
    echo "${protoc_sha}  /tmp/protoc.zip" | sha256sum -c -; \
    unzip -q -o /tmp/protoc.zip -d /usr/local bin/protoc 'include/*'; \
    chmod a+rx /usr/local/bin/protoc; \
    rm -f /tmp/protoc.zip; \
    protoc --version

# The toolchain, pinned. An unpinned rustup would make the artifact's compiler a function of the
# day it was built, which is the same class of drift this whole image exists to remove.
# --no-modify-path: the PATH is set below explicitly rather than through a profile script no
# non-interactive build would source.
# RUSTUP_HOME outside /root, which is 0700 on this image: the release runs the build as the caller's
# unprivileged uid, and that uid cannot TRAVERSE /root however permissive the toolchain files
# themselves are made — `cargo` then fails with "could not execute process `rustc -vV`:
# Permission denied" rather than anything naming the real obstacle.
ARG RUST_VERSION=1.98.0
ENV RUSTUP_HOME=/opt/rust
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
      | CARGO_HOME=/opt/cargo sh -s -- -y --no-modify-path --profile minimal \
        --default-toolchain "${RUST_VERSION}"

# The toolchain's REAL binaries are put on PATH, not rustup's shims in /opt/cargo/bin, and that
# is deliberate rather than tidier: the release runs this container as the caller's unprivileged
# uid so it cannot leave root-owned files in the staging directory, and every rustup shim insists on
# creating and writing RUSTUP_HOME before it will exec anything —
#
#     error: could not create home directory: '/opt/rust': Permission denied (os error 13)
#
# The cargo inside the toolchain needs no rustup at all, so pointing PATH at it removes the write
# instead of granting it. The toolchain directory name embeds RUST_VERSION and the host triple, so
# it is derived from the ARG rather than spelled twice.
# The toolchain directory name embeds the HOST TRIPLE, which differs per architecture, so the real
# binaries are symlinked onto PATH rather than named in an ENV that could only be right for one of
# them. `uname -m` already spells the triple's first component the way rustup does on both.
RUN chmod -R a+rX /opt/rust /opt/cargo \
    && ln -sf "/opt/rust/toolchains/${RUST_VERSION}-$(uname -m)-unknown-linux-gnu/bin/"* /usr/local/bin/

# The guard on that derivation: a RUST_VERSION or an architecture whose toolchain directory is not
# where the symlink pointed would otherwise produce an image where `cargo` is simply missing,
# discovered in the middle of a release.
RUN cargo --version && rustc --version

# No ENTRYPOINT: scripts/lib/release-bundle.sh supplies the exact cargo invocation, so the command
# under test lives beside the rest of the release logic rather than being hidden in an image layer.
