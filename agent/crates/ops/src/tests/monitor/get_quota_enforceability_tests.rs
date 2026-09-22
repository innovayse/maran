//! Tests for `get_quota_enforceability`. Mirrors the source tree
//! (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::get_quota_enforceability;
use crate::accounts::QuotaUnenforceableReason;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro};
use crate::monitor::model::quota_enforceability_status::QuotaEnforceabilityStatus;

#[test]
fn a_filesystem_with_quota_accounting_enabled_is_enforceable() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_mounts("/dev/sda1 /home ext4 rw,relatime,usrquota,grpquota 0 0\n");
    // `quotaon -p` output is answered through `run`, whose default in this
    // fake is `LoadState=not-found...` for an unknown "unit" — but this rpc
    // never asks about a systemd unit, it asks `quotaon -p`, so the default
    // command-manager status (`Ok(0)`) plus an explicit `quotaon` "enabled"
    // line is what a real host would print. Model the positive answer via
    // `with_unit`'s general mechanism is not it either; this fake's `run`
    // always returns whatever unit map is configured, keyed by the LAST
    // argument, which for `quotaon -p /home` is "/home" itself.
    let host = host.with_unit("/home", "Quota for users are enabled on mountpoint /home\n");

    let status = get_quota_enforceability(&host, distro()).expect("the read succeeds");

    assert_eq!(status, QuotaEnforceabilityStatus::Enforceable);
}

/// The **negative control provable everywhere, including the polygon**: a
/// filesystem never mounted with quota accounting, and `quotaon -p`
/// therefore has nothing to report either.
#[test]
fn a_filesystem_never_mounted_with_quota_accounting_is_not_enforceable() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_mounts("/dev/sda1 /home ext4 rw,relatime 0 0\n");

    let status = get_quota_enforceability(&host, distro()).expect("the read succeeds");

    assert_eq!(
        status,
        QuotaEnforceabilityStatus::NotEnforceable(
            QuotaUnenforceableReason::MountedWithoutQuotaAccounting
        )
    );
}

/// A failure to read `/proc/mounts` is an error, never a confident
/// "not enforceable" — the same discipline `MonitorHost::read_sshd_config`'s
/// callers already follow for the SFTP jail check.
#[test]
fn an_unreadable_mounts_file_is_an_error_not_a_finding() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unreadable_mounts();

    let result = get_quota_enforceability(&host, distro());

    assert!(
        result.is_err(),
        "an unreadable /proc/mounts must not be reported as a quota finding"
    );
}
