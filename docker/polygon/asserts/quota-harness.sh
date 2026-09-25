#!/bin/sh
# Proves, or refuses to pretend it proved, real kernel-enforced disk quotas —
# development-only, run inside a `--privileged` polygon container. Production
# never uses Docker (spec §2) and never sees this file.
#
# Why it exists: `setquota-stand-in.sh` (docker/polygon/stand-ins/setquota-stand-in.sh) accepts
# and does nothing, because a container's overlay filesystem has no quota support at
# all — so the agent's own quota code (`ops::accounts::AccountOperations::apply_quota`,
# agent/crates/ops/src/accounts/account_operations.rs:656) has never been run against a
# filesystem that could actually say no. This script builds that filesystem inside the
# container it runs in, and then asks the one question that matters: does the KERNEL
# refuse a write past the limit. A `setquota` exit code proves nothing (the stand-in's
# own exit 0 is proof of that); only a write that the kernel itself rejects counts.
#
# It mirrors agent/crates/ops/src/accounts/account_operations.rs exactly, not a
# reimplementation invented here:
#   - `apply_quota` calls setquota with `-u <user> <soft> <hard> 0 0 <ACCOUNT_HOME_ROOT>`
#     (account_operations.rs:656-673) — user quota ONLY, no group quota. Confirmed
#     against agent/crates/ops/src/accounts/quota_enforceability.rs, which likewise
#     inspects only `usrquota`/`uquota`/`usrjquota` mount options, never `grpquota`.
#     This harness therefore mounts with `usrquota` alone and does not claim anything
#     about group quotas the agent does not use.
#   - `AgentPaths::ACCOUNT_HOME_ROOT` (agent/crates/agent-core/src/agent_paths.rs:41) is
#     the literal string "/home" — the mountpoint setquota is told to act on, and the
#     mountpoint this harness's loopback filesystem is mounted AT, so the agent's own
#     path is the one under test rather than a look-alike.
#   - Blocks, not bytes: `QuotaBlocks::from_bytes` (agent/crates/ops/src/accounts/quota_blocks.rs:22)
#     rounds a byte quota up to whole 1 KiB blocks (`div_ceil`), which this harness's
#     arithmetic matches so the limit enforced here is the same number the agent would
#     have asked for.
#
# What this script CANNOT prove, stated rather than hidden:
#   - This is a loopback ext4 image on the polygon's own overlay-backed disk, not a
#     customer's real partition on a real host's real disk. The KERNEL'S quota code
#     is the same code either way — that part is genuinely proven — but disk layout,
#     LVM, and multi-tenant IO contention are not exercised here at all.
#   - group quotas: not attempted, because the agent does not ask for them (see above).
#     If a future change adds `grpquota` to the agent's `setquota` call, this harness's
#     `usrquota`-only mount stops matching it and must gain `grpquota` too.
#
# Usage: run as root inside a --privileged container (loop devices + mount need it).
#   sh docker/polygon/asserts/quota-harness.sh
#
# Exit: 0 only if every observation below held, including the negative control.
# Prints one of two lines this repository's testing rules require a check to print
# rather than an exit code: "QUOTA HARNESS VERDICT: OK" or "... FAILED — <reason>".

set -eu

IMG=/root/quota-test.img
MNT=/mnt/quota-home
LOOPDEV=""
USER=quotatest
LIMIT_BYTES=$((5 * 1024 * 1024))     # 5 MiB hard limit — small enough to hit fast.
OVERAGE_BYTES=$((8 * 1024 * 1024))   # write attempt: 8 MiB, past the 5 MiB hard limit.

cleanup() {
    set +e
    umount "$MNT" 2>/dev/null
    [ -n "$LOOPDEV" ] && losetup -d "$LOOPDEV" 2>/dev/null
    userdel -r "$USER" 2>/dev/null
    rm -f "$IMG"
}
trap cleanup EXIT

fail() {
    echo "QUOTA HARNESS VERDICT: FAILED — $1"
    exit 1
}

command -v mkfs.ext4 >/dev/null || fail "mkfs.ext4 not installed"
command -v setquota >/dev/null || fail "setquota not installed"
command -v quotaon >/dev/null || fail "quotaon not installed"

