//! Tests for [`SpawnError`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::spawn_error::SpawnError;

/// A start failure names the program and the operating system's reason.
#[test]
fn a_start_failure_names_the_program_and_the_reason() {
    let error = SpawnError::Start {
        program: "/usr/bin/missing".to_owned(),
        reason: "No such file or directory".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "could not start /usr/bin/missing: No such file or directory"
    );
}

/// An input failure names the program and nothing else: the exact text is the
/// guard that no I/O error text and no input byte can reach a log through it.
#[test]
fn an_input_failure_names_the_program_and_never_the_bytes() {
    let error = SpawnError::Input {
        program: "/usr/sbin/chpasswd".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "could not deliver input to /usr/sbin/chpasswd"
    );
    assert_eq!(
        format!("{error:?}"),
        "Input { program: \"/usr/sbin/chpasswd\" }"
    );
}

/// A wait failure names the program and what failed.
#[test]
fn a_wait_failure_names_the_program_and_the_reason() {
    let error = SpawnError::Wait {
        program: "/usr/bin/tar".to_owned(),
        reason: "standard output could not be read".to_owned(),
    };

    assert_eq!(
        error.to_string(),
        "could not wait for /usr/bin/tar: standard output could not be read"
    );
}

/// A bound failure shows exactly the bound's own text, with no wrapper around it.
#[test]
fn a_bound_failure_is_transparent_over_the_bound_it_wraps() {
    let bound = BoundExceeded::TimedOut {
        program: "/usr/bin/tar".to_owned(),
        deadline: Duration::from_secs(5),
    };
    let expected = bound.to_string();
    let error = SpawnError::from(bound);

    assert_eq!(error.to_string(), expected);
    assert!(matches!(
        error,
        SpawnError::Bound(BoundExceeded::TimedOut { .. })
    ));
}
