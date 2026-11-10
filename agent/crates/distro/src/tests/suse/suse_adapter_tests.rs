//! Tests for the SUSE-family adapter's account, web, database, SFTP, cron, firewall, DNS and mail facts.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `suse_adapter.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach private items.
//!
//! What these tests are, said plainly: the record of a DELIBERATE choice of value, written from
//! the distribution's documentation. Nothing here was observed on a SUSE host; every literal is
//! UNVERIFIED until the first SUSE polygon run, which owes `binary_paths_on_a_real_host` a SUSE
//! host and the install of each package. A literal compared to a literal inside this crate
//! proves the value did not change by accident, never that the path exists.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Component;

use maran_agent_core::validation::web::php_version::PhpVersion;

use super::SuseAdapter;
use crate::AdapterBinary;
use crate::AppFacts;
use crate::debian::DebianAdapter;
use crate::rhel::RhelAdapter;
use crate::{
    AccountPrograms, BackupFacts, DatabaseFacts, DnsFacts, FileTransferFacts, FirewallFacts,
    MailFacts, PackageFacts, PhpFacts, SchedulingFacts, ServiceManagerFacts, WebServerFacts,
};

/// The SUSE family spawns the MariaDB client under the real name `mariadb`.
#[test]
fn the_suse_family_runs_the_mysql_client_as_mariadb() {
    assert_eq!(SuseAdapter.mysql_client_binary(), "/usr/bin/mariadb");
    assert_ne!(
        SuseAdapter.mysql_client_binary().to_string(),
        RhelAdapter.mysql_client_binary().to_string()
    );
}

/// The SUSE family restarts the database through MariaDB's own unit name.
#[test]
fn the_suse_family_restarts_the_database_through_the_mariadb_unit() {
    assert_eq!(SuseAdapter.mysql_service(), "mariadb");
}

/// The SUSE family chroots SFTP accounts through the panel's own group, the same name as the
/// other families so the one `Match Group` block the installer writes matches on every host.
#[test]
fn the_suse_family_chroots_sftp_accounts_through_the_panel_group() {
    assert_eq!(SuseAdapter.sftp_group(), "maran-sftp");
    assert_eq!(SuseAdapter.sftp_group(), RhelAdapter.sftp_group());
    assert_eq!(SuseAdapter.sftp_group(), DebianAdapter.sftp_group());
}

/// The SUSE family names the FTPS daemon where upstream's package puts it, although the
/// installer skips FTPS on this family and the answer exists to keep the trait complete.
#[test]
fn the_suse_family_names_the_ftp_daemon_where_upstream_puts_it() {
    assert_eq!(SuseAdapter.vsftpd_binary(), "/usr/sbin/vsftpd");
}

/// The SUSE family authorizes FTPS logins through the panel's own group.
#[test]
fn the_suse_family_authorizes_ftps_logins_through_the_panel_group() {
    assert_eq!(SuseAdapter.ftps_group(), "maran-ftps");
    assert_eq!(SuseAdapter.ftps_group(), RhelAdapter.ftps_group());
}

/// The SUSE family spells the TLS version options with an underscore before the minor, and
/// that is not the Debian spelling.
#[test]
fn the_suse_family_spells_the_tls_version_options_with_underscores() {
    let keys = SuseAdapter.vsftpd_tls_version_keys();

    assert_eq!(keys.tls_v1, "ssl_tlsv1");
    assert_eq!(keys.tls_v1_1, "ssl_tlsv1_1");
    assert_eq!(keys.tls_v1_2, "ssl_tlsv1_2");
    assert_ne!(
        keys.tls_v1_1,
        DebianAdapter.vsftpd_tls_version_keys().tls_v1_1
    );
}

/// The SUSE family runs cron under `cron`, which is the Debian name and not the RHEL one.
#[test]
fn the_suse_family_runs_cron_through_the_cron_unit() {
    assert_eq!(SuseAdapter.cron_service(), "cron");
    assert_ne!(SuseAdapter.cron_service(), RhelAdapter.cron_service());
}

/// The SUSE family serves SSH from the `sshd` unit.
#[test]
fn the_suse_family_serves_ssh_from_the_sshd_unit() {
    assert_eq!(SuseAdapter.ssh_service(), "sshd");
    assert_ne!(SuseAdapter.ssh_service(), DebianAdapter.ssh_service());
}

