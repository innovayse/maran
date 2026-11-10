//! A restore replaces the home inode, so the jail's bind mount must be made
//! again: the unit is restarted for an account that has one and an account
//! without one gets nothing (issue #487).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use crate::sftp::fake_sftp_host::{FakeSftpHost, distro};
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::refresh_account_jail::refresh_account_jail;
use crate::sftp::sftp_error::SftpError;

/// The jail of the account every test here refreshes.
fn jail() -> AccountJail {
    AccountJail::for_account(&account(), distro().systemd_unit_directory())
}

/// The account every test here refreshes.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// A host whose account already has its jail's unit.
fn host_with_a_jail() -> FakeSftpHost {
    FakeSftpHost::new().with_path(jail().unit_path())
}

/// The arguments of every `systemctl restart` the host was asked for.
fn restarts(host: &FakeSftpHost) -> Vec<Vec<String>> {
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
    let host = host_with_a_jail();

    let refreshed = refresh_account_jail(&host, distro(), &account()).unwrap();

    assert!(refreshed);
    let restarts = restarts(&host);
    assert_eq!(restarts.len(), 1, "{restarts:?}");
    assert_eq!(
        restarts[0][1..],
        ["restart", "--", jail().unit_name()],
        "the unit name follows the end-of-options marker"
    );
}

/// The unit is brought to its current render before it is restarted, so an old
/// unit without `LazyUnmount=yes` does not fail to stop on a busy mount.
#[test]
fn the_unit_is_rendered_again_before_the_restart() {
    let host = host_with_a_jail();

    refresh_account_jail(&host, distro(), &account()).unwrap();

    let configs = host.configs();
    let unit = configs.first().expect("the unit was written");
    assert_eq!(unit.target, jail().unit_path());
    assert!(unit.contents.contains("LazyUnmount=yes"));
}

/// The inverse control: an account that never had an SFTP login has no jail,
/// and refreshing it must not create one.
#[test]
fn an_account_without_a_jail_gets_nothing() {
    let host = FakeSftpHost::new();

    let refreshed = refresh_account_jail(&host, distro(), &account()).unwrap();

    assert!(!refreshed);
    assert!(host.spawns().is_empty(), "{:?}", host.spawns());
    assert!(host.directories().is_empty());
    assert!(host.configs().is_empty());
}

/// A restart the service manager refuses is an error, never a refreshed jail.
#[test]
fn a_refused_restart_is_a_failure() {
    let host = host_with_a_jail();
    host.refuse_systemctl_with(1);

    let error = refresh_account_jail(&host, distro(), &account()).unwrap_err();

    assert!(matches!(error, SftpError::JailFailed), "{error:?}");
}
