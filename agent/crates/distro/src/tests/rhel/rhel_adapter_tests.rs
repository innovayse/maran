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

use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_agent_core::validation::web::php_version::PhpVersion;

use super::RhelAdapter;
use crate::AdapterBinary;
use crate::AppFacts;
use crate::{
    AccountPrograms, BackupFacts, DatabaseFacts, DnsFacts, FileTransferFacts, FirewallFacts,
    MailFacts, PackageFacts, PhpFacts, SchedulingFacts, ServiceManagerFacts, WebServerFacts,
};

/// The RHEL family spawns the MariaDB client from the path its package installs it at.
#[test]
fn the_rhel_family_runs_the_mysql_client_from_usr_bin() {
    assert_eq!(RhelAdapter::EL9.mysql_client_binary(), "/usr/bin/mysql");
}

/// The RHEL family restarts the database through MariaDB's own unit name.
#[test]
fn the_rhel_family_restarts_the_database_through_the_mariadb_unit() {
    assert_eq!(RhelAdapter::EL9.mysql_service(), "mariadb");
}

/// The RHEL family chroots SFTP accounts through the panel's own group.
#[test]
fn the_rhel_family_chroots_sftp_accounts_through_the_panel_group() {
    assert_eq!(RhelAdapter::EL9.sftp_group(), "maran-sftp");
}

/// The RHEL family spawns the FTPS daemon from the path its `vsftpd` package installs it at.
#[test]
fn the_rhel_family_names_the_ftp_daemon_where_the_package_puts_it() {
    assert_eq!(RhelAdapter::EL9.vsftpd_binary(), "/usr/sbin/vsftpd");
}

/// The RHEL family authorizes FTPS logins through the panel's own group.
#[test]
fn the_rhel_family_authorizes_ftps_logins_through_the_panel_group() {
    assert_eq!(RhelAdapter::EL9.ftps_group(), "maran-ftps");
}

/// The RHEL family spells the TLS version options with an underscore before the minor.
#[test]
fn the_rhel_family_spells_the_tls_version_options_with_underscores() {
    let keys = RhelAdapter::EL9.vsftpd_tls_version_keys();

    assert_eq!(keys.tls_v1, "ssl_tlsv1");
    assert_eq!(keys.tls_v1_1, "ssl_tlsv1_1");
    assert_eq!(keys.tls_v1_2, "ssl_tlsv1_2");
}

/// The RHEL family runs cron under the unit its `cronie` package registers.
#[test]
fn the_rhel_family_runs_cron_through_the_crond_unit() {
    assert_eq!(RhelAdapter::EL9.cron_service(), "crond");
}

/// The RHEL family serves SSH from the unit its OpenSSH package registers.
#[test]
fn the_rhel_family_serves_ssh_from_the_sshd_unit() {
    assert_eq!(RhelAdapter::EL9.ssh_service(), "sshd");
}

