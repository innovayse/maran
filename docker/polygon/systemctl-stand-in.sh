#!/bin/sh
# A stand-in for systemd's client, installed at /usr/bin/systemctl in the polygon
# images ONLY. Production never uses Docker (spec §2) and never sees this file.
#
# Why it exists: a container has no init system, so the `systemctl reload nginx`
# that ops::sites::write_vhost runs — through DistroAdapter::service_manager(),
# which is /usr/bin/systemctl on both families — has nothing to talk to. Without
# a stand-in every config write in the polygon would fail at the reload step and
# roll back, and the thing the polygon exists to exercise (a real `nginx -t`
# against a real configuration tree) would never be reached.
#
# What it does, and deliberately no more: a reload becomes `nginx -s reload` when
# an nginx master is actually running, and succeeds silently when none is. It
# never starts anything, never enables anything, and answers every other
# subcommand with success — the polygon tests assert what the agent WRITES and
# what nginx makes of it, not what an init system does with it afterwards.
#
# The consequence is stated rather than hidden: the reload half of the
# config-write protocol cannot fail here, so SafeWriteError::ReloadFailed and its
# rollback are covered by the unit tests in ops::safe_write and not by the
# polygon.
set -eu

if [ "${1:-}" != "reload" ]; then
    exit 0
fi

# The pid FILE is not evidence of a running master: both families' packages ship
# an empty /run/nginx.pid, and `nginx -s reload` against it fails with "invalid
# PID number" — which the agent would correctly report as a failed reload, and
# which would then roll back every write the polygon makes.
pid=$(cat /run/nginx.pid 2>/dev/null || true)
case "$pid" in
    '' | *[!0-9]*) exit 0 ;;
esac

if [ ! -d "/proc/$pid" ]; then
    exit 0
fi

exec /usr/sbin/nginx -s reload
