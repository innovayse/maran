//! Ending a suspended account's open sessions, and reaching no other uid.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;

use crate::logins::end_account_sessions::end_account_sessions;
use crate::logins::fake_logins_host::{
    ACCOUNT_UID, FakeLoginsHost, OTHER_UID, account, account_row, distro,
};
use crate::logins::logins_error::LoginsError;

/// The proof the cull is handed, resolved from the one row `alice` has.
///
/// Built through the real resolution and not a stand-in: the refusals (a
/// reserved name, a system id, a foreign home) belong to the managed-account
/// type and are tested there and through `resolve_managed_account`, so this
/// suite only needs a proof that is genuine.
fn managed() -> ManagedAccount {
    let (name, uid, home) = account_row();
    let passwd = format!("{name}:x:{uid}:{uid}::{home}:/bin/sh\n");

    ManagedAccount::resolve(account(), &passwd).expect("alice is a hosting account")
}

/// The argv the cull is expected to run, as a host's `ps` would show it.
fn expected_argv(uid: u32) -> Vec<String> {
    vec![
        distro().pkill_binary().as_str().to_owned(),
        "--signal".to_owned(),
        "KILL".to_owned(),
        "--count".to_owned(),
        "--uid".to_owned(),
        uid.to_string(),
    ]
}

#[test]
fn every_process_running_as_the_account_is_signalled_and_counted() {
    let host = FakeLoginsHost::with_passwd(&[account_row()])
        .with_process(ACCOUNT_UID)
        .with_process(ACCOUNT_UID);

    let ended = end_account_sessions(&host, distro(), &managed()).expect("culled");

    assert_eq!(ended, 2, "the count is what an operator is told");
    assert!(host.running_processes().is_empty());
}

#[test]
fn the_cull_names_the_accounts_own_uid_and_takes_the_program_from_the_adapter() {
    // The exact argv, and not "it ran something": an assertion that only
    // checked a spawn had happened would pass against a cull aimed at the wrong
    // uid, at the wrong signal, or through a program name PATH would resolve.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);

    end_account_sessions(&host, distro(), &managed()).expect("culled");

    let argv: Vec<Vec<String>> = host.spawns().into_iter().map(|spawn| spawn.argv).collect();
    assert_eq!(argv, vec![expected_argv(ACCOUNT_UID)]);
}

#[test]
fn a_process_belonging_to_another_account_survives_the_cull() {
    // The inverse control that matters most here. A cull that killed everything
    // would satisfy every assertion above and would take a neighbouring
    // tenant's sessions down with this account's.
    let host = FakeLoginsHost::with_passwd(&[account_row(), ("bob", OTHER_UID, "/home/bob")])
        .with_process(ACCOUNT_UID)
        .with_process(OTHER_UID);

    let ended = end_account_sessions(&host, distro(), &managed()).expect("culled");

    assert_eq!(ended, 1);
    assert_eq!(host.running_processes(), vec![OTHER_UID]);
}

#[test]
fn an_account_with_nothing_running_is_a_success_and_not_a_failure() {
    // `pkill` exits 1 when it matched nothing, and an account whose customer is
    // not connected is the ordinary case: reading that status as a failure would
    // refuse the suspension of every idle account on the host.
    let host = FakeLoginsHost::with_passwd(&[account_row()]);

    let ended = end_account_sessions(&host, distro(), &managed()).expect("culled");

    assert_eq!(ended, 0);
}

#[test]
fn a_refusing_pkill_fails_the_cull_rather_than_reporting_a_count() {
    // Anything but "signalled" and "nothing matched" leaves some of the
    // account's processes possibly signalled and some possibly not, and a
    // suspension must not be reported on a state nobody observed.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);
    host.refuse_pkill_with(2);

    let refused = end_account_sessions(&host, distro(), &managed());

    assert!(
        matches!(refused, Err(LoginsError::SessionCullFailed { code: 2 })),
        "got {refused:?}"
    );
}

#[test]
fn the_cull_runs_as_a_mutation_and_names_no_end_of_options_marker() {
    // Pins the class (a kill is a short state change, not a query: a Query
    // deadline would also be 60 s but would read as a question to the next
    // maintainer) and the inverse control of the `--` guard: the uid is the
    // argument of `--uid`, so nothing follows it for a marker to protect, and a
    // `--` in front of it would make `pkill` read the uid as an operand.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);

    end_account_sessions(&host, distro(), &managed()).expect("culled");

    let spawns = host.spawns();
    assert_eq!(spawns.len(), 1, "{spawns:?}");
    assert_eq!(spawns[0].class, CallClass::Mutation);
    assert!(
        !spawns[0].argv.iter().any(|argument| argument == "--"),
        "got {:?}",
        spawns[0].argv
    );
}

#[test]
fn a_pkill_that_times_out_is_a_bound_and_not_a_count_of_zero() {
    // How many processes were signalled before the kill is unknown, so the
    // answer is an error. Zero would tell a suspension that nothing was running.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);
    host.time_out("pkill");

    let failed = end_account_sessions(&host, distro(), &managed());

    assert!(
        matches!(failed, Err(LoginsError::Bound(_))),
        "got {failed:?}"
    );
}
