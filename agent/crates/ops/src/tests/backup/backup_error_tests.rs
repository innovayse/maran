//! How the backup area's error holds a bound and tells it from the failures it
//! is easy to confuse it with.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;

use super::BackupError;

/// A deadline failure, as the core builds one.
fn timed_out() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: String::from("/usr/bin/tar"),
        deadline: Duration::from_secs(43_200),
    }
}

#[test]
fn a_bound_converts_into_the_bound_variant_and_no_other() {
    assert_eq!(
        BackupError::from(timed_out()),
        BackupError::Bound(timed_out())
    );
}

#[test]
fn a_bound_names_the_program_and_says_the_kill_is_not_a_rollback() {
    let printed = BackupError::Bound(timed_out()).to_string();

    assert!(printed.contains("/usr/bin/tar"), "{printed}");
    assert!(printed.contains("may still run"), "{printed}");
}

/// A program that was stopped has not been shown to have refused.
#[test]
fn a_bound_is_none_of_the_variants_that_say_a_program_ran_and_refused() {
    let bound = BackupError::Bound(timed_out());

    assert_ne!(bound, BackupError::ArchiveFailed { status: 1 });
    assert_ne!(bound, BackupError::DumpFailed { status: 1 });
    assert_ne!(bound, BackupError::DropFailed { status: 1 });
    assert_ne!(bound, BackupError::LoadFailed { status: 1 });
}
