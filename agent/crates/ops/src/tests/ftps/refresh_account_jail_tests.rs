//! A restore replaces the home inode, so the jail's bind mount must be made
//! again: the unit is restarted for an account that has one and an account
//! without one gets nothing (issue #487).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::ftps::fake_ftps_host::{FakeFtpsHost, account, distro};
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::refresh_account_jail::refresh_account_jail;

/// The account every test here refreshes.
const ACCOUNT: &str = "alice";

/// The arguments of every `systemctl restart` the host was asked for.
fn restarts(host: &FakeFtpsHost) -> Vec<Vec<String>> {
    host.spawns()
        .into_iter()
        .filter(|spawn| spawn.argv.get(1).is_some_and(|verb| verb == "restart"))
        .map(|spawn| spawn.argv)
        .collect()
}

/// The jail's mount unit is restarted, so it binds the directory that is the
/// home now.
#[test]
fn an_account_with_a_jail_has_its_mount_unit_restarted() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).with_jail();

    let refreshed = refresh_account_jail(&host, distro(), &account(ACCOUNT)).unwrap();

    assert!(refreshed);
    let restarts = restarts(&host);
    assert_eq!(restarts.len(), 1, "{restarts:?}");
    assert_eq!(
        restarts[0][1..],
        ["restart", "--", host.jail().unit_name()],
        "the unit name follows the end-of-options marker"
    );
}

/// The inverse control: an account that never had an FTPS login has no jail,
/// and refreshing it must not create one.
#[test]
fn an_account_without_a_jail_gets_nothing() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    let refreshed = refresh_account_jail(&host, distro(), &account(ACCOUNT)).unwrap();

    assert!(!refreshed);
    assert!(host.spawns().is_empty(), "{:?}", host.spawns());
    assert!(host.created_directories().is_empty());
}

/// A restart the service manager refuses is an error, never a refreshed jail.
#[test]
fn a_refused_restart_is_a_failure() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001)
        .with_jail()
        .refusing_mount_restart();

    let error = refresh_account_jail(&host, distro(), &account(ACCOUNT)).unwrap_err();

    assert!(matches!(error, FtpsError::JailFailed), "{error:?}");
}