/// The SUSE family answers the administrator's `sshd_config` path.
#[test]
fn the_suse_family_configures_sshd_at_the_administrators_path() {
    assert_eq!(SuseAdapter.sshd_config_path(), "/etc/ssh/sshd_config");
}

/// The panel reports exactly the four units it manages on the SUSE family.
///
/// A literal list, not a comparison against the accessors it is built from: asking the adapter
/// for both halves of an equality proves only that one function calls another.
#[test]
fn the_suse_family_reports_exactly_the_four_units_the_panel_manages() {
    assert_eq!(
        SuseAdapter.managed_units(),
        ["nginx", "mariadb", "cron", "sshd"]
    );
}

/// Every tool the agent runs or names, paired with the path this family is read to install
/// it at.
///
/// Written out as literals rather than read back from the adapter: an assertion that asks the
/// adapter for the value it is checking proves only that a function returns what it returns.
/// These literals are the record of a deliberate choice, so moving a tool to another directory
/// has to be an edit here as well. What this table cannot do is verify that a path exists: it
/// compares one string literal to another and touches no filesystem. That check is
/// `crates/agent/tests/binary_paths_on_a_real_host.rs`, which owes a SUSE host.
///
/// `sh` is in the table because the path is written into a crontab line, where a wrong one is a
/// cron entry that silently never runs.
const EXPECTED_BINARIES: [(&str, &str); 28] = [
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
    ("package_query", "/usr/bin/zypper"),
    ("package_candidate", "/usr/bin/zypper"),
];

/// The adapter's answer for each tool, in the order of [`EXPECTED_BINARIES`].
fn actual_binaries() -> [String; 28] {
    [
        SuseAdapter.useradd_binary().to_string(),
        SuseAdapter.usermod_binary().to_string(),
        SuseAdapter.pkill_binary().to_string(),
        SuseAdapter.pgrep_binary().to_string(),
        SuseAdapter.atq_binary().to_string(),
        SuseAdapter.atrm_binary().to_string(),
        SuseAdapter.loginctl_binary().to_string(),
        SuseAdapter.ipcrm_binary().to_string(),
        SuseAdapter.userdel_binary().to_string(),
        SuseAdapter.chpasswd_binary().to_string(),
        SuseAdapter.ssh_keygen_binary().to_string(),
        SuseAdapter.rsync_binary().to_string(),
        SuseAdapter.setquota_binary().to_string(),
        SuseAdapter.quota_binary().to_string(),
        SuseAdapter.quotaon_binary().to_string(),
        SuseAdapter.timedatectl_binary().to_string(),
        SuseAdapter.id_binary().to_string(),
        SuseAdapter.chmod_binary().to_string(),
        SuseAdapter.chgrp_binary().to_string(),
        SuseAdapter.crontab_binary().to_string(),
        SuseAdapter.nft_binary().to_string(),
        SuseAdapter.sh_binary().to_owned(),
        SuseAdapter.tar_binary().to_string(),
        SuseAdapter.gzip_binary().to_string(),
        SuseAdapter.database_dump_binary().to_string(),
        SuseAdapter.getent_binary().to_string(),
        SuseAdapter.package_query_binary().to_string(),
        SuseAdapter.package_candidate_binary().to_string(),
    ]
}

/// The SUSE family names each tool at the path its packages are read to install it at.
#[test]
fn the_suse_family_names_every_tool_at_its_documented_path() {
    for ((tool, expected), actual) in EXPECTED_BINARIES.iter().zip(actual_binaries()) {
        assert_eq!(actual, *expected, "{tool}");
    }
}

/// No tool is named by anything `PATH` would resolve for whoever runs it.
///
/// Separate from the equality test above, because the two say different things: one is "this is
/// the path we chose", the other is "whatever the path becomes, it can never be a bare name". A
/// bare name is resolved through `PATH` either way, and whoever controls the environment would
/// then be choosing the program.
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

/// The adapter traits' own sources (every concern trait `DistroAdapter` composes), read as text
/// so the table below is checked against the traits and not against a number somebody has to
/// remember to bump.
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

