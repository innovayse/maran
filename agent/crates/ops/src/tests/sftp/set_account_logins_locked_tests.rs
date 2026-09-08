//! Locking every key into an account's home, and the one the account's own
//! lock never reached.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sftp::fake_sftp_host::{FakeSftpHost, account, distro};
use crate::sftp::set_account_logins_locked::set_account_logins_locked;
use crate::sftp::sftp_error::SftpError;

/// A host holding two of `alice`'s logins, neither of them locked.
fn two_logins() -> FakeSftpHost {
    FakeSftpHost::new()
        .with_login("alice_web")
        .with_login("alice_deploy")
}

#[test]
fn every_login_the_account_holds_is_locked_and_not_only_the_first() {
    let host = two_logins();

    set_account_logins_locked(&host, distro(), &account(), true).expect("locked");

    assert!(host.is_locked("alice_web"));
    assert!(host.is_locked("alice_deploy"));
}

#[test]
fn logins_are_unlocked_again_when_the_account_is_resumed() {
    // The inverse control for the assertion above: an implementation that
    // locked everything unconditionally would satisfy it and fail here.
    let host = two_logins()
        .with_locked("alice_web")
        .with_locked("alice_deploy");

    set_account_logins_locked(&host, distro(), &account(), false).expect("unlocked");

    assert!(!host.is_locked("alice_web"));
    assert!(!host.is_locked("alice_deploy"));
}

#[test]
fn a_neighbouring_accounts_login_is_left_alone() {
    // `alice_bob` is both a login of `alice` and a plausible account name of
    // its own. The enumeration tells them apart by the passwd home, and this is
    // the assertion that fails if anyone replaces it with a name prefix.
    let host = FakeSftpHost::new()
        .with_login("alice_web")
        .with_hosting_account("alicecorp")
        .with_login("alicecorp_web");

    set_account_logins_locked(&host, distro(), &account(), true).expect("locked");

    assert!(host.is_locked("alice_web"));
    assert!(
        !host.is_locked("alicecorp_web"),
        "another account's login must not be locked by this account's suspension"
    );
}

#[test]
fn an_account_with_no_logins_is_a_success_that_touches_nothing() {
    let host = FakeSftpHost::new();

    set_account_logins_locked(&host, distro(), &account(), true).expect("locked");

    assert!(host.spawns().is_empty(), "nothing may be run");
}

#[test]
fn locking_twice_converges() {
    let host = two_logins();

    set_account_logins_locked(&host, distro(), &account(), true).expect("locked");
    set_account_logins_locked(&host, distro(), &account(), true).expect("locked again");

    assert!(host.is_locked("alice_web"));
}

#[test]
fn nothing_is_deleted_and_no_password_is_reset() {
    // Suspension revokes a key; it must not destroy what the key opened, and it
    // must give back the SAME credential on the resume rather than a new one
    // the customer would have to be told about.
    let host = two_logins();

    set_account_logins_locked(&host, distro(), &account(), true).expect("locked");

    assert_eq!(host.users().len(), 2, "no login may be removed");
    assert!(
        host.spawn_of("/usr/sbin/chpasswd").is_none()
            && host.spawn_of("/usr/bin/chpasswd").is_none(),
        "no password may be set: {:?}",
        host.spawns()
    );
}

#[test]
fn a_refused_usermod_stops_the_operation_rather_than_reporting_success() {
    let host = two_logins();
    host.refuse_usermod_with(1);

    let refused = set_account_logins_locked(&host, distro(), &account(), true);

    assert!(
        matches!(refused, Err(SftpError::SpawnFailed { code: 1 })),
        "got {refused:?}"
    );
}

#[test]
fn a_password_database_that_cannot_be_read_refuses_instead_of_locking_nothing() {
    // The vacuity guard on the axis that can go blind: an unreadable database
    // and an account with no logins both produce an empty list, and the empty
    // list is the one that reads as "everything was locked".
    let host = two_logins();
    host.forget_the_account();

    let refused = set_account_logins_locked(&host, distro(), &account(), true);

    assert!(
        matches!(refused, Err(SftpError::AccountMissing)),
        "got {refused:?}"
    );
}
