//! Tests for [`read_source_inventory`]: the whole read, end to end, against a
//! scripted source built from the synthetic fixtures.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{
    RecordingSink, count_of, crontab_of, mailboxes_of, read_apache, read_nginx, usage_of,
};
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::inventory_step::InventoryStep;
use crate::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::read_source_inventory::read_source_inventory;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Hand-written account database (synthetic).
const PASSWD: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/passwd_hosting.txt");
/// A PHP site and nothing else (synthetic).
const NGINX_PHP: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/nginx_php_site.conf");
/// A proxied site, a static site and a catch-all (synthetic).
const NGINX_MIXED: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_proxy_and_static.conf"
);
/// Four Apache virtual hosts (synthetic).
const APACHE: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/apache_vhosts.conf");
/// Databases with refusable names (synthetic).
const DATABASES: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/mysql_listing.txt");
/// A crontab with four entries (synthetic).
const CRONTAB: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/crontab_basic.txt");

/// A source that serves everything the fixtures describe.
fn full_source() -> FakeSourceShell {
    FakeSourceShell::new()
        .answering(RemoteCommand::ListPasswd, PASSWD)
        .answering(RemoteCommand::ListPhpVersions, "/etc/php/8.2\n")
        .answering(
            RemoteCommand::ListNginxVhostFiles,
            "/etc/nginx/sites-enabled/php.conf\n/etc/nginx/sites-enabled/mixed.conf\n",
        )
        .answering(read_nginx("/etc/nginx/sites-enabled/php.conf"), NGINX_PHP)
        .answering(
            read_nginx("/etc/nginx/sites-enabled/mixed.conf"),
            NGINX_MIXED,
        )
        .answering(
            RemoteCommand::ListApacheVhostFiles,
            "/etc/apache2/sites-enabled/vhosts.conf\n",
        )
        .answering(
            read_apache("/etc/apache2/sites-enabled/vhosts.conf"),
            APACHE,
        )
        .answering(RemoteCommand::ListDatabases, DATABASES)
        .answering(crontab_of("acme"), CRONTAB)
        .exiting(crontab_of("shopsite"), 1, "")
        .answering(crontab_of("deploy"), "")
        .answering(usage_of("/home/acme"), "2048\t/home/acme\n")
        .answering(mailboxes_of("/home/acme"), "3\n")
        .answering(usage_of("/home/shopsite"), "10\t/home/shopsite\n")
        .answering(mailboxes_of("/home/shopsite"), "0\n")
        .exiting(usage_of("/opt/deploy"), 1, "")
        .answering(mailboxes_of("/opt/deploy"), "0\n")
}

/// A full read sends the steps in order, each account complete, and every
/// warning the fixtures are built to raise.
#[test]
fn a_full_read_sends_steps_accounts_and_warnings() {
    let shell = full_source();
    let mut sink = RecordingSink::default();

    let summary = read_source_inventory(&shell, &mut sink).unwrap();

    assert_eq!(
        sink.steps,
        [
            InventoryStep::Accounts,
            InventoryStep::Sites,
            InventoryStep::Databases,
            InventoryStep::Cron,
            InventoryStep::Disk,
        ]
    );
    let names: Vec<&str> = sink.accounts.iter().map(|a| a.name.as_str()).collect();
    assert_eq!(names, ["acme", "shopsite", "deploy"]);
    assert_eq!(summary.account_count, 3);
    assert_eq!(summary.warning_count as usize, sink.warnings.len());

    let acme = &sink.accounts[0];
    assert_eq!(acme.home, "/home/acme");
    assert_eq!(acme.disk_bytes, Some(2_097_152));
    assert_eq!(acme.cron_entry_count, 4);
    assert_eq!(acme.mailbox_count, 3);
    let domains: Vec<&str> = acme.sites.iter().map(|s| s.domain.as_str()).collect();
    assert_eq!(
        domains,
        [
            "acme.example.com",
            "app.acme.example.com",
            "api.acme.example.com",
            "plain.acme.example.com"
        ]
    );
    assert_eq!(acme.sites[0].backend, SourceSiteBackend::Php);
    assert_eq!(acme.sites[0].php_version.as_deref(), Some("8.2"));
    assert_eq!(acme.sites[1].backend, SourceSiteBackend::Proxy);
    let databases: Vec<&str> = acme.databases.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(databases, ["acme_wp", "acme_shop"]);
    assert_eq!(acme.databases[0].size_bytes, Some(1_048_576));

    let shopsite = &sink.accounts[1];
    assert_eq!(shopsite.sites.len(), 2);
    assert_eq!(shopsite.sites[1].php_version.as_deref(), Some("8.1"));
    assert_eq!(shopsite.databases.len(), 1);
    assert_eq!(shopsite.cron_entry_count, 0);

    let deploy = &sink.accounts[2];
    assert_eq!(deploy.disk_bytes, None);
    assert!(deploy.sites.is_empty());
}

