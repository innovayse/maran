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

/// The program that materialises the stand-in, in a process of its own.
///
/// A platform literal, which `ops` forbids in production code and
/// `maran structure` rule 17 exempts tests from — the alternative, asking the
/// `DistroAdapter` for it, would put a path in the shipped adapter that nothing
/// the agent does needs.
const COPY_PROGRAM: &str = "/bin/cp";

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
///
/// # Why the script is copied into place by another process
///
/// The obvious version of this helper — write the file, chmod it, exec it —
/// fails about once in twenty-five runs of this crate's suite with `ETXTBSY`,
/// which `run_client` reports as `DbError::ClientUnavailable`. Not because this
/// thread still holds the file open: `std::fs::write` closes its descriptor
/// before returning, and a leak here would fail every run rather than one in
/// twenty-five. It is another thread that holds it. `libtest` runs this crate's
/// tests in parallel and many of them spawn processes; a `fork` in any other
/// thread, landing inside the moment this one has the file open for writing,
/// copies that descriptor into a child that has not reached `execve` yet. The
/// kernel refuses to execute a file any process may still be writing, so the
/// exec a few microseconds later is refused — and the closer the write is to
/// the exec, the likelier the overlap.
///
/// So the file this test executes is written by a process that is not this one.
/// No descriptor in THIS process ever points at `client` for writing, so no
/// fork in any of its threads can copy one, and by the time the copy has been
/// waited for there is no writer anywhere. That is a property, not a smaller
/// window: retrying the spawn or serialising these tests would leave the race
/// in place and merely make it rarer, and the contention is not between these
/// tests — it is with every other test in the binary that spawns anything.
///
/// The source the copy reads from is written here in the ordinary way. It is
/// never executed, so a descriptor briefly open on it is nobody's problem.
fn stand_in_client(directory: &Path, answer: &str) -> std::path::PathBuf {
    let argv = directory.join("argv");
    let stdin = directory.join("stdin");

    stand_in_program(
        directory,
        &format!(
            "printf '%s\\n' \"$@\" > {argv}\ncat > {stdin}\nprintf '%s' '{answer}'\n",
            argv = argv.display(),
            stdin = stdin.display(),
        ),
    )
}

/// Writes an executable stand-in whose body is `body` into `directory` and
/// returns its path.
///
/// The materialisation discipline documented on [`stand_in_client`] lives here,
/// so every stand-in this file plants gets it — a second one written the
/// obvious way would bring the `ETXTBSY` flake back on its own.
fn stand_in_program(directory: &Path, body: &str) -> std::path::PathBuf {
    let source = directory.join("client.source");
    let program = directory.join("client");

    std::fs::write(&source, format!("#!/bin/sh\n{body}")).unwrap();

    let copied = std::process::Command::new(COPY_PROGRAM)
        .arg(&source)
        .arg(&program)
        .status()
        .unwrap();
    assert!(
        copied.success(),
        "the stand-in could not be copied into place: {copied:?}"
    );

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

    assert_eq!(answer, Err(DbError::ClientUnavailable));
}

#[test]
fn a_client_killed_by_a_signal_is_reported_as_killed_and_not_as_one_that_never_started() {
    // The condition that used to be invisible. A client the machine kills
    // mid-statement may already have executed it, so reporting it as a client
    // that was never started tells the panel to retry something that may have
    // happened — and told two earlier sessions the wrong thing about a flake in
    // this very file.
    //
    // The stand-in reads its whole standard input before it kills itself, so
    // this test observes the exit path and not a broken pipe on the way in.
    let root = tempfile::tempdir().unwrap();
    let program = stand_in_program(root.path(), "cat > /dev/null\nkill -9 $$\n");

    let answer = run_client(&program.display().to_string(), "SELECT 1");

    assert_eq!(answer, Err(DbError::ClientKilled { signal: 9 }));
}

#[test]
fn a_client_that_answered_with_a_number_is_told_apart_from_one_that_never_ran() {
    // The finding this file's three variants exist for, asserted as the one
    // proposition that covers it: the four conditions must not share a value.
    // Every one of them used to be `ClientFailed { code: -1 }`, which is why a
    // spawn refused by the kernel was read for two lanes as a server answer.
    let root = tempfile::tempdir().unwrap();
    let refused_by_the_server = stand_in_program(
        root.path(),
        "cat > /dev/null\nprintf 'ERROR 1064 (42000): You have an error\\n' >&2\nexit 1\n",
    );
    let answered = run_client(&refused_by_the_server.display().to_string(), "SELECT 1");

    let killed = tempfile::tempdir().unwrap();
    let suicidal = stand_in_program(killed.path(), "cat > /dev/null\nkill -9 $$\n");
    let signalled = run_client(&suicidal.display().to_string(), "SELECT 1");

    let never_started = run_client(
        &killed.path().join("absent").display().to_string(),
        "SELECT 1",
    );
    let never_sent = run_client(&suicidal.display().to_string(), "SELECT 1; SELECT 2");

    assert_eq!(answered, Err(DbError::ClientFailed { code: 1064 }));
    assert_eq!(signalled, Err(DbError::ClientKilled { signal: 9 }));
    assert_eq!(never_started, Err(DbError::ClientUnavailable));
    assert_eq!(never_sent, Err(DbError::StatementRefused));

    // And none of them carries a word of the client's own output: `ERROR 1064
    // (42000): You have an error` was on the stand-in's standard error, and the
    // server quotes back what it refused — on two paths a customer's password.
    for failure in [&answered, &signalled, &never_started, &never_sent] {
        let Err(error) = failure else {
            panic!("{failure:?} was expected to be a failure");
        };
        assert!(
            !error.to_string().contains("42000") && !error.to_string().contains("You have an"),
            "a database failure carried the client's own words: {error}"
        );
    }
}

#[test]
fn a_statement_carrying_a_second_statement_is_refused_before_anything_is_spawned() {
    let root = tempfile::tempdir().unwrap();
    let program = stand_in_client(root.path(), "");

    let answer = run_client(
        &program.display().to_string(),
        "SELECT 1; DROP DATABASE `other`",
    );

    assert_eq!(answer, Err(DbError::StatementRefused));
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
