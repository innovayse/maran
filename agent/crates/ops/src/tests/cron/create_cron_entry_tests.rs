//! What a creation writes, in which order, and what it refuses to do twice.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::recording_cron_host::{
    FIRST_ID, RecordingCronHost, account, cmd_path, command, distro, entry_id, every_five_minutes,
    schedule,
};

/// The command file holds the command exactly, and one newline after it.
#[test]
fn the_command_file_holds_the_command_verbatim_with_one_trailing_newline() {
    // `%` and `#` are ordinary shell text in a file and must survive untouched:
    // they are legal in a command precisely because the command never reaches a
    // crontab line, where cron would rewrite the first and comment out the
    // rest.
    let text = "printf '%s\\n' hi # done";
    let host = RecordingCronHost::new();

    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command(text),
    )
    .expect("created");

    let written = host
        .file(&cmd_path(&account(), &entry_id(FIRST_ID)))
        .expect("a command file was written");
    assert_eq!(written, format!("{text}\n"));
}

/// A second entry with the same schedule and command is refused before any
/// write.
#[test]
fn creating_an_identical_entry_reports_already_exists_and_writes_nothing() {
    // The comparison reads the `.cmd` files back, because the crontab no longer
    // carries commands. A retry after a lost reply must not leave the customer
    // with one entry they can see and one they cannot explain.
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    let after_first = host.crontab().expect("a table was installed");

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    );

    assert_eq!(refusal, Err(CronError::AlreadyExists));
    assert_eq!(host.installs().len(), 1, "no second table may be installed");
    assert_eq!(host.file_paths().len(), 1, "no second file may be written");
    assert_eq!(host.crontab(), Some(after_first));
}

/// A disabled twin still counts as the entry the customer already has.
#[test]
fn creating_an_entry_that_matches_a_disabled_one_is_still_a_duplicate() {
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

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    );

    assert_eq!(refusal, Err(CronError::AlreadyExists));
}

/// The same command at a different time is a different entry.
#[test]
fn the_same_command_at_another_schedule_is_not_a_duplicate() {
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
        &command("echo one"),
    )
    .expect("created a second entry");

    assert_eq!(host.file_paths().len(), 2);
}

/// A refused install takes the command file away with it.
#[test]
fn a_failed_install_leaves_no_orphan_command_file() {
    // The entry is not in the crontab, so a file left behind is litter inside
    // the customer's home that nothing will ever run and nothing will ever
    // clean up.
    let host = RecordingCronHost::new();
    host.refuse_install_with(1);

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    );

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 1 }));
    assert!(
        host.file_paths().is_empty(),
        "the command file must be removed again: {:?}",
        host.file_paths()
    );
}

/// A creation reports the id it minted, so the caller can name the entry.
#[test]
fn creating_an_entry_reports_the_id_it_minted() {
    let host = RecordingCronHost::new();

    let id = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");

    assert_eq!(id, entry_id(FIRST_ID));
}

/// A host that cannot mint an id writes nothing at all.
#[test]
fn an_entry_id_that_cannot_be_minted_stops_the_creation_before_any_write() {
    let host = RecordingCronHost::new();
    host.refuse_ids();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    );

    assert_eq!(refusal, Err(CronError::EntryIdUnavailable));
    assert!(host.installs().is_empty());
    assert!(host.file_paths().is_empty());
}

/// A command file that cannot be written stops the creation before the install.
#[test]
fn a_command_file_that_cannot_be_written_stops_the_creation() {
    let host = RecordingCronHost::new();
    host.refuse_writes();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    );

    assert_eq!(refusal, Err(CronError::EntryFileUnwritable));
    assert!(
        host.installs().is_empty(),
        "no table may be installed for an entry with no command"
    );
}

/// A crontab that cannot be read stops the creation.
#[test]
fn a_crontab_that_cannot_be_read_stops_the_creation() {
    let host = RecordingCronHost::new();
    host.refuse_crontab_read_with(7);

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    );

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 7 }));
}

/// A command file the duplicate check cannot read is a refusal, not a pass.
#[test]
fn a_command_file_that_cannot_be_read_stops_the_duplicate_check() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    host.refuse_entry_reads();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo two"),
    );

    assert_eq!(refusal, Err(CronError::EntryFileUnreadable));
}
