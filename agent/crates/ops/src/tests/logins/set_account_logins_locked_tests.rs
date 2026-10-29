//! Locking every key into an account's home, including the one no SFTP-only
//! enumeration could see.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::take_account_lock;
use crate::logins::fake_logins_host::{
    ACCOUNT_UID, FakeLoginsHost, OTHER_UID, account, account_row, distro,
};
use crate::logins::logins_error::LoginsError;
use crate::logins::set_account_logins_locked::{
    set_account_logins_locked, set_account_logins_locked_under_lock,
};

/// The jail `alice`'s SFTP logins are homed in.
const SFTP_JAIL: &str = "/var/lib/maran-sftp/alice";

/// The jail `alice`'s FTPS logins are homed in.
const FTPS_JAIL: &str = "/var/lib/maran-ftps/alice";

/// A host holding one login of each protocol, neither of them locked.
fn one_of_each() -> FakeLoginsHost {
    FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("alice_files", ACCOUNT_UID, FTPS_JAIL),
    ])
}

#[test]
fn suspending_an_account_locks_its_ftps_login_too() {
    // The whole reason this module exists. Before the move, this login was
    // invisible to the enumeration and a suspended customer kept a working
    // write credential into their own home.
    let host =
        FakeLoginsHost::with_passwd(&[account_row(), ("alice_files", ACCOUNT_UID, FTPS_JAIL)]);

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(host.locked_users(), vec!["alice_files"]);
}

#[test]
fn every_login_the_account_holds_is_locked_whichever_daemon_serves_it() {
    let host = one_of_each();

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(host.locked_users(), vec!["alice_files", "alice_web"]);
}

#[test]
fn the_answer_reports_every_login_as_locked_because_the_host_was_read_again() {
    // The returned set is what the panel attests on, so it must be an
    // observation and not a restatement of what was asked for: `usermod`
    // answers zero on a login it did nothing to.
    let host = one_of_each();

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(observed.logins.logins.len(), 2);
    assert!(observed.logins.logins.iter().all(|login| login.locked));
}

#[test]
fn logins_are_unlocked_again_when_the_account_is_resumed() {
    // The inverse control for the assertion above: an implementation that
    // locked everything unconditionally would satisfy it and fail here.
    let host = one_of_each()
        .with_locked("alice_web")
        .with_locked("alice_files");

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), false).expect("unlocked");

    assert!(host.locked_users().is_empty());
    assert!(observed.logins.logins.iter().all(|login| !login.locked));
}

#[test]
fn a_login_on_the_accounts_uid_that_is_in_neither_jail_is_counted_and_not_locked() {
    // The positive control for the count, on the write path: it is non-zero
    // because a login genuinely sits outside both jails, and that login must
    // still be there afterwards — this agent did not create it and does not
    // turn it off.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_files", ACCOUNT_UID, FTPS_JAIL),
        ("alice_hand", ACCOUNT_UID, "/home/alice"),
    ]);

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(
        observed.logins.unmanaged, 1,
        "a hand-made login on this uid must be COUNTED"
    );
    assert_eq!(
        host.locked_users(),
        vec!["alice_files"],
        "and must not be locked by us"
    );
}

#[test]
fn a_neighbouring_accounts_login_is_left_alone() {
    // `alice_bob` is both a login of `alice` and a plausible account name of
    // its own. The enumeration tells them apart by the passwd home, and this is
    // the assertion that fails if anyone replaces it with a name prefix.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("alicecorp", OTHER_UID, "/home/alicecorp"),
        ("alicecorp_web", OTHER_UID, "/var/lib/maran-sftp/alicecorp"),
    ]);

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(
        host.locked_users(),
        vec!["alice_web"],
        "another account's login must not be locked by this account's suspension"
    );
}

#[test]
fn an_account_with_no_logins_locks_nothing_and_still_has_its_sessions_ended() {
    // THIS TEST WAS CHANGED DELIBERATELY. It used to assert that a suspension of
    // an account with no login "touches nothing" — `host.spawns().is_empty()` —
    // and that is no longer the behaviour: locking a login refuses the account's
    // NEXT login and does nothing to a session already open, so a suspension now
    // also ends every process running as the account's uid. The two halves are
    // separate questions and this test now asserts both: no login was locked,
    // because there was none, and the cull ran anyway.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    let programs: Vec<String> = host
        .spawns()
        .into_iter()
        .map(|spawn| spawn.argv[0].clone())
        .collect();
    assert!(
        !programs.iter().any(|program| program.ends_with("usermod")),
        "no login exists, so no password may be turned: {programs:?}"
    );
    assert_eq!(
        programs
            .iter()
            .filter(|program| program.ends_with("pkill"))
            .count(),
        1,
        "the account's sessions are ended exactly once: {programs:?}"
    );
    assert!(host.running_processes().is_empty());
}