/// How many methods the traits declare that return an `AdapterBinary`.
///
/// Counted from the declarations (`-> AdapterBinary;`), comment lines skipped, the same
/// source-scan idiom `adapter_binary_tests.rs` uses. A method added to the traits and every
/// impl but not to the table makes this larger than the table, so the completeness assertion
/// goes red without anyone editing a literal.
fn declared_binary_methods() -> usize {
    TRAIT_SOURCES
        .iter()
        .flat_map(|source| source.lines())
        .map(str::trim)
        .filter(|line| !line.starts_with("//"))
        .filter(|line| line.ends_with("-> AdapterBinary;"))
        .count()
}

/// Every program the SUSE adapter lets the agent execute, with the method that names it.
///
/// One entry per `AdapterBinary`-returning method of the traits (the fixed paths plus the
/// version-taking php-fpm validator). `sh` and the login shell are not here: they are written
/// somewhere, never executed by the agent, so they stay plain strings.
fn every_executable() -> Vec<(&'static str, AdapterBinary)> {
    let version = PhpVersion::parse("8.4").unwrap();
    vec![
        ("nginx", SuseAdapter.nginx_binary()),
        ("apache", SuseAdapter.apache_binary()),
        ("node", SuseAdapter.node_binary()),
        ("service_manager", SuseAdapter.service_manager()),
        ("journal", SuseAdapter.journal_binary()),
        ("systemd_run", SuseAdapter.systemd_run_binary()),
        ("php_fpm", SuseAdapter.php_fpm_binary(&version)),
        ("openssl", SuseAdapter.openssl_binary()),
        ("tar", SuseAdapter.tar_binary()),
        ("gzip", SuseAdapter.gzip_binary()),
        ("database_dump", SuseAdapter.database_dump_binary()),
        ("mysql_client", SuseAdapter.mysql_client_binary()),
        ("vsftpd", SuseAdapter.vsftpd_binary()),
        ("dns_checkconf", SuseAdapter.dns_checkconf_binary()),
        ("dns_checkzone", SuseAdapter.dns_checkzone_binary()),
        ("dns_control", SuseAdapter.dns_control_binary()),
        ("useradd", SuseAdapter.useradd_binary()),
        ("userdel", SuseAdapter.userdel_binary()),
        ("usermod", SuseAdapter.usermod_binary()),
        ("pkill", SuseAdapter.pkill_binary()),
        ("pgrep", SuseAdapter.pgrep_binary()),
        ("atq", SuseAdapter.atq_binary()),
        ("atrm", SuseAdapter.atrm_binary()),
        ("loginctl", SuseAdapter.loginctl_binary()),
        ("ipcrm", SuseAdapter.ipcrm_binary()),
        ("passwd", SuseAdapter.passwd_binary()),
        ("getent", SuseAdapter.getent_binary()),
        ("package_query", SuseAdapter.package_query_binary()),
        ("package_candidate", SuseAdapter.package_candidate_binary()),
        ("setquota", SuseAdapter.setquota_binary()),
        ("quota", SuseAdapter.quota_binary()),
        ("quotaon", SuseAdapter.quotaon_binary()),
        ("timedatectl", SuseAdapter.timedatectl_binary()),
        ("id", SuseAdapter.id_binary()),
        ("chmod", SuseAdapter.chmod_binary()),
        ("chgrp", SuseAdapter.chgrp_binary()),
        ("chpasswd", SuseAdapter.chpasswd_binary()),
        ("ssh", SuseAdapter.ssh_binary()),
        ("ssh-keyscan", SuseAdapter.ssh_keyscan_binary()),
        ("ssh-keygen", SuseAdapter.ssh_keygen_binary()),
        ("rsync", SuseAdapter.rsync_binary()),
        ("crontab", SuseAdapter.crontab_binary()),
        ("nft", SuseAdapter.nft_binary()),
        ("postfix", SuseAdapter.postfix_binary()),
        ("postconf", SuseAdapter.postconf_binary()),
        ("postmap", SuseAdapter.postmap_binary()),
        ("postqueue", SuseAdapter.postqueue_binary()),
        ("postsuper", SuseAdapter.postsuper_binary()),
        ("doveadm", SuseAdapter.doveadm_binary()),
        ("doveconf", SuseAdapter.doveconf_binary()),
        ("rspamadm", SuseAdapter.rspamadm_binary()),
    ]
}

