//! Tests for `get_accounts_disk_usage`: which passwd rows are hosting accounts
//! and what each one occupies.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::get_accounts_disk_usage;
use crate::monitor::MonitorError;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro};

/// A password database in the shape both families ship: system users first,
/// then a hosting account, then that account's SFTP login.
const PASSWD: &str = "root:x:0:0:root:/root:/bin/bash\n\
                      daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin\n\
                      alice:x:1001:1001::/home/alice:/usr/sbin/nologin\n\
                      alice_deploy:x:1001:1001::/var/lib/maran/sftp/alice:/usr/sbin/nologin\n";

/// The names reported, in the order they came back.
fn names(host: &FakeMonitorHost) -> Vec<String> {
    get_accounts_disk_usage(host, distro())
        .expect("the password database is readable")
        .into_iter()
        .map(|usage| usage.account.as_str().to_owned())
        .collect()
}

#[test]
fn every_hosting_account_is_measured() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_passwd(PASSWD)
        .with_size("/home/alice", 4096);

    let usage = get_accounts_disk_usage(&host, distro()).expect("the database is readable");

    assert_eq!(usage.len(), 1);
    assert_eq!(usage[0].account.as_str(), "alice");
    assert_eq!(usage[0].used_bytes, 4096);
}

#[test]
fn a_system_user_is_not_a_hosting_account() {
    // `root` and `daemon` are names this panel could not have chosen, and their
    // homes are not under the home root.
    let host = FakeMonitorHost::from_ubuntu_captures().with_passwd(PASSWD);

    assert_eq!(names(&host), ["alice"]);
}

#[test]
fn an_sftp_login_is_not_reported_as_an_account() {
    // Account names may contain underscores, so the login `bob` of account
    // `alice` is spelled exactly like the ACCOUNT `alice_bob` and no inspection
    // of the name tells them apart. Their homes do: a login's home is its
    // account's jail, and only an account lives under the home root. Without
    // this, a login would be billed as an account of its own and its bytes
    // counted twice.
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_passwd(
            "alice:x:1001:1001::/home/alice:/usr/sbin/nologin\n\
             alice_bob:x:1002:1002::/var/lib/maran/sftp/alice:/usr/sbin/nologin\n",
        )
        .with_size("/home/alice", 4096)
        .with_size("/var/lib/maran/sftp/alice", 4096);

    let usage = get_accounts_disk_usage(&host, distro()).expect("the database is readable");

    assert_eq!(usage.len(), 1);
    assert_eq!(usage[0].account.as_str(), "alice");
}

#[test]
fn an_account_whose_home_is_not_its_own_name_is_not_reported() {
    // The home must be exactly `<home root>/<name>`, so a row pointing inside
    // another account's home, or at a traversal of one, is not that account.
    let host = FakeMonitorHost::from_ubuntu_captures().with_passwd(
        "carol:x:1003:1003::/home/alice/carol:/usr/sbin/nologin\n\
         dave:x:1004:1004::/home/alice/../dave:/usr/sbin/nologin\n",
    );

    assert!(names(&host).is_empty());
}

#[test]
fn an_account_with_nothing_in_its_home_measures_zero() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_passwd(PASSWD);

    let usage = get_accounts_disk_usage(&host, distro()).expect("the database is readable");

    assert_eq!(usage[0].used_bytes, 0);
}

#[test]
fn the_accounts_come_back_in_a_stable_order() {
    // Two calls against an unchanged host must answer in the same order,
    // whatever order the file happened to hold its rows in.
    let host = FakeMonitorHost::from_ubuntu_captures().with_passwd(
        "carol:x:1003:1003::/home/carol:/usr/sbin/nologin\n\
         alice:x:1001:1001::/home/alice:/usr/sbin/nologin\n\
         bob:x:1002:1002::/home/bob:/usr/sbin/nologin\n",
    );

    assert_eq!(names(&host), ["alice", "bob", "carol"]);
}

#[test]
fn an_account_named_twice_in_the_database_is_reported_once() {
    // Nothing stops a passwd file from carrying a name twice — the shadow tools
    // will not write one, but a hand edit or a half-finished restore will.
    // Every other tool on the host uses the first row; reporting the account
    // twice would charge its bytes twice on the panel's dashboard.
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_passwd(
            "alice:x:1001:1001::/home/alice:/usr/sbin/nologin\n\
             bob:x:1002:1002::/home/bob:/usr/sbin/nologin\n\
             alice:x:1001:1001::/home/alice:/bin/sh\n",
        )
        .with_size("/home/alice", 4096);

    let usage = get_accounts_disk_usage(&host, distro()).expect("the database is readable");

    assert_eq!(names(&host), ["alice", "bob"]);
    assert_eq!(usage.len(), 2);
}

#[test]
fn an_unreadable_password_database_is_an_error() {
    // Reporting an empty list instead would read to the panel as "every account
    // was deleted".
    let host = FakeMonitorHost::from_ubuntu_captures().with_unreadable_passwd();

    assert_eq!(
        get_accounts_disk_usage(&host, distro()),
        Err(MonitorError::AccountsUnavailable)
    );
}

#[test]
fn no_quota_tool_is_run_to_measure_usage() {
    // Used bytes only. The quota an account is measured against is the panel's
    // own data, so reading it back here would run the quota tools on every
    // dashboard refresh — on a host that may not have them installed — to learn
    // a number the caller already had.
    let host = FakeMonitorHost::from_ubuntu_captures().with_passwd(PASSWD);

    get_accounts_disk_usage(&host, distro()).expect("the database is readable");

    assert!(host.commands().is_empty());
}
