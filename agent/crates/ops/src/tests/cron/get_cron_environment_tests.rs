//! Which assignments a listing calls the panel's, and which it leaves alone.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::cron_error::CronError;
use crate::cron::get_cron_environment::get_cron_environment;
use crate::cron::recording_cron_host::{RecordingCronHost, account, assignment, distro};
use crate::cron::set_cron_environment::set_cron_environment;

/// An account with no crontab has no assignments.
#[test]
fn an_absent_crontab_has_no_environment() {
    let host = RecordingCronHost::new();

    let environment = get_cron_environment(&host, &account()).expect("listed");

    assert!(environment.is_empty());
}

/// What the panel set comes back, in the order it was written.
#[test]
fn the_assignments_the_panel_set_are_listed_in_order() {
    let host = RecordingCronHost::new();
    set_cron_environment(
        &host,
        distro(),
        &account(),
        vec![
            assignment("TZ", "Europe/Yerevan"),
            assignment("PATH", "/usr/local/bin:/usr/bin"),
        ],
    )
    .expect("environment set");

    let environment = get_cron_environment(&host, &account()).expect("listed");

    assert_eq!(environment.len(), 2);
    assert_eq!(environment[0].name.as_str(), "TZ");
    assert_eq!(environment[1].name.as_str(), "PATH");
}

/// An assignment written above the banner belongs to whoever wrote it.
#[test]
fn a_foreign_assignment_is_not_reported_as_the_panels() {
    // Reporting it would invite an edit that would in fact MOVE it, and an
    // assignment's position is its meaning: it governs the lines beneath it.
    let host = RecordingCronHost::with_crontab("PATH=/opt/bin\n30 4 * * 1 /opt/backup.sh\n");

    let environment = get_cron_environment(&host, &account()).expect("listed");

    assert!(environment.is_empty());
}

/// The agent's own two lines are never reported as the account's.
#[test]
fn mailto_and_shell_are_never_listed_as_the_accounts() {
    let host = RecordingCronHost::new();
    set_cron_environment(&host, distro(), &account(), vec![assignment("TZ", "UTC")])
        .expect("environment set");

    let environment = get_cron_environment(&host, &account()).expect("listed");

    assert_eq!(environment.len(), 1);
    assert_eq!(environment[0].name.as_str(), "TZ");
}

/// A crontab that cannot be read is a failure, not an empty list.
#[test]
fn a_crontab_that_cannot_be_read_stops_the_listing() {
    let host = RecordingCronHost::new();
    host.refuse_crontab_read_with(7);

    let refusal = get_cron_environment(&host, &account());

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 7 }));
}
