//! Tests for the payload `GetCronEntryOutput` answers with.
//!
//! Every assertion here is about the difference between "no reading" and "a
//! reading that happens to be empty or zero" — the distinction the three
//! `optional` fields of `cron.proto` exist for, and the one a panel draws a
//! green tick from if it is lost.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::{Duration, UNIX_EPOCH};

use maran_ops::cron::{CronEntryOutput, CronRunRecord};

use super::entry_output;

/// A fixed instant, so the timestamp is asserted against an exact number rather
/// than a range.
///
/// That exactness is what would catch a mapping that reached for
/// `SystemTime::now()` instead of the record's own mtime (rules/testing.md
/// "Determinism"): a "two calls agree" assertion would not, since both calls
/// land in the same second.
const RAN_AT: u64 = 1_780_000_000;

#[test]
fn an_entry_that_has_never_run_reports_three_absences_and_no_zeroes() {
    // The whole point of the explicit presence. A 0 exit code here would show
    // as "the last run succeeded" for a job that has never started.
    let wire = entry_output(CronEntryOutput {
        output: None,
        last_run: None,
    });

    assert_eq!(wire.output, None);
    assert_eq!(wire.last_exit_code, None);
    assert_eq!(wire.last_run_at_unix, None);
}

#[test]
fn an_entry_that_ran_and_said_nothing_reports_an_empty_string_not_an_absence() {
    // The other side of the same distinction: it ran, and it printed nothing.
    let wire = entry_output(CronEntryOutput {
        output: Some(String::new()),
        last_run: Some(CronRunRecord {
            exit_code: Some(0),
            ran_at: UNIX_EPOCH + Duration::from_secs(RAN_AT),
        }),
    });

    assert_eq!(wire.output, Some(String::new()));
    assert_eq!(wire.last_exit_code, Some(0));
    assert_eq!(
        wire.last_run_at_unix,
        Some(i64::try_from(RAN_AT).expect("the fixed instant fits"))
    );
}

#[test]
fn a_failing_run_reports_its_own_status_and_its_own_output() {
    let wire = entry_output(CronEntryOutput {
        output: Some("permission denied\n".to_owned()),
        last_run: Some(CronRunRecord {
            exit_code: Some(13),
            ran_at: UNIX_EPOCH + Duration::from_secs(RAN_AT),
        }),
    });

    assert_eq!(wire.output.as_deref(), Some("permission denied\n"));
    assert_eq!(wire.last_exit_code, Some(13));
}

#[test]
fn a_run_whose_status_could_not_be_read_still_reports_when_it_ran() {
    // The two come from one file — its content and its modification time — so
    // an unreadable content with a readable time is a real state. Reporting the
    // time is what tells an operator the job is running at all.
    let wire = entry_output(CronEntryOutput {
        output: Some("output\n".to_owned()),
        last_run: Some(CronRunRecord {
            exit_code: None,
            ran_at: UNIX_EPOCH + Duration::from_secs(RAN_AT),
        }),
    });

    assert_eq!(wire.last_exit_code, None);
    assert!(wire.last_run_at_unix.is_some());
}

#[test]
fn a_run_timestamp_before_the_epoch_is_reported_as_absent_rather_than_negative() {
    // A host with a badly set clock, or a file whose mtime was set by hand.
    // There is no reading the agent can stand behind, and "absent" is the same
    // answer it gives for a run that never happened. This goes red if somebody
    // reaches for a signed duration and reports a negative instant, which a
    // panel would draw as a run in 1969.
    let wire = entry_output(CronEntryOutput {
        output: Some(String::new()),
        last_run: Some(CronRunRecord {
            exit_code: Some(0),
            ran_at: UNIX_EPOCH - Duration::from_secs(1),
        }),
    });

    assert_eq!(wire.last_run_at_unix, None);
    // And the rest of the reading still travels: one unusable field does not
    // discard the others.
    assert_eq!(wire.last_exit_code, Some(0));
}
