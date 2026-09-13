//! Tests for the `process_site_host` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `process_site_host.rs` declares this
//! file with `#[path]`, which keeps it a child module.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::ProcessSiteHost;
use crate::safe_write::{ConfigHost, SafeWriteError};

/// A program that is not installed anywhere, so the spawn cannot succeed.
///
/// Under `/nonexistent` rather than a plausible name in `/usr/sbin`, so the test
/// cannot start passing on a machine that happens to have the tool.
const MISSING_BINARY: &str = "/nonexistent/maran-no-such-program";

/// A program that really starts and really exits non-zero.
///
/// The inverse control's whole point is that it EXISTS: `/bin/false` is on both
/// supported families and does exactly one thing.
const FAILING_BINARY: &str = "/bin/false";

/// A program that could not be started is reported as a failure to START it.
///
/// This is the witness the defect had none of. A missing or unexecutable binary
/// came back as [`SafeWriteError::ReloadFailed`], so an operator read
/// "configuration reload failed" and went looking at a config file, when the
/// truth was that `nginx` was not installed. The two failures are different jobs
/// — install a package, or fix a vhost — and the error name has to say which.
#[test]
fn a_program_that_cannot_be_started_is_a_spawn_failure() {
    let host = ProcessSiteHost::new();

    let refused = host.run(MISSING_BINARY, &["-t"]);

    match refused {
        Err(SafeWriteError::SpawnFailed { program, reason }) => {
            assert_eq!(program, MISSING_BINARY, "the error must name the program");
            assert!(
                !reason.is_empty(),
                "the operating system's reason must reach the operator"
            );
        }
        other => panic!("expected a spawn failure, got {other:?}"),
    }
}

/// A program that STARTED and then refused is not a spawn failure.
///
/// The inverse control for the test above: a gate that refuses everything proves
/// nothing. Replacing one wrong label with another — calling a validator's real
/// refusal "could not run the program" — would be the same defect wearing the
/// other hat, so the host returns a non-zero status as an outcome and leaves the
/// meaning of that status to the protocol above it, which turns it into
/// [`SafeWriteError::ValidationFailed`] or [`SafeWriteError::ReloadFailed`]
/// (`render_validate_swap_tests.rs`).
#[test]
fn a_program_that_started_and_failed_is_an_outcome_and_not_a_spawn_failure() {
    let host = ProcessSiteHost::new();

    let outcome = host.run(FAILING_BINARY, &[]).unwrap();

    assert_ne!(
        outcome.status, 0,
        "{FAILING_BINARY} is supposed to exit non-zero"
    );
}
