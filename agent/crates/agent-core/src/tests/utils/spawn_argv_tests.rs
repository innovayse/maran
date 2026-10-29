//! Tests for [`spawn_argv`], the bounded default path.
//!
//! Two cases re-enter the test binary: the property is about the environment
//! and the standard input the core's child gets, so the outer half must control
//! what the test process itself holds. The inner half judges only what the
//! CHILD saw.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::process::Stdio;
use std::time::Duration;

use super::spawn_argv;
use crate::utils::apply_child_environment::CHILD_ENVIRONMENT;
use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::call_class::CallClass;
use crate::utils::child_input::ChildInput;
use crate::utils::child_output::ChildOutput;
use crate::utils::output_stream::OutputStream;
use crate::utils::process_probe::run_inner_half;
use crate::utils::run_bounded::run_bounded;
use crate::utils::spawn_error::SpawnError;
use crate::utils::spawn_request::SpawnRequest;

/// Set (to the path of a file) in the inner half of the standard-input witness.
const STDIN_MARKER: &str = "MARAN_SPAWN_ARGV_STDIN_INNER";

/// Set in the inner half of the environment witness.
const ENVIRONMENT_MARKER: &str = "MARAN_SPAWN_ARGV_ENVIRONMENT_INNER";

/// The full test path of the standard-input witness (`--exact` needs all of it).
const STDIN_CASE: &str = "utils::spawn_argv::tests::a_null_input_child_reads_dev_null_whatever_the_daemon_holds_on_descriptor_zero";

/// The full test path of the environment witness.
const ENVIRONMENT_CASE: &str =
    "utils::spawn_argv::tests::a_spawned_program_receives_only_the_declared_environment";

/// Instructions rather than settings: the variables whose arrival would put
/// attacker code or options inside a root child. Values point at nothing,
/// because the assertion is that they do not ARRIVE.
const HOSTILE_ENVIRONMENT: [(&str, &str); 3] = [
    ("LD_PRELOAD", "/nonexistent/maran-test-payload.so"),
    ("BASH_ENV", "/nonexistent/maran-test-payload.sh"),
    ("TAR_OPTIONS", "--absolute-names"),
];

/// A child with a null input reads `/dev/null`, whatever file the daemon holds
/// on descriptor zero.
///
/// The outer half starts this test binary with a temporary file as ITS standard
/// input. The inner half first proves that its own descriptor zero really is
/// that file (the positive control: without it, "the child read /dev/null"
/// could be true because the test process had /dev/null to begin with), then
/// asks `readlink` what the core's child has on descriptor zero.
#[test]
fn a_null_input_child_reads_dev_null_whatever_the_daemon_holds_on_descriptor_zero() {
    let Ok(expected_file) = std::env::var(STDIN_MARKER) else {
        let file = tempfile::NamedTempFile::new().unwrap();
        let held = std::fs::File::open(file.path()).unwrap();
        run_inner_half(
            STDIN_CASE,
            STDIN_MARKER,
            &file.path().display().to_string(),
            Stdio::from(held),
            &[],
        );
        return;
    };

    let own = std::fs::read_link("/proc/self/fd/0").unwrap();
    assert_eq!(
        own.canonicalize().unwrap(),
        Path::new(&expected_file).canonicalize().unwrap(),
        "the inner half does not hold the planted file on descriptor zero"
    );

    let through_default = spawn_argv(
        Path::new("/usr/bin/readlink"),
        &["/proc/self/fd/0"],
        CallClass::Query,
    )
    .unwrap();
    assert_eq!(through_default.stdout, "/dev/null\n");

    let through_core = run_bounded(SpawnRequest {
        program: Path::new("/usr/bin/readlink"),
        arguments: &["/proc/self/fd/0"],
        class: CallClass::Query,
        input: ChildInput::Null,
        output: ChildOutput::DEFAULT,
    })
    .unwrap();
    assert_eq!(through_core.stdout, "/dev/null\n");
}

