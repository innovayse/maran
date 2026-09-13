//! Ending a suspended account's open sessions, and reaching no other uid.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::logins::end_account_sessions::end_account_sessions;
use crate::logins::fake_logins_host::{
    ACCOUNT_UID, FakeLoginsHost, OTHER_UID, account, account_row, distro,
};
use crate::logins::logins_error::LoginsError;

/// The argv the cull is expected to run, as a host's `ps` would show it.
fn expected_argv(uid: u32) -> Vec<String> {
    vec![
        distro().pkill_binary().to_owned(),
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

    let ended = end_account_sessions(&host, distro(), &account()).expect("culled");

    assert_eq!(ended, 2, "the count is what an operator is told");
    assert!(host.running_processes().is_empty());
}

#[test]
fn the_cull_names_the_accounts_own_uid_and_takes_the_program_from_the_adapter() {
    // The exact argv, and not "it ran something": an assertion that only
    // checked a spawn had happened would pass against a cull aimed at the wrong
    // uid, at the wrong signal, or through a program name PATH would resolve.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);

    end_account_sessions(&host, distro(), &account()).expect("culled");

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

    let ended = end_account_sessions(&host, distro(), &account()).expect("culled");

    assert_eq!(ended, 1);
    assert_eq!(host.running_processes(), vec![OTHER_UID]);
}

#[test]
fn an_account_with_nothing_running_is_a_success_and_not_a_failure() {
    // `pkill` exits 1 when it matched nothing, and an account whose customer is
    // not connected is the ordinary case: reading that status as a failure would
    // refuse the suspension of every idle account on the host.
    let host = FakeLoginsHost::with_passwd(&[account_row()]);

    let ended = end_account_sessions(&host, distro(), &account()).expect("culled");

    assert_eq!(ended, 0);
}

#[test]
fn an_account_whose_row_carries_uid_zero_is_refused_before_anything_is_signalled() {
    // The guard whose absence is catastrophic rather than merely wrong: the
    // account-name grammar accepts `root`, and `pkill --uid 0` on a real host
    // ends systemd, sshd, the database and the panel. The home is the one this
    // agent gives a hosting account, so the OTHER guard cannot be what refuses
    // this row — the two are mutated and scored separately.
    let host = FakeLoginsHost::with_passwd(&[("alice", 0, "/home/alice")]);

    let refused = end_account_sessions(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::SessionCullRefusedUid)),
        "got {refused:?}"
    );
    assert!(
        host.spawns().is_empty(),
        "nothing may be run once the uid has been refused"
    );
}

#[test]
fn an_account_not_homed_where_this_agent_homes_one_is_refused_before_anything_is_signalled() {
    // A hosting account colliding with a pre-existing system user: `mail` at
    // uid 8 passes the uid guard and must still be refused, because this agent
    // did not create it and its processes are the host's mail daemon. The uid is
    // non-zero, so the other guard cannot be what refuses this row.
    let host = FakeLoginsHost::with_passwd(&[("alice", 8, "/var/spool/mail")]);

    let refused = end_account_sessions(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::SessionCullRefusedHome)),
        "got {refused:?}"
    );
    assert!(
        host.spawns().is_empty(),
        "nothing may be run once the home has been refused"
    );
}

#[test]
fn a_refusing_pkill_fails_the_cull_rather_than_reporting_a_count() {
    // Anything but "signalled" and "nothing matched" leaves some of the
    // account's processes possibly signalled and some possibly not, and a
    // suspension must not be reported on a state nobody observed.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);
    host.refuse_pkill_with(2);

    let refused = end_account_sessions(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::SessionCullFailed { code: 2 })),
        "got {refused:?}"
    );
}

#[test]
fn a_password_database_that_cannot_be_read_refuses_instead_of_culling_nothing() {
    // The vacuity guard on the axis that can go blind: an unreadable database
    // yields no row, and "no row" must never degrade into a cull of uid 0 or
    // into a silent success.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);
    host.refuse_to_be_read();

    let refused = end_account_sessions(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
    assert_eq!(host.running_processes(), vec![ACCOUNT_UID]);
}

#[test]
fn a_database_holding_no_row_for_the_account_is_refused() {
    let host =
        FakeLoginsHost::with_passwd(&[("bob", OTHER_UID, "/home/bob")]).with_process(OTHER_UID);

    let refused = end_account_sessions(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
    assert_eq!(host.running_processes(), vec![OTHER_UID]);
}
