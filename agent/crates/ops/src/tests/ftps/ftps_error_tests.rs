//! How a failed spawn, and a failed config write, become errors of the FTPS area.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::ftps::ftps_error::FtpsError;
use crate::safe_write::SafeWriteError;

/// A bound as the spawn core reports one.
fn a_bound() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: "/usr/bin/systemctl".to_owned(),
        deadline: Duration::from_secs(240),
    }
}

#[test]
fn a_bound_from_spawning_reaches_the_ftps_area_as_a_bound() {
    let mapped = FtpsError::from(SpawnError::Bound(a_bound()));

    assert_eq!(mapped, FtpsError::Bound(a_bound()));
    assert_ne!(
        mapped,
        FtpsError::program_unavailable(),
        "a kill is not a program that never started"
    );
}

#[test]
fn a_program_that_could_not_start_is_still_program_unavailable() {
    // The inverse control: only the bound moves to its own variant.
    let mapped = FtpsError::from(SpawnError::Start {
        program: "/usr/sbin/vsftpd".to_owned(),
        reason: "No such file or directory".to_owned(),
    });

    assert_eq!(mapped, FtpsError::program_unavailable());
}

#[test]
fn an_input_that_could_not_be_delivered_is_program_unavailable() {
    let mapped = FtpsError::from(SpawnError::Input {
        program: "/usr/sbin/chpasswd".to_owned(),
    });

    assert_eq!(mapped, FtpsError::program_unavailable());
}

#[test]
fn an_ftps_config_write_that_times_out_is_a_bound_and_never_service_refused() {
    let mapped = FtpsError::from_config_write(SafeWriteError::Bound(a_bound()));

    assert_eq!(mapped, FtpsError::Bound(a_bound()));
    assert_ne!(
        mapped,
        FtpsError::ServiceRefused {
            unit: AgentPaths::FTPS_UNIT.to_owned()
        }
    );
}

#[test]
fn a_validator_that_ran_and_refused_is_still_service_refused() {
    // The inverse control for the bound above.
    let mapped = FtpsError::from_config_write(SafeWriteError::ValidationFailed {
        stderr: "500 OOPS".to_owned(),
    });

    assert_eq!(
        mapped,
        FtpsError::ServiceRefused {
            unit: AgentPaths::FTPS_UNIT.to_owned()
        }
    );
}

#[test]
fn a_reload_that_ran_and_refused_is_still_service_refused() {
    let mapped = FtpsError::from_config_write(SafeWriteError::ReloadFailed {
        stderr: "failed".to_owned(),
    });

    assert!(
        matches!(mapped, FtpsError::ServiceRefused { .. }),
        "{mapped:?}"
    );
}

#[test]
fn a_config_write_whose_program_could_not_start_is_program_unavailable() {
    let mapped = FtpsError::from_config_write(SafeWriteError::SpawnFailed {
        program: "/usr/bin/systemctl".to_owned(),
        reason: "gone".to_owned(),
    });

    assert_eq!(mapped, FtpsError::program_unavailable());
}

#[test]
fn the_text_of_a_bound_names_the_program_and_never_claims_a_rollback() {
    let text = FtpsError::Bound(a_bound()).to_string();

    assert!(text.contains("/usr/bin/systemctl"), "{text}");
    assert!(!text.to_lowercase().contains("rolled back"), "{text}");
}
