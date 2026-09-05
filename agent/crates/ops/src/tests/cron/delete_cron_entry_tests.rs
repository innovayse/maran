//! What a deletion removes, in which order, and what it does twice.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::delete_cron_entry::delete_cron_entry;
use crate::cron::recording_cron_host::{
    ABSENT_ID, FIRST_ID, RecordingCronHost, account, cmd_path, command, distro, entry_id,
    every_five_minutes, exit_path, log_path,
};

/// A deletion takes the marker out of the table and every file with it.
#[test]
fn deleting_removes_the_block_and_both_run_files() {
    let host = RecordingCronHost::new();
    let account = account();
    let id = entry_id(FIRST_ID);
    create_cron_entry(
        &host,
        distro(),
        &account,
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    // As a run of the entry would have left them.
    host.put_file(&log_path(&account, &id), "the last run said this\n");
    host.put_file(&exit_path(&account, &id), "0\n");

    delete_cron_entry(&host, distro(), &account, &id).expect("deleted");

    let table = host.crontab().expect("a table was installed");
    assert!(
        !table.contains(FIRST_ID),
        "the marker and its line must be gone: {table}"
    );
    assert_eq!(host.file(&cmd_path(&account, &id)), None);
    assert_eq!(host.file(&log_path(&account, &id)), None);
    assert_eq!(host.file(&exit_path(&account, &id)), None);
}

/// Deleting an entry that is not there is the idempotent answer, not a crash.
#[test]
fn deleting_an_unknown_entry_reports_not_found() {
    let host = RecordingCronHost::new();

    let refusal = delete_cron_entry(&host, distro(), &account(), &entry_id(ABSENT_ID));

    assert_eq!(refusal, Err(CronError::NotFound));
    assert!(host.installs().is_empty(), "nothing may be installed");
}

/// A refused install leaves the entry running and its files in place.
#[test]
fn a_refused_install_leaves_the_entrys_files_alone() {
    // The files are what the still-installed line names: removing them while
    // the entry is live would leave cron failing every minute.
    let host = RecordingCronHost::new();
    let account = account();
    let id = entry_id(FIRST_ID);
    create_cron_entry(
        &host,
        distro(),
        &account,
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    host.refuse_install_with(1);

    let refusal = delete_cron_entry(&host, distro(), &account, &id);

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 1 }));
    assert!(host.file(&cmd_path(&account, &id)).is_some());
}

/// Files that will not come away are reported after the entry is already gone.
#[test]
fn files_that_cannot_be_removed_are_reported_after_the_entry_is_out_of_the_table() {
    let host = RecordingCronHost::new();
    let account = account();
    let id = entry_id(FIRST_ID);
    create_cron_entry(
        &host,
        distro(),
        &account,
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    host.refuse_removals();

    let refusal = delete_cron_entry(&host, distro(), &account, &id);

    assert_eq!(refusal, Err(CronError::EntryFileUnremovable));
    let table = host.crontab().expect("a table was installed");
    assert!(
        !table.contains(FIRST_ID),
        "the entry must already be out of the table: {table}"
    );
}
