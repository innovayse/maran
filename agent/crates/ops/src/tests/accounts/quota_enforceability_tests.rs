//! Tests for the enforceability classifier. Mirrors the source tree
//! (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::classify;
use crate::accounts::model::quota_unenforceable_reason::QuotaUnenforceableReason;

const MOUNTED_WITH_QUOTA: &str = "/dev/sda1 /home ext4 rw,relatime,usrquota,grpquota 0 0\n";
const MOUNTED_WITHOUT_QUOTA: &str = "/dev/sda1 /home ext4 rw,relatime 0 0\n";
const XFS_MOUNTED_WITH_QUOTA: &str = "/dev/sda1 /home xfs rw,relatime,uquota 0 0\n";

const ENABLED: &str = "Quota for users are enabled on mountpoint /home\n";
const NOT_ENABLED: &str = "Quota for users are not enabled on mountpoint /home\n";
const CANNOT_FIND: &str = "quotaon: cannot find /home in /etc/fstab\n";

/// The **negative control this plan's own proof calls provable everywhere,
/// including the polygon**: a filesystem never mounted with quota accounting
/// at all, which is what `docker/polygon/stand-ins/setquota-stand-in.sh`'s overlay
/// filesystem already IS.
#[test]
fn a_filesystem_never_mounted_with_quota_accounting_is_not_enforceable() {
    let result = classify(MOUNTED_WITHOUT_QUOTA, CANNOT_FIND, "/home");

    assert_eq!(
        result,
        Err(QuotaUnenforceableReason::MountedWithoutQuotaAccounting)
    );
}

/// The other reason: mounted with the option, but `quotaon -p` reports it
/// off — the ext4 case Section 3 of the plan names as the reason
/// `/proc/mounts` alone is not sufficient.
#[test]
fn a_filesystem_mounted_with_the_option_but_not_turned_on_gets_the_specific_reason() {
    let result = classify(MOUNTED_WITH_QUOTA, NOT_ENABLED, "/home");

    assert_eq!(result, Err(QuotaUnenforceableReason::AccountingNotEnabled));
}

/// `quotaon -p` reporting enabled is decisive and enough on its own — the
/// **positive case, NOT provable against a real kernel on this machine**
/// (this is a pure-function unit test over fabricated text, not a real
/// mount).
#[test]
fn quotaon_reporting_enabled_is_enforceable_regardless_of_mount_options() {
    let result = classify(MOUNTED_WITHOUT_QUOTA, ENABLED, "/home");

    assert_eq!(result, Ok(()));
}

#[test]
fn xfs_style_mount_options_are_recognised_too() {
    let result = classify(XFS_MOUNTED_WITH_QUOTA, NOT_ENABLED, "/home");

    assert_eq!(result, Err(QuotaUnenforceableReason::AccountingNotEnabled));
}

/// The mutant Tasks 2/3's proof names directly: swapping the two reasons
/// sends an operator the wrong fix instruction — "remount" when the mount is
/// already fine, or "turn it on" when the mount itself never asked. This
/// pins both directions in one test so a swap fails on either one.
#[test]
fn the_two_reasons_are_never_swapped() {
    assert_eq!(
        classify(MOUNTED_WITHOUT_QUOTA, CANNOT_FIND, "/home"),
        Err(QuotaUnenforceableReason::MountedWithoutQuotaAccounting)
    );
    assert_eq!(
        classify(MOUNTED_WITH_QUOTA, NOT_ENABLED, "/home"),
        Err(QuotaUnenforceableReason::AccountingNotEnabled)
    );
}

/// A mount line for a DIFFERENT mountpoint must not be read as this one's.
#[test]
fn a_mount_option_for_a_different_mountpoint_does_not_count() {
    let mounts = "/dev/sda2 /var ext4 rw,relatime,usrquota 0 0\n";

    let result = classify(mounts, CANNOT_FIND, "/home");

    assert_eq!(
        result,
        Err(QuotaUnenforceableReason::MountedWithoutQuotaAccounting)
    );
}

/// The home root on a host where it is a directory of the root filesystem — the stock Ubuntu
/// layout, on which every account creation failed because `/home` was handed to `setquota` as if
/// it were a filesystem (issue #73).
#[test]
fn the_device_of_a_home_inside_the_root_filesystem_is_the_root_device() {
    let mounts = "/dev/mapper/vg-root / ext4 rw,relatime,usrquota 0 0\n\
                  tmpfs /run tmpfs rw,nosuid 0 0\n";

    assert_eq!(
        super::quota_device_for(mounts, "/home"),
        Some("/dev/mapper/vg-root")
    );
}

/// The agent's OWN view, which is the one that matters and the one that misled a mount-point
/// resolution: `ReadWritePaths=/home` is a bind mount, so inside the service's namespace /home IS
/// a mount point — of the same device. Resolving the device gives the same answer in both views.
#[test]
fn the_bind_mount_the_agent_sees_resolves_to_the_same_device() {
    let mounts = "/dev/mapper/vg-root / ext4 rw,relatime 0 0\n\
                  /dev/mapper/vg-root /home ext4 rw,nosuid,relatime,quota,usrquota 0 0\n";

    assert_eq!(
        super::quota_device_for(mounts, "/home"),
        Some("/dev/mapper/vg-root")
    );
}

/// A home on a genuinely separate filesystem resolves to that filesystem's device.
#[test]
fn a_home_on_its_own_filesystem_resolves_to_its_own_device() {
    let mounts = "/dev/mapper/vg-root / ext4 rw,relatime 0 0\n\
                  /dev/mapper/vg-home /home ext4 rw,relatime,usrquota 0 0\n";

    assert_eq!(
        super::quota_device_for(mounts, "/home"),
        Some("/dev/mapper/vg-home")
    );
}

/// A prefix has to end at a path boundary, or `/homer` would claim `/home`'s accounts and quotas
/// would be written to the wrong filesystem.
#[test]
fn a_mount_point_that_merely_starts_the_same_does_not_claim_the_path() {
    let mounts = "/dev/mapper/vg-root / ext4 rw 0 0\n\
                  /dev/mapper/vg-other /homer ext4 rw 0 0\n";

    assert_eq!(
        super::quota_device_for(mounts, "/home"),
        Some("/dev/mapper/vg-root")
    );
}

/// An empty or unreadable mount table yields nothing rather than a guess: the caller reports that
/// quotas cannot be enforced, which is true, instead of running setquota against a path that
/// cannot be right.
#[test]
fn an_empty_mount_table_resolves_to_nothing() {
    assert_eq!(super::quota_device_for("", "/home"), None);
}
