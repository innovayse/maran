# A FRESH DEBIAN-FAMILY SERVER with real systemd as PID 1, so the documented install path can be
# executed end to end. One template for every Debian-family version; the version is the BASE_IMAGE
# build argument, read from `<version>/base.txt` by scripts/lib/install-polygon.sh.
# Production never uses Docker — see spec §2.
#
# Read docker/polygon/images/README.md first: it carries what these images prove, what they cannot,
# and why they contain nothing Maran needs.
#
# One template rather than one Dockerfile per version, deliberately. The body below is the same on
# Ubuntu 22.04 and Debian 13 — only the base differs — and five copies of it would be five places for
# a change to be applied four times. The per-version FACT (which digest is under test) lives in that
# version's own folder; the per-family LOGIC lives here, once.
ARG BASE_IMAGE
FROM ${BASE_IMAGE}

# noninteractive: no debconf prompt can block a build that runs with no attached tty.
ENV DEBIAN_FRONTEND=noninteractive

# systemd-sysv so /sbin/init resolves to systemd rather than the SysV stand-in; dbus because
# systemd's own IPC is what several unit types need to be more than a silent no-op.
#
# Deliberately ABSENT: everything Maran needs — no PostgreSQL, no nginx, no openssh-server, no .NET,
# no python. The installer must bring its own, exactly as on the operator's fresh server, and an
# image that pre-installed them would answer an easier question than the one asked. `curl`,
# `ca-certificates` and `sudo` are the READER's starting point, because the documented first command
# is `curl -sSL https://get.maran.innovayse.com | sudo bash`.
RUN apt-get update && apt-get install -y --no-install-recommends \
        systemd \
        systemd-sysv \
        dbus \
        sudo \
        curl \
        ca-certificates \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Mask units whose failure a container's fake hardware guarantees and whose purpose here is nil.
# Not cosmetic: this polygon's verdict rests on "did anything fail?", so a boot that fails for
# reasons unrelated to Maran would make every later observation ambiguous.
RUN systemctl mask \
        systemd-udevd.service \
        systemd-udevd-kernel.socket \
        systemd-udevd-control.socket \
        systemd-modules-load.service \
        sys-kernel-debug.mount \
        sys-kernel-tracing.mount \
    || true

STOPSIGNAL SIGRTMIN+3

# systemd as PID 1, with no wrapper, so a bare `docker run --privileged` is already a real boot
# rather than a script pretending to be one.
ENTRYPOINT ["/sbin/init"]