#[test]
fn suspending_an_account_ends_the_sessions_it_already_has_open() {
    // The defect this cull closes: before it, the two logins below were locked —
    // which sshd and vsftpd consult at AUTHENTICATION — and the session the
    // customer already held kept reading and writing their home.
    let host = one_of_each()
        .with_process(ACCOUNT_UID)
        .with_process(ACCOUNT_UID);

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert!(host.running_processes().is_empty());
    assert_eq!(host.locked_users(), vec!["alice_files", "alice_web"]);
}

#[test]
fn resuming_an_account_ends_no_session() {
    // The inverse control for the assertion above, and a real requirement rather
    // than symmetry for its own sake: an implementation that culled on both
    // directions would satisfy every cull assertion in this file and would kill
    // whatever the account had legitimately started by the time it was resumed.
    let host = one_of_each()
        .with_locked("alice_web")
        .with_locked("alice_files")
        .with_process(ACCOUNT_UID);

    set_account_logins_locked_under_lock(&host, distro(), &account(), false).expect("resumed");

    assert_eq!(host.running_processes(), vec![ACCOUNT_UID]);
    assert!(
        !host
            .spawns()
            .iter()
            .any(|spawn| spawn.argv[0].ends_with("pkill")),
        "a resume must not run the cull at all"
    );
}

#[test]
fn a_neighbouring_accounts_session_survives_this_accounts_suspension() {
    // The cross-tenant inverse control, at the operation the panel actually
    // calls rather than only at the unit below it.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("bob", OTHER_UID, "/home/bob"),
    ])
    .with_process(ACCOUNT_UID)
    .with_process(OTHER_UID);

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(host.running_processes(), vec![OTHER_UID]);
    assert_eq!(host.locked_users(), vec!["alice_web"]);
}

#[test]
fn a_failed_cull_fails_the_whole_suspension_and_leaves_the_logins_locked() {
    // A suspension that reported success while the cull silently failed would be
    // worse than the gap it replaced, so the failure propagates. The second
    // assertion is why that is safe to retry: the locks are already turned, so
    // the failure leaves an account refusing NEW logins — which is what this
    // product shipped before the cull existed — and never one whose access came
    // back.
    let host = one_of_each().with_process(ACCOUNT_UID);
    host.refuse_pkill_with(2);

    let refused = set_account_logins_locked_under_lock(&host, distro(), &account(), true);

    assert!(
        matches!(refused, Err(LoginsError::SessionCullFailed { code: 2 })),
        "got {refused:?}"
    );
    assert_eq!(host.locked_users(), vec!["alice_files", "alice_web"]);
}

#[test]
fn locking_twice_converges() {
    let host = one_of_each();

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");
    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked again");

    assert_eq!(host.locked_users(), vec!["alice_files", "alice_web"]);
}

#[test]
fn a_login_with_no_password_stays_locked_after_a_resume_and_is_reported_as_such() {
    // The one measured asymmetry of the real tool, reported rather than hidden:
    // `usermod --unlock` refuses a passwordless login while exiting zero.
    let host = one_of_each().with_passwordless("alice_files");
    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), false).expect("resumed");

    let files = observed
        .logins
        .logins
        .iter()
        .find(|login| login.name == "alice_files")
        .expect("the login is reported");
    assert!(
        files.locked,
        "the panel must be able to SEE that this login did not come back"
    );
}

#[test]
fn nothing_is_deleted_and_the_lock_is_turned_with_the_flag_that_keeps_the_hash() {
    // Suspension revokes a key; it must not destroy what the key opened, and it
    // must give back the SAME credential on the resume rather than a new one
    // the customer would have to be told about. `usermod --lock` prefixes the
    // stored hash; anything else here would be a new password.
    let host = one_of_each();

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(observed.logins.logins.len(), 2, "no login may be removed");
    let locks: Vec<Vec<String>> = host
        .spawns()
        .into_iter()
        .map(|spawn| spawn.argv)
        .filter(|argv| argv[0].ends_with("usermod"))
        .collect();
    assert_eq!(
        locks,
        vec![
            vec![
                distro().usermod_binary().as_str().to_owned(),
                "--lock".to_owned(),
                "--".to_owned(),
                "alice_files".to_owned()
            ],
            vec![
                distro().usermod_binary().as_str().to_owned(),
                "--lock".to_owned(),
                "--".to_owned(),
                "alice_web".to_owned()
            ],
        ]
    );
}