/// The RHEL family answers the same `sshd_config` path as the Debian family —
/// the installer writes to this literal path on both, unconditionally.
#[test]
fn the_rhel_family_configures_sshd_at_the_documented_path() {
    assert_eq!(RhelAdapter::EL9.sshd_config_path(), "/etc/ssh/sshd_config");
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
        RhelAdapter::EL9.managed_units(),
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
        RhelAdapter::EL9.useradd_binary().to_string(),
        RhelAdapter::EL9.usermod_binary().to_string(),
        RhelAdapter::EL9.pkill_binary().to_string(),
        RhelAdapter::EL9.pgrep_binary().to_string(),
        RhelAdapter::EL9.atq_binary().to_string(),
        RhelAdapter::EL9.atrm_binary().to_string(),
        RhelAdapter::EL9.loginctl_binary().to_string(),
        RhelAdapter::EL9.ipcrm_binary().to_string(),
        RhelAdapter::EL9.userdel_binary().to_string(),
        RhelAdapter::EL9.chpasswd_binary().to_string(),
        RhelAdapter::EL9.ssh_keygen_binary().to_string(),
        RhelAdapter::EL9.ssh_binary().to_string(),
        RhelAdapter::EL9.ssh_keyscan_binary().to_string(),
        RhelAdapter::EL9.rsync_binary().to_string(),
        RhelAdapter::EL9.setquota_binary().to_string(),
        RhelAdapter::EL9.quota_binary().to_string(),
        RhelAdapter::EL9.quotaon_binary().to_string(),
        RhelAdapter::EL9.timedatectl_binary().to_string(),
        RhelAdapter::EL9.id_binary().to_string(),
        RhelAdapter::EL9.chmod_binary().to_string(),
        RhelAdapter::EL9.chgrp_binary().to_string(),
        RhelAdapter::EL9.crontab_binary().to_string(),
        RhelAdapter::EL9.nft_binary().to_string(),
        RhelAdapter::EL9.sh_binary().to_owned(),
        RhelAdapter::EL9.tar_binary().to_string(),
        RhelAdapter::EL9.gzip_binary().to_string(),
        RhelAdapter::EL9.database_dump_binary().to_string(),
        RhelAdapter::EL9.getent_binary().to_string(),
        RhelAdapter::EL9.package_query_binary().to_string(),
        RhelAdapter::EL9.package_candidate_binary().to_string(),
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
    assert_eq!(RhelAdapter::EL9.ssh_binary(), "/usr/bin/ssh");
}

/// The RHEL family spawns `ssh-keyscan` from the path its `openssh-clients` package installs it at.
#[test]
fn the_rhel_family_names_ssh_keyscan_where_the_package_puts_it() {
    assert_eq!(
        RhelAdapter::EL9.ssh_keyscan_binary(),
        "/usr/bin/ssh-keyscan"
    );
}

/// The three programs a migration source is reached with are three different files.
///
/// The inverse control for the two tests above: each is satisfied by an adapter
/// that answers the same path for all three, which would make a connection run
/// the key generator or a scan run the client.
#[test]
fn the_rhel_family_names_the_three_ssh_programs_apart() {
    let all = [
        RhelAdapter::EL9.ssh_binary().to_string(),
        RhelAdapter::EL9.ssh_keyscan_binary().to_string(),
        RhelAdapter::EL9.ssh_keygen_binary().to_string(),
    ];

    assert_ne!(all[0], all[1]);
    assert_ne!(all[0], all[2]);
    assert_ne!(all[1], all[2]);
}

