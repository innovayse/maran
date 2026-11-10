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
use crate::{
    AccountPrograms, BackupFacts, DatabaseFacts, DnsFacts, FileTransferFacts, FirewallFacts,
    MailFacts, PackageFacts, PhpFacts, SchedulingFacts, ServiceManagerFacts, WebServerFacts,
};

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
const EXPECTED_BINARIES: [(&str, &str); 30] = [
    ("useradd", "/usr/sbin/useradd"),
    ("usermod", "/usr/sbin/usermod"),
    ("pkill", "/usr/bin/pkill"),
    ("pgrep", "/usr/bin/pgrep"),
    ("atq", "/usr/bin/atq"),
    ("atrm", "/usr/bin/atrm"),
    ("loginctl", "/usr/bin/loginctl"),
    ("ipcrm", "/usr/bin/ipcrm"),
    ("userdel", "/usr/sbin/userdel"),
    ("chpasswd", "/usr/sbin/chpasswd"),
    ("ssh-keygen", "/usr/bin/ssh-keygen"),
    ("ssh", "/usr/bin/ssh"),
    ("ssh-keyscan", "/usr/bin/ssh-keyscan"),
    ("rsync", "/usr/bin/rsync"),
    ("setquota", "/usr/sbin/setquota"),
    ("quota", "/usr/bin/quota"),
    ("quotaon", "/usr/sbin/quotaon"),
    ("timedatectl", "/usr/bin/timedatectl"),
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
    ("package_query", "/usr/bin/dnf"),
    ("package_candidate", "/usr/bin/dnf"),
];

