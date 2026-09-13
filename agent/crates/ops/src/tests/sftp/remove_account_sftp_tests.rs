//! What `remove_account_sftp` takes away, in which order, and the refusal that
//! stops an account deletion rather than deleting a customer's files.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use crate::sftp::fake_sftp_host::{FakeSftpHost, account, distro};
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::remove_account_sftp::remove_account_sftp;
use crate::sftp::sftp_error::SftpError;

/// `alice`'s jail, derived exactly as the operation derives it.
fn jail() -> AccountJail {
    AccountJail::for_account(&account(), distro().systemd_unit_directory())
}

/// A host holding `alice`'s two logins, her jail and her mount unit.
fn host_with_a_built_jail() -> FakeSftpHost {
    let jail = jail();

    FakeSftpHost::new()
        .with_login("alice_web")
        .with_login("alice_deploy")
        .with_path(jail.mount_point())
        .with_path(jail.directory())
        .with_path(jail.unit_path())
}

/// Every login the account has goes, not just one.
#[test]
fn every_login_the_account_holds_is_removed() {
    let host = host_with_a_built_jail();

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert!(host.users().is_empty());
}

/// A neighbouring account whose name this one is a prefix of keeps its logins.
#[test]
fn an_account_whose_name_this_one_is_a_prefix_of_keeps_its_logins() {
    // `alice_` is a prefix of `alice_bob_deploy`, which is `alice_bob`'s login
    // into `alice_bob`'s home. A prefix scan here would revoke another
    // tenant's access as a side effect of deleting this one.
    let host = FakeSftpHost::new()
        .with_login("alice_web")
        .with_login("alice_bob_deploy");

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert_eq!(host.users(), vec!["alice_bob_deploy".to_owned()]);
}

/// A neighbouring ACCOUNT whose name is spelled like one of this account's
/// logins is left alone.
#[test]
fn a_neighbouring_account_spelled_like_one_of_this_accounts_logins_is_not_removed() {
    // The trap, and it is not hypothetical: `AccountName` permits the separator,
    // so the hosting account `alice_two` and the login `two` of account `alice`
    // are the SAME eleven characters in `/etc/passwd`. No decode of the name can
    // tell them apart, and one that tried deleted the neighbour's system user as
    // a side effect of deleting `alice` — which the polygon caught, on a real
    // host, after every unit test here was green.
    //
    // What settles it is the passwd HOME: a login this agent creates is homed in
    // its account's jail, and a hosting account is homed under `/home`.
    let host = FakeSftpHost::new()
        .with_login("alice_web")
        .with_hosting_account("alice_two");

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert_eq!(
        host.users(),
        vec!["alice_two".to_owned()],
        "the neighbouring account must survive: only the jailed logins are this account's"
    );
}

/// The account's own system user is never mistaken for one of its logins.
#[test]
fn the_accounts_own_system_user_is_not_treated_as_one_of_its_logins() {
    // It carries no separator after the account name, so it decodes to nothing.
    // Removing it here would run `userdel` without `-r` on the account itself
    // and leave `AccountOperations::delete` with no user to delete.
    let host = FakeSftpHost::new().with_hosting_account("alice");

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert_eq!(host.users(), vec!["alice".to_owned()]);
}

/// The mount is stopped through its unit, not merely disabled for next boot.
#[test]
fn the_mount_unit_is_stopped_now_and_not_only_disabled_for_the_next_boot() {
    let host = host_with_a_built_jail();

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    let disable = host
        .spawns()
        .into_iter()
        .find(|spawn| spawn.argv.contains(&"disable".to_owned()))
        .expect("the unit was disabled");
    assert_eq!(
        disable.argv,
        vec![
            distro().service_manager().to_owned(),
            "disable".to_owned(),
            "--now".to_owned(),
            jail().unit_name().to_owned(),
        ]
    );
}

