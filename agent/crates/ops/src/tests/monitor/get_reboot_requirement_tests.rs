//! Tests for `get_reboot_requirement`. Mirrors the source tree (rules/testing.md).
//!
//! The control that matters here is the one on the failure paths: a probe that
//! did not run, or ran and gave no answer, must never read as "not required",
//! because that is the one wrong answer that tells an operator a host is safe to
//! leave. Each way the probe can fail is its own test, and each asserts it is NOT
//! `NotRequired`; the answers that are real (marker present and absent, exit 0
//! and exit 1) are tested beside them as the inverse control, so a function
//! mutated to refuse everything cannot pass.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{RebootProbeFailure, RebootRequirement};

use super::get_reboot_requirement;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro, rhel_distro, suse_distro};
use crate::monitor::monitor_error::MonitorError;

/// The one argument `needs-restarting` is given: the reboot question alone.
const RHEL_PROBE_ARGUMENT: &str = "-r";

/// A RHEL-family host whose probe program exited `status`.
fn rhel_host_exiting(status: i32) -> FakeMonitorHost {
    FakeMonitorHost::from_alma_captures().with_exact_command_answer(
        &[RHEL_PROBE_ARGUMENT],
        status,
        "",
    )
}

/// The one argument `zypper` is given by the SUSE family's probe: the reboot question alone.
const SUSE_PROBE_ARGUMENT: &str = "needs-rebooting";

/// The status `zypper needs-rebooting` is documented to exit with when a reboot is needed.
///
/// UNVERIFIED: read from `man zypper`, not measured; confirm with `zypper needs-rebooting; echo $?`
/// on a container where a core package was just updated.
const SUSE_REBOOT_NEEDED_STATUS: i32 = 102;

/// A SUSE-family host whose probe program exited `status`.
///
/// The host's other captures are the AlmaLinux ones the fake already holds; this probe reads none
/// of them, and the SUSE family has no capture of its own (nothing was recorded on that family).
fn suse_host_exiting(status: i32) -> FakeMonitorHost {
    FakeMonitorHost::from_alma_captures().with_exact_command_answer(
        &[SUSE_PROBE_ARGUMENT],
        status,
        "",
    )
}

#[test]
fn a_debian_host_whose_marker_exists_requires_a_reboot() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_marker_present();

    let result = get_reboot_requirement(&host, distro());

    assert_eq!(result, Ok(RebootRequirement::Required));
}

#[test]
fn a_debian_host_whose_marker_is_absent_does_not_require_a_reboot() {
    let host = FakeMonitorHost::from_ubuntu_captures();

    let result = get_reboot_requirement(&host, distro());

    assert_eq!(result, Ok(RebootRequirement::NotRequired));
}

/// A marker that cannot be examined (permission denied, an I/O error) has not
/// said the host needs no reboot: it is the error, and never `NotRequired`.
#[test]
fn a_debian_marker_that_cannot_be_examined_is_an_error_and_not_a_no() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unexaminable_marker();

    let result = get_reboot_requirement(&host, distro());

    assert_eq!(result, Err(MonitorError::RebootProbeNotRun));
    assert_ne!(result, Ok(RebootRequirement::NotRequired));
}

/// The path asked about is the one the adapter names, not a literal of this
/// operation's own; and the marker family runs no program at all.
#[test]
fn the_marker_asked_about_is_the_one_the_adapter_names_and_no_program_is_run() {
    let host = FakeMonitorHost::from_ubuntu_captures();

    get_reboot_requirement(&host, distro()).unwrap();

    assert_eq!(
        host.marker_path_requested(),
        Some(PathBuf::from("/var/run/reboot-required"))
    );
    assert!(host.commands().is_empty(), "{:?}", host.commands());
}

#[test]
fn a_rhel_probe_that_exits_zero_does_not_require_a_reboot() {
    let host = rhel_host_exiting(0);

    let result = get_reboot_requirement(&host, rhel_distro());

    assert_eq!(result, Ok(RebootRequirement::NotRequired));
}

#[test]
fn a_rhel_probe_that_exits_one_requires_a_reboot() {
    let host = rhel_host_exiting(1);

    let result = get_reboot_requirement(&host, rhel_distro());

    assert_eq!(result, Ok(RebootRequirement::Required));
}

