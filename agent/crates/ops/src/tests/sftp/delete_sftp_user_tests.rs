//! What `delete_sftp_user` takes away, everything it leaves alone, and the
//! neighbouring tenant it must not touch.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::accounts::take_account_lock;
use crate::sftp::delete_sftp_user::delete_sftp_user;
use crate::sftp::fake_sftp_host::{FakeSftpHost, distro};
use crate::sftp::sftp_error::SftpError;

/// A validated account name, for a fixture that names its own.
///
/// Each test names its OWN account, because the operation's entry point takes a
/// process-wide per-account lock that never waits: two tests sharing one account
/// name on the harness's threads would refuse each other, and the flake would
/// say nothing about the code.
fn account(name: &str) -> AccountName {
    AccountName::parse(name).expect("the fixture's own names are valid")
}

/// The `web` login of `account`, as the constructor builds it.
fn login_of(account: &AccountName) -> SftpUserName {
    SftpUserName::for_account(account, "web").expect("a valid login name")
}

/// A host holding `account`'s `web` login and nothing else.
fn host_with_login(account: &AccountName) -> FakeSftpHost {
    FakeSftpHost::new().with_login(login_of(account).as_str())
}

/// The login goes and the files stay.
#[test]
fn deleting_an_sftp_user_removes_the_login_without_removing_its_files() {
    // `userdel -r` would walk into the bind mount and delete the customer's
    // whole website, for an operation that means "revoke one login".
    let account = account("sftpdelfiles");
    let host = host_with_login(&account);

    delete_sftp_user(&host, distro(), &account, &login_of(&account)).expect("deleted");

    let userdel = host.spawn_of("userdel").expect("userdel was run");
    assert_eq!(
        userdel.argv,
        vec![
            "/usr/sbin/userdel".to_owned(),
            login_of(&account).as_str().to_owned()
        ]
    );
    assert!(
        !userdel.argv.iter().any(|argument| argument == "-r"
            || argument == "--remove"
            || argument == "--remove-home"),
        "the home belongs to the account, not to this login: {:?}",
        userdel.argv
    );
    assert!(host.users().is_empty());
}

/// The account's jail survives the login, because other logins may use it.
#[test]
fn deleting_an_sftp_user_leaves_the_accounts_jail_and_mount_in_place() {
    let account = account("sftpdeljail");
    let host = host_with_login(&account);

    delete_sftp_user(&host, distro(), &account, &login_of(&account)).expect("deleted");

    assert!(
        host.configs().is_empty() && host.directories().is_empty(),
        "the jail is an account resource with account lifetime"
    );
}

/// A repeat converges on `NotFound` instead of failing.
#[test]
fn deleting_a_user_that_is_not_there_reports_not_found() {
    let account = account("sftpdelgone");
    let host = FakeSftpHost::new();

    let error =
        delete_sftp_user(&host, distro(), &account, &login_of(&account)).expect_err("must fail");

    assert!(matches!(error, SftpError::NotFound));
}

/// Any other refusal arrives as a status and nothing else.
#[test]
fn a_refusal_that_is_not_a_missing_user_carries_the_status_alone() {
    assert!(matches!(
        SftpError::from_userdel(8),
        SftpError::SpawnFailed { code: 8 }
    ));
    assert!(matches!(SftpError::from_userdel(6), SftpError::NotFound));
}

#[test]
fn a_neighbouring_hosting_account_whose_name_collides_is_not_deleted() {
    // The defect this operation shipped with. `AccountName` permits
    // underscores, so a request authorised for `sftpdelx` naming the login
    // `bob` addresses the system user `sftpdelx_bob` — which is here a
    // NEIGHBOURING hosting account, homed under /home and not in any jail.
    // `userdel` on it destroys that tenant's identity and leaves their
    // populated home owned by a uid the next `useradd` can be given.
    let account = account("sftpdelx");
    let neighbour = "sftpdelx_bob";
    let host = FakeSftpHost::new()
        .with_hosting_account(neighbour)
        .with_login(login_of(&account).as_str());
    let colliding = SftpUserName::for_account(&account, "bob").expect("a valid login name");

    let refused = delete_sftp_user(&host, distro(), &account, &colliding);

    // The artefact, not the return value: the neighbour's passwd entry is still
    // there. A `userdel` that ran and an operation that refused are told apart
    // by the password database, not by an `Err`.
    assert!(
        host.users().contains(&neighbour.to_owned()),
        "the neighbouring account's passwd entry must survive: {:?}",
        host.users()
    );
    assert!(
        host.spawn_of("userdel").is_none(),
        "no `userdel` may run at all for a login this account does not hold: {:?}",
        host.spawns()
    );
    assert!(
        matches!(refused, Err(SftpError::NotFound)),
        "the refusal is NotFound, so it does not confirm the neighbour exists: {refused:?}"
    );
}

#[test]
fn the_accounts_own_login_is_still_deleted_when_a_colliding_neighbour_exists() {
    // The positive control. Without it the test above is satisfied by an
    // operation that refuses everything, which would be a worse defect than the
    // one being closed.
    let account = account("sftpdely");
    let host = FakeSftpHost::new()
        .with_hosting_account("sftpdely_bob")
        .with_login(login_of(&account).as_str());

    delete_sftp_user(&host, distro(), &account, &login_of(&account)).expect("deleted");

    assert_eq!(
        host.users(),
        vec!["sftpdely_bob".to_owned()],
        "the account's own login is gone and the neighbour is untouched"
    );
}

#[test]
fn an_ftps_login_of_the_same_account_is_not_reachable_through_this_operation() {
    // The two daemons' logins share a uid and a name shape; only the home tells
    // them apart, and this operation's enumeration is filtered by the SFTP
    // jail's directory. So a caller cannot revoke the account's FTPS key
    // through the rpc that revokes its SFTP one.
    let account = account("sftpdelz");
    let foreign = "sftpdelz_ftp";
    let host = FakeSftpHost::new()
        .with_foreign_login(foreign, "/var/lib/maran-ftps/sftpdelz")
        .with_login(login_of(&account).as_str());
    let colliding = SftpUserName::for_account(&account, "ftp").expect("a valid login name");

    let refused = delete_sftp_user(&host, distro(), &account, &colliding);

    assert!(
        host.users().contains(&foreign.to_owned()),
        "the other daemon's login must survive: {:?}",
        host.users()
    );
    assert!(matches!(refused, Err(SftpError::NotFound)), "{refused:?}");
}

#[test]
fn a_deletion_is_refused_while_the_hosting_accounts_lock_is_held() {
    let account = account("sftpdelbusy");
    let held = take_account_lock(&account).expect("the lock is free at the start of this test");
    let host = host_with_login(&account);

    let refused = delete_sftp_user(&host, distro(), &account, &login_of(&account));

    assert!(
        matches!(refused, Err(SftpError::AccountBusy)),
        "{refused:?}"
    );
    assert!(
        host.spawns().is_empty(),
        "the operation never started: {:?}",
        host.spawns()
    );
    drop(held);
}

#[test]
fn a_deletion_that_finished_leaves_the_hosting_accounts_lock_free() {
    // The inverse control. A guard leaked by the entry point would make the
    // refusal above pass forever and every later operation for that account
    // impossible — a worse defect than the race being closed.
    let account = account("sftpdelfree");
    let host = host_with_login(&account);

    delete_sftp_user(&host, distro(), &account, &login_of(&account)).expect("deleted");

    assert!(
        take_account_lock(&account).is_some(),
        "the lock must be free once the operation returned"
    );
}
