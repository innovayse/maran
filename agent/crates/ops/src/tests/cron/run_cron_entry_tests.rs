//! What starting an entry by hand runs, and what it refuses before anything runs.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use maran_agent_core::utils::bounded_slots::BoundedSlots;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_run::CronRun;
use crate::cron::model::cron_run_end::CronRunEnd;
use crate::cron::recording_cron_host::{
    ABSENT_ID, FIRST_ID, RecordingCronHost, account, assert_refused_untouched, assignment, command,
    distro, entry_id, every_five_minutes, unmanaged_cases,
};
use crate::cron::run_cron_entry::{ACCOUNT_RUN_NOW_SLOTS, HOST_RUN_NOW_SLOTS, run_within};
use crate::cron::set_account_cron_suspended::set_account_cron_suspended;
use crate::cron::set_cron_entry_enabled::set_cron_entry_enabled;
use crate::cron::set_cron_environment::set_cron_environment;

/// The operation under test, against ceilings of its own.
///
/// The public entry point counts runs in ONE process-wide set of slots, and
/// every test in this file runs for the same account on the harness's threads:
/// two of them in flight at once would take the account's two slots and refuse
/// a third, and the flake would say nothing about the code. So each call here
/// gets slots of its own at the shipped ceilings; the tests of the ceilings
/// themselves build their own with the numbers they state.
fn run_cron_entry(
    host: &dyn CronHost,
    account: &AccountName,
    id: &CronEntryId,
) -> Result<CronRun, CronError> {
    run_within(
        &BoundedSlots::new(HOST_RUN_NOW_SLOTS, ACCOUNT_RUN_NOW_SLOTS),
        host,
        account,
        id,
    )
}

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

    assert_eq!(run.end, CronRunEnd::Exited { code: 0 });
    assert_eq!(run.stdout, "done\n");
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

/// A run past the account's ceiling is refused, and the refusal reads nothing
/// and starts nothing (issue #333).
#[test]
fn a_run_past_the_accounts_ceiling_is_refused_before_the_crontab_is_read() {
    let host = host_with_one_entry();
    let slots = BoundedSlots::new(10, 1);
    let _running = slots.try_acquire(&account()).expect("one slot");
    let reads_before = host.reads();

    let result = run_within(&slots, &host, &account(), &entry_id(FIRST_ID));

    assert_eq!(result, Err(CronError::RunCapacityReached));
    assert!(host.runs().is_empty());
    assert_eq!(host.reads(), reads_before, "a refusal must read nothing");
}

/// A run past the host's ceiling is refused for an account that is under its own.
#[test]
fn a_run_past_the_hosts_ceiling_is_refused_for_an_account_under_its_own() {
    let host = host_with_one_entry();
    let slots = BoundedSlots::new(1, 2);
    let _elsewhere = slots
        .try_acquire(&AccountName::parse("bobby").unwrap())
        .expect("another account holds the host's only slot");

    let result = run_within(&slots, &host, &account(), &entry_id(FIRST_ID));

    assert_eq!(result, Err(CronError::RunCapacityReached));
    assert!(host.runs().is_empty());
}

/// A finished run gives its slot back, so the next one is admitted.
#[test]
fn a_finished_run_frees_its_slot_for_the_next() {
    let host = host_with_one_entry();
    let slots = BoundedSlots::new(1, 1);

    run_within(&slots, &host, &account(), &entry_id(FIRST_ID)).expect("first");
    run_within(&slots, &host, &account(), &entry_id(FIRST_ID)).expect("second");

    assert_eq!(host.runs().len(), 2);
}

/// A run that failed to start gives its slot back too: every path out frees it.
#[test]
fn a_run_that_could_not_start_frees_its_slot() {
    let host = host_with_one_entry();
    host.refuse_runs();
    let slots = BoundedSlots::new(1, 1);

    let first = run_within(&slots, &host, &account(), &entry_id(FIRST_ID));
    let second = run_within(&slots, &host, &account(), &entry_id(FIRST_ID));

    assert_eq!(first, Err(CronError::RunUnavailable));
    assert_eq!(second, Err(CronError::RunUnavailable));
}

#[test]
fn the_shipped_ceilings_are_a_small_share_of_the_blocking_pool() {
    // tokio's default blocking pool is 512 threads. The host ceiling must stay
    // far below it, and an account's must stay below the host's.
    //
    // Both are constants, so the comparisons are decided when the test binary is
    // compiled: a ceiling raised past these bounds stops the build.
    const {
        assert!(HOST_RUN_NOW_SLOTS * 8 <= 512);
        assert!(ACCOUNT_RUN_NOW_SLOTS < HOST_RUN_NOW_SLOTS);
        assert!(ACCOUNT_RUN_NOW_SLOTS >= 1);
    }
}
