//! Tests for the Debian-family adapter's database, SFTP, cron and firewall facts.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `debian_adapter.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Component;

use maran_agent_core::validation::web::php_version::PhpVersion;

use super::DebianAdapter;
use crate::AdapterBinary;
use crate::AppFacts;
use crate::{
    AccountPrograms, BackupFacts, DatabaseFacts, DnsFacts, FileTransferFacts, FirewallFacts,
    MailFacts, PackageFacts, PhpFacts, SchedulingFacts, ServiceManagerFacts, WebServerFacts,
};

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

/// The Debian family spawns the FTPS daemon from the path its `vsftpd` package installs it at.
#[test]
fn the_debian_family_names_the_ftp_daemon_where_the_package_puts_it() {
    assert_eq!(DebianAdapter.vsftpd_binary(), "/usr/sbin/vsftpd");
}

/// The Debian family authorizes FTPS logins through the panel's own group.
#[test]
fn the_debian_family_authorizes_ftps_logins_through_the_panel_group() {
    assert_eq!(DebianAdapter.ftps_group(), "maran-ftps");
}

/// The Debian family spells the TLS version options with no underscore before the minor.
#[test]
fn the_debian_family_spells_the_tls_version_options_without_underscores() {
    let keys = DebianAdapter.vsftpd_tls_version_keys();

    assert_eq!(keys.tls_v1, "ssl_tlsv1");
    assert_eq!(keys.tls_v1_1, "ssl_tlsv11");
    assert_eq!(keys.tls_v1_2, "ssl_tlsv12");
}

/// The Debian family runs cron under the unit its `cron` package registers.
#[test]
fn the_debian_family_runs_cron_through_the_cron_unit() {
    assert_eq!(DebianAdapter.cron_service(), "cron");
}

/// The Debian family serves SSH from the unit its OpenSSH package registers.
#[test]
fn the_debian_family_serves_ssh_from_the_ssh_unit() {
    assert_eq!(DebianAdapter.ssh_service(), "ssh");
}

/// The installer's `86-sftp.sh` appends its `Match Group` block to this exact
/// path — a drift check that read a different file would find nothing wrong
/// with a config nobody wrote to.
#[test]
fn the_debian_family_configures_sshd_at_the_documented_path() {
    assert_eq!(DebianAdapter.sshd_config_path(), "/etc/ssh/sshd_config");
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
    ("package_query", "/usr/bin/apt-get"),
    ("package_candidate", "/usr/bin/apt-cache"),
];

