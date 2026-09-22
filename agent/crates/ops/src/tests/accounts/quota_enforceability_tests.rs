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
/// at all, which is what `docker/polygon/setquota-stand-in.sh`'s overlay
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
