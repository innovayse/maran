//! What replacing the environment rewrites, and what it leaves where it was.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::recording_cron_host::{
    FIRST_ID, RecordingCronHost, account, assignment, command, distro, every_five_minutes,
};
use crate::cron::set_cron_environment::set_cron_environment;

/// The list replaces what was there rather than merging into it.
#[test]
fn setting_the_environment_replaces_every_assignment() {
    // A merge would make removing an assignment inexpressible: the panel holds
    // the list and sends the one it wants.
    let host = RecordingCronHost::new();
    set_cron_environment(
        &host,
        distro(),
        &account(),
        vec![assignment("TZ", "UTC"), assignment("LANG", "C")],
    )
    .expect("environment set");

    set_cron_environment(&host, distro(), &account(), vec![assignment("TZ", "UTC")])
        .expect("environment set");

    let table = host.crontab().expect("a table was installed");
    assert!(table.contains("TZ=UTC"));
    assert!(
        !table.contains("LANG=C"),
        "the dropped one is gone: {table}"
    );
}

/// An empty list leaves the agent's own two lines and nothing else.
#[test]
fn an_empty_environment_still_renders_the_agents_own_lines() {
    let host = RecordingCronHost::new();
    set_cron_environment(&host, distro(), &account(), vec![assignment("TZ", "UTC")])
        .expect("environment set");

    set_cron_environment(&host, distro(), &account(), Vec::new()).expect("environment cleared");

    let table = host.crontab().expect("a table was installed");
    assert!(table.contains("MAILTO=\"\""));
    assert!(table.contains("SHELL=/bin/sh"));
    assert!(!table.contains("TZ=UTC"));
}

/// Changing the environment leaves the entries alone.
#[test]
fn setting_the_environment_leaves_every_entry_installed() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");

    set_cron_environment(&host, distro(), &account(), vec![assignment("TZ", "UTC")])
        .expect("environment set");

    let table = host.crontab().expect("a table was installed");
    assert!(table.contains(FIRST_ID), "the entry survives: {table}");
}

/// The agent's own assignments are written above the customer's.
#[test]
fn the_agents_own_assignments_are_written_above_the_customers() {
    // Ours re-set the mail policy and the interpreter for the region beneath,
    // so a customer assignment can never come between them and the entries.
    let host = RecordingCronHost::new();

    set_cron_environment(&host, distro(), &account(), vec![assignment("TZ", "UTC")])
        .expect("environment set");

    let table = host.crontab().expect("a table was installed");
    let lines: Vec<&str> = table.lines().collect();
    let ours = lines
        .iter()
        .position(|line| *line == "SHELL=/bin/sh")
        .expect("our own shell line");
    let theirs = lines
        .iter()
        .position(|line| *line == "TZ=UTC")
        .expect("the customer's line");
    assert!(ours < theirs);
}

/// A refused install leaves the assignments as they were.
#[test]
fn a_refused_install_leaves_the_environment_alone() {
    let host = RecordingCronHost::new();
    set_cron_environment(&host, distro(), &account(), vec![assignment("TZ", "UTC")])
        .expect("environment set");
    let before = host.crontab().expect("a table was installed");
    host.refuse_install_with(1);

    let refusal = set_cron_environment(
        &host,
        distro(),
        &account(),
        vec![assignment("TZ", "Europe/Yerevan")],
    );

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 1 }));
    assert_eq!(host.crontab(), Some(before));
}
