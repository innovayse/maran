//! What removing one FTPS login takes away, what it must leave standing, and
//! the neighbouring tenant it must not touch.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;

use crate::accounts::take_account_lock;
use crate::ftps::delete_ftps_user::delete_ftps_user;
use crate::ftps::fake_ftps_host::{FakeFtpsHost, distro};
use crate::ftps::ftps_error::FtpsError;

/// The login suffix the tests here remove.
const SUFFIX: &str = "files";

/// A host holding `account`'s jail and its `files` login.
///
/// Each test names its OWN account, because the operation's entry point takes a
/// process-wide per-account lock that never waits: two tests sharing one account
/// name on the harness's threads would refuse each other, and the flake would
/// say nothing about the code.
fn host_with_login(account: &str) -> FakeFtpsHost {
    FakeFtpsHost::for_account(account, 1001, 1001)
        .with_jail()
        .with_existing_login(&format!("{account}_{SUFFIX}"))
}

/// The validated login name, built the only way one can be.
fn login(host: &FakeFtpsHost, name: &str) -> FtpsUserName {
    FtpsUserName::for_account(&host.account_name(), name).expect("a valid login name")
}

#[test]
fn deleting_a_login_removes_it_from_the_password_database() {
    let host = host_with_login("ftpsdelone");

    delete_ftps_user(&host, distro(), &host.account_name(), &login(&host, SUFFIX))
        .expect("the deletion must succeed");

    assert!(host.login_names().is_empty());
}

#[test]
fn userdel_is_run_without_the_flag_that_would_delete_the_customers_files() {
    // `-r` would walk into the bind mount inside the jail and delete the
    // account's entire website, for an operation whose meaning is "revoke one
    // login".
    let host = host_with_login("ftpsdelflag");

    delete_ftps_user(&host, distro(), &host.account_name(), &login(&host, SUFFIX))
        .expect("the deletion must succeed");

    let spawn = host.last_spawn().expect("userdel ran");
    assert_eq!(
        spawn.argv,
        vec![
            distro().userdel_binary().to_owned(),
            "ftpsdelflag_files".to_owned()
        ],
        "the argument vector is the program and the name, and nothing else"
    );
}

#[test]
fn deleting_one_login_leaves_the_accounts_jail_and_its_mount_alone() {
    // The jail is shared by every login the account holds, so unmounting on the
    // first deletion would break the others. Its lifetime is the ACCOUNT's.
    let host = FakeFtpsHost::for_account("ftpsdeljail", 1001, 1001)
        .with_jail_still_mounted()
        .with_existing_login("ftpsdeljail_files");

    delete_ftps_user(&host, distro(), &host.account_name(), &login(&host, SUFFIX))
        .expect("the deletion must succeed");

    assert!(host.is_mounted(), "the bind mount must still be up");
    assert!(host.directory_still_exists(host.jail().directory()));
    assert!(host.directory_still_exists(host.jail().mount_point()));
    assert!(host.unit_file_exists(host.jail().unit_path()));
}

#[test]
fn deleting_a_login_that_is_not_there_reports_not_found_rather_than_failing() {
    let host = FakeFtpsHost::for_account("ftpsdelgone", 1001, 1001).with_jail();

    let result = delete_ftps_user(&host, distro(), &host.account_name(), &login(&host, SUFFIX));

    assert!(
        matches!(result, Err(FtpsError::NotFound)),
        "a second deletion must converge, which is what makes a retry after a \
         lost response safe: {result:?}"
    );
}

#[test]
fn a_neighbouring_hosting_account_whose_name_collides_is_not_deleted() {
    // The defect this operation was written with, and the witness the review
    // ran. `AccountName` permits underscores, so a request authorised for
    // `ftpsdelx` naming the login `bob` addresses the system user
    // `ftpsdelx_bob` — which is here a NEIGHBOURING hosting account, homed
    // under /home and not in any jail. `userdel` on it destroys that tenant's
    // identity and leaves their populated home owned by a uid the next
    // `useradd` can be given.
    let host = host_with_login("ftpsdelx").with_foreign_login("ftpsdelx_bob", "/home/ftpsdelx_bob");

    let refused = delete_ftps_user(&host, distro(), &host.account_name(), &login(&host, "bob"));

    // The artefact, not the return value: the neighbour's passwd entry is still
    // there. A `userdel` that ran and an operation that refused are told apart
    // by the password database, not by an `Err`.
    assert!(
        host.login_names().contains(&"ftpsdelx_bob".to_owned()),
        "the neighbouring account's passwd entry must survive: {:?}",
        host.login_names()
    );
    assert!(
        host.last_spawn().is_none(),
        "no `userdel` may run at all for a login this account does not hold: {:?}",
        host.spawns()
    );
    assert!(
        matches!(refused, Err(FtpsError::NotFound)),
        "the refusal is NotFound, so it does not confirm the neighbour exists: {refused:?}"
    );
}

#[test]
fn the_accounts_own_login_is_still_deleted_when_a_colliding_neighbour_exists() {
    // The positive control. Without it the test above is satisfied by an
    // operation that refuses everything, which would be a worse defect than the
    // one being closed.
    let host = host_with_login("ftpsdely").with_foreign_login("ftpsdely_bob", "/home/ftpsdely_bob");

    delete_ftps_user(&host, distro(), &host.account_name(), &login(&host, SUFFIX))
        .expect("the account's own login must still be removable");

    assert_eq!(
        host.login_names(),
        vec!["ftpsdely_bob".to_owned()],
        "the account's own login is gone and the neighbour is untouched"
    );
}

#[test]
fn an_sftp_login_of_the_same_account_is_not_reachable_through_this_operation() {
    // The two daemons' logins share a uid and a name shape; only the home tells
    // them apart, and this operation's enumeration is filtered by the FTPS
    // jail's directory.
    let host = host_with_login("ftpsdelsftp")
        .with_foreign_login("ftpsdelsftp_ssh", "/var/lib/maran-sftp/ftpsdelsftp");

    let refused = delete_ftps_user(&host, distro(), &host.account_name(), &login(&host, "ssh"));

    assert!(
        host.login_names().contains(&"ftpsdelsftp_ssh".to_owned()),
        "the SFTP login must survive: {:?}",
        host.login_names()
    );
    assert!(matches!(refused, Err(FtpsError::NotFound)), "{refused:?}");
}

#[test]
fn a_second_operation_for_the_same_account_is_refused_rather_than_queued() {
    let host = host_with_login("ftpsdelbusy");
    let account = host.account_name();
    let guard = take_account_lock(&account).expect("the lock is free");

    let result = delete_ftps_user(&host, distro(), &account, &login(&host, SUFFIX));

    assert!(matches!(result, Err(FtpsError::AccountBusy)), "{result:?}");
    assert!(
        host.spawns().is_empty(),
        "nothing may be read and nothing removed when the lock refuses"
    );
    assert!(
        host.login_names().contains(&"ftpsdelbusy_files".to_owned()),
        "the login is untouched"
    );
    drop(guard);
}

#[test]
fn a_deletion_that_finished_leaves_the_hosting_accounts_lock_free() {
    // The inverse control. A guard leaked by the entry point would make the
    // refusal above pass forever and every later operation for that account
    // impossible — a worse defect than the race being closed.
    let host = host_with_login("ftpsdelfree");
    let account = host.account_name();

    delete_ftps_user(&host, distro(), &account, &login(&host, SUFFIX)).expect("deleted");

    assert!(
        take_account_lock(&account).is_some(),
        "the lock must be free once the operation returned"
    );
}