/// A spawned program's environment is exactly `CHILD_ENVIRONMENT`, whatever the
/// daemon inherited.
///
/// The outer half plants `LD_PRELOAD`, `BASH_ENV` and `TAR_OPTIONS` in a copy of
/// this test binary; the inner half spawns `env` and compares the child's REAL
/// environment, as a whole set, to the declared one, so an inherited variable of
/// any kind fails it. This is the witness that used to live beside the accounts
/// host; it lives here because the spawn it guards is now the core's.
#[test]
fn a_spawned_program_receives_only_the_declared_environment() {
    if std::env::var(ENVIRONMENT_MARKER).is_err() {
        run_inner_half(
            ENVIRONMENT_CASE,
            ENVIRONMENT_MARKER,
            "1",
            Stdio::null(),
            &HOSTILE_ENVIRONMENT,
        );
        return;
    }

    let outcome = spawn_argv(Path::new("/usr/bin/env"), &[], CallClass::Query).unwrap();
    assert_eq!(outcome.status, 0, "env did not run: {}", outcome.stderr);

    let mut seen: Vec<&str> = outcome.stdout.lines().collect();
    seen.sort_unstable();
    let mut declared: Vec<String> = CHILD_ENVIRONMENT
        .iter()
        .map(|(name, value)| format!("{name}={value}"))
        .collect();
    declared.sort_unstable();

    assert_eq!(
        seen, declared,
        "the child's environment is not the declared one; it saw:\n{}",
        outcome.stdout
    );
}

/// A non-zero exit is an outcome the caller decides on, not an error.
#[test]
fn a_non_zero_exit_is_an_outcome_not_an_error() {
    let outcome = spawn_argv(Path::new("/usr/bin/false"), &[], CallClass::Query).unwrap();

    assert_eq!(outcome.status, 1);
}

/// A program that cannot be started is a start error naming it.
#[test]
fn a_program_that_cannot_start_is_a_start_error() {
    let result = spawn_argv(
        Path::new("/nonexistent/maran-test-program"),
        &[],
        CallClass::Query,
    );

    match result {
        Err(SpawnError::Start { program, .. }) => {
            assert_eq!(program, "/nonexistent/maran-test-program");
        }
        other => panic!("expected a start error, got {other:?}"),
    }
}

/// The default path is the bounded one: standard output is captured under the
/// default cap, exactly the cap is accepted and one byte more is refused.
#[test]
fn spawn_argv_is_the_bounded_default_path_with_a_null_input_and_the_default_caps() {
    let exact = ChildOutput::DEFAULT_CAP.to_string();
    let over = (ChildOutput::DEFAULT_CAP + 1).to_string();

    let accepted = spawn_argv(
        Path::new("/usr/bin/head"),
        &["-c", exact.as_str(), "/dev/zero"],
        CallClass::Query,
    )
    .unwrap();
    assert_eq!(
        u64::try_from(accepted.stdout.len()).unwrap(),
        ChildOutput::DEFAULT_CAP
    );

    let refused = spawn_argv(
        Path::new("/usr/bin/head"),
        &["-c", over.as_str(), "/dev/zero"],
        CallClass::Query,
    );
    match refused {
        Err(SpawnError::Bound(BoundExceeded::OutputTooLarge { stream, cap, .. })) => {
            assert_eq!(stream, OutputStream::Stdout);
            assert_eq!(cap, ChildOutput::DEFAULT_CAP);
        }
        other => panic!("expected an output overflow, got {other:?}"),
    }
}

/// Output that is not UTF-8 is replaced, not refused.
#[test]
fn output_that_is_not_utf8_is_replaced_and_not_refused() {
    // A file whose bytes are not UTF-8.
    let mut invalid = tempfile::NamedTempFile::new().unwrap();
    std::io::Write::write_all(&mut invalid, &[0xff, 0xfe, b'o', b'k']).unwrap();
    std::io::Write::flush(&mut invalid).unwrap();
    let path = invalid.path().display().to_string();

    let read = spawn_argv(
        Path::new("/usr/bin/cat"),
        &[path.as_str()],
        CallClass::Query,
    )
    .unwrap();

    assert!(read.stdout.ends_with("ok"), "{:?}", read.stdout);
    assert!(read.stdout.contains('\u{fffd}'), "{:?}", read.stdout);
}

/// The class's deadline is the one that applies: a query-class call returns a
/// finished program's outcome well inside it.
#[test]
fn a_quick_program_returns_long_before_its_class_deadline() {
    let started = std::time::Instant::now();
    let outcome = spawn_argv(Path::new("/usr/bin/echo"), &["quick"], CallClass::Query).unwrap();

    assert_eq!(outcome.stdout, "quick\n");
    assert!(started.elapsed() < CallClass::Query.deadline() - Duration::from_secs(30));
}
