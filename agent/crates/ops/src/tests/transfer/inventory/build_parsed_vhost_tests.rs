//! Tests for [`build_parsed_vhost`]: validating one virtual host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::build_parsed_vhost::build_parsed_vhost;
use crate::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;

/// Turns string literals into the owned names the function takes.
fn names(raw: &[&str]) -> Vec<String> {
    raw.iter().map(|name| (*name).to_owned()).collect()
}

/// A virtual host with good names and root is built without a warning.
#[test]
fn a_good_virtual_host_is_built() {
    let mut warnings = Vec::new();

    let vhost = build_parsed_vhost(
        &names(&["a.example.com", "b.example.com"]),
        Some("/home/acme/www"),
        SourceSiteBackend::Php,
        Some("8.2".to_owned()),
        &mut warnings,
    )
    .unwrap();

    assert_eq!(vhost.primary.as_str(), "a.example.com");
    assert_eq!(vhost.document_root.unwrap().as_str(), "/home/acme/www");
    assert_eq!(vhost.php_version.as_deref(), Some("8.2"));
    assert!(warnings.is_empty());
}

/// A document root with a variable is refused: the site is dropped with a
/// warning naming its validated primary.
#[test]
fn a_root_with_a_variable_drops_the_site_with_a_warning() {
    let mut warnings = Vec::new();

    let vhost = build_parsed_vhost(
        &names(&["a.example.com"]),
        Some("/home/acme/$host"),
        SourceSiteBackend::Static,
        None,
        &mut warnings,
    );

    assert!(vhost.is_none());
    assert_eq!(warnings.len(), 1);
    assert_eq!(warnings[0].kind(), SourceWarningKind::SiteUnreadable);
    assert_eq!(warnings[0].subject(), "a.example.com");
}

/// A host whose names all fail is dropped with a warning that has no subject.
#[test]
fn a_host_with_only_refused_names_is_dropped_without_a_subject() {
    let mut warnings = Vec::new();

    let vhost = build_parsed_vhost(
        &names(&["*.example.com"]),
        Some("/home/acme/www"),
        SourceSiteBackend::Static,
        None,
        &mut warnings,
    );

    assert!(vhost.is_none());
    assert_eq!(warnings.len(), 1);
    assert_eq!(warnings[0].kind(), SourceWarningKind::NameRefused);
    assert_eq!(warnings[0].subject(), "");
}

/// A refused alias beside a valid primary keeps the site and warns about it.
#[test]
fn a_refused_alias_keeps_the_site_and_warns() {
    let mut warnings = Vec::new();

    let vhost = build_parsed_vhost(
        &names(&["a.example.com", "*.a.example.com"]),
        None,
        SourceSiteBackend::Proxy,
        None,
        &mut warnings,
    )
    .unwrap();

    assert!(vhost.document_root.is_none());
    assert_eq!(warnings[0].kind(), SourceWarningKind::NameRefused);
    assert_eq!(warnings[0].subject(), "a.example.com");
}

/// The catch-all host is dropped silently.
#[test]
fn the_catch_all_host_is_dropped_silently() {
    let mut warnings = Vec::new();

    let vhost = build_parsed_vhost(
        &names(&["_"]),
        Some("/var/www/html"),
        SourceSiteBackend::Static,
        None,
        &mut warnings,
    );

    assert!(vhost.is_none());
    assert!(warnings.is_empty());
}
