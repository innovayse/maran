//! What a run of `crontab -l` is allowed to mean.
//!
//! One decision is tested here and it is the one that can destroy a customer's
//! data: whether an account has no crontab. Answering "none" when it has one
//! makes every caller parse an empty document, and the next install writes that
//! empty document back over everything the account had. Standard output is the
//! account's own file, so nothing on it may reach this decision.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::command_outcome::CommandOutcome;

use super::{read_outcome, run_bounded_for_test};
use crate::cron::cron_error::CronError;

/// The ceiling the spawn probes below run under.
const PROBE_CEILING: u64 = 4096;

/// How many missing files `cat` is asked for, to overflow a pipe's capacity.
///
/// Each one is about eighty bytes of standard error, so three thousand is a
/// couple of hundred kilobytes — comfortably past the roughly 64 KiB a pipe
/// holds before it blocks its writer.
const FLOODING_ARGUMENTS: usize = 3000;

/// An outcome with the three fields a spawn produces.
fn outcome(status: i32, stdout: &str, stderr: &str) -> CommandOutcome {
    CommandOutcome {
        status,
        stdout: stdout.to_owned(),
        stderr: stderr.to_owned(),
    }
}

/// A successful listing is the table, whatever the table happens to say.
#[test]
fn a_successful_listing_is_the_table_it_printed() {
    let table = "# no crontab for alice\n30 4 * * 1 /opt/backup.sh\n";

    let answer = read_outcome(&outcome(0, table, "")).expect("read");

    assert_eq!(answer.as_deref(), Some(table));
}

/// An account that has never had a crontab reads as absent.
#[test]
fn an_account_with_no_table_reads_as_absent() {
    let answer = read_outcome(&outcome(1, "", "no crontab for alice\n")).expect("read");

    assert_eq!(answer, None);
}

/// A crontab whose own bytes say "no crontab for" is never read as absent.
#[test]
fn a_crontab_whose_own_bytes_say_no_crontab_for_is_never_read_as_absent() {
    // The account writes standard output. If a failed listing that had already
    // printed the table could be read as "this account has nothing", the next
    // install would write an empty document over every entry the account had,
    // foreign lines included. The refusal must come back as a refusal.
    let table = "# no crontab for alice\n30 4 * * 1 /opt/backup.sh --keep 7\n";

    let refusal = read_outcome(&outcome(1, table, "no crontab for alice\n"));

    assert_eq!(
        refusal,
        Err(CronError::CrontabRefused { code: 1 }),
        "a customer's own bytes must not decide that they have no crontab"
    );
}

/// The message is believed on standard error alone, not wherever it appears.
#[test]
fn the_absent_message_on_standard_output_alone_is_not_believed() {
    let refusal = read_outcome(&outcome(1, "no crontab for alice\n", ""));

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 1 }));
}

/// A refusal that is not the absent case keeps the program's own status.
#[test]
fn another_refusal_is_reported_with_the_programs_status() {
    let refusal = read_outcome(&outcome(7, "", "crontab: must be privileged\n"));

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 7 }));
}

/// An account with an empty crontab is not the same as one with none.
#[test]
fn an_empty_table_that_listed_successfully_is_not_an_absent_one() {
    // Exit zero and no output is an account whose crontab exists and holds
    // nothing. Reading it as absent would be harmless today and is still wrong:
    // the two are different states of the host.
    let answer = read_outcome(&outcome(0, "", "")).expect("read");

    assert_eq!(answer.as_deref(), Some(""));
}

/// Every spawn runs under a pinned locale, so a message is matched in one
/// language.
#[test]
fn every_spawn_pins_the_locale_so_the_message_language_is_not_ambient() {
    // The absent-crontab answer is decided by matching a MESSAGE, so the
    // language that message is printed in is part of that decision. Inheriting
    // the daemon's environment would make it whatever `LANG` the unit file, the
    // installer or an operator's shell happened to leave behind.
    //
    // `printenv LC_ALL` prints the value and exits 0 when it is set, and prints
    // nothing and exits 1 when it is not, so the two cases are told apart by
    // both fields. One argv array, no shell.
    let outcome =
        run_bounded_for_test("printenv", &["LC_ALL"], PROBE_CEILING).expect("printenv runs");

    assert_eq!(outcome.status, 0, "LC_ALL reached the child");
    assert_eq!(outcome.stdout.trim(), "C");
}

/// A child that floods standard error does not deadlock the read.
#[test]
fn a_child_that_floods_standard_error_does_not_deadlock_the_read() {
    // A pipe nobody reads fills at about 64 KiB, and a child blocked writing
    // into a full one never exits. Reading standard output to its end first and
    // standard error afterwards therefore deadlocks both, in the root daemon,
    // with no timeout anywhere — the class `Command::output()` avoids by
    // reading concurrently, and `output()` is what the bounded spawn replaced.
    //
    // **If the concurrent drain were taken away this test would never return**,
    // so it fails as a timeout rather than as an assertion — the honest shape
    // for this particular bug, and the same one `follow_log`'s FIFO test has.
    //
    // `cat` against many missing files writes one error line each to standard
    // error and nothing at all to standard output: far past a pipe's capacity,
    // from one argv array, with no shell.
    let names: Vec<String> = (0..FLOODING_ARGUMENTS)
        .map(|index| format!("/nonexistent/maran-cron-stderr-flood-probe-{index:06}"))
        .collect();
    let arguments: Vec<&str> = names.iter().map(String::as_str).collect();

    let outcome =
        run_bounded_for_test("cat", &arguments, PROBE_CEILING).expect("cat runs and exits");

    assert_ne!(outcome.status, 0, "every named file is missing");
    assert!(
        outcome.stdout.is_empty(),
        "a missing file produces no standard output: {:?}",
        outcome.stdout
    );
    assert!(
        outcome.stderr.len() as u64 <= PROBE_CEILING,
        "only the bounded prefix is kept, not the whole flood: {}",
        outcome.stderr.len()
    );
    assert!(
        outcome
            .stderr
            .contains("maran-cron-stderr-flood-probe-000000"),
        "and the prefix is the START of what the child said: {:?}",
        outcome.stderr
    );
}

/// A child that outruns the ceiling still reports its own status.
#[test]
fn a_child_that_outruns_the_ceiling_still_reports_its_own_status() {
    // The other half of the drain, and the half the deadlock test cannot see.
    // If the drain stopped at the ceiling it would return and drop the pipe,
    // closing the read end — and the child's next write would earn `SIGPIPE`.
    // A child killed by a signal has no exit code, so the caller would be told
    // `-1` and a legible refusal would arrive as an unexplained one.
    //
    // `cat` exits 1 for a missing file. Under a drain that stops at the ceiling
    // this comes back as `-1` instead; under the drain as written, as `1`.
    let names: Vec<String> = (0..FLOODING_ARGUMENTS)
        .map(|index| format!("/nonexistent/maran-cron-status-probe-{index:06}"))
        .collect();
    let arguments: Vec<&str> = names.iter().map(String::as_str).collect();

    let outcome =
        run_bounded_for_test("cat", &arguments, PROBE_CEILING).expect("cat runs and exits");

    assert_eq!(
        outcome.status, 1,
        "the child's own status, not the -1 of a process killed by a signal"
    );
}
