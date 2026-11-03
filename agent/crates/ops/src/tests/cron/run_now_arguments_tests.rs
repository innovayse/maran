//! The argument vector a run started by hand is given.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::managed_account::ManagedAccount;

use crate::cron::model::run_unit_name::RunUnitName;
use crate::cron::recording_cron_host::{
    ACCOUNT_GID, ACCOUNT_UID, FIRST_ID, account, assignment, entry_id,
};
use crate::cron::run_now_arguments::run_now_arguments;

/// The managed account the tests build arguments for.
fn managed() -> ManagedAccount {
    let passwd = format!(
        "{}:x:{ACCOUNT_UID}:{ACCOUNT_GID}::/home/{}:/bin/sh\n",
        account().as_str(),
        account().as_str()
    );

    ManagedAccount::resolve(account(), &passwd).expect("a managed account")
}

/// The unit every test here starts its run under.
fn unit() -> RunUnitName {
    RunUnitName::numbered(ACCOUNT_UID, &entry_id(FIRST_ID), 3)
}

/// The job runs as the account, in the account's slice, under the runtime ceiling.
#[test]
fn the_job_runs_as_the_account_in_its_slice_under_the_runtime_ceiling() {
    let arguments = run_now_arguments(&managed(), &entry_id(FIRST_ID), &unit(), &[], "/bin/sh");

    assert_eq!(
        arguments,
        vec![
            format!("--uid={ACCOUNT_UID}"),
            format!("--gid={ACCOUNT_GID}"),
            format!("--slice=user-{ACCOUNT_UID}.slice"),
            format!("--unit=maran-cron-run-{ACCOUNT_UID}-{FIRST_ID}-3"),
            "--wait".to_owned(),
            "--pipe".to_owned(),
            "--collect".to_owned(),
            "--quiet".to_owned(),
            "--property=RuntimeMaxSec=120".to_owned(),
            "--property=TimeoutStopSec=10".to_owned(),
            "--property=WorkingDirectory=~".to_owned(),
            "--".to_owned(),
            "/bin/sh".to_owned(),
            format!("/home/alice/.maran/cron/{FIRST_ID}.cmd"),
        ]
    );
}

/// Each managed assignment is one `--setenv` argument, before the end of the options.
#[test]
fn each_assignment_is_one_setenv_argument_before_the_end_of_options() {
    let environment = [
        assignment("APP_ENV", "-production"),
        assignment("NOTE", "a b"),
    ];

    let arguments = run_now_arguments(
        &managed(),
        &entry_id(FIRST_ID),
        &unit(),
        &environment,
        "/bin/sh",
    );

    let end = arguments
        .iter()
        .position(|argument| argument == "--")
        .unwrap();
    let first = arguments
        .iter()
        .position(|argument| argument == "--setenv=APP_ENV=-production")
        .expect("the first assignment is one argument");
    let second = arguments
        .iter()
        .position(|argument| argument == "--setenv=NOTE=a b")
        .expect("a value with a space stays one argument");
    assert!(first < second && second < end);
}

/// The unit's name is one `--unit=` argument, so it cannot be read as an option
/// or as a second unit, and it is the name the agent will stop it by.
#[test]
fn the_unit_is_named_by_one_argument_the_agent_can_stop_it_by() {
    let arguments = run_now_arguments(&managed(), &entry_id(FIRST_ID), &unit(), &[], "/bin/sh");

    let named: Vec<&String> = arguments
        .iter()
        .filter(|argument| argument.starts_with("--unit="))
        .collect();
    assert_eq!(named.len(), 1);
    assert_eq!(
        named[0].strip_prefix("--unit="),
        Some(unit().stem()),
        "the argument must carry exactly the stem the stop will name"
    );
}

/// A stop is bounded: the job is killed outright a few seconds after `SIGTERM`.
#[test]
fn a_stop_waits_seconds_for_the_job_and_not_the_service_managers_ninety() {
    let arguments = run_now_arguments(&managed(), &entry_id(FIRST_ID), &unit(), &[], "/bin/sh");

    assert!(
        arguments
            .iter()
            .any(|argument| argument == "--property=TimeoutStopSec=10")
    );
}
