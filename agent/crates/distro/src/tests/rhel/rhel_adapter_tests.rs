//! Tests for the RHEL-family adapter's database and SFTP facts.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `rhel_adapter.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::RhelAdapter;
use crate::DistroAdapter;

/// The RHEL family spawns the MariaDB client from the path its package installs it at.
#[test]
fn the_rhel_family_runs_the_mysql_client_from_usr_bin() {
    assert_eq!(RhelAdapter.mysql_client_binary(), "/usr/bin/mysql");
}

/// The RHEL family restarts the database through MariaDB's own unit name.
#[test]
fn the_rhel_family_restarts_the_database_through_the_mariadb_unit() {
    assert_eq!(RhelAdapter.mysql_service(), "mariadb");
}

/// The RHEL family chroots SFTP accounts through the panel's own group.
#[test]
fn the_rhel_family_chroots_sftp_accounts_through_the_panel_group() {
    assert_eq!(RhelAdapter.sftp_group(), "maran-sftp");
}

/// Every tool the accounts area runs, paired with the path this family installs
/// it at.
///
/// Written out as literals rather than read back from the adapter, which is the
/// whole point: an assertion that asks the adapter for the value it is checking
/// proves only that a function returns what it returns. These literals are the
/// record of what was verified on a real RHEL host, so moving a tool to a
/// different directory has to be a deliberate edit here as well.
const EXPECTED_BINARIES: [(&str, &str); 9] = [
    ("useradd", "/usr/sbin/useradd"),
    ("usermod", "/usr/sbin/usermod"),
    ("userdel", "/usr/sbin/userdel"),
    ("chpasswd", "/usr/sbin/chpasswd"),
    ("setquota", "/usr/sbin/setquota"),
    ("quota", "/usr/bin/quota"),
    ("id", "/usr/bin/id"),
    ("chmod", "/usr/bin/chmod"),
    ("chgrp", "/usr/bin/chgrp"),
];

/// The adapter's answer for each tool, in the order of [`EXPECTED_BINARIES`].
fn actual_binaries() -> [&'static str; 9] {
    [
        RhelAdapter.useradd_binary(),
        RhelAdapter.usermod_binary(),
        RhelAdapter.userdel_binary(),
        RhelAdapter.chpasswd_binary(),
        RhelAdapter.setquota_binary(),
        RhelAdapter.quota_binary(),
        RhelAdapter.id_binary(),
        RhelAdapter.chmod_binary(),
        RhelAdapter.chgrp_binary(),
    ]
}

/// The RHEL family names each account tool at the path its packages install it at.
#[test]
fn the_rhel_family_names_every_account_tool_at_its_installed_path() {
    for ((tool, expected), actual) in EXPECTED_BINARIES.iter().zip(actual_binaries()) {
        assert_eq!(actual, *expected, "{tool}");
    }
}

/// No tool is named by anything a root process would resolve through `PATH`.
///
/// Separate from the equality test above, because the two say different things:
/// one is "this is the path we checked", the other is "whatever the path
/// becomes, it can never be a bare name". A future edit that mistypes a path
/// fails the first; one that shortens it to `usermod` fails both, and the second
/// is the one that names the consequence.
#[test]
fn no_account_tool_is_named_by_something_path_would_resolve() {
    for ((tool, _), actual) in EXPECTED_BINARIES.iter().zip(actual_binaries()) {
        assert!(
            actual.starts_with('/'),
            "{tool} is named {actual}, which PATH would resolve for a process running as root",
        );
    }
}
