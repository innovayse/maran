//! The one place a backup program's bound is told apart from its failure.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

use super::*;
use crate::backup::backup_error::PROGRAM_UNAVAILABLE;

/// The area's own "could not run it" answer, which a bound must never become.
fn otherwise() -> BackupError {
    BackupError::ArchiveFailed {
        status: PROGRAM_UNAVAILABLE,
    }
}

#[test]
fn a_deadline_is_a_bound_and_never_the_areas_could_not_run_it() {
    let bound = BoundExceeded::TimedOut {
        program: String::from("/usr/bin/tar"),
        deadline: Duration::from_secs(43_200),
    };

    let mapped = bound_or(SpawnError::Bound(bound.clone()), otherwise());

    assert_eq!(mapped, BackupError::Bound(bound));
}

#[test]
fn an_output_past_its_cap_is_a_bound_and_never_the_areas_could_not_run_it() {
    let bound = BoundExceeded::OutputTooLarge {
        program: String::from("/usr/bin/tar"),
        stream: OutputStream::Stdout,
        cap: 4,
    };

    let mapped = bound_or(SpawnError::Bound(bound.clone()), otherwise());

    assert_eq!(mapped, BackupError::Bound(bound));
}

/// The inverse control: every other failure becomes exactly the caller's own
/// answer, so each call site keeps its distinct "could not run it" variant.
#[test]
fn a_program_that_could_not_be_started_becomes_the_callers_own_answer() {
    let mapped = bound_or(
        SpawnError::Start {
            program: String::from("/usr/bin/tar"),
            reason: String::from("No such file or directory"),
        },
        BackupError::DumpFailed {
            status: PROGRAM_UNAVAILABLE,
        },
    );

    assert_eq!(
        mapped,
        BackupError::DumpFailed {
            status: PROGRAM_UNAVAILABLE
        }
    );
}

#[test]
fn input_that_could_not_be_delivered_becomes_the_callers_own_answer() {
    let mapped = bound_or(
        SpawnError::Input {
            program: String::from("/usr/bin/mariadb"),
        },
        otherwise(),
    );

    assert_eq!(mapped, otherwise());
}