/// Every warning of a full read is the kind and subject the fixtures call for.
#[test]
fn the_warnings_of_a_full_read_are_exactly_the_expected_ones() {
    let shell = full_source();
    let mut sink = RecordingSink::default();

    read_source_inventory(&shell, &mut sink).unwrap();

    assert_eq!(sink.warnings.len(), 7);
    let has = |kind: SourceWarningKind, subject: &str| {
        sink.warnings
            .iter()
            .any(|w| w.kind() == kind && w.subject() == subject)
    };
    // The static site's wildcard and regular-expression names.
    assert!(has(
        SourceWarningKind::NameRefused,
        "static.shopsite.example.org"
    ));
    // The Apache host whose root is outside every home.
    assert!(has(
        SourceWarningKind::SiteUnreadable,
        "outside.example.net"
    ));
    // The database with a bad shape and the two nobody owns.
    assert_eq!(
        sink.warnings
            .iter()
            .filter(|w| w.kind() == SourceWarningKind::NameRefused && w.subject().is_empty())
            .count(),
        3
    );
    // The home whose size command failed, and the mail found under acme.
    assert!(has(SourceWarningKind::DiskUnreadable, "deploy"));
    assert!(has(SourceWarningKind::MailNotMigrated, "acme"));
}

/// Nothing from the source is trusted for a subject: every subject is empty or a
/// name of an account or site of the inventory.
#[test]
fn every_subject_is_a_validated_name_or_empty() {
    let shell = full_source();
    let mut sink = RecordingSink::default();

    read_source_inventory(&shell, &mut sink).unwrap();

    let mut known: Vec<String> = sink
        .accounts
        .iter()
        .map(|a| a.name.as_str().to_owned())
        .collect();
    known.push("outside.example.net".to_owned());
    known.push("static.shopsite.example.org".to_owned());
    for warning in &sink.warnings {
        assert!(warning.subject().is_empty() || known.iter().any(|name| name == warning.subject()));
    }
}

/// Only closed commands are run, and only the ones the steps need: the files
/// read are the listed ones.
#[test]
fn only_the_expected_commands_are_run() {
    let shell = full_source();
    let mut sink = RecordingSink::default();

    read_source_inventory(&shell, &mut sink).unwrap();

    let ran = shell.ran();
    assert_eq!(ran[0], RemoteCommand::ListPasswd);
    assert!(ran.contains(&read_nginx("/etc/nginx/sites-enabled/mixed.conf")));
    assert!(ran.contains(&crontab_of("deploy")));
    assert!(!ran.contains(&RemoteCommand::EffectiveUid));
    // 1 passwd + 1 php + 2 lists + 3 file reads + 1 databases + 3 crontabs + 6 disk.
    assert_eq!(ran.len(), 17);
}

/// A source with no accounts to host is an empty read, not an error.
#[test]
fn a_source_with_no_hosting_accounts_is_an_empty_read() {
    let shell = FakeSourceShell::new().answering(
        RemoteCommand::ListPasswd,
        "root:x:0:0:root:/root:/bin/bash\n",
    );
    let mut sink = RecordingSink::default();

    let summary = read_source_inventory(&shell, &mut sink).unwrap();

    assert_eq!(summary.account_count, 0);
    assert!(sink.accounts.is_empty());
    assert_eq!(sink.steps.len(), 5);
}

/// The account list failing is an error: there is nothing to build on.
#[test]
fn a_failed_account_list_is_an_error() {
    let shell = FakeSourceShell::new().exiting(RemoteCommand::ListPasswd, 1, "");
    let mut sink = RecordingSink::default();

    let result = read_source_inventory(&shell, &mut sink);

    assert!(matches!(result, Err(TransferOpError::InvalidRemoteOutput)));
    assert!(sink.accounts.is_empty());
}