/// The adapter's answer for each tool, in the order of [`EXPECTED_BINARIES`].
fn actual_binaries() -> [String; 30] {
    [
        DebianAdapter.useradd_binary().to_string(),
        DebianAdapter.usermod_binary().to_string(),
        DebianAdapter.pkill_binary().to_string(),
        DebianAdapter.pgrep_binary().to_string(),
        DebianAdapter.atq_binary().to_string(),
        DebianAdapter.atrm_binary().to_string(),
        DebianAdapter.loginctl_binary().to_string(),
        DebianAdapter.ipcrm_binary().to_string(),
        DebianAdapter.userdel_binary().to_string(),
        DebianAdapter.chpasswd_binary().to_string(),
        DebianAdapter.ssh_keygen_binary().to_string(),
        DebianAdapter.ssh_binary().to_string(),
        DebianAdapter.ssh_keyscan_binary().to_string(),
        DebianAdapter.rsync_binary().to_string(),
        DebianAdapter.setquota_binary().to_string(),
        DebianAdapter.quota_binary().to_string(),
        DebianAdapter.quotaon_binary().to_string(),
        DebianAdapter.timedatectl_binary().to_string(),
        DebianAdapter.id_binary().to_string(),
        DebianAdapter.chmod_binary().to_string(),
        DebianAdapter.chgrp_binary().to_string(),
        DebianAdapter.crontab_binary().to_string(),
        DebianAdapter.nft_binary().to_string(),
        DebianAdapter.sh_binary().to_owned(),
        DebianAdapter.tar_binary().to_string(),
        DebianAdapter.gzip_binary().to_string(),
        DebianAdapter.database_dump_binary().to_string(),
        DebianAdapter.getent_binary().to_string(),
        DebianAdapter.package_query_binary().to_string(),
        DebianAdapter.package_candidate_binary().to_string(),
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

/// The Debian family spawns the OpenSSH client from the path its `openssh-client` package installs it at.
#[test]
fn the_debian_family_names_the_ssh_client_where_the_package_puts_it() {
    assert_eq!(DebianAdapter.ssh_binary(), "/usr/bin/ssh");
}

/// The Debian family spawns `ssh-keyscan` from the path its `openssh-client` package installs it at.
#[test]
fn the_debian_family_names_ssh_keyscan_where_the_package_puts_it() {
    assert_eq!(DebianAdapter.ssh_keyscan_binary(), "/usr/bin/ssh-keyscan");
}

/// The three programs a migration source is reached with are three different files.
///
/// The inverse control for the two tests above: each is satisfied by an adapter
/// that answers the same path for all three, which would make a connection run
/// the key generator or a scan run the client.
#[test]
fn the_debian_family_names_the_three_ssh_programs_apart() {
    let all = [
        DebianAdapter.ssh_binary().to_string(),
        DebianAdapter.ssh_keyscan_binary().to_string(),
        DebianAdapter.ssh_keygen_binary().to_string(),
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

/// Every program the Debian adapter lets the agent execute, with the method that names it.
///
/// One entry per `AdapterBinary`-returning method of the trait (the fixed paths
/// plus the version-built php-fpm validator). `sh` and the login shell are not
/// here: they are written somewhere, never executed by the agent, so they stay
/// plain strings (see the type assertion below).
fn every_executable() -> Vec<(&'static str, AdapterBinary)> {
    let version = PhpVersion::parse("8.3").unwrap();
    vec![
        ("nginx", DebianAdapter.nginx_binary()),
        ("apache", DebianAdapter.apache_binary()),
        ("node", DebianAdapter.node_binary()),
        ("service_manager", DebianAdapter.service_manager()),
        ("journal", DebianAdapter.journal_binary()),
        ("systemd_run", DebianAdapter.systemd_run_binary()),
        ("php_fpm", DebianAdapter.php_fpm_binary(&version)),
        ("openssl", DebianAdapter.openssl_binary()),
        ("tar", DebianAdapter.tar_binary()),
        ("gzip", DebianAdapter.gzip_binary()),
        ("database_dump", DebianAdapter.database_dump_binary()),
        ("mysql_client", DebianAdapter.mysql_client_binary()),
        ("vsftpd", DebianAdapter.vsftpd_binary()),
        ("dns_checkconf", DebianAdapter.dns_checkconf_binary()),
        ("dns_checkzone", DebianAdapter.dns_checkzone_binary()),
        ("dns_control", DebianAdapter.dns_control_binary()),
        ("useradd", DebianAdapter.useradd_binary()),
        ("userdel", DebianAdapter.userdel_binary()),
        ("usermod", DebianAdapter.usermod_binary()),
        ("pkill", DebianAdapter.pkill_binary()),
        ("pgrep", DebianAdapter.pgrep_binary()),
        ("atq", DebianAdapter.atq_binary()),
        ("atrm", DebianAdapter.atrm_binary()),
        ("loginctl", DebianAdapter.loginctl_binary()),
        ("ipcrm", DebianAdapter.ipcrm_binary()),
        ("passwd", DebianAdapter.passwd_binary()),
        ("getent", DebianAdapter.getent_binary()),
        ("package_query", DebianAdapter.package_query_binary()),
        (
            "package_candidate",
            DebianAdapter.package_candidate_binary(),
        ),
        ("setquota", DebianAdapter.setquota_binary()),
        ("quota", DebianAdapter.quota_binary()),
        ("quotaon", DebianAdapter.quotaon_binary()),
        ("timedatectl", DebianAdapter.timedatectl_binary()),
        ("id", DebianAdapter.id_binary()),
        ("chmod", DebianAdapter.chmod_binary()),
        ("chgrp", DebianAdapter.chgrp_binary()),
        ("chpasswd", DebianAdapter.chpasswd_binary()),
        ("ssh-keygen", DebianAdapter.ssh_keygen_binary()),
        ("ssh", DebianAdapter.ssh_binary()),
        ("ssh_keyscan", DebianAdapter.ssh_keyscan_binary()),
        ("rsync", DebianAdapter.rsync_binary()),
        ("crontab", DebianAdapter.crontab_binary()),
        ("nft", DebianAdapter.nft_binary()),
        ("postfix", DebianAdapter.postfix_binary()),
        ("postconf", DebianAdapter.postconf_binary()),
        ("postmap", DebianAdapter.postmap_binary()),
        ("postqueue", DebianAdapter.postqueue_binary()),
        ("postsuper", DebianAdapter.postsuper_binary()),
        ("doveadm", DebianAdapter.doveadm_binary()),
        ("doveconf", DebianAdapter.doveconf_binary()),
        ("rspamadm", DebianAdapter.rspamadm_binary()),
    ]
}

/// The Debian adapter names every program it lets the agent execute by an absolute path.
///
/// An absolute path is what makes "the agent starts only what an adapter named"
/// hold: a bare name would be resolved through `PATH` by a root process. The
/// table is checked for being complete first, so a method added to the list
/// without a path check cannot hide.
#[test]
fn every_program_the_debian_adapter_executes_is_an_absolute_path() {
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

/// No program path the Debian adapter names climbs out of its directory with `..`.
#[test]
fn no_program_the_debian_adapter_executes_has_a_parent_directory_component() {
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

/// No program path the Debian adapter names carries whitespace or a control character.
///
/// Such a character cannot be part of a real install path and would corrupt any
/// log line or unit file the path is later written into.
#[test]
fn no_program_the_debian_adapter_executes_has_whitespace_or_control_characters() {
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

    assert_eq!(DebianAdapter.php_fpm_binary(&first), "/usr/sbin/php-fpm8.3");
    assert_eq!(
        DebianAdapter.php_fpm_binary(&second),
        "/usr/sbin/php-fpm8.4"
    );
    assert_ne!(
        DebianAdapter.php_fpm_binary(&first),
        DebianAdapter.php_fpm_binary(&second)
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
    let sh: &str = DebianAdapter.sh_binary();
    let nologin: &str = DebianAdapter.nologin_shell();

    assert!(sh.starts_with('/'));
    assert!(nologin.starts_with('/'));
}

/// The Debian family documents `/bin/bash` for a login shell, and a login shell is a
/// different path from the shell of an account that must not log in.
#[test]
fn the_debian_family_names_its_documented_bash_and_not_its_nologin_shell() {
    assert_eq!(DebianAdapter.bash_shell(), "/bin/bash");
    assert_ne!(DebianAdapter.bash_shell(), DebianAdapter.nologin_shell());
}

/// The Debian family keeps `at` job files in the directory its `at` package spools to.
#[test]
fn the_debian_family_names_the_directory_its_at_daemon_spools_to() {
    assert_eq!(DebianAdapter.at_spool_directory(), "/var/spool/cron/atjobs");
}

/// The Debian family names the real directory its mail spool files live in.
#[test]
fn the_debian_family_names_the_real_directory_of_its_mail_spool() {
    assert_eq!(DebianAdapter.mail_spool_directory(), "/var/mail");
}

/// The pending-updates read on the Debian family is a simulation, and says so in
/// its arguments: `-s` is what makes the run unable to change the host.
#[test]
fn the_debian_family_reads_pending_updates_through_a_simulation() {
    assert!(DebianAdapter.pending_updates_arguments().contains(&"-s"));
    assert_eq!(
        DebianAdapter.package_query_binary().to_string(),
        DebianAdapter.package_manager()
    );
}

/// An empty lists directory reads as up to date to the simulation, so this family
/// asks whether the catalogue exists first, and the probe it names refuses an
/// empty answer.
#[test]
fn the_debian_family_probes_for_a_built_catalogue_before_reading_updates() {
    let probe = DebianAdapter.catalogue_probe().expect("a probe");

    assert_eq!(
        probe.arguments,
        &["indextargets", "--format", "$(FILENAME)"]
    );
    assert!((probe.read)(0, "").is_err());
    assert!((probe.read)(0, "/var/lib/apt/lists/x_Packages.lz4\n").is_ok());
}

/// The simulated upgrade prints every package's origins, so this family needs
/// no second reading to classify.
#[test]
fn the_debian_family_runs_no_second_security_reading() {
    assert!(DebianAdapter.security_pending_updates_arguments().is_none());
}

/// With no second reading, marking hands the rows back as they were.
#[test]
fn the_debian_family_marks_nothing_and_returns_the_rows_unchanged() {
    let rows = vec![crate::PendingUpdate {
        package_name: String::from("libc6"),
        available_version: String::from("2.39-0ubuntu8.9"),
        installed_version: Some(String::from("2.39-0ubuntu8.1")),
        source: String::from("Ubuntu:24.04/noble-security"),
        classification: crate::UpdateClassification::Security,
    }];

    assert_eq!(
        DebianAdapter
            .mark_security_updates(rows.clone(), 0, "")
            .unwrap(),
        rows
    );
}

/// The Debian family answers the reboot question with the marker file its package
/// hooks create, and the path is absolute.
#[test]
fn the_debian_family_probes_for_a_reboot_with_a_marker_file() {
    assert_eq!(
        DebianAdapter.reboot_probe(),
        crate::RebootProbe::Marker(std::path::PathBuf::from("/var/run/reboot-required"))
    );
}

/// A marker file has no exit status, so reading one is refused and never answered
/// "not required".
#[test]
fn the_debian_family_refuses_to_read_a_program_status_for_its_marker_probe() {
    assert_eq!(
        DebianAdapter.read_reboot_probe_run(0),
        Err(crate::RebootProbeFailure::NotAProgramProbe)
    );
}

/// The Debian family names the daemon's checkers and control client by absolute path.
#[test]
fn the_debian_family_names_the_dns_checkers_and_control_client() {
    assert_eq!(
        DebianAdapter.dns_checkconf_binary(),
        "/usr/bin/named-checkconf"
    );
    assert_eq!(
        DebianAdapter.dns_checkzone_binary(),
        "/usr/bin/named-checkzone"
    );
    assert_eq!(DebianAdapter.dns_control_binary(), "/usr/sbin/rndc");
}

/// The Debian family runs the DNS daemon as `bind`, whose group is the one the control key is readable by.
#[test]
fn the_debian_family_runs_the_dns_daemon_as_its_own_user_and_group() {
    assert_eq!(DebianAdapter.dns_user(), "bind");
    assert_eq!(DebianAdapter.dns_group(), "bind");
}

/// Zone files sit below the daemon's state directory, never beside it, so a zone name can never name the zone-list store.
#[test]
fn the_debian_family_keeps_zone_files_below_the_dns_state_directory() {
    assert_eq!(DebianAdapter.dns_state_directory(), "/var/lib/bind/maran");
    assert_eq!(
        DebianAdapter.dns_zone_directory(),
        "/var/lib/bind/maran/zones"
    );
}

/// Signed zones and their keys sit in two directories of their own below the state directory, apart from the read-only plain zone directory and from each other.
#[test]
fn the_debian_family_keeps_signed_zones_and_keys_in_their_own_directories() {
    assert_eq!(
        DebianAdapter.dns_signed_zone_directory(),
        "/var/lib/bind/maran/signed-zones"
    );
    assert_eq!(
        DebianAdapter.dns_key_directory(),
        "/var/lib/bind/maran/keys"
    );

    let directories = [
        DebianAdapter.dns_zone_directory(),
        DebianAdapter.dns_signed_zone_directory(),
        DebianAdapter.dns_key_directory(),
    ];
    for (index, directory) in directories.iter().enumerate() {
        assert!(directory.starts_with(DebianAdapter.dns_state_directory()));
        for other in &directories[index + 1..] {
            assert!(
                !other.starts_with(directory) && !directory.starts_with(other),
                "{directory} and {other} must not nest"
            );
        }
    }
}

#[test]
fn the_debian_family_names_rsync_at_its_documented_path() {
    assert_eq!(DebianAdapter.rsync_binary(), "/usr/bin/rsync");
}

/// The Debian family names Apache's control script, unit, user and group as its packages ship them.
#[test]
fn the_debian_family_names_the_apache_facts_of_its_packages() {
    assert_eq!(DebianAdapter.apache_binary(), "/usr/sbin/apache2ctl");
    assert_eq!(DebianAdapter.apache_service(), "apache2");
    assert_eq!(DebianAdapter.apache_user(), "www-data");
    assert_eq!(DebianAdapter.apache_group(), "www-data");
}

/// Apache's identity is its own answer and not nginx's: the inverse control for the test above,
/// which an adapter that returned the nginx user for both would fail on every family but Debian.
#[test]
fn the_debian_family_does_not_answer_the_nginx_facts_for_apache() {
    assert_ne!(
        DebianAdapter.apache_service(),
        DebianAdapter.nginx_service()
    );
    assert_eq!(
        DebianAdapter.apache_user() == DebianAdapter.web_server_user(),
        "debian" == "debian"
    );
}

/// A site's unit runs Node.js and npm from absolute paths the adapter names.
#[test]
fn the_debian_family_names_the_node_launchers_by_absolute_path() {
    assert!(DebianAdapter.node_executable().starts_with('/'));
    assert!(DebianAdapter.npm_executable().starts_with('/'));
    assert_ne!(
        DebianAdapter.node_executable(),
        DebianAdapter.npm_executable()
    );
}

/// The facts asked with a web server answer that server's own, never the other's.
///
/// Both halves are asserted: an adapter that always answered nginx's would pass the first and an
/// adapter that always answered Apache's the second.
#[test]
fn the_debian_family_answers_each_site_web_server_with_its_own_facts() {
    use crate::SiteWebServer;
    assert_eq!(
        DebianAdapter.site_web_server_service(SiteWebServer::Nginx),
        "nginx"
    );
    assert_eq!(
        DebianAdapter.site_web_server_service(SiteWebServer::Apache),
        "apache2"
    );
    assert_eq!(
        DebianAdapter.site_web_server_binary(SiteWebServer::Nginx),
        "/usr/sbin/nginx"
    );
    assert_eq!(
        DebianAdapter.site_web_server_binary(SiteWebServer::Apache),
        "/usr/sbin/apache2ctl"
    );
    assert_eq!(
        DebianAdapter.site_web_server_group(SiteWebServer::Apache),
        "www-data"
    );
}

/// The debian family names the packages of the app catalogue as its sources carry them.
#[test]
fn the_debian_family_names_the_packages_of_the_app_catalogue() {
    assert_eq!(DebianAdapter.nginx_package(), "nginx");
    assert_eq!(DebianAdapter.apache_package(), "apache2");
    assert_eq!(DebianAdapter.apache_extra_packages(), &[] as &[&str]);
    assert_eq!(
        DebianAdapter.apache_include_snippet_path(),
        "/etc/apache2/conf-enabled/maran.conf"
    );
    assert_eq!(DebianAdapter.nodejs_package("20"), "nodejs");
    assert_eq!(
        DebianAdapter.nodejs_packaging(),
        crate::nodejs_packaging::NodejsPackaging::SingleDistroPackage
    );
}
