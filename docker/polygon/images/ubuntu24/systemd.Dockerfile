# Development-only test container that boots real systemd as PID 1.
# Production never uses Docker — see spec §2.
#
# Why this image exists, and what it can prove that ubuntu24.Dockerfile / alma9.Dockerfile
# CANNOT: those two images run no init at all — CMD is the test binary itself — so
# `systemctl-stand-in.sh` had to exist (docker/polygon/stand-ins/systemctl-stand-in.sh), and its own
# header says "starts or stops NOTHING" and that enablement-at-boot is a claim "only a real
# host settles". This image is the attempt at that real host, inside what a container can
# still offer: a real systemd, PID 1, with cgroups it actually manages.
#
# What it does NOT prove, stated here and repeated in docker/README.md because the risk of
# succeeding at this is someone reading it as "systemd now proven in production":
#   - no bootloader, no initramfs, no real device enumeration — systemd starts already
#     inside a running Linux kernel that is THIS MACHINE'S kernel, not the operator's;
#   - the kernel is shared with the host; systemd here get cgroups, not a new kernel;
#   - a container "restart" (docker restart) is not a machine reboot — see run-systemd.sh
#     for exactly what stands in for one and what it does not.
#
# EVERYTHING FETCHED HERE IS PINNED AND VERIFIED, matching ubuntu24.Dockerfile's posture.
#
# Digest-pinned for the same reason as the other two polygon images: a moving point release
# would make this Dockerfile's meaning drift with no diff to review.
FROM ubuntu:24.04@sha256:33ceb71981b602c1a7443a53469e4dba065f7503eab3078a2d7a57a2ab987517

# noninteractive: no debconf prompt can block a build that runs with no attached tty.
ENV DEBIAN_FRONTEND=noninteractive

# systemd, systemd-sysv (so /sbin/init resolves to systemd rather than the SysV
# stand-in), and a real web server (nginx) plus a trivial static page — the unit
# this image starts must SERVE, per the plan, not merely report itself active.
# dbus is systemd's own IPC and several unit types silently no-op without it.
RUN apt-get update && apt-get install -y --no-install-recommends \
        systemd \
        systemd-sysv \
        dbus \
        nginx-light \
        curl \
        iproute2 \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Mask units that a container's fake hardware makes fail loudly rather than serve any
# purpose here: none of these are anything the polygon exercises, and a failed unit at
# boot would make every later "the unit came up clean" observation ambiguous — a reader
# would have to first prove the failure they are LOOKING at is not one of these.
RUN systemctl mask \
        systemd-udevd.service \
        systemd-udevd-kernel.socket \
        systemd-udevd-control.socket \
        systemd-modules-load.service \
        sys-kernel-debug.mount \
        sys-kernel-tracing.mount \
    || true

# The one unit this image exists to drive through a real systemd: nginx.service,
# shipped by the nginx-light package, already wired to `systemctl start|stop|enable`.
# Nothing further is defined here — the point is the STOCK unit, not a bespoke one,
# because the bespoke unit is exactly what the agent's own polygon Dockerfiles already
# validate against (safe_write + nginx -t). This image is answering a different
# question: does `systemctl start nginx` on a container's systemd make nginx SERVE.

STOPSIGNAL SIGRTMIN+3

# systemd as PID 1. `run-systemd.sh` is what `docker run` actually invokes in
# docker/README.md's commands; ENTRYPOINT here is /sbin/init directly so that a bare
# `docker run --privileged this-image` is already a real boot, with no wrapper needed
# to make that claim true.
ENTRYPOINT ["/sbin/init"]
