//! What a run of `crontab -l` is allowed to mean.
//!
//! One decision is tested here and it is the one that can destroy a customer's
//! data: whether an account has no crontab. Answering "none" when it has one
//! makes every caller parse an empty document, and the next install writes that
//! empty document back over everything the account had. Standard output is the
//! account's own file, so nothing on it may reach this decision.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_agent_core::utils::spawn_error::SpawnError;

use super::{
    TABLE_TOO_LARGE, install_arguments, read_arguments, read_outcome, run_bounded_for_test,
    spawn_failure,
};
use crate::cron::cron_error::{CronError, PROGRAM_UNAVAILABLE};
use crate::safe_write::adapter_programs::timed_out;

/// The ceiling the spawn probes below run under.
const PROBE_CEILING: u64 = 4096;

/// A ceiling comfortably above everything the flooding probes print.
const ROOMY_CEILING: u64 = 1024 * 1024;

/// Where `printenv` lives on both families.
const PRINTENV: &str = "/usr/bin/printenv";

/// Where `cat` lives on both families.
const CAT: &str = "/usr/bin/cat";

/// Where `head` lives on both families.
const HEAD: &str = "/usr/bin/head";

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
    let outcome = run_bounded_for_test(Path::new(PRINTENV), &["LC_ALL"], PROBE_CEILING)
        .expect("printenv runs");

    assert_eq!(outcome.status, 0, "LC_ALL reached the child");
    assert_eq!(outcome.stdout.trim(), "C");
}

/// A child that floods standard error does not deadlock the read.
#[test]
fn a_child_that_floods_standard_error_does_not_deadlock_the_read() {
    // A pipe nobody reads fills at about 64 KiB, and a child blocked writing
    // into a full one never exits. Reading standard output to its end first and
    // standard error afterwards therefore deadlocks both, in the root daemon,
    // until the deadline kills it - the class `Command::output()` avoids by
    // reading concurrently.
    //
    // The flood is far past a pipe's capacity and under the ceiling, so the run
    // must END with the child's own status and the whole of what it said.
    //
    // `cat` against many missing files writes one error line each to standard
    // error and nothing at all to standard output, from one argv array, with no
    // shell.
    let names: Vec<String> = (0..FLOODING_ARGUMENTS)
        .map(|index| format!("/nonexistent/maran-cron-stderr-flood-probe-{index:06}"))
        .collect();
    let arguments: Vec<&str> = names.iter().map(String::as_str).collect();

    let outcome = run_bounded_for_test(Path::new(CAT), &arguments, ROOMY_CEILING)
        .expect("cat runs and exits");

    assert_eq!(
        outcome.status, 1,
        "the child's own status, not the -1 of a process killed by a signal"
    );
    assert!(
        outcome.stdout.is_empty(),
        "a missing file produces no standard output: {:?}",
        outcome.stdout
    );
    assert!(
        outcome
            .stderr
            .contains("maran-cron-stderr-flood-probe-000000"),
        "the start of what the child said is kept: {:?}",
        outcome.stderr.len()
    );
    assert!(
        outcome.stderr.contains(&format!(
            "maran-cron-stderr-flood-probe-{:06}",
            FLOODING_ARGUMENTS - 1
        )),
        "and so is the end: nothing was cut"
    );
}

/// An answer larger than the ceiling is refused whole, never cut short.
#[test]
fn a_child_that_outruns_the_ceiling_is_refused_whole_and_not_truncated() {
    // Half a crontab parsed and re-installed would delete the other half, so a table
    // above the ceiling must surface as an error and never as a shortened answer. The
    // same flood under the roomy ceiling above succeeds, which is the inverse control:
    // only the ceiling differs.
    let names: Vec<String> = (0..FLOODING_ARGUMENTS)
        .map(|index| format!("/nonexistent/maran-cron-status-probe-{index:06}"))
        .collect();
    let arguments: Vec<&str> = names.iter().map(String::as_str).collect();

    let refusal = run_bounded_for_test(Path::new(CAT), &arguments, PROBE_CEILING);

    assert_eq!(
        refusal.unwrap_err(),
        CronError::CrontabRefused {
            code: TABLE_TOO_LARGE
        }
    );
}

