//! How a failed spawn becomes an error of the login area.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::logins::logins_error::LoginsError;

/// A bound as the spawn core reports one.
fn a_bound() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: "/usr/sbin/usermod".to_owned(),
        deadline: Duration::from_secs(120),
    }
}

#[test]
fn a_bound_from_spawning_reaches_the_login_area_as_a_bound() {
    let mapped = LoginsError::from(SpawnError::Bound(a_bound()));

    assert_eq!(mapped, LoginsError::Bound(a_bound()));
    assert_ne!(
        mapped,
        LoginsError::program_unavailable(),
        "a kill is not a program that never started"
    );
}

#[test]
fn a_program_that_could_not_start_is_still_program_unavailable() {
    // The inverse control: only the bound moves to its own variant.
    let mapped = LoginsError::from(SpawnError::Start {
        program: "/usr/sbin/usermod".to_owned(),
        reason: "No such file or directory".to_owned(),
    });

    assert_eq!(mapped, LoginsError::program_unavailable());
}

#[test]
fn a_program_that_could_not_be_waited_for_is_program_unavailable() {
    let mapped = LoginsError::from(SpawnError::Wait {
        program: "/usr/sbin/usermod".to_owned(),
        reason: "interrupted".to_owned(),
    });

    assert_eq!(mapped, LoginsError::program_unavailable());
}

#[test]
fn the_text_of_a_bound_names_the_program_and_never_claims_a_rollback() {
    let text = LoginsError::Bound(a_bound()).to_string();

    assert!(text.contains("/usr/sbin/usermod"), "{text}");
    assert!(!text.to_lowercase().contains("rolled back"), "{text}");
}
