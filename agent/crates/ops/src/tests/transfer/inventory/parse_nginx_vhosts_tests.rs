//! Tests for [`parse_nginx_vhosts`]: reading a source's nginx virtual hosts.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::inventory_fixtures::{count_of, kinds_of, vhost_file};
use crate::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::parse_nginx_vhosts::parse_nginx_vhosts;

/// A PHP site (synthetic).
const PHP_SITE: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/nginx_php_site.conf");

/// A proxied site, a static site with refusable names, and a catch-all (synthetic).
const PROXY_AND_STATIC: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_proxy_and_static.conf"
);

/// Two files that include each other (synthetic).
const LOOP_A: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_include_loop_a.conf"
);

/// The other half of the loop (synthetic).
const LOOP_B: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_include_loop_b.conf"
);

/// A site that includes a snippet (synthetic).
const CHAIN_ROOT: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_include_chain_root.conf"
);

/// The snippet that names the PHP socket (synthetic).
const CHAIN_SNIPPET: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_include_chain_snippet.conf"
);

/// A server block that is never closed (synthetic).
const UNBALANCED: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_malformed_unbalanced.conf"
);

/// A closing brace with no opening one (synthetic).
const EXTRA_BRACE: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_malformed_extra_brace.conf"
);

/// A quote that is never closed (synthetic).
const BAD_QUOTE: &str = include_str!(
    "../../../../tests/fixtures/transfer/synthetic_inventory/nginx_malformed_quote.conf"
);

/// A PHP site is read with its aliases, root and the version its socket names.
#[test]
fn a_php_site_is_read_with_its_version() {
    let parsed = parse_nginx_vhosts(&[vhost_file("/etc/nginx/conf.d/php.conf", PHP_SITE)]);

    assert_eq!(parsed.items.len(), 1);
    let site = &parsed.items[0];
    assert_eq!(site.primary.as_str(), "acme.example.com");
    assert_eq!(site.aliases[0].as_str(), "www.acme.example.com");
    assert_eq!(site.backend, SourceSiteBackend::Php);
    assert_eq!(site.php_version.as_deref(), Some("8.2"));
    assert_eq!(
        site.document_root.as_ref().unwrap().as_str(),
        "/home/acme/public_html"
    );
    assert!(parsed.warnings.is_empty());
}

/// A proxied site and a static one are told apart; the catch-all is dropped
/// silently and the refused names cost one warning about the static site.
#[test]
fn proxy_static_and_catch_all_are_told_apart() {
    let parsed =
        parse_nginx_vhosts(&[vhost_file("/etc/nginx/conf.d/mixed.conf", PROXY_AND_STATIC)]);

    assert_eq!(parsed.items.len(), 2);
    assert_eq!(parsed.items[0].primary.as_str(), "app.acme.example.com");
    assert_eq!(parsed.items[0].backend, SourceSiteBackend::Proxy);
    assert_eq!(
        parsed.items[1].primary.as_str(),
        "static.shopsite.example.org"
    );
    assert_eq!(parsed.items[1].backend, SourceSiteBackend::Static);
    assert_eq!(parsed.items[1].aliases[0].as_str(), "shopsite.example.org");
    assert_eq!(
        parsed.items[1].document_root.as_ref().unwrap().as_str(),
        "/home/shopsite/www"
    );
    assert_eq!(parsed.warnings.len(), 1);
    assert_eq!(parsed.warnings[0].kind(), SourceWarningKind::NameRefused);
    assert_eq!(parsed.warnings[0].subject(), "static.shopsite.example.org");
}

/// An include loop skips both files with one warning each and reads nothing.
#[test]
fn an_include_loop_skips_both_files() {
    let files = [
        vhost_file("/etc/nginx/sites-enabled/loop_a.conf", LOOP_A),
        vhost_file("/etc/nginx/sites-enabled/loop_b.conf", LOOP_B),
    ];

    let parsed = parse_nginx_vhosts(&files);

    assert!(parsed.items.is_empty());
    assert_eq!(
        kinds_of(&parsed.warnings),
        [SourceWarningKind::SiteUnreadable; 2]
    );
}

/// A snippet LISTED by the directory listing is followed and supplies the PHP
/// socket.
#[test]
fn a_listed_snippet_is_followed() {
    let files = [
        vhost_file("/etc/nginx/sites-enabled/chain.conf", CHAIN_ROOT),
        vhost_file("/etc/nginx/snippets/chain_php.conf", CHAIN_SNIPPET),
    ];

    let parsed = parse_nginx_vhosts(&files);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(parsed.items[0].backend, SourceSiteBackend::Php);
    assert_eq!(parsed.items[0].php_version.as_deref(), Some("7.4"));
}

/// Inverse control: the same site with the snippet NOT listed is static,
/// because the include is not followed.
#[test]
fn an_unlisted_snippet_is_not_followed() {
    let parsed = parse_nginx_vhosts(&[vhost_file(
        "/etc/nginx/sites-enabled/chain.conf",
        CHAIN_ROOT,
    )]);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(parsed.items[0].backend, SourceSiteBackend::Static);
    assert_eq!(parsed.items[0].php_version, None);
}

/// Malformed files are skipped with a warning each, and a good file beside them
/// is still read.
#[test]
fn malformed_files_are_skipped_and_the_good_one_is_read() {
    let files = [
        vhost_file("/etc/nginx/conf.d/a.conf", UNBALANCED),
        vhost_file("/etc/nginx/conf.d/b.conf", EXTRA_BRACE),
        vhost_file("/etc/nginx/conf.d/c.conf", BAD_QUOTE),
        vhost_file("/etc/nginx/conf.d/d.conf", PHP_SITE),
    ];

    let parsed = parse_nginx_vhosts(&files);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::SiteUnreadable),
        3
    );
}

/// A NUL byte or a huge word makes the file unreadable, not a panic.
#[test]
fn hostile_files_are_unreadable_not_fatal() {
    let huge_word = format!("server {{ server_name {}; }}", "a".repeat(5000));
    let files = [
        vhost_file(
            "/etc/nginx/conf.d/nul.conf",
            "server { server_name a.example.com;\0 }",
        ),
        vhost_file("/etc/nginx/conf.d/huge.conf", &huge_word),
    ];

    let parsed = parse_nginx_vhosts(&files);

    assert!(parsed.items.is_empty());
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::SiteUnreadable),
        2
    );
}

/// A site reached twice — as a listed file and through an include — is kept
/// once.
#[test]
fn a_site_reached_twice_is_kept_once() {
    let files = [
        vhost_file("/etc/nginx/conf.d/php.conf", PHP_SITE),
        vhost_file(
            "/etc/nginx/conf.d/root.conf",
            "include /etc/nginx/conf.d/php.conf;",
        ),
    ];

    let parsed = parse_nginx_vhosts(&files);

    assert_eq!(parsed.items.len(), 1);
}

/// No files give no sites and no warnings.
#[test]
fn no_files_give_nothing() {
    let parsed = parse_nginx_vhosts(&[]);

    assert!(parsed.items.is_empty());
    assert!(parsed.warnings.is_empty());
}