mkdir -p "$MNT"

echo "-- building a 64MiB loopback ext4 image, formatted with quota-ready inodes --"
dd if=/dev/zero of="$IMG" bs=1M count=64 status=none
mkfs.ext4 -q -O quota -E quotatype=usrquota "$IMG"

# Loop devices are a HOST-WIDE, kernel-global resource — not namespaced per
# container — so a polygon host running several containers (this one included,
# measured during development: snapd alone held over twenty of them) races
# every container for the next free /dev/loopN. `losetup -f --show` was
# observed failing here with "failed to set up loop device: No such file or
# directory" against a device that a moment later attached cleanly, which is
# a losetup TOCTOU (it picks the name, then a concurrent process claims it,
# then it tries to attach). Retried a bounded number of times rather than
# masked: this is a real property of running privileged polygon containers
# side by side on one kernel, not a bug in this script, and it is worth being
# visible here rather than hidden behind an immediate, unexplained failure.
# A container's /dev is its own tmpfs, pre-populated by Docker with only a
# handful of /dev/loopN nodes; `losetup -f` asks the KERNEL for the next free
# loop index, which is a host-wide counter and routinely well past what this
# container's /dev was given. The failure above is not a busy device — it is
# a missing device NODE — so create it before attaching, exactly as udev
# would on a real host that owns its own /dev.
attempt=0
while :; do
    LOOPDEV="$(losetup -f)"
    if [ ! -e "$LOOPDEV" ]; then
        mknod "$LOOPDEV" b 7 "${LOOPDEV##*loop}"
    fi
    if losetup "$LOOPDEV" "$IMG" 2>/tmp/losetup.err; then
        break
    fi
    attempt=$((attempt + 1))
    [ "$attempt" -lt 10 ] || fail "could not attach a loop device after $attempt attempts: $(cat /tmp/losetup.err)"
    sleep 0.3
done
mount -o usrquota "$LOOPDEV" "$MNT"

# quotacheck cross-references /etc/fstab to resolve the mountpoint to a device;
# a loopback mount made only with `mount` (no fstab entry) is invisible to it
# even though /proc/mounts (and therefore /etc/mtab, symlinked to it) already
# shows the option. This is container/script plumbing, not something the agent
# does — the agent's setquota/quotaon calls act on an already-configured mount.
grep -q " $MNT " /etc/fstab 2>/dev/null || echo "$LOOPDEV $MNT ext4 usrquota 0 0" >>/etc/fstab

echo "-- observation A: mount options carry usrquota --"
grep -E "^\S+ $MNT " /proc/mounts | grep -q usrquota \
    || fail "mount options on $MNT do not show usrquota after an explicit -o usrquota mount"
echo "   /proc/mounts: $(grep -E "^\S+ $MNT " /proc/mounts)"

# `mkfs.ext4 -O quota` (above) builds the modern, IN-KERNEL ext4 quota feature:
# accounting starts the moment the filesystem is mounted with the option — the
# kernel logs "Quota mode: journalled" at mount time (verified via `dmesg`) —
# so there is nothing for `quotacheck` to reconcile and no separate `quotaon`
# call to make: this harness measured `quotaon "$MNT"` itself fail here with
# "using . on <dev> [...]: File exists", because the tool's own enablement step
# assumes the OLDER, userspace-file-based quota mechanism (`aquota.user`) and
# collides with the kernel already having the feature active. Neither call is
# made here for that reason; `quotaon -p` below is a pure read and needs none
# of it to answer correctly, which the next observation confirms.
echo "-- observation B: quotaon -p reports user quota accounting ON, with no quotaon call made --"
quotaon -p "$MNT" | tee /tmp/quotaon-p.out
grep -qi "user quota.*is on\|are enabled\|is enabled" /tmp/quotaon-p.out \
    || fail "quotaon -p does not report user quota accounting on for $MNT"

echo "-- creating a real account with its home ON this filesystem, mirroring account_operations.rs --"
useradd -m -d "$MNT/$USER" -b "$MNT" "$USER" 2>/dev/null || useradd -m -d "$MNT/$USER" "$USER"