/// The adapter traits' own sources (every concern trait `DistroAdapter` composes),
/// read as text so the table below is checked against the traits and not against a
/// number somebody has to remember to bump.
const TRAIT_SOURCES: [&str; 13] = [
    include_str!("../../account_programs.rs"),
    include_str!("../../app_facts.rs"),
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
        ("nginx", RhelAdapter::EL9.nginx_binary()),
        ("apache", RhelAdapter::EL9.apache_binary()),
        (
            "node",
            RhelAdapter::EL9.node_binary(&NodeMajor::parse("20").unwrap()),
        ),
        ("service_manager", RhelAdapter::EL9.service_manager()),
        ("journal", RhelAdapter::EL9.journal_binary()),
        ("systemd_run", RhelAdapter::EL9.systemd_run_binary()),
        ("php_fpm", RhelAdapter::EL9.php_fpm_binary(&version)),
        ("openssl", RhelAdapter::EL9.openssl_binary()),
        ("tar", RhelAdapter::EL9.tar_binary()),
        ("gzip", RhelAdapter::EL9.gzip_binary()),
        ("database_dump", RhelAdapter::EL9.database_dump_binary()),
        ("mysql_client", RhelAdapter::EL9.mysql_client_binary()),
        ("vsftpd", RhelAdapter::EL9.vsftpd_binary()),
        ("dns_checkconf", RhelAdapter::EL9.dns_checkconf_binary()),
        ("dns_checkzone", RhelAdapter::EL9.dns_checkzone_binary()),
        ("dns_control", RhelAdapter::EL9.dns_control_binary()),
        ("useradd", RhelAdapter::EL9.useradd_binary()),
        ("userdel", RhelAdapter::EL9.userdel_binary()),
        ("usermod", RhelAdapter::EL9.usermod_binary()),
        ("pkill", RhelAdapter::EL9.pkill_binary()),
        ("pgrep", RhelAdapter::EL9.pgrep_binary()),
        ("atq", RhelAdapter::EL9.atq_binary()),
        ("atrm", RhelAdapter::EL9.atrm_binary()),
        ("loginctl", RhelAdapter::EL9.loginctl_binary()),
        ("ipcrm", RhelAdapter::EL9.ipcrm_binary()),
        ("passwd", RhelAdapter::EL9.passwd_binary()),
        ("getent", RhelAdapter::EL9.getent_binary()),
        ("package_query", RhelAdapter::EL9.package_query_binary()),
        (
            "package_candidate",
            RhelAdapter::EL9.package_candidate_binary(),
        ),
        ("setquota", RhelAdapter::EL9.setquota_binary()),
        ("quota", RhelAdapter::EL9.quota_binary()),
        ("quotaon", RhelAdapter::EL9.quotaon_binary()),
        ("timedatectl", RhelAdapter::EL9.timedatectl_binary()),
        ("id", RhelAdapter::EL9.id_binary()),
        ("chmod", RhelAdapter::EL9.chmod_binary()),
        ("chgrp", RhelAdapter::EL9.chgrp_binary()),
        ("chpasswd", RhelAdapter::EL9.chpasswd_binary()),
        ("ssh-keygen", RhelAdapter::EL9.ssh_keygen_binary()),
        ("ssh", RhelAdapter::EL9.ssh_binary()),
        ("ssh_keyscan", RhelAdapter::EL9.ssh_keyscan_binary()),
        ("rsync", RhelAdapter::EL9.rsync_binary()),
        ("crontab", RhelAdapter::EL9.crontab_binary()),
        ("nft", RhelAdapter::EL9.nft_binary()),
        ("postfix", RhelAdapter::EL9.postfix_binary()),
        ("postconf", RhelAdapter::EL9.postconf_binary()),
        ("postmap", RhelAdapter::EL9.postmap_binary()),
        ("postqueue", RhelAdapter::EL9.postqueue_binary()),
        ("postsuper", RhelAdapter::EL9.postsuper_binary()),
        ("doveadm", RhelAdapter::EL9.doveadm_binary()),
        ("doveconf", RhelAdapter::EL9.doveconf_binary()),
        ("rspamadm", RhelAdapter::EL9.rspamadm_binary()),
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
        RhelAdapter::EL9.php_fpm_binary(&first),
        "/opt/remi/php83/root/usr/sbin/php-fpm"
    );
    assert_eq!(
        RhelAdapter::EL9.php_fpm_binary(&second),
        "/opt/remi/php84/root/usr/sbin/php-fpm"
    );
    assert_ne!(
        RhelAdapter::EL9.php_fpm_binary(&first),
        RhelAdapter::EL9.php_fpm_binary(&second)
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
    let sh: &str = RhelAdapter::EL9.sh_binary();
    let nologin: &str = RhelAdapter::EL9.nologin_shell();

    assert!(sh.starts_with('/'));
    assert!(nologin.starts_with('/'));
}

/// The RHEL family documents `/bin/bash` for a login shell, and a login shell is a
/// different path from the shell of an account that must not log in.
#[test]
fn the_rhel_family_names_its_documented_bash_and_not_its_nologin_shell() {
    assert_eq!(RhelAdapter::EL9.bash_shell(), "/bin/bash");
    assert_ne!(
        RhelAdapter::EL9.bash_shell(),
        RhelAdapter::EL9.nologin_shell()
    );
}

/// The Rhel family keeps `at` job files in the directory its `at` package spools to.
#[test]
fn the_rhel_family_names_the_directory_its_at_daemon_spools_to() {
    assert_eq!(RhelAdapter::EL9.at_spool_directory(), "/var/spool/at");
}