#[test]
fn the_answer_carries_the_number_of_sessions_the_cull_signalled() {
    // The count is the whole point of this lane: before it was carried out of
    // here it reached nothing but this agent's log, while the panel's own
    // confirmation dialog was already promising the operator the transfer would
    // be cut.
    let host = one_of_each()
        .with_process(ACCOUNT_UID)
        .with_process(ACCOUNT_UID)
        .with_process(OTHER_UID);

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(
        observed.sessions_ended,
        Some(2),
        "the account's own two processes, and not the neighbour's"
    );
}

#[test]
fn a_cull_that_matched_nothing_answers_a_measured_zero_and_not_an_absent_count() {
    // The axis that can go blind. "Nothing matched" and "no count at all" are
    // the same NUMBER and different facts: the first is a completeness claim the
    // attestation may state, the second is one it must refuse to state. A test
    // that passed in both states would measure neither, so this asserts on the
    // presence and the value together.
    let host = one_of_each();

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    assert_eq!(
        observed.sessions_ended,
        Some(0),
        "an account with no process running must report a MEASURED none"
    );
    assert!(
        observed.sessions_ended.is_some(),
        "and it must be present, because an absent count is the other fact"
    );
}

#[test]
fn the_resume_direction_carries_no_count_because_it_ends_no_session() {
    // The inverse control for the two above: an implementation that reported a
    // count unconditionally would satisfy both of them and fail here, and it
    // would make every resume claim a cull that never ran.
    let host = one_of_each()
        .with_locked("alice_web")
        .with_locked("alice_files")
        .with_process(ACCOUNT_UID);

    let observed =
        set_account_logins_locked_under_lock(&host, distro(), &account(), false).expect("unlocked");

    assert_eq!(observed.sessions_ended, None);
    assert_eq!(
        host.running_processes(),
        vec![ACCOUNT_UID],
        "and the resume must not have culled anything to have no count OF"
    );
}

#[test]
fn a_refused_cull_answers_no_outcome_at_all_rather_than_a_count_of_zero() {
    // The third thing the cull can answer. `pkill` refusing means some of the
    // account's processes may have been signalled and some may not, so a
    // suspension that cannot be accounted for is reported as FAILED — never as
    // a zero, which would read as the completeness claim of the test above.
    let host = one_of_each().with_process(ACCOUNT_UID);
    host.refuse_pkill_with(2);

    let refused = set_account_logins_locked_under_lock(&host, distro(), &account(), true);

    assert!(
        matches!(refused, Err(LoginsError::SessionCullFailed { code: 2 })),
        "got {refused:?}"
    );
}

#[test]
fn a_refused_usermod_stops_the_operation_rather_than_reporting_success() {
    let host = one_of_each();
    host.refuse_usermod_with(1);

    let refused = set_account_logins_locked_under_lock(&host, distro(), &account(), true);

    assert!(
        matches!(refused, Err(LoginsError::SpawnFailed { code: 1 })),
        "got {refused:?}"
    );
}

#[test]
fn a_password_database_that_cannot_be_read_refuses_instead_of_locking_nothing() {
    // The vacuity guard on the axis that can go blind: an unreadable database
    // and an account with no logins both produce an empty list, and the empty
    // list is the one that reads as "everything was locked".
    let host = one_of_each();
    host.refuse_to_be_read();

    let refused = set_account_logins_locked_under_lock(&host, distro(), &account(), true);

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
}

/// An account no other test names, so the process-wide account lock this suite
/// takes cannot be contended by the harness's own threads.
fn contended_account() -> AccountName {
    AccountName::parse("loginlockcontended").expect("the fixture name is valid")
}

#[test]
fn a_suspension_is_refused_while_the_accounts_lock_is_held() {
    // The race half. The operation enumerates the account's logins and then
    // acts on each of them: without the lock, a login created inside that
    // window is a login the suspension never sees, and a password change
    // landing in it replaces the very field this operation writes.
    let account = contended_account();
    let held = take_account_lock(&account).expect("the lock is free at the start of this test");
    let host = FakeLoginsHost::with_passwd(&[
        (
            "loginlockcontended",
            ACCOUNT_UID,
            "/home/loginlockcontended",
        ),
        (
            "loginlockcontended_web",
            ACCOUNT_UID,
            "/var/lib/maran-sftp/loginlockcontended",
        ),
    ]);

    let refused = set_account_logins_locked(&host, distro(), &account, true);

    assert!(
        matches!(refused, Err(LoginsError::AccountBusy)),
        "got {refused:?}"
    );
    assert!(
        !host.is_locked("loginlockcontended_web"),
        "the suspension never started, so nothing may have been locked"
    );
    drop(held);
}

