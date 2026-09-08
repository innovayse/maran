//! What the real host hands the real client.
//!
//! Every other test in this area drives a fake that records the statement text,
//! which can say nothing about the thing that matters here: WHERE the statement
//! travels. A customer's database password is inside it, and an argv array is
//! world-readable through `/proc/<pid>/cmdline` while the client runs. Only a
//! real spawn can answer that, so these tests point [`run_client`] at a
//! stand-in program that records its own argv and its own standard input and
//! then read both back. No server, no root, no database.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;

use super::{is_single_statement, run_client};
use crate::db::db_error::DbError;

/// A statement of the shape that actually carries a credential.
const CREATE_USER: &str =
    "CREATE USER IF NOT EXISTS 'alice_shop'@'localhost' IDENTIFIED BY 'Str0ng-Pass_word'";

/// The password inside [`CREATE_USER`], as the string a leak would show.
const PASSWORD: &str = "Str0ng-Pass_word";

/// Writes an executable stand-in client into `directory` and returns its path.
///
/// It records its whole argv into `argv` and everything it reads from standard
/// input into `stdin`, then prints `answer` and exits successfully — which is
/// what makes it a client this code path cannot tell from the real one.
///
/// It is a script rather than a compiled program because the recording has to
/// happen inside the process this code spawned; nothing about the spawn under
/// test is a shell, and the script's own interpreter is chosen by its shebang
/// exactly as any other executable's interpreter is.
fn stand_in_client(directory: &Path, answer: &str) -> std::path::PathBuf {
    let argv = directory.join("argv");
    let stdin = directory.join("stdin");
    let program = directory.join("client");

    std::fs::write(
        &program,
        format!(
            "#!/bin/sh\nprintf '%s\\n' \"$@\" > {argv}\ncat > {stdin}\nprintf '%s' '{answer}'\n",
            argv = argv.display(),
            stdin = stdin.display(),
        ),
    )
    .unwrap();
    std::fs::set_permissions(&program, std::fs::Permissions::from_mode(0o700)).unwrap();

    program
}

#[test]
fn the_statement_never_becomes_an_argv_element() {
    let root = tempfile::tempdir().unwrap();
    let program = stand_in_client(root.path(), "");

    run_client(&program.display().to_string(), CREATE_USER).unwrap();

    let argv = std::fs::read_to_string(root.path().join("argv")).unwrap();
    // The vacuity guard is on the axis that can go blind: if the stand-in never
    // ran, or ran and recorded nothing, `argv` would be empty and an assertion
    // that the password is absent would pass for the wrong reason.
    assert!(
        argv.contains("--batch"),
        "the stand-in recorded no argv at all, so the check below sees nothing: {argv:?}"
    );
    assert!(
        !argv.contains(PASSWORD),
        "the customer's password reached the client's argv, where /proc exposes it: {argv:?}"
    );
    assert!(
        !argv.contains("--execute"),
        "the statement is still being passed as a flag argument: {argv:?}"
    );
}

#[test]
fn the_statement_reaches_the_client_on_standard_input() {
    let root = tempfile::tempdir().unwrap();
    let program = stand_in_client(root.path(), "");

    run_client(&program.display().to_string(), CREATE_USER).unwrap();

    // The inverse control for the test above: moving the statement off argv is
    // only correct if the client still RECEIVES it. A guard that merely stopped
    // sending the statement would pass the argv test and break every operation.
    let stdin = std::fs::read_to_string(root.path().join("stdin")).unwrap();
    assert_eq!(stdin, format!("{CREATE_USER}\n"));
}

#[test]
fn the_clients_output_comes_back_to_the_caller() {
    let root = tempfile::tempdir().unwrap();
    let program = stand_in_client(root.path(), "4\n");

    let answer = run_client(
        &program.display().to_string(),
        "SELECT COUNT(*) FROM mysql.user",
    );

    assert_eq!(answer.unwrap(), "4\n");
}

#[test]
fn a_client_that_cannot_be_started_is_reported_as_unavailable() {
    let root = tempfile::tempdir().unwrap();
    let missing = root.path().join("no-such-client");

    let answer = run_client(&missing.display().to_string(), "SELECT 1");

    assert_eq!(answer, Err(DbError::client_unavailable()));
}

#[test]
fn a_statement_carrying_a_second_statement_is_refused_before_anything_is_spawned() {
    let root = tempfile::tempdir().unwrap();
    let program = stand_in_client(root.path(), "");

    let answer = run_client(
        &program.display().to_string(),
        "SELECT 1; DROP DATABASE `other`",
    );

    assert_eq!(answer, Err(DbError::client_unavailable()));
    // Refused BEFORE the spawn, not after: the stand-in leaves both recordings
    // behind when it runs, so their absence is what proves nothing was started.
    assert!(!root.path().join("argv").exists());
    assert!(!root.path().join("stdin").exists());
}

#[test]
fn a_statement_carrying_a_newline_is_refused() {
    assert!(!is_single_statement("SELECT 1\nDROP DATABASE `other`"));
}

#[test]
fn a_statement_longer_than_the_ceiling_is_refused() {
    let long = format!("SELECT '{}'", "a".repeat(4096));

    assert!(!is_single_statement(&long));
}

#[test]
fn the_statements_this_area_really_builds_are_accepted() {
    // The inverse control for the guard: a check mutated to refuse everything
    // passes every test that only ever hands it something broken.
    assert!(is_single_statement(CREATE_USER));
    assert!(is_single_statement("DROP DATABASE `alice_shop`"));
    assert!(is_single_statement(
        "SELECT COALESCE(SUM(data_length + index_length), 0) \
         FROM information_schema.tables WHERE table_schema = 'alice_shop'"
    ));
}
