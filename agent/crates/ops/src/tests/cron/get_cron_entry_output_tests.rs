//! What the panel is told about an entry's last run.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::get_cron_entry_output::get_cron_entry_output;
use crate::cron::recording_cron_host::{
    ABSENT_ID, FIRST_ID, RecordingCronHost, account, command, distro, entry_id, every_five_minutes,
    exit_path, log_path, run_record,
};

/// A host with one entry created, ready to have run files put on it.
fn host_with_one_entry() -> RecordingCronHost {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");

    host
}

/// The output and the run record come back together.
#[test]
fn an_entrys_output_and_last_run_are_reported_together() {
    let host = host_with_one_entry();
    let account = account();
    let id = entry_id(FIRST_ID);
    host.put_file(&log_path(&account, &id), "the last run said this\n");
    host.put_run_record(
        &exit_path(&account, &id),
        run_record(Some(0), 1_700_000_000),
    );

    let output = get_cron_entry_output(&host, &account, &id).expect("read");

    assert_eq!(output.output.as_deref(), Some("the last run said this\n"));
    assert_eq!(
        output.last_run.expect("a record").exit_code,
        Some(0),
        "the exit file's content is the status"
    );
}

/// An entry that has never run reports both halves absent, not an error.
#[test]
fn an_entry_that_has_never_run_reports_no_output_and_no_record() {
    let host = host_with_one_entry();

    let output = get_cron_entry_output(&host, &account(), &entry_id(FIRST_ID)).expect("read");

    assert_eq!(output.output, None);
    assert_eq!(output.last_run, None);
}

/// An id this account does not own is refused rather than answered emptily.
#[test]
fn asking_for_an_unknown_entry_reports_not_found() {
    // Without the crontab check this would report "this entry has never run",
    // which reads to an operator exactly like a real entry that has not fired.
    let host = host_with_one_entry();

    let refusal = get_cron_entry_output(&host, &account(), &entry_id(ABSENT_ID));

    assert_eq!(refusal, Err(CronError::NotFound));
}

/// An exit file holding something that is not a status reads as unknown.
#[test]
fn an_exit_file_that_is_not_a_status_reports_an_unknown_code() {
    let host = host_with_one_entry();
    let account = account();
    let id = entry_id(FIRST_ID);
    host.put_run_record(&exit_path(&account, &id), run_record(None, 1_700_000_000));

    let output = get_cron_entry_output(&host, &account, &id).expect("read");

    assert_eq!(output.last_run.expect("a record").exit_code, None);
}

/// A file that is there and cannot be read is a failure, not an absent one.
#[test]
fn an_unreadable_entry_file_is_reported() {
    let host = host_with_one_entry();
    host.refuse_entry_reads();

    let refusal = get_cron_entry_output(&host, &account(), &entry_id(FIRST_ID));

    assert_eq!(refusal, Err(CronError::EntryFileUnreadable));
}

/// Only the end of a long output is returned.
#[test]
fn only_the_tail_of_a_long_output_is_returned() {
    // The file is written by a command the customer chose, so its size is
    // theirs to decide; the whole of it must never reach the daemon's memory.
    let host = host_with_one_entry();
    let account = account();
    let id = entry_id(FIRST_ID);
    let long = "x".repeat(128 * 1024);
    host.put_file(&log_path(&account, &id), &long);

    let output = get_cron_entry_output(&host, &account, &id).expect("read");

    assert_eq!(output.output.expect("output").len(), 64 * 1024);
}
