//! Tests for [`read_sites`]: the second step of an inventory read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{
    RecordingSink, count_of, passwd_entry, read_apache, read_nginx,
};
use crate::transfer::inventory::inventory_run::InventoryRun;
use crate::transfer::inventory::model::account_draft::AccountDraft;
use crate::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::read_sites::read_sites;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Two drafts: `acme` and `shopsite`.
fn drafts() -> Vec<AccountDraft> {
    vec![
        AccountDraft::new(passwd_entry("acme", "/home/acme")),
        AccountDraft::new(passwd_entry("shopsite", "/home/shopsite")),
    ]
}

/// A source with one nginx site, two Apache hosts and one installed PHP.
fn source() -> FakeSourceShell {
    FakeSourceShell::new()
        .answering(RemoteCommand::ListPhpVersions, "/etc/php/8.2\n")
        .answering(RemoteCommand::ListNginxVhostFiles, "/etc/nginx/conf.d/a.conf\n")
        .answering(
            read_nginx("/etc/nginx/conf.d/a.conf"),
            "server { server_name a.example.com; root /home/acme/w; location ~ \\.php$ { fastcgi_pass 127.0.0.1:9000; } }",
        )
        .answering(RemoteCommand::ListApacheVhostFiles, "/etc/apache2/sites-enabled/s.conf\n")
        .answering(
            read_apache("/etc/apache2/sites-enabled/s.conf"),
            "<VirtualHost *:80>\nServerName s.example.org\nDocumentRoot /home/shopsite/x\n</VirtualHost>\n<VirtualHost *:80>\nServerName lost.example.net\nDocumentRoot /var/www/lost\n</VirtualHost>\n",
        )
}

/// Sites of both servers are attached to their accounts, the PHP version comes
/// from the only installed one, and a site no account holds is a warning.
#[test]
fn sites_of_both_servers_are_attached_and_the_unowned_one_is_a_warning() {
    let shell = source();
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_sites(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].sites.len(), 1);
    assert_eq!(drafts[0].sites[0].domain.as_str(), "a.example.com");
    assert_eq!(drafts[0].sites[0].backend, SourceSiteBackend::Php);
    assert_eq!(drafts[0].sites[0].php_version.as_deref(), Some("8.2"));
    assert_eq!(drafts[1].sites[0].domain.as_str(), "s.example.org");
    assert_eq!(sink.warnings.len(), 1);
    assert_eq!(sink.warnings[0].kind(), SourceWarningKind::SiteUnreadable);
    assert_eq!(sink.warnings[0].subject(), "lost.example.net");
}

/// Servers with no files give no sites and no warnings.
#[test]
fn servers_with_no_files_give_no_sites() {
    let shell = FakeSourceShell::new()
        .answering(RemoteCommand::ListPhpVersions, "")
        .answering(RemoteCommand::ListNginxVhostFiles, "")
        .answering(RemoteCommand::ListApacheVhostFiles, "");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_sites(&mut run, &mut drafts).unwrap();

    assert!(drafts.iter().all(|draft| draft.sites.is_empty()));
    assert!(sink.warnings.is_empty());
}

/// A PHP list that cannot be read is one warning and the site keeps no version.
#[test]
fn an_unreadable_php_list_leaves_the_version_unknown() {
    let shell = FakeSourceShell::new()
        .exiting(RemoteCommand::ListPhpVersions, 127, "")
        .answering(RemoteCommand::ListNginxVhostFiles, "/etc/nginx/conf.d/a.conf\n")
        .answering(
            read_nginx("/etc/nginx/conf.d/a.conf"),
            "server { server_name a.example.com; root /home/acme/w; location / { fastcgi_pass 127.0.0.1:9000; } }",
        )
        .answering(RemoteCommand::ListApacheVhostFiles, "");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_sites(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].sites[0].php_version, None);
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::SiteUnreadable),
        1
    );
}

/// A failed connection ends the step with an error, not a warning.
#[test]
fn a_connection_failure_ends_the_step() {
    let shell = FakeSourceShell::new()
        .answering(RemoteCommand::ListPhpVersions, "")
        .unreachable(RemoteCommand::ListNginxVhostFiles);
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let result = read_sites(&mut run, &mut drafts);

    assert!(matches!(result, Err(TransferOpError::Unreachable)));
}