/// A connection that fails part-way is an error and no account was sent; the
/// same source with a working connection is the inverse control and completes.
#[test]
fn a_connection_lost_part_way_is_an_error_with_no_account_sent() {
    let broken = FakeSourceShell::new()
        .unreachable(crontab_of("shopsite"))
        .answering(RemoteCommand::ListPasswd, PASSWD)
        .answering(RemoteCommand::ListPhpVersions, "")
        .answering(RemoteCommand::ListNginxVhostFiles, "")
        .answering(RemoteCommand::ListApacheVhostFiles, "")
        .answering(RemoteCommand::ListDatabases, "")
        .answering(crontab_of("acme"), CRONTAB);
    let working = full_source();
    let mut sink = RecordingSink::default();
    let mut control = RecordingSink::default();

    let result = read_source_inventory(&broken, &mut sink);
    let control_result = read_source_inventory(&working, &mut control);

    assert!(matches!(result, Err(TransferOpError::Unreachable)));
    assert!(sink.accounts.is_empty());
    assert!(control_result.is_ok());
    assert_eq!(control.accounts.len(), 3);
}

/// A bound reached on one command is a warning and the rest of the read goes on.
#[test]
fn a_bounded_command_is_a_warning_and_the_read_goes_on() {
    let shell = FakeSourceShell::new()
        .bounded(RemoteCommand::ListDatabases)
        .answering(RemoteCommand::ListPasswd, PASSWD)
        .answering(RemoteCommand::ListPhpVersions, "")
        .answering(RemoteCommand::ListNginxVhostFiles, "")
        .answering(RemoteCommand::ListApacheVhostFiles, "");
    let mut sink = RecordingSink::default();

    let summary = read_source_inventory(&shell, &mut sink).unwrap();

    assert_eq!(summary.account_count, 3);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 1);
}

/// A sink that stops listening ends the read quietly between steps: no account
/// is sent and no further command is run. The inverse control is every test
/// above, whose open sink receives all five steps.
#[test]
fn a_sink_that_stops_listening_ends_the_read_quietly() {
    let shell = full_source();
    let mut sink = RecordingSink {
        closes_after_steps: Some(2),
        ..RecordingSink::default()
    };

    let summary = read_source_inventory(&shell, &mut sink).unwrap();

    assert_eq!(sink.steps, [InventoryStep::Accounts, InventoryStep::Sites]);
    assert!(sink.accounts.is_empty());
    assert_eq!(summary.account_count, 0);
    assert!(!shell.ran().contains(&RemoteCommand::ListDatabases));
}

/// A source built to flood the stream: more accounts than the bound, every one of
/// them failing its crontab and its measurement. The accounts stop at the bound
/// and the warnings stop at theirs, each with one Truncated.
#[test]
fn a_flood_of_accounts_and_warnings_is_bounded() {
    let mut passwd = String::new();
    for index in 0..(InventoryLimits::MAX_ACCOUNTS + 1) {
        passwd.push_str(&format!(
            "u{index:05}:x:{}:1000::/home/u{index:05}:/bin/bash\n",
            2000 + index
        ));
    }
    let shell = FakeSourceShell::new()
        .answering(RemoteCommand::ListPasswd, &passwd)
        .answering(RemoteCommand::ListPhpVersions, "")
        .answering(RemoteCommand::ListNginxVhostFiles, "")
        .answering(RemoteCommand::ListApacheVhostFiles, "")
        .answering(RemoteCommand::ListDatabases, "");
    let mut sink = RecordingSink::default();

    let summary = read_source_inventory(&shell, &mut sink).unwrap();

    assert_eq!(sink.accounts.len(), InventoryLimits::MAX_ACCOUNTS);
    assert_eq!(
        summary.account_count as usize,
        InventoryLimits::MAX_ACCOUNTS
    );
    assert_eq!(
        sink.warnings.len(),
        InventoryLimits::MAX_WARNINGS as usize + 1
    );
    assert_eq!(
        sink.warnings.last().unwrap().kind(),
        SourceWarningKind::Truncated
    );
    assert_eq!(summary.warning_count, InventoryLimits::MAX_WARNINGS + 1);
}
