//! The FTPS half of the account-deletion cascade: every login, the mount, the
//! jail — and what it refuses to do when the mount is still up.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::ftps::fake_ftps_host::{FakeFtpsHost, account, distro};
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::remove_account_ftps::remove_account_ftps;

/// The account whose FTPS resources these tests take away.
const ACCOUNT: &str = "alice";

#[test]
fn every_login_the_account_holds_is_removed_before_the_jail_comes_down() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail()
        .with_existing_login("alice_files")
        .with_existing_login("alice_media");

    remove_account_ftps(&host, distro(), &account(ACCOUNT)).expect("the teardown must succeed");

    assert!(host.login_names().is_empty(), "both logins must be revoked");

    let order: Vec<String> = host
        .spawns()
        .into_iter()
        .map(|spawn| spawn.argv.join(" "))
        .collect();
    let last_userdel = order
        .iter()
        .rposition(|line| line.starts_with(distro().userdel_binary()))
        .expect("userdel ran");
    let disable = order
        .iter()
        .position(|line| line.contains("disable"))
        .expect("the mount unit was stopped");
    assert!(
        last_userdel < disable,
        "no session may be opened against a jail that is being dismantled, and \
         userdel on the ACCOUNT refuses a home another passwd entry claims: {order:?}"
    );
}

#[test]
fn removing_an_accounts_ftps_refuses_to_delete_a_jail_whose_mount_is_still_up() {
    // remove_dir, never remove_dir_all: under that mount point is the customer's
    // real home, and a recursive delete across a live bind mount deletes it.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail_still_mounted()
        .with_a_mount_that_will_not_come_down();

    let result = remove_account_ftps(&host, distro(), &account(ACCOUNT));

    assert!(matches!(result, Err(FtpsError::JailFailed)), "{result:?}");
    assert!(
        host.directory_still_exists(host.jail().mount_point()),
        "the mount point must survive: refusing a non-empty directory is the \
         safety property, and the account is still present and recoverable"
    );
}

#[test]
fn a_mount_the_service_manager_will_not_stop_aborts_the_teardown() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail_still_mounted()
        .refusing_unit_stop();

    let result = remove_account_ftps(&host, distro(), &account(ACCOUNT));

    assert!(matches!(result, Err(FtpsError::JailFailed)), "{result:?}");
    assert!(host.is_mounted(), "nothing came down");
    assert!(host.unit_file_exists(host.jail().unit_path()));
}

#[test]
fn a_successful_teardown_leaves_no_login_no_mount_no_jail_and_no_unit() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail_still_mounted()
        .with_existing_login("alice_files");

    remove_account_ftps(&host, distro(), &account(ACCOUNT)).expect("the teardown must succeed");

    assert!(host.login_names().is_empty());
    assert!(!host.is_mounted());
    assert!(!host.directory_still_exists(host.jail().mount_point()));
    assert!(!host.directory_still_exists(host.jail().directory()));
    assert!(
        !host.unit_file_exists(host.jail().unit_path()),
        "a unit file naming a Where= that no longer exists is a failing unit on \
         the next boot, and the unit a re-created account of the same name would \
         inherit"
    );
}

#[test]
fn an_account_that_never_had_ftps_is_torn_down_without_asking_the_service_manager() {
    // The ordinary case: FTPS is off on this host, so the cascade's new step
    // must touch nothing rather than refuse. `systemctl disable` refuses a unit
    // it has no file for, which would fail the deletion of every account on a
    // host that never enabled FTPS.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    remove_account_ftps(&host, distro(), &account(ACCOUNT)).expect("the teardown must succeed");

    assert!(
        !host.spawns().iter().any(|spawn| spawn
            .argv
            .iter()
            .any(|argument| argument.contains("disable"))),
        "a unit that was correctly never written must not be stopped: {:?}",
        host.spawns()
    );
}

#[test]
fn a_login_of_this_account_in_another_jail_is_not_this_teardowns_to_remove() {
    // The SFTP login of the SAME account: same uid, same name shape, home in the
    // other jail. Only the home field tells the two apart, and revoking it here
    // would take away a credential this operation was never asked about.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail()
        .with_foreign_login("alice_files", "/var/lib/maran-sftp/alice");

    remove_account_ftps(&host, distro(), &account(ACCOUNT)).expect("the teardown must succeed");

    assert_eq!(
        host.login_names(),
        vec!["alice_files".to_owned()],
        "the SFTP login must survive: its home is the SFTP jail, and the two \
         protocols enumerate by their own jail directory"
    );
}

#[test]
fn a_neighbouring_accounts_login_is_never_taken_by_this_accounts_teardown() {
    // `alice_` is a prefix of `alice_bob_deploy`, which belongs to the ACCOUNT
    // `alice_bob`. A prefix scan would delete a neighbour's system user as a
    // side effect of this deletion; the decode splits at the LAST separator and
    // compares the whole account.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail()
        .with_foreign_login("alice_bob_deploy", "/var/lib/maran-ftps/alice_bob");

    remove_account_ftps(&host, distro(), &account(ACCOUNT)).expect("the teardown must succeed");

    assert_eq!(host.login_names(), vec!["alice_bob_deploy".to_owned()]);
}

#[test]
fn a_password_database_that_cannot_be_read_stops_the_teardown_rather_than_reporting_no_logins() {
    // "The host would not answer" must never be read as "there is nothing
    // there": that is the direction this whole area fails in, and an empty list
    // would let the cascade run `userdel` over live FTPS credentials.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail()
        .unreadable_passwd();

    let result = remove_account_ftps(&host, distro(), &account(ACCOUNT));

    assert!(
        matches!(result, Err(FtpsError::AccountMissing)),
        "{result:?}"
    );
    assert!(host.directory_still_exists(host.jail().directory()));
}
