//! The argument vector a run started by hand is given.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::managed_account::ManagedAccount;

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

/// The job runs as the account, in the account's slice, under the runtime ceiling.
#[test]
fn the_job_runs_as_the_account_in_its_slice_under_the_runtime_ceiling() {
    let arguments = run_now_arguments(&managed(), &entry_id(FIRST_ID), &[], "/bin/sh");

    assert_eq!(
        arguments,
        vec![
            format!("--uid={ACCOUNT_UID}"),
            format!("--gid={ACCOUNT_GID}"),
            format!("--slice=user-{ACCOUNT_UID}.slice"),
            "--wait".to_owned(),
            "--pipe".to_owned(),
            "--collect".to_owned(),
            "--quiet".to_owned(),
            "--property=RuntimeMaxSec=120".to_owned(),
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

    let arguments = run_now_arguments(&managed(), &entry_id(FIRST_ID), &environment, "/bin/sh");

    let end = arguments.iter().position(|argument| argument == "--").unwrap();
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
