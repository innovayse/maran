//! The mapping from DNS failures onto the wire error.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_ops::dns::DnsError;

use crate::proto::ErrorCode;
use crate::services::dns::dns_status::to_agent_error;

#[test]
fn a_configuration_the_checker_refused_is_a_validation_failure() {
    let error = to_agent_error(&DnsError::ConfigRejected {
        output: "named.conf:3: unknown option".to_owned(),
    });

    assert_eq!(error.code, ErrorCode::ValidationFailed as i32);
    assert!(error.message.contains("named.conf:3"));
    assert!(error.tool_output.is_empty());
}

#[test]
fn every_fault_of_the_machine_is_a_system_failure() {
    let faults = [
        DnsError::ServiceRefused {
            unit: "maran-dns.service".to_owned(),
        },
        DnsError::NotListening,
        DnsError::SpawnFailed { code: -1 },
        DnsError::Bound(BoundExceeded::TimedOut {
            program: "/usr/sbin/rndc".to_owned(),
            deadline: Duration::from_secs(60),
        }),
        DnsError::Render,
        DnsError::ConfigUnreadable,
        DnsError::ConfigWrite {
            reason: "disk full".to_owned(),
        },
    ];

    for fault in faults {
        assert_eq!(
            to_agent_error(&fault).code,
            ErrorCode::SystemFailure as i32,
            "{fault:?}"
        );
    }
}

#[test]
fn a_zone_the_checker_refused_is_a_validation_failure() {
    let error = to_agent_error(&DnsError::ZoneRejected {
        output: "zone example.com/IN: loading from master file failed".to_owned(),
    });

    assert_eq!(error.code, ErrorCode::ValidationFailed as i32);
    assert!(error.message.contains("example.com"));
    assert!(error.tool_output.is_empty());
}

#[test]
fn a_zone_that_broke_a_rule_of_its_own_is_invalid_input() {
    let error = to_agent_error(&DnsError::ZoneContent(
        maran_ops::dns::ZoneContentFault::CnameWithOtherData { index: 4 },
    ));

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(error.message.contains("record 4"));
}

#[test]
fn a_full_disk_is_the_free_space_floor_code() {
    let error = to_agent_error(&DnsError::ZoneBelowFloor {
        available: 1,
        floor: 2,
    });

    assert_eq!(error.code, ErrorCode::BelowFreeSpaceFloor as i32);
}

#[test]
fn every_other_zone_fault_is_a_system_failure() {
    let faults = [
        DnsError::ControlRefused {
            output: "rndc: connect failed".to_owned(),
        },
        DnsError::ZoneStateUnreadable,
        DnsError::ZoneDirectoryUnreadable,
        DnsError::ZoneUnmeasurable,
    ];

    for fault in faults {
        assert_eq!(
            to_agent_error(&fault).code,
            ErrorCode::SystemFailure as i32,
            "{fault:?}"
        );
    }
}