/// The adapter's answer for each tool, in the order of [`EXPECTED_BINARIES`].
fn actual_binaries() -> [String; 30] {
    [
        RhelAdapter.useradd_binary().to_string(),
        RhelAdapter.usermod_binary().to_string(),
        RhelAdapter.pkill_binary().to_string(),
        RhelAdapter.pgrep_binary().to_string(),
        RhelAdapter.atq_binary().to_string(),
        RhelAdapter.atrm_binary().to_string(),
        RhelAdapter.loginctl_binary().to_string(),
        RhelAdapter.ipcrm_binary().to_string(),
        RhelAdapter.userdel_binary().to_string(),
        RhelAdapter.chpasswd_binary().to_string(),
        RhelAdapter.ssh_keygen_binary().to_string(),
        RhelAdapter.ssh_binary().to_string(),
        RhelAdapter.ssh_keyscan_binary().to_string(),
        RhelAdapter.rsync_binary().to_string(),
        RhelAdapter.setquota_binary().to_string(),
        RhelAdapter.quota_binary().to_string(),
        RhelAdapter.quotaon_binary().to_string(),
        RhelAdapter.timedatectl_binary().to_string(),
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
        RhelAdapter.package_query_binary().to_string(),
        RhelAdapter.package_candidate_binary().to_string(),
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

/// The RHEL family spawns the OpenSSH client from the path its `openssh-clients` package installs it at.
#[test]
fn the_rhel_family_names_the_ssh_client_where_the_package_puts_it() {
    assert_eq!(RhelAdapter.ssh_binary(), "/usr/bin/ssh");
}

/// The RHEL family spawns `ssh-keyscan` from the path its `openssh-clients` package installs it at.
#[test]
fn the_rhel_family_names_ssh_keyscan_where_the_package_puts_it() {
    assert_eq!(RhelAdapter.ssh_keyscan_binary(), "/usr/bin/ssh-keyscan");
}

/// The three programs a migration source is reached with are three different files.
///
/// The inverse control for the two tests above: each is satisfied by an adapter
/// that answers the same path for all three, which would make a connection run
/// the key generator or a scan run the client.
#[test]
fn the_rhel_family_names_the_three_ssh_programs_apart() {
    let all = [
        RhelAdapter.ssh_binary().to_string(),
        RhelAdapter.ssh_keyscan_binary().to_string(),
        RhelAdapter.ssh_keygen_binary().to_string(),
    ];

    assert_ne!(all[0], all[1]);
    assert_ne!(all[0], all[2]);
    assert_ne!(all[1], all[2]);
}

/// The adapter traits' own sources (every concern trait `DistroAdapter` composes),
/// read as text so the table below is checked against the traits and not against a
/// number somebody has to remember to bump.
const TRAIT_SOURCES: [&str; 12] = [
    include_str!("../../account_programs.rs"),
    include_str!("../../backup_facts.rs"),
    include_str!("../../database_facts.rs"),
    include_str!("../../dns_facts.rs"),
    include_str!("../../file_transfer_facts.rs"),
    include_str!("../../firewall_facts.rs"),
    include_str!("../../mail_facts.rs"),
    include_str!("../../package_facts.rs"),
    include_str!("../../php_facts.rs"),
    include_str!("../../scheduling_facts.rs"),
    include_str!("../../service_manager_facts.rs"),
    include_str!("../../web_server_facts.rs"),
];

/// How many methods the trait declares that return an `AdapterBinary`.
///
/// Counted from the declarations (`-> AdapterBinary;`), comment lines skipped,
/// the same source-scan idiom `adapter_binary_tests.rs` uses. A method added to
/// the trait and both impls but not to the table makes this larger than the
/// table, so the completeness assertion goes red without anyone editing a
/// literal.
fn declared_binary_methods() -> usize {
    TRAIT_SOURCES
        .iter()
        .flat_map(|source| source.lines())
        .map(str::trim)
        .filter(|line| !line.starts_with("//"))
        .filter(|line| line.ends_with("-> AdapterBinary;"))
        .count()
}

/// Every program the RHEL adapter lets the agent execute, with the method that names it.
///
/// One entry per `AdapterBinary`-returning method of the trait (the fixed paths
/// plus the version-built php-fpm validator). `sh` and the login shell are not
/// here: they are written somewhere, never executed by the agent, so they stay
/// plain strings (see the type assertion below).
fn every_executable() -> Vec<(&'static str, AdapterBinary)> {
    let version = PhpVersion::parse("8.3").unwrap();
    vec![
        ("nginx", RhelAdapter.nginx_binary()),
        ("service_manager", RhelAdapter.service_manager()),
        ("journal", RhelAdapter.journal_binary()),
        ("systemd_run", RhelAdapter.systemd_run_binary()),
        ("php_fpm", RhelAdapter.php_fpm_binary(&version)),
        ("openssl", RhelAdapter.openssl_binary()),
        ("tar", RhelAdapter.tar_binary()),
        ("gzip", RhelAdapter.gzip_binary()),
        ("database_dump", RhelAdapter.database_dump_binary()),
        ("mysql_client", RhelAdapter.mysql_client_binary()),
        ("vsftpd", RhelAdapter.vsftpd_binary()),
        ("dns_checkconf", RhelAdapter.dns_checkconf_binary()),
        ("dns_checkzone", RhelAdapter.dns_checkzone_binary()),
        ("dns_control", RhelAdapter.dns_control_binary()),
        ("useradd", RhelAdapter.useradd_binary()),
        ("userdel", RhelAdapter.userdel_binary()),
        ("usermod", RhelAdapter.usermod_binary()),
        ("pkill", RhelAdapter.pkill_binary()),
        ("pgrep", RhelAdapter.pgrep_binary()),
        ("atq", RhelAdapter.atq_binary()),
        ("atrm", RhelAdapter.atrm_binary()),
        ("loginctl", RhelAdapter.loginctl_binary()),
        ("ipcrm", RhelAdapter.ipcrm_binary()),
        ("passwd", RhelAdapter.passwd_binary()),
        ("getent", RhelAdapter.getent_binary()),
        ("package_query", RhelAdapter.package_query_binary()),
        ("package_candidate", RhelAdapter.package_candidate_binary()),
        ("setquota", RhelAdapter.setquota_binary()),
        ("quota", RhelAdapter.quota_binary()),
        ("quotaon", RhelAdapter.quotaon_binary()),
        ("timedatectl", RhelAdapter.timedatectl_binary()),
        ("id", RhelAdapter.id_binary()),
        ("chmod", RhelAdapter.chmod_binary()),
        ("chgrp", RhelAdapter.chgrp_binary()),
        ("chpasswd", RhelAdapter.chpasswd_binary()),
        ("ssh-keygen", RhelAdapter.ssh_keygen_binary()),
        ("ssh", RhelAdapter.ssh_binary()),
        ("ssh_keyscan", RhelAdapter.ssh_keyscan_binary()),
        ("rsync", RhelAdapter.rsync_binary()),
        ("crontab", RhelAdapter.crontab_binary()),
        ("nft", RhelAdapter.nft_binary()),
        ("postfix", RhelAdapter.postfix_binary()),
        ("postconf", RhelAdapter.postconf_binary()),
        ("postmap", RhelAdapter.postmap_binary()),
        ("postqueue", RhelAdapter.postqueue_binary()),
        ("postsuper", RhelAdapter.postsuper_binary()),
        ("doveadm", RhelAdapter.doveadm_binary()),
        ("doveconf", RhelAdapter.doveconf_binary()),
        ("rspamadm", RhelAdapter.rspamadm_binary()),
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

/// The RHEL family documents `/bin/bash` for a login shell, and a login shell is a
/// different path from the shell of an account that must not log in.
#[test]
fn the_rhel_family_names_its_documented_bash_and_not_its_nologin_shell() {
    assert_eq!(RhelAdapter.bash_shell(), "/bin/bash");
    assert_ne!(RhelAdapter.bash_shell(), RhelAdapter.nologin_shell());
}

/// The Rhel family keeps `at` job files in the directory its `at` package spools to.
#[test]
fn the_rhel_family_names_the_directory_its_at_daemon_spools_to() {
    assert_eq!(RhelAdapter.at_spool_directory(), "/var/spool/at");
}

/// The Rhel family names the real directory its mail spool files live in.
#[test]
fn the_rhel_family_names_the_real_directory_of_its_mail_spool() {
    assert_eq!(RhelAdapter.mail_spool_directory(), "/var/spool/mail");
}

/// The pending-updates read on the RHEL family is cache-only, and says so in its
/// arguments: `-C` is what stops the run contacting a repository.
#[test]
fn the_rhel_family_reads_pending_updates_from_the_cache_only() {
    assert!(RhelAdapter.pending_updates_arguments().contains(&"-C"));
    assert_eq!(
        RhelAdapter.package_query_binary().to_string(),
        RhelAdapter.package_manager()
    );
}

/// `dnf check-update -C` exits 1 on a host whose cache was never built, so this
/// family needs no separate probe.
#[test]
fn the_rhel_family_runs_no_catalogue_probe() {
    assert!(RhelAdapter.catalogue_probe().is_none());
}

/// The security reading of the RHEL family is the cache-only run with the
/// security filter, as owned strings.
#[test]
fn the_rhel_family_reads_security_updates_from_the_cache_only() {
    assert_eq!(
        RhelAdapter.security_pending_updates_arguments(),
        Some(vec![
            String::from("check-update"),
            String::from("--security"),
            String::from("-C"),
            String::from("-q"),
        ])
    );
}

/// The adapter's marking classifies by the second reading: a package it names is
/// security, another is ordinary.
#[test]
fn the_rhel_family_marks_rows_by_the_security_reading() {
    let mut rows = RhelAdapter
        .read_pending_updates(
            100,
            "curl-minimal.x86_64 7.76.1-40.el9_8.9 baseos\nkrb5-libs.x86_64 1.21.1-10.el9_8.1 baseos\n",
        )
        .unwrap();
    rows = RhelAdapter
        .mark_security_updates(rows, 100, "curl-minimal.x86_64 7.76.1-40.el9_8.9 baseos\n")
        .unwrap();

    assert_eq!(
        rows[0].classification,
        crate::UpdateClassification::Security
    );
    assert_eq!(
        rows[1].classification,
        crate::UpdateClassification::Ordinary
    );
}

/// The RHEL family answers the reboot question with a program, named by an
/// absolute path and given the one `-r` argument.
#[test]
fn the_rhel_family_probes_for_a_reboot_with_needs_restarting() {
    match RhelAdapter.reboot_probe() {
        crate::RebootProbe::Program { binary, arguments } => {
            assert_eq!(binary.to_string(), "/usr/bin/needs-restarting");
            assert!(binary.as_path().is_absolute());
            assert_eq!(arguments, vec![String::from("-r")]);
        }
        other => panic!("expected a program probe, got {other:?}"),
    }
}

/// The adapter reads the probe's status: 0 is not required, 1 is required.
#[test]
fn the_rhel_family_reads_the_reboot_probe_status() {
    assert_eq!(
        RhelAdapter.read_reboot_probe_run(0),
        Ok(crate::RebootRequirement::NotRequired)
    );
    assert_eq!(
        RhelAdapter.read_reboot_probe_run(1),
        Ok(crate::RebootRequirement::Required)
    );
    assert_eq!(
        RhelAdapter.read_reboot_probe_run(127),
        Err(crate::RebootProbeFailure::Unsuccessful { status: 127 })
    );
}

/// The RHEL family names the daemon's checkers and control client by absolute path.
#[test]
fn the_rhel_family_names_the_dns_checkers_and_control_client() {
    assert_eq!(
        RhelAdapter.dns_checkconf_binary(),
        "/usr/sbin/named-checkconf"
    );
    assert_eq!(
        RhelAdapter.dns_checkzone_binary(),
        "/usr/sbin/named-checkzone"
    );
    assert_eq!(RhelAdapter.dns_control_binary(), "/usr/sbin/rndc");
}

/// The RHEL family runs the DNS daemon as `named`, whose group is the one the control key is readable by.
#[test]
fn the_rhel_family_runs_the_dns_daemon_as_its_own_user_and_group() {
    assert_eq!(RhelAdapter.dns_user(), "named");
    assert_eq!(RhelAdapter.dns_group(), "named");
}

/// Zone files sit below the daemon's state directory, never beside it, so a zone name can never name the zone-list store.
#[test]
fn the_rhel_family_keeps_zone_files_below_the_dns_state_directory() {
    assert_eq!(RhelAdapter.dns_state_directory(), "/var/named/maran");
    assert_eq!(RhelAdapter.dns_zone_directory(), "/var/named/maran/zones");
}

/// Signed zones and their keys sit in two directories of their own below the state directory, apart from the read-only plain zone directory and from each other.
#[test]
fn the_rhel_family_keeps_signed_zones_and_keys_in_their_own_directories() {
    assert_eq!(
        RhelAdapter.dns_signed_zone_directory(),
        "/var/named/maran/signed-zones"
    );
    assert_eq!(RhelAdapter.dns_key_directory(), "/var/named/maran/keys");

    let directories = [
        RhelAdapter.dns_zone_directory(),
        RhelAdapter.dns_signed_zone_directory(),
        RhelAdapter.dns_key_directory(),
    ];
    for (index, directory) in directories.iter().enumerate() {
        assert!(directory.starts_with(RhelAdapter.dns_state_directory()));
        for other in &directories[index + 1..] {
            assert!(
                !other.starts_with(directory) && !directory.starts_with(other),
                "{directory} and {other} must not nest"
            );
        }
    }
}

#[test]
fn the_rhel_family_names_rsync_at_its_documented_path() {
    assert_eq!(RhelAdapter.rsync_binary(), "/usr/bin/rsync");
}