#[test]
fn a_suspension_that_finished_leaves_the_accounts_lock_free() {
    // The inverse control. A guard leaked by the entry point would make the
    // refusal above pass forever and every later suspension of that account
    // impossible — locking a customer's suspension out is a worse defect than
    // the race being closed.
    let account = AccountName::parse("loginlockreleases").expect("the fixture name is valid");
    let host = FakeLoginsHost::with_passwd(&[
        ("loginlockreleases", ACCOUNT_UID, "/home/loginlockreleases"),
        (
            "loginlockreleases_web",
            ACCOUNT_UID,
            "/var/lib/maran-sftp/loginlockreleases",
        ),
    ]);

    set_account_logins_locked(&host, distro(), &account, true).expect("locked");

    let free = take_account_lock(&account);
    assert!(
        free.is_some(),
        "the lock must be free once the operation returned"
    );
}

/// Asserts that suspending `account` over `host` is refused with a variant
/// `expected` accepts, and that nothing at all was run.
fn assert_refused_before_any_command(
    host: &FakeLoginsHost,
    account: &AccountName,
    expected: fn(&LoginsError) -> bool,
) {
    let refused = set_account_logins_locked_under_lock(host, distro(), account, true);

    match refused {
        Err(error) => assert!(expected(&error), "the wrong refusal: {error:?}"),
        Ok(_) => panic!("the suspension was not refused"),
    }
    assert!(
        host.spawns().is_empty(),
        "usermod and pkill must not run once the account is refused: {:?}",
        host.spawns()
    );
}

#[test]
fn a_reserved_name_is_refused_before_usermod_or_pkill_runs() {
    // The name grammar accepts `root`, and `usermod --lock` plus `pkill --uid`
    // against the real root would take the host down. The row is otherwise
    // perfect (a managed home, an id above the floor), so only the reserved
    // list can be what refuses it.
    let root = AccountName::parse("root").expect("the grammar accepts it");
    let host = FakeLoginsHost::with_passwd(&[("root", ACCOUNT_UID, "/home/root")])
        .with_process(ACCOUNT_UID);

    assert_refused_before_any_command(&host, &root, |error| {
        matches!(error, LoginsError::AccountNotManaged)
    });
    assert_eq!(host.running_processes(), vec![ACCOUNT_UID]);
}

#[test]
fn a_system_identity_is_refused_before_usermod_or_pkill_runs() {
    // The managed home, so the home check cannot be what refuses this row.
    let host = FakeLoginsHost::with_passwd(&[("alice", 0, "/home/alice")]).with_process(0);

    assert_refused_before_any_command(&host, &account(), |error| {
        matches!(error, LoginsError::AccountNotManaged)
    });
    assert_eq!(host.running_processes(), vec![0]);
}

#[test]
fn an_id_below_the_floor_is_refused_even_when_it_is_not_zero() {
    // `mail` is uid 8 on every supported image: non-zero, so a guard that only
    // compared against zero would let it through.
    let host = FakeLoginsHost::with_passwd(&[("alice", 8, "/home/alice")]).with_process(8);

    assert_refused_before_any_command(&host, &account(), |error| {
        matches!(error, LoginsError::AccountNotManaged)
    });
}

#[test]
fn a_home_other_than_the_managed_one_is_refused_before_anything_runs() {
    let host = FakeLoginsHost::with_passwd(&[("alice", ACCOUNT_UID, "/var/spool/mail")])
        .with_process(ACCOUNT_UID);

    assert_refused_before_any_command(&host, &account(), |error| {
        matches!(error, LoginsError::AccountNotManaged)
    });
}

