//! What turning an entry off changes, and what it deliberately does not.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::recording_cron_host::{
    ABSENT_ID, FIRST_ID, RecordingCronHost, account, cmd_path, command, distro, entry_id,
    every_five_minutes, exit_path, log_path,
};
use crate::cron::set_cron_entry_enabled::set_cron_entry_enabled;

/// The line that follows the marker for `id` in `text`.
fn entry_line_of(text: &str, id: &str) -> String {
    let lines: Vec<&str> = text.lines().collect();
    let marker = format!("# maran-entry: {id}");
    let at = lines
        .iter()
        .position(|line| *line == marker)
        .expect("the marker is in the table");

    (*lines.get(at + 1).expect("a line after the marker")).to_owned()
}

/// A disabled entry keeps every file, and cron can no longer read its line.
#[test]
fn a_disabled_entry_keeps_its_files_but_cron_cannot_run_it() {
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
    host.put_file(&log_path(&account, &id), "the last run said this\n");
    host.put_file(&exit_path(&account, &id), "0\n");

    set_cron_entry_enabled(&host, distro(), &account, &id, false).expect("disabled");

    let table = host.crontab().expect("a table was installed");
    let line = entry_line_of(&table, FIRST_ID);
    assert!(
        line.starts_with("#off# "),
        "cron reads the line as a comment: {line}"
    );
    assert!(
        table.contains(FIRST_ID),
        "the entry stays findable while it is off"
    );
    assert_eq!(
        host.file(&cmd_path(&account, &id)),
        Some("echo one\n".to_owned())
    );
    assert!(host.file(&log_path(&account, &id)).is_some());
    assert!(host.file(&exit_path(&account, &id)).is_some());
}

/// Re-enabling gives back the same entry rather than a new one.
#[test]
fn re_enabling_an_entry_restores_the_line_cron_reads() {
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
    let enabled = host.crontab().expect("a table was installed");

    set_cron_entry_enabled(&host, distro(), &account(), &id, false).expect("disabled");
    set_cron_entry_enabled(&host, distro(), &account(), &id, true).expect("re-enabled");

    assert_eq!(host.crontab(), Some(enabled));
}

/// Toggling an entry that is not there is refused, and installs nothing.
#[test]
fn toggling_an_unknown_entry_reports_not_found() {
    let host = RecordingCronHost::new();

    let refusal = set_cron_entry_enabled(&host, distro(), &account(), &entry_id(ABSENT_ID), false);

    assert_eq!(refusal, Err(CronError::NotFound));
    assert!(host.installs().is_empty());
}

/// A refused install leaves the entry in the state it had.
#[test]
fn a_refused_install_leaves_the_entry_enabled() {
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
    let before = host.crontab().expect("a table was installed");
    host.refuse_install_with(1);

    let refusal = set_cron_entry_enabled(&host, distro(), &account(), &id, false);

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 1 }));
    assert_eq!(host.crontab(), Some(before));
}