/// The Rhel family names the real directory its mail spool files live in.
#[test]
fn the_rhel_family_names_the_real_directory_of_its_mail_spool() {
    assert_eq!(RhelAdapter::EL9.mail_spool_directory(), "/var/spool/mail");
}

/// The pending-updates read on the RHEL family is cache-only, and says so in its
/// arguments: `-C` is what stops the run contacting a repository.
#[test]
fn the_rhel_family_reads_pending_updates_from_the_cache_only() {
    assert!(RhelAdapter::EL9.pending_updates_arguments().contains(&"-C"));
    assert_eq!(
        RhelAdapter::EL9.package_query_binary().to_string(),
        RhelAdapter::EL9.package_manager()
    );
}

/// `dnf check-update -C` exits 1 on a host whose cache was never built, so this
/// family needs no separate probe.
#[test]
fn the_rhel_family_runs_no_catalogue_probe() {
    assert!(RhelAdapter::EL9.catalogue_probe().is_none());
}

/// The security reading of the RHEL family is the cache-only run with the
/// security filter, as owned strings.
#[test]
fn the_rhel_family_reads_security_updates_from_the_cache_only() {
    assert_eq!(
        RhelAdapter::EL9.security_pending_updates_arguments(),
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
    let mut rows = RhelAdapter::EL9
        .read_pending_updates(
            100,
            "curl-minimal.x86_64 7.76.1-40.el9_8.9 baseos\nkrb5-libs.x86_64 1.21.1-10.el9_8.1 baseos\n",
        )
        .unwrap();
    rows = RhelAdapter::EL9
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
    match RhelAdapter::EL9.reboot_probe() {
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
        RhelAdapter::EL9.read_reboot_probe_run(0),
        Ok(crate::RebootRequirement::NotRequired)
    );
    assert_eq!(
        RhelAdapter::EL9.read_reboot_probe_run(1),
        Ok(crate::RebootRequirement::Required)
    );
    assert_eq!(
        RhelAdapter::EL9.read_reboot_probe_run(127),
        Err(crate::RebootProbeFailure::Unsuccessful { status: 127 })
    );
}

/// The RHEL family names the daemon's checkers and control client by absolute path.
#[test]
fn the_rhel_family_names_the_dns_checkers_and_control_client() {
    assert_eq!(
        RhelAdapter::EL9.dns_checkconf_binary(),
        "/usr/sbin/named-checkconf"
    );
    assert_eq!(
        RhelAdapter::EL9.dns_checkzone_binary(),
        "/usr/sbin/named-checkzone"
    );
    assert_eq!(RhelAdapter::EL9.dns_control_binary(), "/usr/sbin/rndc");
}

/// The RHEL family runs the DNS daemon as `named`, whose group is the one the control key is readable by.
#[test]
fn the_rhel_family_runs_the_dns_daemon_as_its_own_user_and_group() {
    assert_eq!(RhelAdapter::EL9.dns_user(), "named");
    assert_eq!(RhelAdapter::EL9.dns_group(), "named");
}

/// Zone files sit below the daemon's state directory, never beside it, so a zone name can never name the zone-list store.
#[test]
fn the_rhel_family_keeps_zone_files_below_the_dns_state_directory() {
    assert_eq!(RhelAdapter::EL9.dns_state_directory(), "/var/named/maran");
    assert_eq!(
        RhelAdapter::EL9.dns_zone_directory(),
        "/var/named/maran/zones"
    );
}

/// Signed zones and their keys sit in two directories of their own below the state directory, apart from the read-only plain zone directory and from each other.
#[test]
fn the_rhel_family_keeps_signed_zones_and_keys_in_their_own_directories() {
    assert_eq!(
        RhelAdapter::EL9.dns_signed_zone_directory(),
        "/var/named/maran/signed-zones"
    );
    assert_eq!(
        RhelAdapter::EL9.dns_key_directory(),
        "/var/named/maran/keys"
    );

    let directories = [
        RhelAdapter::EL9.dns_zone_directory(),
        RhelAdapter::EL9.dns_signed_zone_directory(),
        RhelAdapter::EL9.dns_key_directory(),
    ];
    for (index, directory) in directories.iter().enumerate() {
        assert!(directory.starts_with(RhelAdapter::EL9.dns_state_directory()));
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
    assert_eq!(RhelAdapter::EL9.rsync_binary(), "/usr/bin/rsync");
}

/// The Rhel family names Apache's control script, unit, user and group as its packages ship them.
#[test]
fn the_rhel_family_names_the_apache_facts_of_its_packages() {
    assert_eq!(RhelAdapter::EL9.apache_binary(), "/usr/sbin/apachectl");
    assert_eq!(RhelAdapter::EL9.apache_service(), "httpd");
    assert_eq!(RhelAdapter::EL9.apache_user(), "apache");
    assert_eq!(RhelAdapter::EL9.apache_group(), "apache");
}

/// Apache's identity is its own answer and not nginx's: the inverse control for the test above,
/// which an adapter that returned the nginx user for both would fail on every family but Debian.
#[test]
fn the_rhel_family_does_not_answer_the_nginx_facts_for_apache() {
    assert_ne!(
        RhelAdapter::EL9.apache_service(),
        RhelAdapter::EL9.nginx_service()
    );
    assert_eq!(
        RhelAdapter::EL9.apache_user() == RhelAdapter::EL9.web_server_user(),
        "rhel" == "debian"
    );
}

/// A site's unit runs Node.js and npm from absolute paths the adapter names.
#[test]
fn the_rhel_family_names_the_node_launchers_by_absolute_path() {
    let major = NodeMajor::parse("20").unwrap();
    assert!(RhelAdapter::EL9.node_executable(&major).starts_with('/'));
    assert!(RhelAdapter::EL9.npm_executable(&major).starts_with('/'));
    assert_ne!(
        RhelAdapter::EL9.node_executable(&major),
        RhelAdapter::EL9.npm_executable(&major)
    );
}

/// A Node.js module stream installs one interpreter at the unversioned path and enabling another
/// stream replaces it, so every major answers the same two files (observed on AlmaLinux 9: only
/// `/usr/bin/node` and `/usr/bin/npm` exist, no `node-20`). The inverse control for the SUSE
/// family's test, which gives each major its own file.
#[test]
fn the_rhel_family_names_the_one_module_stream_interpreter_for_every_node_major() {
    let twenty = NodeMajor::parse("20").unwrap();
    let twenty_two = NodeMajor::parse("22").unwrap();

    for major in [&twenty, &twenty_two] {
        assert_eq!(RhelAdapter::EL9.node_binary(major), "/usr/bin/node");
        assert_eq!(RhelAdapter::EL9.node_executable(major), "/usr/bin/node");
        assert_eq!(RhelAdapter::EL9.npm_executable(major), "/usr/bin/npm");
    }
    assert_ne!(RhelAdapter::EL9.node_binary(&twenty), "/usr/bin/node-20");
}

/// The facts asked with a web server answer that server's own, never the other's: the nginx user
/// and the Apache user are different accounts on this family, and so are the units.
#[test]
fn the_rhel_family_answers_each_site_web_server_with_its_own_facts() {
    use crate::SiteWebServer;
    assert_eq!(
        RhelAdapter::EL9.site_web_server_service(SiteWebServer::Nginx),
        "nginx"
    );
    assert_eq!(
        RhelAdapter::EL9.site_web_server_service(SiteWebServer::Apache),
        "httpd"
    );
    assert_eq!(
        RhelAdapter::EL9.site_web_server_user(SiteWebServer::Nginx),
        "nginx"
    );
    assert_eq!(
        RhelAdapter::EL9.site_web_server_user(SiteWebServer::Apache),
        "apache"
    );
    assert_eq!(
        RhelAdapter::EL9.site_web_server_group(SiteWebServer::Apache),
        "apache"
    );
    assert_eq!(
        RhelAdapter::EL9.site_web_server_binary(SiteWebServer::Apache),
        "/usr/sbin/apachectl"
    );
}

/// The rhel family names the packages of the app catalogue as its sources carry them.
#[test]
fn the_rhel_family_names_the_packages_of_the_app_catalogue() {
    assert_eq!(RhelAdapter::EL9.nginx_package(), "nginx");
    assert_eq!(RhelAdapter::EL9.apache_package(), "httpd");
    assert_eq!(RhelAdapter::EL9.apache_extra_packages(), &["mod_ssl"][..]);
    assert_eq!(RhelAdapter::EL9.apache_companion_packages(), &[] as &[&str]);
    assert_eq!(
        RhelAdapter::EL9.apache_include_snippet_path(),
        "/etc/httpd/conf.d/maran.conf"
    );
    // AlmaLinux 9 has no `nodejs20`: Node.js there is a module stream of the one `nodejs`
    // package (issue #532), so every major names the same package.
    assert_eq!(RhelAdapter::EL9.nodejs_package("20"), "nodejs");
    assert_eq!(RhelAdapter::EL9.nodejs_package("22"), "nodejs");
    assert_eq!(
        RhelAdapter::EL9.nodejs_packaging(),
        crate::nodejs_packaging::NodejsPackaging::ModuleStreams
    );
    assert_eq!(
        RhelAdapter::EL9.nodejs_stream_list_arguments(),
        Some(vec![
            "-C".to_owned(),
            "module".to_owned(),
            "list".to_owned(),
            "nodejs".to_owned()
        ])
    );
    assert!(RhelAdapter::EL9.nodejs_module_packages().contains(&"npm"));
    assert_eq!(
        RhelAdapter::EL9.nodejs_unmanaged_removal_command(),
        "dnf remove nodejs"
    );
    assert_eq!(
        RhelAdapter::EL9.nodejs_stream_release_command(),
        "dnf module reset nodejs"
    );
}

/// The firewall's customer-uid floor is read from the host's own `login.defs`;
/// an absolute path, `/etc` first so the administrator's file overrides a
/// vendor copy.
#[test]
fn rhel_names_the_files_that_may_define_the_customer_uid_floor() {
    let paths = RhelAdapter::EL9.login_defs_paths();

    assert_eq!(paths, &["/etc/login.defs"]);
    assert!(paths.iter().all(|path| path.starts_with('/')));
}

/// `ls -l /usr/bin/node* /usr/bin/npm*` on `almalinux:10` (10.2) with `nodejs`, `nodejs-npm`,
/// `nodejs24` and `nodejs24-npm` installed.
const EL10_BINARIES: &str =
    include_str!("../../../tests/fixtures/package_manager/el10_nodejs_binaries.txt");

/// The same listing with `nodejs24` and `nodejs24-npm` installed and nothing else of Node.js.
const EL10_BINARIES_24_ALONE: &str =
    include_str!("../../../tests/fixtures/package_manager/el10_nodejs24_alone_binaries.txt");

fn ten() -> RhelAdapter {
    RhelAdapter::EL10
}

/// Whether the recorded listing holds a file or link named exactly `path`.
fn listing_names(listing: &str, path: &str) -> bool {
    listing
        .lines()
        .any(|line| line.split_whitespace().any(|word| word == path))
}

#[test]
fn the_rhel_10_generation_packages_node_js_per_major_and_not_as_module_streams() {
    use crate::nodejs_packaging::NodejsPackaging;

    assert_eq!(ten().nodejs_packaging(), NodejsPackaging::PerMajorPackages);
    assert_eq!(
        RhelAdapter::EL9.nodejs_packaging(),
        NodejsPackaging::ModuleStreams
    );
    // Both generations are the same family, picked by the release.
    assert_eq!(
        RhelAdapter::for_release("10.2").nodejs_packaging(),
        NodejsPackaging::PerMajorPackages
    );
    assert_eq!(
        RhelAdapter::for_release("9.8").nodejs_packaging(),
        NodejsPackaging::ModuleStreams
    );
}

#[test]
fn the_rhel_10_default_major_is_the_plain_package_and_the_others_are_numbered() {
    assert_eq!(ten().nodejs_package("22"), "nodejs");
    assert_eq!(ten().nodejs_package("24"), "nodejs24");
    assert_eq!(ten().nodejs_package("20"), "nodejs20");
    assert_eq!(ten().nodejs_extra_packages("22"), vec!["nodejs-npm"]);
    assert_eq!(ten().nodejs_extra_packages("24"), vec!["nodejs24-npm"]);
    // The 9 generation names one package whatever the major and installs npm with it.
    assert_eq!(RhelAdapter::EL9.nodejs_package("24"), "nodejs");
    assert!(RhelAdapter::EL9.nodejs_extra_packages("24").is_empty());
}

#[test]
fn the_rhel_10_generation_names_each_majors_own_interpreter_and_npm_as_recorded() {
    for major in ["22", "24"] {
        let parsed = NodeMajor::parse(major).unwrap();
        let node = format!("/usr/bin/node-{major}");
        let npm = format!("/usr/bin/npm-{major}");

        assert_eq!(ten().node_binary(&parsed), node.as_str());
        assert_eq!(ten().node_executable(&parsed), node);
        assert_eq!(ten().npm_executable(&parsed), npm);
        assert!(
            listing_names(EL10_BINARIES, &node),
            "{node} is not in the recording"
        );
        assert!(
            listing_names(EL10_BINARIES, &npm),
            "{npm} is not in the recording"
        );
    }
}

#[test]
fn the_rhel_10_versioned_interpreter_exists_where_the_unversioned_one_does_not() {
    // Why the versioned file is the one to ask: with only `nodejs24` installed the recording has
    // `node-24` and `npm-24` and no `node` or `npm` at all.
    let major = NodeMajor::parse("24").unwrap();

    assert!(listing_names(
        EL10_BINARIES_24_ALONE,
        ten().node_binary(&major).as_str()
    ));
    assert!(listing_names(
        EL10_BINARIES_24_ALONE,
        &ten().npm_executable(&major)
    ));
    assert!(!listing_names(EL10_BINARIES_24_ALONE, "/usr/bin/node"));
    assert!(!listing_names(EL10_BINARIES_24_ALONE, "/usr/bin/npm"));
    // Inverse control: the 9 generation's unversioned file is not what a 10 host is asked for.
    assert_ne!(
        ten().node_binary(&major).as_str(),
        RhelAdapter::EL9.node_binary(&major).as_str()
    );
    assert_ne!(
        ten().npm_executable(&major),
        RhelAdapter::EL9.npm_executable(&major)
    );
}

#[test]
fn the_rhel_10_generation_asks_no_module_question_and_has_no_module_commands() {
    assert_eq!(ten().nodejs_stream_list_arguments(), None);
    assert_eq!(
        ten()
            .read_nodejs_streams(1, "", "Error: No matching Modules to list")
            .unwrap(),
        crate::NodejsStreamListing::default()
    );
    assert!(ten().nodejs_module_packages().is_empty());
    assert_eq!(ten().nodejs_unmanaged_removal_command(), "");
    assert_eq!(ten().nodejs_stream_release_command(), "");
    // Inverse control: the 9 generation still has all of them (the test above in this file).
    assert!(RhelAdapter::EL9.nodejs_stream_list_arguments().is_some());
    assert!(
        RhelAdapter::EL9
            .read_nodejs_streams(1, "", "Error: No matching Modules to list")
            .is_err()
    );
}

#[test]
fn every_10_generation_executable_is_absolute_and_closed_by_the_node_major_grammar() {
    // The versioned file is built from a validated major, so the path cannot carry a separator.
    for major in ["18", "20", "22", "24"] {
        let parsed = NodeMajor::parse(major).unwrap();
        let path = ten().node_binary(&parsed);

        assert!(path.as_str().starts_with("/usr/bin/node-"));
        assert!(
            path.as_path()
                .components()
                .all(|part| part != Component::ParentDir)
        );
    }
}
