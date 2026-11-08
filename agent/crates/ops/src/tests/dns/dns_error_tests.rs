//! How a failed spawn, and a failed config write, become errors of the DNS area.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::dns::dns_error::DnsError;
use crate::safe_write::SafeWriteError;

/// A bound as the spawn core reports one.
fn a_bound() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: "/usr/bin/systemctl".to_owned(),
        deadline: Duration::from_secs(240),
    }
}

#[test]
fn a_bound_from_spawning_reaches_the_dns_area_as_a_bound() {
    let mapped = DnsError::from(SpawnError::Bound(a_bound()));

    assert_eq!(mapped, DnsError::Bound(a_bound()));
    assert_ne!(mapped, DnsError::program_unavailable());
}

#[test]
fn a_program_that_could_not_start_is_program_unavailable() {
    let mapped = DnsError::from(SpawnError::Start {
        program: "/usr/sbin/rndc".to_owned(),
        reason: "No such file or directory".to_owned(),
    });

    assert_eq!(mapped, DnsError::program_unavailable());
}

#[test]
fn a_refusal_by_the_checker_is_a_rejected_configuration_carrying_what_it_printed() {
    let mapped = DnsError::from_config_write(SafeWriteError::ValidationFailed {
        stderr: "named.conf:3: unknown option".to_owned(),
    });

    assert_eq!(
        mapped,
        DnsError::ConfigRejected {
            output: "named.conf:3: unknown option".to_owned()
        }
    );
}

#[test]
fn a_refused_restart_is_the_service_manager_refusing_the_unit_and_never_a_rejected_configuration() {
    let mapped = DnsError::from_config_write(SafeWriteError::ReloadFailed {
        stderr: "Job failed".to_owned(),
    });

    assert_eq!(
        mapped,
        DnsError::ServiceRefused {
            unit: AgentPaths::DNS_UNIT.to_owned()
        }
    );
}

#[test]
fn a_config_write_that_times_out_is_a_bound() {
    let mapped = DnsError::from_config_write(SafeWriteError::Bound(a_bound()));

    assert_eq!(mapped, DnsError::Bound(a_bound()));
}

#[test]
fn a_program_the_protocol_could_not_start_is_program_unavailable() {
    let mapped = DnsError::from_config_write(SafeWriteError::SpawnFailed {
        program: "/usr/bin/named-checkconf".to_owned(),
        reason: "No such file or directory".to_owned(),
    });

    assert_eq!(mapped, DnsError::program_unavailable());
}

#[test]
fn a_mechanical_failure_of_the_protocol_is_a_config_write_error() {
    let mapped = DnsError::from_config_write(SafeWriteError::Rename);

    assert!(matches!(mapped, DnsError::ConfigWrite { .. }));
}
