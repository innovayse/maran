# A FRESH SUSE-FAMILY SERVER with real systemd as PID 1, so the documented install path can be
# executed end to end. One template for every SUSE-family version; the version is the BASE_IMAGE
# build argument, read from `<version>/base.txt` by scripts/lib/install-polygon.sh. Production never
# uses Docker — see spec §2.
#
# Read docker/polygon/images/README.md first: it carries what these images prove, what they cannot,
# and why they contain nothing Maran needs.
#
# NOTHING in this file about openSUSE was measured by us. Every package name, unit name and path
# below is UNVERIFIED: read from the distribution's documentation and confirmed only by this build
# and the run that follows it. A name that is wrong stops the build at the RUN that uses it, which is
# the intended failure; the first run of this image is what turns each UNVERIFIED into a fact. The
# commands that settle them, in the pinned image: `zypper --non-interactive search -x systemd sudo
# ca-certificates procps curl firewalld`, `ls -l /usr/lib/systemd/systemd /sbin/init`,
# `systemctl list-unit-files firewalld.service`.
#
# Why a family template of its own rather than a shared one with the RHEL family: the three families
# differ in exactly the things this polygon exists to exercise — the package manager (zypper, whose
# exit statuses and prompts are not dnf's), the unit names (`cron.service`, `sshd.service`), the
# default mandatory access control (SELinux enforcing, as on the RHEL family, but a different
# policy), and above all the distribution's own firewall: firewalld is enabled by default on this
# family, and the installer must take it away and mask it.
ARG BASE_IMAGE
FROM ${BASE_IMAGE}

# `--no-recommends`: a throwaway image installs what is named and nothing it suggests.
#
# `curl`, `ca-certificates` and `sudo` are the reader's starting point, because the documented first
# command is `curl -sSL https://get.maran.innovayse.com | sudo bash`. `procps` is for the polygon's
# OWN verdict (`ps`), not on Maran's behalf: the installer names its own copy in
# `signalling_packages_for_family` and must still install it.
#
# `systemd` brings the init the whole image exists for. The container base ships none.
#
# `firewalld` is present and ENABLED on purpose, and it is the one thing here that is the
# distribution's default rather than the reader's starting point. A fresh Leap server has it on,
# the installer's job is to stop, disable and MASK it (decision 220: the agent's nftables is the
# only firewall owner), and a polygon without it could not tell a masked firewalld from an absent
# one. It is not something Maran needs; it is something Maran must deal with.
#
# Deliberately ABSENT: everything Maran needs — no PostgreSQL, no nginx, no openssh-server, no .NET,
# no python, no nftables, no cron, no MariaDB, no BIND.
RUN zypper --non-interactive install --no-recommends \
        systemd \
        sudo \
        ca-certificates \
        curl \
        procps \
        firewalld \
    && zypper --non-interactive clean --all

# firewalld enabled the way a stock server has it, and then LOOKED AT. `systemctl enable` works
# without a running init (it writes the .wants link), and `is-enabled` reads that link back, so this
# precondition is observed and not assumed: if the unit were absent or enabling it did nothing, the
# installer's later mask would be tested against a host that never had a firewall to retire.
RUN systemctl enable firewalld.service \
    && [ "$(systemctl is-enabled firewalld.service)" = "enabled" ]

# Mask units whose failure a container's fake hardware guarantees. Same reasoning as the other
# family templates: this polygon's verdict rests on "did anything fail?". The list is theirs, and it
# is UNVERIFIED that every name exists on this family; `|| true` covers a unit that does not, and the
# boot-state wait in scripts/lib/install-polygon.sh accepts `degraded`.
RUN systemctl mask \
        systemd-udevd.service \
        systemd-udevd-kernel.socket \
        systemd-udevd-control.socket \
        systemd-modules-load.service \
        sys-kernel-debug.mount \
        sys-kernel-tracing.mount \
    || true

STOPSIGNAL SIGRTMIN+3

# systemd's own binary, not `/sbin/init`: whether this family's `systemd` package ships the `/sbin/init`
# compatibility link (or leaves it to a separate package) is UNVERIFIED, whereas
# `/usr/lib/systemd/systemd` is where every systemd distribution installs the manager.
ENTRYPOINT ["/usr/lib/systemd/systemd"]
