//! Tests for the RHEL-family adapter's database, SFTP, cron and firewall facts.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `rhel_adapter.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Component;

use maran_agent_core::validation::web::php_version::PhpVersion;

use super::RhelAdapter;
use crate::AdapterBinary;
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

/// The RHEL family spawns the FTPS daemon from the path its `vsftpd` package installs it at.
#[test]
fn the_rhel_family_names_the_ftp_daemon_where_the_package_puts_it() {
    assert_eq!(RhelAdapter.vsftpd_binary(), "/usr/sbin/vsftpd");
}

/// The RHEL family authorizes FTPS logins through the panel's own group.
#[test]
fn the_rhel_family_authorizes_ftps_logins_through_the_panel_group() {
    assert_eq!(RhelAdapter.ftps_group(), "maran-ftps");
}

/// The RHEL family spells the TLS version options with an underscore before the minor.
#[test]
fn the_rhel_family_spells_the_tls_version_options_with_underscores() {
    let keys = RhelAdapter.vsftpd_tls_version_keys();

    assert_eq!(keys.tls_v1, "ssl_tlsv1");
    assert_eq!(keys.tls_v1_1, "ssl_tlsv1_1");
    assert_eq!(keys.tls_v1_2, "ssl_tlsv1_2");
}

/// The RHEL family runs cron under the unit its `cronie` package registers.
#[test]
fn the_rhel_family_runs_cron_through_the_crond_unit() {
    assert_eq!(RhelAdapter.cron_service(), "crond");
}

/// The RHEL family serves SSH from the unit its OpenSSH package registers.
#[test]
fn the_rhel_family_serves_ssh_from_the_sshd_unit() {
    assert_eq!(RhelAdapter.ssh_service(), "sshd");
}

/// The RHEL family answers the same `sshd_config` path as the Debian family —
/// the installer writes to this literal path on both, unconditionally.
#[test]
fn the_rhel_family_configures_sshd_at_the_documented_path() {
    assert_eq!(RhelAdapter.sshd_config_path(), "/etc/ssh/sshd_config");
}

