//! Counting what an account's crontab is still doing, and refusing to guess.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::inspect_account_cron::inspect_account_cron;
use crate::cron::recording_cron_host::{
    RecordingCronHost, SECOND_ID, account, command, distro, entry_id, every_five_minutes, schedule,
};
use crate::cron::set_account_cron_suspended::set_account_cron_suspended;
use crate::cron::set_cron_entry_enabled::set_cron_entry_enabled;

/// Creates two managed entries, the second disabled by the customer.
fn two_entries_one_customer_disabled() -> RecordingCronHost {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    )
    .expect("the first entry is created");
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &schedule("7", "*", "*", "*", "*"),
        &command("echo two"),
        None,
    )
    .expect("the second entry is created");
    set_cron_entry_enabled(&host, distro(), &account(), &entry_id(SECOND_ID), false)
        .expect("the customer turns their second entry off");

    host
}

#[test]
fn an_account_that_was_never_suspended_reports_none_of_its_entries_suppressed() {
    // The INVERSE CONTROL for every assertion below: a counter that answered
    // "all suspended" whatever it was shown would pass them all.
    let host = two_entries_one_customer_disabled();

    let state = inspect_account_cron(&host, &account()).expect("observed");

    assert_eq!(state.entries_total, 2);
    assert_eq!(state.entries_suspended, 0);
}

#[test]
fn an_entry_the_customer_disabled_is_not_counted_as_suspended() {
    // The two flags are two facts. Counting the customer's own switch here
    // would report an account as suspended because its owner happened to have
    // turned their jobs off, which is a suspension nobody performed.
    let host = two_entries_one_customer_disabled();

    let state = inspect_account_cron(&host, &account()).expect("observed");

    assert_eq!(
        state.entries_suspended, 0,
        "one entry is off, and neither is SUSPENDED"
    );
}

#[test]
fn a_suspended_account_reports_every_managed_entry_suppressed() {
    let host = two_entries_one_customer_disabled();
    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    let state = inspect_account_cron(&host, &account()).expect("observed");

    assert_eq!(state.entries_total, 2);
    assert_eq!(state.entries_suspended, 2);
}

#[test]
fn the_count_comes_from_the_lines_and_not_from_the_account_marker() {
    // The marker line governs the next render; it is not evidence. A table
    // whose marker says "suspended" over lines cron can still read must be
    // reported as what the lines say, or the attestation certifies a silence
    // the file does not have.
    let host = two_entries_one_customer_disabled();
    let table = host.crontab().expect("a table was installed");
    let forged = table.replace(
        "# maran: managed section - every line below is rewritten by the panel\n",
        "# maran: managed section - every line below is rewritten by the panel\n# maran-suspended\n",
    );
    let host = RecordingCronHost::with_crontab(&forged);

    let state = inspect_account_cron(&host, &account()).expect("observed");

    assert_eq!(
        state.entries_suspended, 0,
        "the lines are what cron reads, and none of them is marked"
    );
    assert_eq!(state.entries_total, 2);
}

#[test]
fn a_line_the_agent_did_not_write_is_counted_and_never_silently_ignored() {
    // Suspension does not touch these lines and they keep firing. Reporting
    // the number is the difference between saying what was silenced and
    // claiming a silence that was not achieved.
    let host = RecordingCronHost::with_crontab(
        "# my own note\n*/2 * * * * /opt/bin/theirs\n\
         # maran: managed section - every line below is rewritten by the panel\nMAILTO=\"\"\n",
    );

    let state = inspect_account_cron(&host, &account()).expect("observed");

    assert_eq!(state.foreign_lines, 2);
    assert_eq!(state.entries_total, 0);
}

#[test]
fn an_account_with_no_crontab_is_observed_as_nothing_firing() {
    let host = RecordingCronHost::new();

    let state = inspect_account_cron(&host, &account()).expect("observed");

    assert_eq!(state.entries_total, 0);
    assert_eq!(state.entries_suspended, 0);
    assert_eq!(state.foreign_lines, 0);
}

#[test]
fn a_crontab_that_cannot_be_read_is_refused_and_not_reported_as_empty() {
    // The vacuity guard, on the axis that can actually go blind. Zero entries
    // and an unreadable crontab are the same number to any caller that guesses,
    // and zero is the one that reads as "nothing is firing".
    let host = two_entries_one_customer_disabled();
    host.refuse_crontab_read_with(1);

    let refused = inspect_account_cron(&host, &account());

    assert!(
        matches!(refused, Err(CronError::CrontabRefused { .. })),
        "an unreadable crontab must refuse to answer, got {refused:?}"
    );
}