/// Any other status is an error: 127 is a program that is not installed, 2 is
/// whatever the tool says when it fails. Neither is a "no".
#[test]
fn a_rhel_probe_that_exits_with_any_other_status_is_an_error_and_not_a_no() {
    for status in [2, 127, -1] {
        let host = rhel_host_exiting(status);

        let result = get_reboot_requirement(&host, rhel_distro());

        assert_eq!(
            result,
            Err(MonitorError::RebootProbeFailed {
                reason: RebootProbeFailure::Unsuccessful { status }
            })
        );
        assert_ne!(result, Ok(RebootRequirement::NotRequired), "{status}");
    }
}

/// A program that could not be started at all is the probe not running, which
/// is not the program's own status.
#[test]
fn a_rhel_probe_that_cannot_be_started_is_an_error_and_not_a_no() {
    let host = FakeMonitorHost::from_alma_captures().with_absent_service_manager();

    let result = get_reboot_requirement(&host, rhel_distro());

    assert_eq!(result, Err(MonitorError::RebootProbeNotRun));
    assert_ne!(result, Ok(RebootRequirement::NotRequired));
}

/// A run killed at its deadline has said nothing: it is the bound carried
/// through by name, and neither "not run" nor "not required".
#[test]
fn a_rhel_probe_killed_at_its_deadline_is_a_bound_and_not_a_no() {
    let host = FakeMonitorHost::from_alma_captures().with_timed_out_service_manager();

    let result = get_reboot_requirement(&host, rhel_distro());

    assert!(matches!(result, Err(MonitorError::Bound(_))), "{result:?}");
}

/// The exact argv and the one class the probe runs in. `-r` keeps the question
/// to the reboot alone, and the class is the table's, not this operation's.
#[test]
fn the_rhel_probe_is_the_program_with_its_one_constant_argument_in_the_query_class() {
    let host = rhel_host_exiting(0);

    get_reboot_requirement(&host, rhel_distro()).unwrap();

    assert_eq!(
        host.commands(),
        vec![vec![
            String::from("/usr/bin/needs-restarting"),
            String::from("-r"),
        ]]
    );
    assert_eq!(host.classes(), vec![CallClass::Query]);
}

/// A deadline is the one thing no behaviour test waits long enough to observe,
/// so the class is read from the production source.
#[test]
fn the_source_names_the_query_class_and_no_other() {
    let classes = crate::test_support::call_class_census::classes_named_in_code(include_str!(
        "../../monitor/get_reboot_requirement.rs"
    ));

    assert_eq!(classes, vec![String::from("Query")]);
}

#[test]
fn a_suse_probe_that_exits_zero_does_not_require_a_reboot() {
    let host = suse_host_exiting(0);

    let result = get_reboot_requirement(&host, suse_distro());

    assert_eq!(result, Ok(RebootRequirement::NotRequired));
}

#[test]
fn a_suse_probe_that_exits_one_hundred_and_two_requires_a_reboot() {
    let host = suse_host_exiting(SUSE_REBOOT_NEEDED_STATUS);

    let result = get_reboot_requirement(&host, suse_distro());

    assert_eq!(result, Ok(RebootRequirement::Required));
}

/// Any other status is an error, and in particular 1, which is `needs-restarting`'s "reboot" on the
/// RHEL family and means nothing of the kind to `zypper`: reading it across families would tell an
/// operator a host needs a reboot on the strength of an error.
#[test]
fn a_suse_probe_that_exits_with_any_other_status_is_an_error_and_not_a_no() {
    for status in [1, 2, 127, -1] {
        let host = suse_host_exiting(status);

        let result = get_reboot_requirement(&host, suse_distro());

        assert_eq!(
            result,
            Err(MonitorError::RebootProbeFailed {
                reason: RebootProbeFailure::Unsuccessful { status }
            })
        );
        assert_ne!(result, Ok(RebootRequirement::NotRequired), "{status}");
        assert_ne!(result, Ok(RebootRequirement::Required), "{status}");
    }
}

#[test]
fn a_suse_probe_that_cannot_be_started_is_an_error_and_not_a_no() {
    let host = FakeMonitorHost::from_alma_captures().with_absent_service_manager();

    let result = get_reboot_requirement(&host, suse_distro());

    assert_eq!(result, Err(MonitorError::RebootProbeNotRun));
    assert_ne!(result, Ok(RebootRequirement::NotRequired));
}

#[test]
fn the_suse_probe_is_zypper_with_its_one_constant_argument_in_the_query_class() {
    let host = suse_host_exiting(0);

    get_reboot_requirement(&host, suse_distro()).unwrap();

    assert_eq!(
        host.commands(),
        vec![vec![
            String::from("/usr/bin/zypper"),
            String::from(SUSE_PROBE_ARGUMENT),
        ]]
    );
    assert_eq!(host.classes(), vec![CallClass::Query]);
}