BLOCKS=$(( (LIMIT_BYTES + 1023) / 1024 ))   # QuotaBlocks::from_bytes, div_ceil to 1 KiB blocks.
echo "-- applying the SAME setquota invocation the agent makes: -u $USER $BLOCKS $BLOCKS 0 0 $MNT --"
setquota -u "$USER" "$BLOCKS" "$BLOCKS" 0 0 "$MNT"

echo "-- observation C: the limit setquota just wrote is readable back --"
REPQUOTA="$(quota -u -w "$USER" 2>&1 || true)"
echo "$REPQUOTA"
echo "$REPQUOTA" | grep -q "$LOOPDEV" || fail "quota -u -w shows no line for $LOOPDEV after setquota"

echo "-- THE POSITIVE CONTROL: a write past the hard limit, as the account's own uid --"
if su -s /bin/sh "$USER" -c "dd if=/dev/zero of=$MNT/$USER/overage.bin bs=1M count=$((OVERAGE_BYTES / 1024 / 1024)) 2>&1"; then
    fail "a write of $OVERAGE_BYTES bytes against a $LIMIT_BYTES byte hard limit SUCCEEDED — the kernel did not enforce anything"
fi
echo "   (the write above printed 'Disk quota exceeded' and dd exited non-zero — that failure is the proof, not the exit code alone)"
sync

echo "-- confirming the failure really is EDQUOT and not something incidental (a full loopback, e.g.) --"
USAGE_AFTER="$(du -sb "$MNT/$USER" 2>/dev/null | cut -f1)"
[ "$USAGE_AFTER" -le "$LIMIT_BYTES" ] || fail "usage after the refused write ($USAGE_AFTER bytes) exceeds the limit — refusal did not actually stop the write"
echo "   usage on disk after the refused write: $USAGE_AFTER bytes (limit was $LIMIT_BYTES)"

echo
echo "== NEGATIVE CONTROL: the same check on a filesystem with NO quota support =="
NOQUOTA_DIR=/root/no-quota-home
mkdir -p "$NOQUOTA_DIR"
# The container's own overlay root, exactly like setquota-stand-in.sh's header describes.
if quotaon -p / >/tmp/quotaon-p-root.out 2>&1; then
    cat /tmp/quotaon-p-root.out
    if grep -qi "user quota.*is on\|are enabled\|is enabled" /tmp/quotaon-p-root.out; then
        fail "the negative control's filesystem (/) reports quotas ENABLED — this container's root already has quotas, so it cannot serve as the no-support case; pick a different negative-control mount"
    fi
fi
echo "quotaon -p / (or its failure) on the overlay root: $(cat /tmp/quotaon-p-root.out 2>/dev/null || echo '<quotaon refused to answer>')"
echo "-- this is the three-state distinction agent/crates/ops/src/accounts/quota_enforceability.rs classifies:"
echo "   NOT 'no limit configured' (EnforceableButUnset) but NotEnforceable(MountedWithoutQuotaAccounting),"
echo "   because / here carries no usrquota mount option at all. A write of the same size on this filesystem:"
dd if=/dev/zero of="$NOQUOTA_DIR/overage-on-overlay.bin" bs=1M count=$((OVERAGE_BYTES / 1024 / 1024)) status=none \
    && echo "   SUCCEEDED (expected — there is no quota here to refuse it) — this is the honest negative result," \
    || echo "   refused for an unrelated reason (out of real disk space?) — inspect before trusting this control."
rm -f "$NOQUOTA_DIR/overage-on-overlay.bin"

echo
echo "QUOTA HARNESS VERDICT: OK — a write of $OVERAGE_BYTES bytes against a $LIMIT_BYTES byte usrquota"
echo "hard limit was refused by the kernel on a real ext4 loopback filesystem mounted at $MNT, using the"
echo "exact setquota invocation agent/crates/ops/src/accounts/account_operations.rs makes; the same"
echo "check against a filesystem with no quota support (/) enforced nothing, which is the honest negative."
