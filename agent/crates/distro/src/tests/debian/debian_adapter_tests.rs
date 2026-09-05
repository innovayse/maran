//! Tests for the Debian-family adapter's database, SFTP, cron and firewall facts.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `debian_adapter.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::DebianAdapter;
use crate::DistroAdapter;

/// The Debian family spawns the MariaDB client from the path its package installs it at.
#[test]
fn the_debian_family_runs_the_mysql_client_from_usr_bin() {
    assert_eq!(DebianAdapter.mysql_client_binary(), "/usr/bin/mysql");
}

/// The Debian family restarts the database through MariaDB's own unit name.
#[test]
fn the_debian_family_restarts_the_database_through_the_mariadb_unit() {
    assert_eq!(DebianAdapter.mysql_service(), "mariadb");
}

/// The Debian family chroots SFTP accounts through the panel's own group.
#[test]
fn the_debian_family_chroots_sftp_accounts_through_the_panel_group() {
    assert_eq!(DebianAdapter.sftp_group(), "maran-sftp");
}

/// The Debian family runs cron under the unit its `cron` package registers.
#[test]
fn the_debian_family_runs_cron_through_the_cron_unit() {
    assert_eq!(DebianAdapter.cron_service(), "cron");
}

/// The Debian family drives the firewall through the nftables unit.
#[test]
fn the_debian_family_drives_the_firewall_through_the_nftables_unit() {
    assert_eq!(DebianAdapter.firewall_service(), "nftables");
}

/// The Debian family serves SSH from the unit its OpenSSH package registers.
#[test]
fn the_debian_family_serves_ssh_from_the_ssh_unit() {
    assert_eq!(DebianAdapter.ssh_service(), "ssh");
}

/// The Debian family wires its firewall include into the file its nftables unit reads.
#[test]
fn the_debian_family_wires_its_firewall_include_into_the_file_its_unit_reads() {
    assert_eq!(
        DebianAdapter.nftables_include_target(),
        "/etc/nftables.conf"
    );
}

/// The panel reports exactly the four units it manages on the Debian family.
///
/// A literal list, not a comparison against the accessors it is built from:
/// asking the adapter for both halves of an equality proves only that one
/// function calls another. This is the record of WHICH units the closed set
/// holds and what each is called here.
#[test]
fn the_debian_family_reports_exactly_the_four_units_the_panel_manages() {
    assert_eq!(
        DebianAdapter.managed_units(),
        ["nginx", "mariadb", "cron", "ssh"]
    );
}

/// Every tool the agent runs or names, paired with the path this family installs
/// it at.
///
/// Written out as literals rather than read back from the adapter, which is the
/// whole point: an assertion that asks the adapter for the value it is checking
/// proves only that a function returns what it returns. These literals are the
/// record of a DELIBERATE choice, so moving a tool to a different directory has
/// to be an edit here as well.
///
/// What this table cannot do, said plainly because its previous wording claimed
/// otherwise: it cannot verify that a path exists. It compares one string
/// literal to another inside this crate and touches no filesystem, so it read
/// green for as long as `/usr/bin/quota` was on neither test image. That check
/// is `crates/agent/tests/binary_paths_on_a_real_host.rs`, which stats what the
/// adapter declares on a real host of each family; this table is the record of
/// intent, and that suite is the observation.
///
/// `sh` is in the table for the same reason and not because the agent spawns it
/// — it never does — but because the path is written into a crontab line, where
/// a wrong one is a cron entry that silently never runs.
const EXPECTED_BINARIES: [(&str, &str); 12] = [
    ("useradd", "/usr/sbin/useradd"),
    ("usermod", "/usr/sbin/usermod"),
    ("userdel", "/usr/sbin/userdel"),
    ("chpasswd", "/usr/sbin/chpasswd"),
    ("setquota", "/usr/sbin/setquota"),
    ("quota", "/usr/bin/quota"),
    ("id", "/usr/bin/id"),
    ("chmod", "/usr/bin/chmod"),
    ("chgrp", "/usr/bin/chgrp"),
    ("crontab", "/usr/bin/crontab"),
    ("nft", "/usr/sbin/nft"),
    ("sh", "/bin/sh"),
];

/// The adapter's answer for each tool, in the order of [`EXPECTED_BINARIES`].
fn actual_binaries() -> [&'static str; 12] {
    [
        DebianAdapter.useradd_binary(),
        DebianAdapter.usermod_binary(),
        DebianAdapter.userdel_binary(),
        DebianAdapter.chpasswd_binary(),
        DebianAdapter.setquota_binary(),
        DebianAdapter.quota_binary(),
        DebianAdapter.id_binary(),
        DebianAdapter.chmod_binary(),
        DebianAdapter.chgrp_binary(),
        DebianAdapter.crontab_binary(),
        DebianAdapter.nft_binary(),
        DebianAdapter.sh_binary(),
    ]
}

/// The Debian family names each tool at the path its packages install it at.
#[test]
fn the_debian_family_names_every_tool_at_its_installed_path() {
    for ((tool, expected), actual) in EXPECTED_BINARIES.iter().zip(actual_binaries()) {
        assert_eq!(actual, *expected, "{tool}");
    }
}

/// No tool is named by anything `PATH` would resolve for whoever runs it.
///
/// Separate from the equality test above, because the two say different things:
/// one is "this is the path we checked", the other is "whatever the path
/// becomes, it can never be a bare name". A future edit that mistypes a path
/// fails the first; one that shortens it to `usermod` fails both, and the second
/// is the one that names the consequence.
///
/// "Whoever runs it" is deliberately not one actor, and the message below names
/// none: the agent spawns most of these itself as root, and `sh` it never spawns
/// at all — that path is written into a crontab line and `cron` runs it as the
/// account. A bare name is resolved through `PATH` either way, and either way
/// whoever controls the environment would then be choosing the program.
#[test]
fn no_tool_is_named_by_something_path_would_resolve() {
    for ((tool, _), actual) in EXPECTED_BINARIES.iter().zip(actual_binaries()) {
        assert!(
            actual.starts_with('/'),
            "{tool} is named {actual}: a bare name is resolved through PATH by whoever runs it, \
             so the environment and not this crate would be choosing the program",
        );
    }
}
