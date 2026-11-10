//! Tests for [`read_apache_virtual_hosts`]: finding the virtual hosts of an Apache file.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::apache_syntax_error::ApacheSyntaxError;
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use crate::transfer::inventory::read_apache_virtual_hosts::read_apache_virtual_hosts;

/// Four virtual hosts in several shapes (synthetic).
const VHOSTS: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/apache_vhosts.conf");

/// A virtual host that is never closed (synthetic).
const UNCLOSED: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/apache_unclosed.conf");

/// Names, aliases across a continuation, the root and the PHP handler are read.
#[test]
fn a_php_virtual_host_is_read_across_a_continuation() {
    let hosts = read_apache_virtual_hosts(VHOSTS).unwrap();

    assert_eq!(hosts.len(), 4);
    assert_eq!(
        hosts[0].server_name.as_deref(),
        Some("www.shopsite.example.org")
    );
    assert_eq!(
        hosts[0].aliases,
        ["shopsite.example.org", "shop.shopsite.example.org"]
    );
    assert_eq!(
        hosts[0].document_root.as_deref(),
        Some("/home/shopsite/public_html")
    );
    assert_eq!(hosts[0].backend, SourceSiteBackend::Php);
    assert_eq!(hosts[0].php_version.as_deref(), Some("8.1"));
}

/// A ProxyPass is a proxy, a virtual host inside an IfModule is still read, and
/// directive names are case-insensitive.
#[test]
fn proxy_wrapped_and_lowercase_hosts_are_read() {
    let hosts = read_apache_virtual_hosts(VHOSTS).unwrap();

    assert_eq!(
        hosts[1].server_name.as_deref(),
        Some("api.acme.example.com")
    );
    assert_eq!(hosts[1].backend, SourceSiteBackend::Proxy);
    assert_eq!(
        hosts[2].server_name.as_deref(),
        Some("plain.acme.example.com")
    );
    assert_eq!(hosts[2].document_root.as_deref(), Some("/home/acme/plain"));
    assert_eq!(hosts[2].backend, SourceSiteBackend::Static);
}

/// A ProxyPass to a FastCGI address is PHP, not a proxy; a `!` exclusion is
/// neither.
#[test]
fn a_fastcgi_proxy_pass_is_php_and_an_exclusion_is_nothing() {
    let php = read_apache_virtual_hosts(
        "<VirtualHost *:80>\nServerName a.example.com\nProxyPassMatch ^/(.*\\.php)$ unix:/run/php/php8.3-fpm.sock|fcgi://localhost/x/$1\n</VirtualHost>\n",
    )
    .unwrap();
    let excluded = read_apache_virtual_hosts(
        "<VirtualHost *:80>\nServerName b.example.com\nProxyPass /static !\n</VirtualHost>\n",
    )
    .unwrap();

    assert_eq!(php[0].backend, SourceSiteBackend::Php);
    assert_eq!(php[0].php_version.as_deref(), Some("8.3"));
    assert_eq!(excluded[0].backend, SourceSiteBackend::Static);
}

/// AddHandler and AddType naming PHP make a PHP site; one that does not is the
/// inverse control.
#[test]
fn add_handler_naming_php_is_php() {
    let php = read_apache_virtual_hosts(
        "<VirtualHost *:80>\nAddHandler application/x-httpd-php .php\n</VirtualHost>\n",
    )
    .unwrap();
    let other = read_apache_virtual_hosts(
        "<VirtualHost *:80>\nAddHandler cgi-script .cgi\n</VirtualHost>\n",
    )
    .unwrap();

    assert_eq!(php[0].backend, SourceSiteBackend::Php);
    assert_eq!(other[0].backend, SourceSiteBackend::Static);
}

/// A quoted value that is not closed in its own word is not read as a path.
#[test]
fn a_document_root_with_a_quoted_space_is_not_read() {
    let hosts = read_apache_virtual_hosts(
        "<VirtualHost *:80>\nServerName a.example.com\nDocumentRoot \"/home/a/my site\"\n</VirtualHost>\n",
    )
    .unwrap();

    assert_eq!(hosts[0].document_root, None);
}

/// Text outside any virtual host is ignored.
#[test]
fn directives_outside_a_virtual_host_are_ignored() {
    let hosts = read_apache_virtual_hosts("ServerName global.example.com\nDocumentRoot /var/www\n")
        .unwrap();

    assert!(hosts.is_empty());
}

/// Sections that do not balance are refused in each way they can fail.
#[test]
fn unbalanced_sections_are_refused() {
    assert_eq!(
        read_apache_virtual_hosts(UNCLOSED),
        Err(ApacheSyntaxError::UnbalancedSection)
    );
    assert_eq!(
        read_apache_virtual_hosts("</VirtualHost>\n"),
        Err(ApacheSyntaxError::UnbalancedSection)
    );
    assert_eq!(
        read_apache_virtual_hosts("<VirtualHost *:80>\n</Directory>\n"),
        Err(ApacheSyntaxError::UnbalancedSection)
    );
    assert_eq!(
        read_apache_virtual_hosts("<VirtualHost *:80>\n<VirtualHost *:81>\n"),
        Err(ApacheSyntaxError::UnbalancedSection)
    );
}

/// Nesting past the bound is refused; the edge is the inverse control.
#[test]
fn nesting_past_the_bound_is_refused_and_the_edge_is_accepted() {
    let nested = |depth: usize| {
        format!(
            "{}{}",
            "<Directory /x>\n".repeat(depth),
            "</Directory>\n".repeat(depth)
        )
    };

    assert!(read_apache_virtual_hosts(&nested(InventoryLimits::MAX_BLOCK_DEPTH)).is_ok());
    assert_eq!(
        read_apache_virtual_hosts(&nested(InventoryLimits::MAX_BLOCK_DEPTH + 1)),
        Err(ApacheSyntaxError::TooDeep)
    );
}

/// A huge line, a NUL byte and an endless continuation are refused, not read.
#[test]
fn hostile_lines_are_refused() {
    let huge = format!(
        "<VirtualHost *:80>\nServerAlias {}\n</VirtualHost>\n",
        "a".repeat(InventoryLimits::MAX_LINE_BYTES)
    );
    let nul = "<VirtualHost *:80>\nServerName a\0b.example.com\n</VirtualHost>\n";
    let endless = format!(
        "<VirtualHost *:80>\n{}</VirtualHost>\n",
        "ServerAlias aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \\\n".repeat(400)
    );

    assert_eq!(
        read_apache_virtual_hosts(&huge),
        Err(ApacheSyntaxError::LineRefused)
    );
    assert_eq!(
        read_apache_virtual_hosts(nul),
        Err(ApacheSyntaxError::LineRefused)
    );
    assert_eq!(
        read_apache_virtual_hosts(&endless),
        Err(ApacheSyntaxError::LineTooLong)
    );
}

/// A file over the byte bound is refused before it is read.
#[test]
fn a_file_over_the_byte_bound_is_refused() {
    let text = "#".repeat(InventoryLimits::MAX_OUTPUT_BYTES + 1);

    assert_eq!(
        read_apache_virtual_hosts(&text),
        Err(ApacheSyntaxError::TooLarge)
    );
}

/// An empty file has no virtual hosts.
#[test]
fn an_empty_file_has_no_virtual_hosts() {
    assert_eq!(read_apache_virtual_hosts("").unwrap(), []);
}
