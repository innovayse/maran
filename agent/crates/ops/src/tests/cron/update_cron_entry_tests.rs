//! What an update rewrites, and what a refusal leaves behind.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::recording_cron_host::{
    ABSENT_ID, FIRST_ID, RecordingCronHost, account, cmd_path, command, distro, entry_id,
    every_five_minutes, schedule,
};
use crate::cron::update_cron_entry::update_cron_entry;

/// An update replaces the schedule in the table and the command in the file.
#[test]
fn updating_an_entry_rewrites_its_schedule_and_its_command_file() {
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

    update_cron_entry(
        &host,
        distro(),
        &account,
        &id,
        &schedule("0", "3", "*", "*", "*"),
        &command("echo two"),
    )
    .expect("updated");

    let table = host.crontab().expect("a table was installed");
    assert!(table.contains("0 3 * * * "), "the new schedule: {table}");
    assert!(!table.contains("*/5 * * * * "), "the old one is gone");
    assert_eq!(
        host.file(&cmd_path(&account, &id)),
        Some("echo two\n".to_owned())
    );
    assert!(!table.contains("echo two"), "still no command in the table");
}

/// Updating an entry that is not there is refused before anything is written.
#[test]
fn updating_an_unknown_entry_reports_not_found() {
    let host = RecordingCronHost::new();

    let refusal = update_cron_entry(
        &host,
        distro(),
        &account(),
        &entry_id(ABSENT_ID),
        &every_five_minutes(),
        &command("echo one"),
    );

    assert_eq!(refusal, Err(CronError::NotFound));
    assert!(host.installs().is_empty());
    assert!(host.file_paths().is_empty());
}

/// A refused install leaves both halves of the entry exactly as they were.
#[test]
fn a_refused_install_leaves_the_command_file_untouched() {
    // The install goes first for exactly this: `crontab` is the step that
    // refuses tables, and taking its refusal before the file is touched means a
    // rejected update changes nothing at all.
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
    let before = host.crontab().expect("a table was installed");
    host.refuse_install_with(1);

    let refusal = update_cron_entry(
        &host,
        distro(),
        &account,
        &id,
        &schedule("0", "3", "*", "*", "*"),
        &command("echo two"),
    );

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 1 }));
    assert_eq!(host.crontab(), Some(before));
    assert_eq!(
        host.file(&cmd_path(&account, &id)),
        Some("echo one\n".to_owned())
    );
}

/// A command file that will not take the new command is reported.
#[test]
fn a_command_file_that_cannot_be_written_is_reported_after_the_schedule_landed() {
    let host = RecordingCronHost::new();
    let id = entry_id(FIRST_ID);
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    host.refuse_writes();

    let refusal = update_cron_entry(
        &host,
        distro(),
        &account(),
        &id,
        &schedule("0", "3", "*", "*", "*"),
        &command("echo two"),
    );

    assert_eq!(refusal, Err(CronError::EntryFileUnwritable));
    let table = host.crontab().expect("a table was installed");
    assert!(table.contains("0 3 * * * "));
}
