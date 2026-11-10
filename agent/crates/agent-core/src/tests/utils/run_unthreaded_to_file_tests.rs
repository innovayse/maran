//! Tests for [`run_unthreaded_to_file`], the bounded core for a forked child
//! whose output is a file.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::io::Read as _;
use std::path::Path;
use std::process::Stdio;
use std::time::Duration;

use super::run_unthreaded_to_file;
use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::spawn_error::SpawnError;

/// The program every case runs: it prints its arguments.
const ECHO: &str = "/usr/bin/echo";

/// A program that writes an endless stream of `y` lines.
const YES: &str = "/usr/bin/yes";

/// The child's standard output lands in the file and the exit code comes back.
#[test]
fn the_output_of_a_finishing_child_lands_in_the_file() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("out");
    let output = File::create(&path).unwrap();

    let code = run_unthreaded_to_file(
        Path::new(ECHO),
        &["dumped"],
        Stdio::null(),
        &output,
        1024,
        Duration::from_secs(10),
    )
    .unwrap();

    let mut text = String::new();
    File::open(&path)
        .unwrap()
        .read_to_string(&mut text)
        .unwrap();
    assert_eq!(code, 0);
    assert_eq!(text, "dumped\n");
}

/// Inverse control: a child that writes past the cap is killed and the typed
/// error says so, not a file that merely stopped growing.
#[test]
fn a_child_that_writes_past_the_cap_is_killed_with_the_typed_error() {
    let directory = tempfile::tempdir().unwrap();
    let output = File::create(directory.path().join("out")).unwrap();

    let result = run_unthreaded_to_file(
        Path::new(YES),
        &[],
        Stdio::null(),
        &output,
        64 * 1024,
        Duration::from_secs(30),
    );

    assert!(matches!(
        result,
        Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
            cap: 65_536,
            ..
        }))
    ));
}

/// A program that does not exist is a start failure, not a bound.
#[test]
fn a_missing_program_is_a_start_failure() {
    let directory = tempfile::tempdir().unwrap();
    let output = File::create(directory.path().join("out")).unwrap();

    let result = run_unthreaded_to_file(
        Path::new("/nonexistent/maran-no-such-program"),
        &[],
        Stdio::null(),
        &output,
        1024,
        Duration::from_secs(5),
    );

    assert!(matches!(result, Err(SpawnError::Start { .. })));
}
