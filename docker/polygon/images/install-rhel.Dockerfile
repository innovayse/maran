# A FRESH RHEL-FAMILY SERVER with real systemd as PID 1, so the documented install path can be
# executed end to end. One template for every RHEL-family version; the version is the BASE_IMAGE
# build argument, read from `<version>/base.txt` by scripts/lib/install-polygon.sh.
# Production never uses Docker — see spec §2.
#
# Read docker/polygon/images/README.md first: it carries what these images prove, what they cannot,
# and why they contain nothing Maran needs.
#
# Why a family template of its own rather than a shared one with Debian: the two families differ in
# exactly the things this polygon exists to exercise — the package manager, the unit names, SELinux,
# and the packaging splits 20-dependencies.sh documents (`passwd` separate from `shadow-utils`,
# `procps-ng` rather than `procps`, `cronie` rather than `cron`). The `passwd` split alone once made
# every account suspension fail on this family.
ARG BASE_IMAGE
FROM ${BASE_IMAGE}

# `--nodocs`: nothing in a throwaway image is ever read by a person.
#
# `curl` is NOT named here even though the documented command needs it, and the omission is a
# measured fact rather than an oversight: these bases ship `curl-minimal`, which PROVIDES
# /usr/bin/curl and CONFLICTS with the full `curl` package, so naming it fails the build outright
# ("curl-minimal conflicts with curl"). The reader's starting point is satisfied by the base image.
# The same conflict is a real installer defect on this family — see issue #46.
#
# procps-ng is for the polygon's OWN verdict (`ps`), not on Maran's behalf: the installer names its
# own copy in `signalling_packages_for_family` and must still install it.
#
# Deliberately ABSENT: everything Maran needs — no PostgreSQL, no nginx, no openssh-server, no .NET,
# no python.
RUN dnf install -y --nodocs \
        systemd \
        sudo \
        ca-certificates \
        procps-ng \
    && dnf clean all

# Mask units whose failure a container's fake hardware guarantees. Same reasoning as the Debian
# template: this polygon's verdict rests on "did anything fail?".
RUN systemctl mask \
        systemd-udevd.service \
        systemd-udevd-kernel.socket \
        systemd-udevd-control.socket \
        systemd-modules-load.service \
        sys-kernel-debug.mount \
        sys-kernel-tracing.mount \
    || true

STOPSIGNAL SIGRTMIN+3

ENTRYPOINT ["/sbin/init"]
