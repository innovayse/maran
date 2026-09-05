//! Where a listing gets each half of an entry from.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::list_cron_entries::list_cron_entries;
use crate::cron::recording_cron_host::{
    FIRST_ID, RecordingCronHost, SECOND_ID, account, cmd_path, command, distro, entry_id,
    every_five_minutes, schedule,
};

/// An account with no crontab lists as empty rather than as a failure.
#[test]
fn an_absent_crontab_lists_as_empty() {
    let host = RecordingCronHost::new();

    let entries = list_cron_entries(&host, &account()).expect("listed");

    assert!(entries.is_empty());
}

/// Each entry's command comes back from its own file, not from the crontab.
#[test]
fn listing_returns_each_entrys_command_from_its_file() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &schedule("0", "3", "*", "*", "*"),
        &command("echo two"),
    )
    .expect("created");

    let entries = list_cron_entries(&host, &account()).expect("listed");

    assert_eq!(entries.len(), 2);
    assert_eq!(entries[0].id, entry_id(FIRST_ID));
    assert_eq!(entries[0].command.as_deref(), Some("echo one"));
    assert_eq!(entries[1].id, entry_id(SECOND_ID));
    assert_eq!(entries[1].command.as_deref(), Some("echo two"));
    // The table never held either command — that is the whole design.
    let table = host.crontab().expect("a table was installed");
    assert!(!table.contains("echo one"));
    assert!(!table.contains("echo two"));
}

/// An entry whose command file has gone is still listed, with no command.
#[test]
fn an_entry_whose_command_file_is_missing_is_listed_without_one() {
    // Hiding it would hide exactly the entry an operator needs to see: the
    // schedule is still installed and cron will still try to run it.
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    host.take_file(&cmd_path(&account(), &entry_id(FIRST_ID)));

    let entries = list_cron_entries(&host, &account()).expect("listed");

    assert_eq!(entries.len(), 1);
    assert_eq!(entries[0].command, None);
}

/// Lines an administrator wrote are never reported as the panel's.
#[test]
fn a_foreign_entry_is_not_listed() {
    let host = RecordingCronHost::with_crontab("30 4 * * 1 /opt/backup.sh\n");

    let entries = list_cron_entries(&host, &account()).expect("listed");

    assert!(entries.is_empty());
}

/// A disabled entry is listed, and says so.
#[test]
fn a_disabled_entry_is_listed_as_disabled() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    crate::cron::set_cron_entry_enabled::set_cron_entry_enabled(
        &host,
        distro(),
        &account(),
        &entry_id(FIRST_ID),
        false,
    )
    .expect("disabled");

    let entries = list_cron_entries(&host, &account()).expect("listed");

    assert_eq!(entries.len(), 1);
    assert!(!entries[0].enabled);
    assert_eq!(entries[0].command.as_deref(), Some("echo one"));
}

/// A crontab that cannot be read is a failure, not an empty list.
#[test]
fn a_crontab_that_cannot_be_read_stops_the_listing() {
    let host = RecordingCronHost::new();
    host.refuse_crontab_read_with(7);

    let refusal = list_cron_entries(&host, &account());

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 7 }));
}
