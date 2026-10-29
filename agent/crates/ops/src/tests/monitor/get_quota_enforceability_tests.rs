//! Tests for `get_quota_enforceability`. Mirrors the source tree
//! (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;

use super::get_quota_enforceability;
use crate::accounts::QuotaUnenforceableReason;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro};
use crate::monitor::model::quota_enforceability_status::QuotaEnforceabilityStatus;
use crate::monitor::monitor_error::MonitorError;

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

/// `quotaon -p` names its mount point after an end-of-options marker, in the
/// query class, and the option and the operand are both still there.
#[test]
fn quotaon_is_a_guarded_query_naming_the_home_mount() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_mounts("/dev/sda1 /home ext4 rw,relatime,usrquota,grpquota 0 0\n");

    get_quota_enforceability(&host, distro()).expect("the read succeeds");

    let commands = host.commands();
    let command = commands.first().expect("quotaon was spawned");
    assert_eq!(
        command[1..],
        [
            String::from("-p"),
            String::from("--"),
            String::from(AgentPaths::ACCOUNT_HOME_ROOT)
        ]
    );
    assert_eq!(host.classes(), vec![CallClass::Query]);
}

/// A `quotaon` killed at its deadline is a bound, never a confident "not
/// enforceable".
#[test]
fn a_quotaon_killed_at_its_deadline_is_a_bound_and_not_a_finding() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_mounts("/dev/sda1 /home ext4 rw,relatime,usrquota,grpquota 0 0\n")
        .with_timed_out_service_manager();

    let outcome = get_quota_enforceability(&host, distro());

    assert!(
        matches!(
            outcome,
            Err(MonitorError::Bound(BoundExceeded::TimedOut { .. }))
        ),
        "expected the typed timeout, got {outcome:?}"
    );
}
