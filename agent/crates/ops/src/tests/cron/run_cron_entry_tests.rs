//! What starting an entry by hand runs, and what it refuses before anything runs.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::recording_cron_host::{
    ABSENT_ID, FIRST_ID, RecordingCronHost, account, assert_refused_untouched, assignment, command,
    distro, entry_id, every_five_minutes, unmanaged_cases,
};
use crate::cron::run_cron_entry::run_cron_entry;
use crate::cron::set_account_cron_suspended::set_account_cron_suspended;
use crate::cron::set_cron_entry_enabled::set_cron_entry_enabled;
use crate::cron::set_cron_environment::set_cron_environment;

/// A host holding one entry, created through the real operation.
fn host_with_one_entry() -> RecordingCronHost {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("php artisan schedule:run"),
        None,
    )
    .expect("created");

    host
}

/// An existing entry is run, and what the run reported comes back unchanged.
#[test]
fn an_existing_entry_is_run_and_its_outcome_returned() {
    let host = host_with_one_entry();

    let run = run_cron_entry(&host, &account(), &entry_id(FIRST_ID)).expect("ran");

    assert_eq!(run.exit_code, Some(0));
    assert_eq!(run.stdout, "done\n");
    assert_eq!(run.limit, None);
    assert_eq!(host.runs().len(), 1);
    assert_eq!(host.runs()[0].0, FIRST_ID);
}

/// The job is given the crontab's managed environment, as cron gives it.
#[test]
fn the_run_is_given_the_crontabs_managed_environment() {
    let host = host_with_one_entry();
    set_cron_environment(
        &host,
        distro(),
        &account(),
        vec![assignment("APP_ENV", "production"), assignment("TZ", "UTC")],
    )
    .expect("environment set");

    run_cron_entry(&host, &account(), &entry_id(FIRST_ID)).expect("ran");

    assert_eq!(
        host.runs()[0].1,
        vec![
            ("APP_ENV".to_owned(), "production".to_owned()),
            ("TZ".to_owned(), "UTC".to_owned()),
        ]
    );
}

/// An entry the account does not have is not found, and nothing runs.
#[test]
fn an_absent_entry_is_not_found_and_nothing_runs() {
    let host = host_with_one_entry();

    let result = run_cron_entry(&host, &account(), &entry_id(ABSENT_ID));

    assert_eq!(result, Err(CronError::NotFound));
    assert!(host.runs().is_empty());
}

/// A disabled entry still runs: disabling is the customer's switch for the schedule.
#[test]
fn a_disabled_entry_still_runs_by_hand() {
    let host = host_with_one_entry();
    set_cron_entry_enabled(&host, distro(), &account(), &entry_id(FIRST_ID), false)
        .expect("disabled");

    run_cron_entry(&host, &account(), &entry_id(FIRST_ID)).expect("ran");

    assert_eq!(host.runs().len(), 1);
}

/// A suspended account's entry is refused before anything runs.
#[test]
fn a_suspended_accounts_entry_is_refused_and_nothing_runs() {
    let host = host_with_one_entry();
    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    let result = run_cron_entry(&host, &account(), &entry_id(FIRST_ID));

    assert_eq!(result, Err(CronError::Suspended));
    assert!(host.runs().is_empty());
}

/// A run that could not be started is reported as such.
#[test]
fn a_run_that_cannot_start_is_reported_unavailable() {
    let host = host_with_one_entry();
    host.refuse_runs();

    let result = run_cron_entry(&host, &account(), &entry_id(FIRST_ID));

    assert_eq!(result, Err(CronError::RunUnavailable));
}

/// An account this panel does not manage is refused before its crontab is read.
#[test]
fn an_unmanaged_account_is_refused_before_anything_runs() {
    for case in unmanaged_cases() {
        let result = run_cron_entry(&case.host, &case.account, &entry_id(FIRST_ID));

        assert!(case.host.runs().is_empty(), "{}: nothing ran", case.label);
        assert_refused_untouched(&case, result);
    }
}
