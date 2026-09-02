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
# What it does, and deliberately no more:
#
#   reload <service>        becomes `nginx -s reload` when an nginx master is
#                           actually running, and succeeds silently when none is.
#   enable --now <x>.mount  performs the bind mount the unit describes, AFTER
#                           checking the unit the way systemd checks it.
#   disable --now <x>.mount unmounts it again, which the account-deletion
#                           cascade needs before `userdel` removes the home.
#   anything else           succeeds silently. It never starts a service and
#                           never enables one at boot; the polygon suites assert
#                           what the agent WRITES and what the real tools make of
#                           it, not what an init system does with it afterwards.
#
# The mount arm is not "faking systemd to make a test pass" — it is here because
# two things about ops::sftp cannot be observed anywhere else:
#
#   1. A `.mount` unit's FILE NAME must be systemd's escaping of its own
#      `Where=`, or systemd refuses to load it. `AccountJail::unit_name`
#      implements that escaping, and a mistake in it shows up on a real host as
#      an SFTP login that lands in an empty directory — never in a build. The
#      check below is made with `systemd-escape`, systemd's OWN tool, so the
#      expectation does not come from the code under test.
#   2. Without the bind mount actually happening, the account's home is not
#      inside the jail, and the SFTP suite could not tell a working jail from an
#      empty one.
#
# It needs privileges a default container does not have. Run the SFTP suite with
# `docker run --privileged`; without it the mount fails, `create_sftp_user`
# returns JailFailed, and the suite fails loudly rather than passing on a jail
# that was never filled. That is the intended behaviour: see docker/README.md.
#
# The consequence is stated rather than hidden: `reload` cannot fail here, so
# SafeWriteError::ReloadFailed and its rollback are covered by the unit tests in
# ops::safe_write and not by the polygon.
set -eu

# Where the agent writes unit files, matching DistroAdapter::systemd_unit_directory
# on both families.
UNIT_DIRECTORY=/etc/systemd/system

# fail: say what systemd would have said, on stderr, and refuse.
fail() {
    echo "systemctl-stand-in: $1" >&2
    exit 1
}

# value_of: the value of `Key=` in a unit file, first occurrence only.
value_of() {
    sed -n "s/^$1=//p" "$2" | head -1
}

# mount_unit: the `enable --now <name>.mount` arm.
#
# Checked before mounted, in the order systemd checks: a unit that is not there
# is an error, and a unit whose name is not the escaping of its own Where= is
# refused at load time. Both are answered with a non-zero status, which the
# agent's config-write protocol turns into a rolled-back write and a typed
# JailFailed — the same outcome a real host would produce.
mount_unit() {
    unit="$1"
    path="${UNIT_DIRECTORY}/${unit}"

    [ -f "$path" ] || fail "unit ${unit} not found in ${UNIT_DIRECTORY}"

    what="$(value_of What "$path")"
    where="$(value_of Where "$path")"
    [ -n "$what" ] || fail "unit ${unit} has no What="
    [ -n "$where" ] || fail "unit ${unit} has no Where="

    # systemd's own escaping, from systemd's own tool. Deriving the expectation
    # any other way would let a bug in the agent's escaping agree with a bug in
    # the check.
    expected="$(systemd-escape --path --suffix=mount "$where")"
    [ "$expected" = "$unit" ] || \
        fail "unit ${unit} does not match the escaping of its own Where=${where} (${expected}); systemd would refuse to load it"

    [ -d "$what" ] || fail "What=${what} is not a directory"
    [ -d "$where" ] || fail "Where=${where} is not a directory"

    # Already mounted is success: the unit is enabled on every SFTP user this
    # account gets, and the second one must not fail on the first one's work.
    if grep -qE "[[:space:]]${where}[[:space:]]" /proc/self/mountinfo; then
        exit 0
    fi

    exec mount --bind "$what" "$where"
}

# unmount_unit: the `disable --now <name>.mount` arm.
#
# The mirror of mount_unit, and here for the same reason: the account-deletion
# cascade takes an account's jail down before `userdel` removes the home that is
# bind-mounted into it, and the unmount is NOT best-effort. Without this arm the
# stand-in would fall through to "succeed silently", the mount would survive, and
# the cascade's next step — a plain `rmdir` of the mount point, which is what
# stops a still-mounted jail from being deleted recursively — would fail with
# EBUSY. The polygon suite would then go red for a reason that has nothing to do
# with the agent.
#
# systemd refuses to disable a unit it has no file for, and so does this. Not
# mounted is success: the cascade is idempotent, and a second deletion must
# converge rather than fail on its own previous work.
unmount_unit() {
    unit="$1"
    path="${UNIT_DIRECTORY}/${unit}"

    [ -f "$path" ] || fail "unit ${unit} not found in ${UNIT_DIRECTORY}"

    where="$(value_of Where "$path")"
    [ -n "$where" ] || fail "unit ${unit} has no Where="

    grep -qE "[[:space:]]${where}[[:space:]]" /proc/self/mountinfo || exit 0

    exec umount "$where"
}

case "${1:-}" in
    enable)
        # `enable --now <unit>`: the only form the agent uses.
        shift
        [ "${1:-}" = "--now" ] && shift
        case "${1:-}" in
            *.mount) mount_unit "$1" ;;
            *) exit 0 ;;
        esac
        ;;
    disable)
        # `disable --now <unit>`: the only form the agent uses.
        shift
        [ "${1:-}" = "--now" ] && shift
        case "${1:-}" in
            *.mount) unmount_unit "$1" ;;
            *) exit 0 ;;
        esac
        ;;
    reload) ;;
    *) exit 0 ;;
esac

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
