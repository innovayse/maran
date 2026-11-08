//! Tests for `zone_write_error`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;

use super::super::dns_error::DnsError;
use super::zone_write_error;
use crate::safe_write::SafeWriteError;

#[test]
fn a_refusing_checker_is_a_rejected_zone() {
    let error = zone_write_error(SafeWriteError::ValidationFailed {
        stderr: "bad".to_owned(),
    });

    assert_eq!(
        error,
        DnsError::ZoneRejected {
            output: "bad".to_owned()
        }
    );
}

#[test]
fn a_refusing_daemon_is_a_refused_control_command() {
    let error = zone_write_error(SafeWriteError::ReloadFailed {
        stderr: "no".to_owned(),
    });

    assert_eq!(
        error,
        DnsError::ControlRefused {
            output: "no".to_owned()
        }
    );
}

#[test]
fn a_tool_that_never_ran_is_the_negative_sentinel() {
    let error = zone_write_error(SafeWriteError::SpawnFailed {
        program: "p".to_owned(),
        reason: "r".to_owned(),
    });

    assert_eq!(error, DnsError::program_unavailable());
}

#[test]
fn a_bound_keeps_its_identity() {
    let bound = BoundExceeded::TimedOut {
        program: "/usr/sbin/rndc".to_owned(),
        deadline: Duration::from_secs(30),
    };

    let error = zone_write_error(SafeWriteError::Bound(bound.clone()));

    assert_eq!(error, DnsError::Bound(bound));
}

#[test]
fn a_mechanical_failure_of_the_protocol_is_a_config_write_failure() {
    let error = zone_write_error(SafeWriteError::Rename);

    assert!(matches!(error, DnsError::ConfigWrite { .. }));
}