#[test]
fn the_unlock_direction_is_gated_by_the_same_proof_and_runs_nothing() {
    // The gate is not a property of the locking direction: unlocking the
    // logins of an operator's own user is as much a change to a login this
    // agent did not create. Every refusal reads as the one variant.
    let rows: [(&str, &str, u32, &str); 4] = [
        ("root", "root", ACCOUNT_UID, "/home/root"),
        ("alice", "alice", 0, "/home/alice"),
        ("alice", "alice", 8, "/home/alice"),
        ("alice", "alice", ACCOUNT_UID, "/var/spool/mail"),
    ];
    for (requested, row_name, uid, home) in rows {
        let name = AccountName::parse(requested).expect("the grammar accepts it");
        let host = FakeLoginsHost::with_passwd(&[(row_name, uid, home)]);

        let refused = set_account_logins_locked_under_lock(&host, distro(), &name, false);

        assert!(
            matches!(refused, Err(LoginsError::AccountNotManaged)),
            "{requested} {uid} {home}: got {refused:?}"
        );
        assert!(host.spawns().is_empty(), "{:?}", host.spawns());
    }
}

#[test]
fn an_absent_account_is_refused_before_anything_runs() {
    let host = FakeLoginsHost::with_passwd(&[("bob", OTHER_UID, "/home/bob")]);

    assert_refused_before_any_command(&host, &account(), |error| {
        matches!(error, LoginsError::AccountMissing)
    });
}

#[test]
fn a_hosting_account_passes_the_same_gate_that_refuses_the_rows_above() {
    // The accepting control: the refusals above would all hold for a gate that
    // refused everything.
    let host = FakeLoginsHost::with_passwd(&[account_row()]).with_process(ACCOUNT_UID);

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("suspended");

    assert!(!host.spawns().is_empty());
    assert!(host.running_processes().is_empty());
}

#[test]
fn a_usermod_that_times_out_is_a_bound_and_never_a_spawn_failure() {
    // A killed `usermod` has said neither "locked" nor "refused". Folding the
    // kill into `SpawnFailed { code }` would invent an exit status the tool never
    // returned.
    let host = one_of_each();
    host.time_out("usermod");

    let outcome = set_account_logins_locked_under_lock(&host, distro(), &account(), true);

    assert!(
        matches!(outcome, Err(LoginsError::Bound(_))),
        "got {outcome:?}"
    );
}

#[test]
fn a_passwd_status_read_that_times_out_is_a_bound() {
    let host = one_of_each();
    host.time_out("passwd");

    let outcome = set_account_logins_locked_under_lock(&host, distro(), &account(), true);

    assert!(
        matches!(outcome, Err(LoginsError::Bound(_))),
        "got {outcome:?}"
    );
}

#[test]
fn a_cull_that_times_out_fails_the_suspension_with_a_bound() {
    // Some processes may have been signalled and some not, so the suspension is
    // reported as failed, exactly as for a refusing `pkill`.
    let host = one_of_each().with_process(ACCOUNT_UID);
    host.time_out("pkill");

    let outcome = set_account_logins_locked_under_lock(&host, distro(), &account(), true);

    assert!(
        matches!(outcome, Err(LoginsError::Bound(_))),
        "got {outcome:?}"
    );
}

#[test]
fn each_tool_of_the_lock_runs_in_the_class_of_what_it_does() {
    // `usermod` and `pkill` change state (Mutation, 120 s); `passwd -S` is a
    // question (Query). A wrong class is a wrong deadline: a Reload class would
    // let a wedged `usermod` hold the account lock for four minutes.
    let host = one_of_each().with_process(ACCOUNT_UID);

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    for spawn in host.spawns() {
        let expected = if spawn.argv[0].ends_with("passwd") {
            CallClass::Query
        } else {
            CallClass::Mutation
        };
        assert_eq!(spawn.class, expected, "{:?}", spawn.argv);
    }
    assert!(
        host.spawns()
            .iter()
            .any(|spawn| spawn.argv[0].ends_with("passwd")),
        "the status read must have run, or the loop above proved nothing"
    );
}

#[test]
fn passwd_status_is_asked_without_an_end_of_options_marker() {
    // The inverse control of the `--` guard on `usermod`: `passwd` is the one
    // tool of this area whose handling of `--` differs between the families, so
    // it is deliberately NOT guarded and a marker appearing here is a change
    // somebody has to have measured.
    let host = one_of_each();

    set_account_logins_locked_under_lock(&host, distro(), &account(), true).expect("locked");

    let status_reads: Vec<Vec<String>> = host
        .spawns()
        .into_iter()
        .map(|spawn| spawn.argv)
        .filter(|argv| argv[0].ends_with("passwd"))
        .collect();
    assert!(!status_reads.is_empty());
    for argv in status_reads {
        assert!(!argv.iter().any(|argument| argument == "--"), "{argv:?}");
    }
}