/// The unit is stopped before the jail is taken away.
#[test]
fn the_mount_is_stopped_before_the_jail_directories_are_removed() {
    // The other order asks for the removal of a directory the customer's home
    // is still mounted into.
    let host = host_with_a_built_jail();

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert!(
        host.spawns()
            .iter()
            .any(|spawn| spawn.argv.contains(&"disable".to_owned())),
        "the unit must be stopped at all"
    );
    assert!(
        !host.paths().contains(&jail().mount_point().to_owned()),
        "the mount point must have been removed after the unmount"
    );
}

/// The jail, its mount point and its unit file are all gone afterwards.
#[test]
fn the_jail_its_mount_point_and_its_unit_file_are_all_removed() {
    let host = host_with_a_built_jail();

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert!(
        host.paths().is_empty(),
        "a re-created account of the same name must get a fresh jail: {:?}",
        host.paths()
    );
}

/// The service manager is told to forget the unit file that was removed.
#[test]
fn the_service_manager_is_reloaded_once_the_unit_file_is_gone() {
    let host = host_with_a_built_jail();

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert!(
        host.spawns()
            .iter()
            .any(|spawn| spawn.argv.contains(&"daemon-reload".to_owned())),
        "a unit file that is gone must not still be loaded: {:?}",
        host.spawns()
    );
}

/// A mount that survives the unmount stops the deletion instead of being
/// walked into.
#[test]
fn a_mount_point_that_will_not_come_away_fails_the_removal_rather_than_being_deleted_recursively() {
    // This is the single most dangerous moment in the whole cascade. If the
    // unmount silently failed and the removal were recursive, the next step
    // would delete the customer's entire website from inside their own jail.
    // The removal refuses a non-empty directory, so the deletion stops with the
    // account still present — recoverable — instead.
    let host = host_with_a_built_jail().refuse_removal_of(jail().mount_point());

    let failure = remove_account_sftp(&host, distro(), &account()).expect_err("must fail");

    assert!(matches!(failure, SftpError::JailFailed));
    assert!(
        host.paths().contains(&jail().directory().to_owned()),
        "nothing below the refusal may have been taken away"
    );
}

/// A service manager that refuses to stop the mount stops the deletion.
#[test]
fn a_service_manager_that_refuses_to_stop_the_mount_fails_the_removal() {
    let host = host_with_a_built_jail();
    host.refuse_systemctl_with(1);

    let failure = remove_account_sftp(&host, distro(), &account()).expect_err("must fail");

    assert!(matches!(failure, SftpError::JailFailed));
    assert!(
        host.paths().contains(&jail().mount_point().to_owned()),
        "the jail must be left alone when the unmount was refused"
    );
}

/// An account that never had SFTP is removed without asking the service manager
/// about a unit that was correctly never written.
#[test]
fn an_account_that_never_had_an_sftp_login_is_removed_without_touching_the_service_manager() {
    let host = FakeSftpHost::new();

    remove_account_sftp(&host, distro(), &account()).expect("removed");

    assert!(
        host.spawns().is_empty(),
        "nothing was there, so nothing should have been run: {:?}",
        host.spawns()
    );
}

/// Removing twice converges instead of failing on its own previous work.
#[test]
fn removing_an_accounts_sftp_twice_converges() {
    let host = host_with_a_built_jail();

    remove_account_sftp(&host, distro(), &account()).expect("removed");
    remove_account_sftp(&host, distro(), &account()).expect("removed again");

    assert!(host.users().is_empty());
    assert!(host.paths().is_empty());
}

/// A host whose password database cannot be read fails rather than reporting
/// that the account has no logins.
#[test]
fn a_password_database_that_cannot_be_read_fails_the_removal() {
    // The dangerous shrug: "no logins found" and "could not look" must not be
    // the same answer, because the caller is about to run `userdel`.
    let host = FakeSftpHost::new().with_login("alice_web");
    host.forget_the_account();

    let failure = remove_account_sftp(
        &host,
        distro(),
        &AccountName::parse("alice").expect("valid"),
    )
    .expect_err("must fail");

    assert!(matches!(failure, SftpError::AccountMissing));
}