/// The SUSE adapter names every program it lets the agent execute by an absolute path.
///
/// The table is checked for being complete first, so a method added to the traits without a
/// path check cannot hide.
#[test]
fn every_program_the_suse_adapter_executes_is_an_absolute_path() {
    let all = every_executable();
    assert_eq!(
        all.len(),
        declared_binary_methods(),
        "the table must list every AdapterBinary method the traits declare"
    );

    for (name, binary) in &all {
        assert!(
            binary.as_path().is_absolute(),
            "{name} is {binary}, not an absolute path"
        );
    }
}

/// No program path the SUSE adapter names climbs out of its directory with `..`.
#[test]
fn no_program_the_suse_adapter_executes_has_a_parent_directory_component() {
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

/// No program path the SUSE adapter names carries whitespace or a control character.
///
/// Such a character cannot be part of a real install path and would corrupt any log line or
/// unit file the path is later written into.
#[test]
fn no_program_the_suse_adapter_executes_has_whitespace_or_control_characters() {
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

/// The family offers one PHP minor and keeps php-fpm at one plain path: two versions give the
/// same binary, pool directory and unit, none of them in the RHEL software-collection layout.
#[test]
fn the_php_fpm_facts_do_not_vary_with_the_version_on_the_suse_family() {
    let first = PhpVersion::parse("8.3").unwrap();
    let second = PhpVersion::parse("8.4").unwrap();

    assert_eq!(SuseAdapter.php_fpm_binary(&first), "/usr/sbin/php-fpm");
    assert_eq!(
        SuseAdapter.php_fpm_binary(&first),
        SuseAdapter.php_fpm_binary(&second)
    );
    assert_eq!(SuseAdapter.php_fpm_service("8.4"), "php-fpm");
    assert_eq!(
        SuseAdapter.php_fpm_pool_directory("8.4"),
        "/etc/php8/fpm/php-fpm.d"
    );
    assert_ne!(
        SuseAdapter.php_fpm_binary(&second),
        RhelAdapter.php_fpm_binary(&second)
    );
    assert_ne!(
        SuseAdapter.php_fpm_binary(&second),
        DebianAdapter.php_fpm_binary(&second)
    );
}

/// `sh` and the login shell stay plain strings: the agent never executes them (they are only
/// written into a crontab line and an account record), so the executable type is deliberately
/// not put on them.
///
/// This is a compile-time guard: if either method returned `AdapterBinary`, the bindings below
/// would not compile.
#[test]
fn sh_and_nologin_stay_plain_paths_and_are_not_executable_typed() {
    let sh: &str = SuseAdapter.sh_binary();
    let nologin: &str = SuseAdapter.nologin_shell();

    assert!(sh.starts_with('/'));
    assert!(nologin.starts_with('/'));
}

/// The SUSE family names the usr-merged nologin and bash paths, and a login shell is a
/// different path from the shell of an account that must not log in.
#[test]
fn the_suse_family_names_its_documented_bash_and_nologin_shells() {
    assert_eq!(SuseAdapter.nologin_shell(), "/usr/sbin/nologin");
    assert_eq!(SuseAdapter.bash_shell(), "/usr/bin/bash");
    assert_ne!(SuseAdapter.bash_shell(), SuseAdapter.nologin_shell());
    assert_ne!(SuseAdapter.nologin_shell(), RhelAdapter.nologin_shell());
}

/// The SUSE family keeps `at` job files in the directory its `at` package spools to, which is
/// neither the RHEL nor the Debian one.
#[test]
fn the_suse_family_names_the_directory_its_at_daemon_spools_to() {
    assert_eq!(SuseAdapter.at_spool_directory(), "/var/spool/atjobs");
    assert_ne!(
        SuseAdapter.at_spool_directory(),
        RhelAdapter.at_spool_directory()
    );
}

/// The SUSE family names the real directory its mail spool files live in.
#[test]
fn the_suse_family_names_the_real_directory_of_its_mail_spool() {
    assert_eq!(SuseAdapter.mail_spool_directory(), "/var/spool/mail");
}

/// The local password database is the same file on every family, asked of the adapter all the same.
#[test]
fn the_suse_family_reads_logins_from_the_local_password_database() {
    assert_eq!(SuseAdapter.passwd_database(), "/etc/passwd");
    assert_eq!(SuseAdapter.machine_id_path(), "/etc/machine-id");
    assert_eq!(SuseAdapter.systemd_unit_directory(), "/etc/systemd/system");
}

/// The web server runs as `nginx` in group `nginx` on the SUSE family, as an account's home is
/// group-owned by the group and not by the user.
#[test]
fn the_suse_family_runs_the_web_server_as_nginx() {
    assert_eq!(SuseAdapter.web_server_user(), "nginx");
    assert_eq!(SuseAdapter.web_server_group(), "nginx");
    assert_eq!(SuseAdapter.nginx_service(), "nginx");
    assert_ne!(
        SuseAdapter.web_server_user(),
        DebianAdapter.web_server_user()
    );
}

/// The pending-updates read on the SUSE family is cache-only, and says so in its arguments:
/// `--no-refresh` is what stops the run contacting a repository.
#[test]
fn the_suse_family_reads_pending_updates_from_the_cache_only() {
    assert!(
        SuseAdapter
            .pending_updates_arguments()
            .contains(&"--no-refresh")
    );
    assert_eq!(
        SuseAdapter.package_query_binary().to_string(),
        SuseAdapter.package_manager()
    );
}

/// A cache-only run against metadata never fetched fails with its own status, which the reader
/// refuses, so this family needs no separate probe.
#[test]
fn the_suse_family_runs_no_catalogue_probe() {
    assert!(SuseAdapter.catalogue_probe().is_none());
}

/// The security reading of the SUSE family is a second cache-only run, as owned strings, and
/// asks for security patches.
#[test]
fn the_suse_family_reads_security_updates_from_the_cache_only() {
    let arguments = SuseAdapter.security_pending_updates_arguments().unwrap();

    assert!(arguments.iter().any(|argument| argument == "--no-refresh"));
    assert!(arguments.iter().any(|argument| argument == "security"));
}

/// The SUSE family answers the reboot question with `zypper needs-rebooting`, named by an
/// absolute path and given the one constant argument, and not with the RHEL probe.
#[test]
fn the_suse_family_probes_for_a_reboot_with_zypper_needs_rebooting() {
    match SuseAdapter.reboot_probe() {
        crate::RebootProbe::Program { binary, arguments } => {
            assert_eq!(binary.to_string(), "/usr/bin/zypper");
            assert!(binary.as_path().is_absolute());
            assert_eq!(arguments, vec![String::from("needs-rebooting")]);
        }
        other => panic!("expected a program probe, got {other:?}"),
    }
    assert_ne!(SuseAdapter.reboot_probe(), RhelAdapter.reboot_probe());
}

/// The adapter reads the probe's status: 0 is not required, 102 is required, anything else is
/// an error and never "not required".
#[test]
fn the_suse_family_reads_the_reboot_probe_status() {
    assert_eq!(
        SuseAdapter.read_reboot_probe_run(0),
        Ok(crate::RebootRequirement::NotRequired)
    );
    assert_eq!(
        SuseAdapter.read_reboot_probe_run(102),
        Ok(crate::RebootRequirement::Required)
    );
    assert_eq!(
        SuseAdapter.read_reboot_probe_run(127),
        Err(crate::RebootProbeFailure::Unsuccessful { status: 127 })
    );
    assert_eq!(
        SuseAdapter.read_reboot_probe_run(1),
        Err(crate::RebootProbeFailure::Unsuccessful { status: 1 })
    );
}

/// The SUSE family names the daemon's checkers and control client by absolute path.
#[test]
fn the_suse_family_names_the_dns_checkers_and_control_client() {
    assert_eq!(
        SuseAdapter.dns_checkconf_binary(),
        "/usr/sbin/named-checkconf"
    );
    assert_eq!(
        SuseAdapter.dns_checkzone_binary(),
        "/usr/sbin/named-checkzone"
    );
    assert_eq!(SuseAdapter.dns_control_binary(), "/usr/sbin/rndc");
}

/// The SUSE family runs the DNS daemon as `named`, whose group is the one the control key is readable by.
#[test]
fn the_suse_family_runs_the_dns_daemon_as_its_own_user_and_group() {
    assert_eq!(SuseAdapter.dns_user(), "named");
    assert_eq!(SuseAdapter.dns_group(), "named");
}

/// Zone files sit below the daemon's state directory, never beside it, so a zone name can
/// never name the zone-list store.
#[test]
fn the_suse_family_keeps_zone_files_below_the_dns_state_directory() {
    assert_eq!(SuseAdapter.dns_state_directory(), "/var/lib/named/maran");
    assert_eq!(
        SuseAdapter.dns_zone_directory(),
        "/var/lib/named/maran/zones"
    );
    assert_ne!(
        SuseAdapter.dns_state_directory(),
        RhelAdapter.dns_state_directory()
    );
}

/// Signed zones and their keys sit in two directories of their own below the state directory,
/// apart from the read-only plain zone directory and from each other.
#[test]
fn the_suse_family_keeps_signed_zones_and_keys_in_their_own_directories() {
    assert_eq!(
        SuseAdapter.dns_signed_zone_directory(),
        "/var/lib/named/maran/signed-zones"
    );
    assert_eq!(SuseAdapter.dns_key_directory(), "/var/lib/named/maran/keys");

    let directories = [
        SuseAdapter.dns_zone_directory(),
        SuseAdapter.dns_signed_zone_directory(),
        SuseAdapter.dns_key_directory(),
    ];
    for (index, directory) in directories.iter().enumerate() {
        assert!(directory.starts_with(SuseAdapter.dns_state_directory()));
        for other in &directories[index + 1..] {
            assert!(
                !other.starts_with(directory) && !directory.starts_with(other),
                "{directory} and {other} must not nest"
            );
        }
    }
}

/// The SUSE family drops Postfix's submission directory into `maildrop`, not the `postdrop`
/// group the other families use, and runs ClamAV as `vscan`.
#[test]
fn the_suse_family_names_the_mail_identities_this_distribution_creates() {
    assert_eq!(SuseAdapter.postfix_user(), "postfix");
    assert_eq!(SuseAdapter.postdrop_group(), "maildrop");
    assert_ne!(SuseAdapter.postdrop_group(), RhelAdapter.postdrop_group());
    assert_eq!(SuseAdapter.dovecot_user(), "dovecot");
    assert_eq!(SuseAdapter.dovecot_login_user(), "dovenull");
    assert_eq!(SuseAdapter.rspamd_user(), "_rspamd");
    assert_eq!(SuseAdapter.clamd_user(), "vscan");
    assert_ne!(SuseAdapter.clamd_user(), RhelAdapter.clamd_user());
}

/// The SUSE family names the mail units, with a plain `clamd` and not the RHEL instance unit.
#[test]
fn the_suse_family_names_the_mail_units() {
    assert_eq!(SuseAdapter.postfix_service(), "postfix");
    assert_eq!(SuseAdapter.dovecot_service(), "dovecot");
    assert_eq!(SuseAdapter.rspamd_service(), "rspamd");
    assert_eq!(SuseAdapter.clamd_service(), "clamd");
    assert_eq!(SuseAdapter.freshclam_service(), "freshclam");
    assert_ne!(SuseAdapter.clamd_service(), RhelAdapter.clamd_service());
}

/// The SUSE family names the mail configuration locations, every one an absolute path.
#[test]
fn the_suse_family_names_the_mail_configuration_locations() {
    assert_eq!(SuseAdapter.postfix_config_directory(), "/etc/postfix");
    assert_eq!(SuseAdapter.dovecot_config_directory(), "/etc/dovecot");
    assert_eq!(SuseAdapter.rspamd_config_directory(), "/etc/rspamd");
    assert_eq!(SuseAdapter.clamd_config_file(), "/etc/clamd.conf");
    assert_ne!(
        SuseAdapter.clamd_config_file(),
        RhelAdapter.clamd_config_file()
    );
}

/// The mail stack is not offered on the SUSE family until measured: no release has a Dovecot
/// generation or a key-value server, so the mail stack refuses what it cannot lay out.
#[test]
fn the_suse_family_has_no_mail_layout_for_any_release() {
    for version_id in ["16.0", "16.1", "15.6", ""] {
        assert!(
            SuseAdapter.dovecot_major(version_id).is_none(),
            "{version_id}"
        );
        assert!(
            SuseAdapter.key_value_server(version_id).is_none(),
            "{version_id}"
        );
    }
    // The inverse control: a family that does offer the stack answers for a release it ships,
    // so the refusal above is the SUSE family's and not a reader that refuses everything.
    assert!(RhelAdapter.dovecot_major("9").is_some());
}

/// A PHP version the distribution does not ship must not share the shipped version's pool
/// directory: the installed-versions listing decides "installed" by that directory's existence,
/// so a shared one would report all six supported versions as installed. The shipped version
/// keeps the real directory (the inverse control).
#[test]
fn a_php_version_the_suse_family_does_not_ship_has_a_pool_directory_of_its_own() {
    assert_eq!(
        SuseAdapter.php_fpm_pool_directory("8.4"),
        "/etc/php8/fpm/php-fpm.d"
    );
    assert_eq!(
        SuseAdapter.php_fpm_pool_directory("8.3"),
        "/etc/php8.3/fpm/php-fpm.d"
    );
    assert_ne!(
        SuseAdapter.php_fpm_pool_directory("8.3"),
        SuseAdapter.php_fpm_pool_directory("8.4")
    );
}

#[test]
fn the_suse_family_names_rsync_at_its_documented_path() {
    assert_eq!(SuseAdapter.rsync_binary(), "/usr/bin/rsync");
}

/// The Suse family names Apache's control script, unit, user and group as its packages ship them.
#[test]
fn the_suse_family_names_the_apache_facts_of_its_packages() {
    assert_eq!(SuseAdapter.apache_binary(), "/usr/sbin/apache2ctl");
    assert_eq!(SuseAdapter.apache_service(), "apache2");
    assert_eq!(SuseAdapter.apache_user(), "wwwrun");
    assert_eq!(SuseAdapter.apache_group(), "www");
}

/// Apache's identity is its own answer and not nginx's: the inverse control for the test above,
/// which an adapter that returned the nginx user for both would fail on every family but Debian.
#[test]
fn the_suse_family_does_not_answer_the_nginx_facts_for_apache() {
    assert_ne!(SuseAdapter.apache_service(), SuseAdapter.nginx_service());
    assert_eq!(
        SuseAdapter.apache_user() == SuseAdapter.web_server_user(),
        "suse" == "debian"
    );
}

/// A site's unit runs Node.js and npm from absolute paths the adapter names.
#[test]
fn the_suse_family_names_the_node_launchers_by_absolute_path() {
    assert!(SuseAdapter.node_executable().starts_with('/'));
    assert!(SuseAdapter.npm_executable().starts_with('/'));
    assert_ne!(SuseAdapter.node_executable(), SuseAdapter.npm_executable());
}

/// The facts asked with a web server answer that server's own, never the other's.
#[test]
fn the_suse_family_answers_each_site_web_server_with_its_own_facts() {
    use crate::SiteWebServer;
    assert_eq!(
        SuseAdapter.site_web_server_service(SiteWebServer::Nginx),
        "nginx"
    );
    assert_eq!(
        SuseAdapter.site_web_server_service(SiteWebServer::Apache),
        "apache2"
    );
    assert_eq!(
        SuseAdapter.site_web_server_user(SiteWebServer::Apache),
        "wwwrun"
    );
    assert_eq!(
        SuseAdapter.site_web_server_group(SiteWebServer::Apache),
        "www"
    );
    assert_eq!(
        SuseAdapter.site_web_server_binary(SiteWebServer::Nginx),
        "/usr/sbin/nginx"
    );
}

/// The suse family names the packages of the app catalogue as its sources carry them.
#[test]
fn the_suse_family_names_the_packages_of_the_app_catalogue() {
    assert_eq!(SuseAdapter.nginx_package(), "nginx");
    assert_eq!(SuseAdapter.apache_package(), "apache2");
    assert_eq!(SuseAdapter.apache_extra_packages(), &[] as &[&str]);
    assert_eq!(
        SuseAdapter.apache_include_snippet_path(),
        "/etc/apache2/conf.d/maran.conf"
    );
    assert_eq!(SuseAdapter.nodejs_package("20"), "nodejs20");
    assert_eq!(
        SuseAdapter.nodejs_packaging(),
        crate::nodejs_packaging::NodejsPackaging::PerMajorPackages
    );
}
