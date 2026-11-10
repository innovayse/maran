//! Tests for [`parse_apache_vhosts`]: reading a source's Apache virtual hosts.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::inventory_fixtures::{count_of, vhost_file};
use crate::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::parse_apache_vhosts::parse_apache_vhosts;

/// Four virtual hosts in several shapes (synthetic).
const VHOSTS: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/apache_vhosts.conf");

/// A virtual host that is never closed (synthetic).
const UNCLOSED: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/apache_unclosed.conf");

/// Every virtual host of a file is read with validated names and roots.
#[test]
fn every_virtual_host_is_read_with_validated_names() {
    let parsed = parse_apache_vhosts(&[vhost_file("/etc/apache2/sites-enabled/v.conf", VHOSTS)]);

    assert_eq!(parsed.items.len(), 4);
    let first = &parsed.items[0];
    assert_eq!(first.primary.as_str(), "www.shopsite.example.org");
    assert_eq!(first.aliases.len(), 2);
    assert_eq!(first.backend, SourceSiteBackend::Php);
    assert_eq!(first.php_version.as_deref(), Some("8.1"));
    assert_eq!(
        first.document_root.as_ref().unwrap().as_str(),
        "/home/shopsite/public_html"
    );
    assert_eq!(parsed.items[1].backend, SourceSiteBackend::Proxy);
    assert!(parsed.warnings.is_empty());
}

/// An unreadable file is skipped with one warning and the next file is read.
#[test]
fn an_unreadable_file_is_skipped_and_the_next_is_read() {
    let files = [
        vhost_file("/etc/apache2/sites-enabled/open.conf", UNCLOSED),
        vhost_file("/etc/apache2/sites-enabled/v.conf", VHOSTS),
    ];

    let parsed = parse_apache_vhosts(&files);

    assert_eq!(parsed.items.len(), 4);
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::SiteUnreadable),
        1
    );
}

/// A name the domain validator refuses is a warning, never repaired.
#[test]
fn a_refused_name_is_a_warning_and_the_site_is_kept() {
    let file = vhost_file(
        "/etc/apache2/sites-enabled/w.conf",
        "<VirtualHost *:80>\nServerName ok.example.com\nServerAlias *.ok.example.com\nDocumentRoot /home/acme/w\n</VirtualHost>\n",
    );

    let parsed = parse_apache_vhosts(&[file]);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(parsed.warnings.len(), 1);
    assert_eq!(parsed.warnings[0].kind(), SourceWarningKind::NameRefused);
    assert_eq!(parsed.warnings[0].subject(), "ok.example.com");
}

/// A primary name repeated in another file keeps the first.
#[test]
fn a_repeated_primary_keeps_the_first() {
    let one = "<VirtualHost *:80>\nServerName dup.example.com\nDocumentRoot /home/acme/one\n</VirtualHost>\n";
    let two = "<VirtualHost *:80>\nServerName dup.example.com\nDocumentRoot /home/acme/two\n</VirtualHost>\n";

    let parsed = parse_apache_vhosts(&[
        vhost_file("/etc/apache2/sites-enabled/1.conf", one),
        vhost_file("/etc/apache2/sites-enabled/2.conf", two),
    ]);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(
        parsed.items[0].document_root.as_ref().unwrap().as_str(),
        "/home/acme/one"
    );
}