/// Exactly the ceiling is accepted and one byte more is refused.
#[test]
fn an_answer_of_exactly_the_ceiling_is_accepted_and_one_byte_more_is_refused() {
    // The boundary, with its inverse in the same test, so a ceiling mutated by one
    // in either direction turns one of the two assertions red.
    let exactly = PROBE_CEILING.to_string();
    let one_more = (PROBE_CEILING + 1).to_string();

    let accepted = run_bounded_for_test(
        Path::new(HEAD),
        &["-c", &exactly, "/dev/zero"],
        PROBE_CEILING,
    )
    .expect("exactly the ceiling is accepted");
    let refused = run_bounded_for_test(
        Path::new(HEAD),
        &["-c", &one_more, "/dev/zero"],
        PROBE_CEILING,
    );

    assert_eq!(accepted.stdout.len() as u64, PROBE_CEILING);
    assert_eq!(
        refused.unwrap_err(),
        CronError::CrontabRefused {
            code: TABLE_TOO_LARGE
        }
    );
}

/// A program that cannot be started is the sentinel code, not a bound.
#[test]
fn a_program_that_cannot_be_started_is_reported_with_the_unavailable_code() {
    let refusal = run_bounded_for_test(
        Path::new("/nonexistent/maran-cron-no-such-program"),
        &[],
        PROBE_CEILING,
    );

    assert_eq!(
        refusal.unwrap_err(),
        CronError::CrontabRefused {
            code: PROGRAM_UNAVAILABLE
        }
    );
}

/// A crontab that outlives its deadline is a bound, and never "too large" or "missing".
#[test]
fn a_crontab_spawn_that_times_out_is_a_bound_and_never_table_too_large() {
    // "The program never ran" and "its answer was too large" are both false of a
    // program that was running when it was stopped, and `CrontabRefused` also carries
    // the statement that the live table is unchanged, which a kill cannot make.
    let bound = timed_out("/usr/bin/crontab");

    let mapped = spawn_failure(SpawnError::Bound(bound.clone()));

    assert_eq!(mapped, CronError::Bound(bound));
}

/// The inverse controls for the mapping: each other failure keeps its own code.
#[test]
fn an_output_overflow_is_the_too_large_code_and_every_other_failure_the_unavailable_one() {
    let overflow = spawn_failure(SpawnError::Bound(BoundExceeded::OutputTooLarge {
        program: "/usr/bin/crontab".to_owned(),
        stream: OutputStream::Stdout,
        cap: PROBE_CEILING,
    }));
    let not_started = spawn_failure(SpawnError::Start {
        program: "/usr/bin/crontab".to_owned(),
        reason: "missing".to_owned(),
    });
    let not_fed = spawn_failure(SpawnError::Input {
        program: "/usr/bin/crontab".to_owned(),
    });
    let not_waited = spawn_failure(SpawnError::Wait {
        program: "/usr/bin/crontab".to_owned(),
        reason: "gone".to_owned(),
    });

    assert_eq!(
        overflow,
        CronError::CrontabRefused {
            code: TABLE_TOO_LARGE
        }
    );
    for other in [not_started, not_fed, not_waited] {
        assert_eq!(
            other,
            CronError::CrontabRefused {
                code: PROGRAM_UNAVAILABLE
            }
        );
    }
}

/// The install ends option parsing before the file it names.
#[test]
fn the_install_receives_end_of_options_before_the_file() {
    let arguments = install_arguments("alice", "/run/maran/crontab-1-2");

    assert_eq!(
        arguments,
        ["-u", "alice", "--", "/run/maran/crontab-1-2"],
        "the file comes after `--`, so it can never be read as an option"
    );
}

/// The inverse control: the read has no operand after its options, so it gets no `--`.
#[test]
fn the_read_has_no_end_of_options_because_no_operand_follows_its_options() {
    let arguments = read_arguments("alice");

    assert_eq!(arguments, ["-u", "alice", "-l"]);
    assert!(!arguments.contains(&"--"));
}

/// The read is a query and the install a mutation, in that order in the file.
/// A table read under the install's class (or the reverse) would change how
/// long a hung `crontab` may hold the account's cron lock.
#[test]
fn the_crontab_read_is_a_query_and_the_install_a_mutation() {
    let classes = crate::test_support::call_class_census::classes_named_in_code(include_str!(
        "../../cron/crontab_spool.rs"
    ));

    assert_eq!(classes, ["Query", "Mutation"]);
}
