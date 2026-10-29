//! How a failed spawn becomes an error of the SFTP area.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::safe_write::SafeWriteError;
use crate::sftp::sftp_error::SftpError;

/// A bound as the spawn core reports one.
fn a_bound() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: "/usr/sbin/usermod".to_owned(),
        deadline: Duration::from_secs(120),
    }
}

#[test]
fn a_bound_from_spawning_reaches_the_sftp_area_as_a_bound() {
    let mapped = SftpError::from(SpawnError::Bound(a_bound()));

    assert_eq!(mapped, SftpError::Bound(a_bound()));
    assert_ne!(
        mapped,
        SftpError::program_unavailable(),
        "a kill is not a program that never started"
    );
}

#[test]
fn a_program_that_could_not_start_is_still_program_unavailable() {
    // The inverse control: only the bound moves to its own variant.
    let mapped = SftpError::from(SpawnError::Start {
        program: "/usr/sbin/usermod".to_owned(),
        reason: "No such file or directory".to_owned(),
    });

    assert_eq!(mapped, SftpError::program_unavailable());
}

#[test]
fn a_program_that_could_not_be_waited_for_is_program_unavailable() {
    let mapped = SftpError::from(SpawnError::Wait {
        program: "/usr/sbin/usermod".to_owned(),
        reason: "interrupted".to_owned(),
    });

    assert_eq!(mapped, SftpError::program_unavailable());
}

#[test]
fn the_text_of_a_bound_names_the_program_and_never_claims_a_rollback() {
    let text = SftpError::Bound(a_bound()).to_string();

    assert!(text.contains("/usr/sbin/usermod"), "{text}");
    assert!(!text.to_lowercase().contains("rolled back"), "{text}");
}

#[test]
fn a_bound_from_the_config_protocol_reaches_the_sftp_area_as_a_bound() {
    let mapped = SftpError::from_config_write(SafeWriteError::Bound(a_bound()));

    assert_eq!(mapped, SftpError::Bound(a_bound()));
}

#[test]
fn any_other_failure_of_the_config_protocol_is_still_a_jail_failure() {
    // The inverse control: only the bound keeps its identity.
    let mapped = SftpError::from_config_write(SafeWriteError::SpawnFailed {
        program: "/usr/bin/systemctl".to_owned(),
        reason: "gone".to_owned(),
    });

    assert_eq!(mapped, SftpError::JailFailed);
}