/// The panel reports exactly the four units it manages on the RHEL family.
///
/// A literal list, not a comparison against the accessors it is built from:
/// asking the adapter for both halves of an equality proves only that one
/// function calls another. This is the record of WHICH units the closed set
/// holds and what each is called here — two of the four spelled differently
/// from the Debian family.
#[test]
fn the_rhel_family_reports_exactly_the_four_units_the_panel_manages() {
    assert_eq!(
        RhelAdapter.managed_units(),
        ["nginx", "mariadb", "crond", "sshd"]
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
const EXPECTED_BINARIES: [(&str, &str); 19] = [
    ("useradd", "/usr/sbin/useradd"),
    ("usermod", "/usr/sbin/usermod"),
    ("pkill", "/usr/bin/pkill"),
    ("pgrep", "/usr/bin/pgrep"),
    ("userdel", "/usr/sbin/userdel"),
    ("chpasswd", "/usr/sbin/chpasswd"),
    ("setquota", "/usr/sbin/setquota"),
    ("quota", "/usr/bin/quota"),
    ("quotaon", "/usr/sbin/quotaon"),
    ("id", "/usr/bin/id"),
    ("chmod", "/usr/bin/chmod"),
    ("chgrp", "/usr/bin/chgrp"),
    ("crontab", "/usr/bin/crontab"),
    ("nft", "/usr/sbin/nft"),
    ("sh", "/bin/sh"),
    ("tar", "/usr/bin/tar"),
    ("gzip", "/usr/bin/gzip"),
    ("database_dump", "/usr/bin/mariadb-dump"),
    ("getent", "/usr/bin/getent"),
];

/// The adapter's answer for each tool, in the order of [`EXPECTED_BINARIES`].
fn actual_binaries() -> [String; 19] {
    [
        RhelAdapter.useradd_binary().to_string(),
        RhelAdapter.usermod_binary().to_string(),
        RhelAdapter.pkill_binary().to_string(),
        RhelAdapter.pgrep_binary().to_string(),
        RhelAdapter.userdel_binary().to_string(),
        RhelAdapter.chpasswd_binary().to_string(),
        RhelAdapter.setquota_binary().to_string(),
        RhelAdapter.quota_binary().to_string(),
        RhelAdapter.quotaon_binary().to_string(),
        RhelAdapter.id_binary().to_string(),
        RhelAdapter.chmod_binary().to_string(),
        RhelAdapter.chgrp_binary().to_string(),
        RhelAdapter.crontab_binary().to_string(),
        RhelAdapter.nft_binary().to_string(),
        RhelAdapter.sh_binary().to_owned(),
        RhelAdapter.tar_binary().to_string(),
        RhelAdapter.gzip_binary().to_string(),
        RhelAdapter.database_dump_binary().to_string(),
        RhelAdapter.getent_binary().to_string(),
    ]
}

/// The RHEL family names each tool at the path its packages install it at.
#[test]
fn the_rhel_family_names_every_tool_at_its_installed_path() {
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

/// The trait's own source, read as text so the table below is checked against
/// the trait and not against a number somebody has to remember to bump.
const TRAIT_SOURCE: &str = include_str!("../../adapter.rs");

/// How many methods the trait declares that return an `AdapterBinary`.
///
/// Counted from the declarations (`-> AdapterBinary;`), comment lines skipped,
/// the same source-scan idiom `adapter_binary_tests.rs` uses. A method added to
/// the trait and both impls but not to the table makes this larger than the
/// table, so the completeness assertion goes red without anyone editing a
/// literal.
fn declared_binary_methods() -> usize {
    TRAIT_SOURCE
        .lines()
        .map(str::trim)
        .filter(|line| !line.starts_with("//"))
        .filter(|line| line.ends_with("-> AdapterBinary;"))
        .count()
}

/// Every program the RHEL adapter lets the agent execute, with the method that names it.
///
/// One entry per `AdapterBinary`-returning method of the trait (24 fixed paths
/// plus the version-built php-fpm validator). `sh` and the login shell are not
/// here: they are written somewhere, never executed by the agent, so they stay
/// plain strings (see the type assertion below).
fn every_executable() -> Vec<(&'static str, AdapterBinary)> {
    let version = PhpVersion::parse("8.3").unwrap();
    vec![
        ("nginx", RhelAdapter.nginx_binary()),
        ("service_manager", RhelAdapter.service_manager()),
        ("php_fpm", RhelAdapter.php_fpm_binary(&version)),
        ("openssl", RhelAdapter.openssl_binary()),
        ("tar", RhelAdapter.tar_binary()),
        ("gzip", RhelAdapter.gzip_binary()),
        ("database_dump", RhelAdapter.database_dump_binary()),
        ("mysql_client", RhelAdapter.mysql_client_binary()),
        ("vsftpd", RhelAdapter.vsftpd_binary()),
        ("useradd", RhelAdapter.useradd_binary()),
        ("userdel", RhelAdapter.userdel_binary()),
        ("usermod", RhelAdapter.usermod_binary()),
        ("pkill", RhelAdapter.pkill_binary()),
        ("pgrep", RhelAdapter.pgrep_binary()),
        ("passwd", RhelAdapter.passwd_binary()),
        ("getent", RhelAdapter.getent_binary()),
        ("setquota", RhelAdapter.setquota_binary()),
        ("quota", RhelAdapter.quota_binary()),
        ("quotaon", RhelAdapter.quotaon_binary()),
        ("id", RhelAdapter.id_binary()),
        ("chmod", RhelAdapter.chmod_binary()),
        ("chgrp", RhelAdapter.chgrp_binary()),
        ("chpasswd", RhelAdapter.chpasswd_binary()),
        ("crontab", RhelAdapter.crontab_binary()),
        ("nft", RhelAdapter.nft_binary()),
    ]
}

/// The RHEL adapter names every program it lets the agent execute by an absolute path.
///
/// An absolute path is what makes "the agent starts only what an adapter named"
/// hold: a bare name would be resolved through `PATH` by a root process. The
/// table is checked for being complete first, so a method added to the list
/// without a path check cannot hide.
#[test]
fn every_program_the_rhel_adapter_executes_is_an_absolute_path() {
    let all = every_executable();
    assert_eq!(
        all.len(),
        declared_binary_methods(),
        "the table must list every AdapterBinary method the trait declares"
    );

    for (name, binary) in &all {
        assert!(
            binary.as_path().is_absolute(),
            "{name} is {binary}, not an absolute path"
        );
    }
}

/// No program path the RHEL adapter names climbs out of its directory with `..`.
#[test]
fn no_program_the_rhel_adapter_executes_has_a_parent_directory_component() {
    for (name, binary) in every_executable() {
        assert!(
            !binary
                .as_path()
                .components()
                .any(|part| matches!(part, Component::ParentDir)),
            "{name} is {binary}, which contains a `..`",
        );
    }
}

/// No program path the RHEL adapter names carries whitespace or a control character.
///
/// Such a character cannot be part of a real install path and would corrupt any
/// log line or unit file the path is later written into.
#[test]
fn no_program_the_rhel_adapter_executes_has_whitespace_or_control_characters() {
    for (name, binary) in every_executable() {
        assert!(
            !binary
                .as_str()
                .chars()
                .any(|c| c.is_whitespace() || c.is_control()),
            "{name} is {binary:?}",
        );
    }
}

/// The php-fpm validator path is built from the validated version, so two versions
/// give two different paths, each in this family's layout.
#[test]
fn the_php_fpm_validator_path_is_built_from_the_validated_version() {
    let first = PhpVersion::parse("8.3").unwrap();
    let second = PhpVersion::parse("8.4").unwrap();

    assert_eq!(
        RhelAdapter.php_fpm_binary(&first),
        "/opt/remi/php83/root/usr/sbin/php-fpm"
    );
    assert_eq!(
        RhelAdapter.php_fpm_binary(&second),
        "/opt/remi/php84/root/usr/sbin/php-fpm"
    );
    assert_ne!(
        RhelAdapter.php_fpm_binary(&first),
        RhelAdapter.php_fpm_binary(&second)
    );
}

/// `sh` and the login shell stay plain strings: the agent never executes them
/// (they are only written into a crontab line and an account record), so the
/// executable type is deliberately not put on them.
///
/// This is a compile-time guard: if either method returned `AdapterBinary`, the
/// bindings below would not compile.
#[test]
fn sh_and_nologin_stay_plain_paths_and_are_not_executable_typed() {
    let sh: &str = RhelAdapter.sh_binary();
    let nologin: &str = RhelAdapter.nologin_shell();

    assert!(sh.starts_with('/'));
    assert!(nologin.starts_with('/'));
}
