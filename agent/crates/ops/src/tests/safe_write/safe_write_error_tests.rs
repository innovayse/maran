//! Tests for [`SafeWriteError`]: how a bound is told apart from a refusal, and what
//! it says about the files.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints").
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_distro::{DistroFamily, adapter_for};

use super::SafeWriteError;
use crate::safe_write::adapter_programs::timed_out;

/// A bound says the program was killed, and never claims a live rollback.
#[test]
fn a_bound_is_displayed_as_restored_on_disk_and_never_as_a_live_rollback() {
    // The files are restored on disk, but a reload that was killed may already have
    // taken effect in the service manager. Text saying "rolled back" would promise
    // the service went back too.
    let text = SafeWriteError::Bound(timed_out("/usr/sbin/nginx")).to_string();

    assert!(
        text.contains("/usr/sbin/nginx"),
        "names the program: {text}"
    );
    assert!(text.contains("60s"), "names the bound: {text}");
    assert!(
        !text.to_lowercase().contains("rolled back"),
        "must not promise a rollback of the running service: {text}"
    );
}

/// A bound the core enforced stays a bound.
#[test]
fn a_bound_from_the_spawn_core_is_a_bound_and_not_a_failed_spawn() {
    let program = adapter_for(DistroFamily::Debian).nginx_binary();

    let timed =
        SafeWriteError::from_spawn(&program, SpawnError::Bound(timed_out("/usr/sbin/nginx")));
    let flooded = SafeWriteError::from_spawn(
        &program,
        SpawnError::Bound(BoundExceeded::OutputTooLarge {
            program: "/usr/sbin/nginx".to_owned(),
            stream: OutputStream::Stderr,
            cap: 4,
        }),
    );

    assert!(matches!(
        timed,
        SafeWriteError::Bound(BoundExceeded::TimedOut { .. })
    ));
    assert!(matches!(
        flooded,
        SafeWriteError::Bound(BoundExceeded::OutputTooLarge { .. })
    ));
}

/// The inverse control: a program that never started is a failed spawn naming it.
#[test]
fn a_program_that_never_started_is_a_failed_spawn_that_names_it() {
    let program = adapter_for(DistroFamily::Debian).nginx_binary();

    let failed = SafeWriteError::from_spawn(
        &program,
        SpawnError::Start {
            program: "/usr/sbin/nginx".to_owned(),
            reason: "No such file or directory".to_owned(),
        },
    );

    match failed {
        SafeWriteError::SpawnFailed { program, reason } => {
            assert_eq!(program, "/usr/sbin/nginx");
            assert!(reason.contains("No such file or directory"), "{reason}");
        }
        other => panic!("expected SpawnFailed, got {other:?}"),
    }
}
