//! Tests for [`read_nginx_servers`]: finding the `server` blocks of a configuration.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::nginx_syntax_error::NginxSyntaxError;
use crate::transfer::inventory::read_nginx_servers::read_nginx_servers;
use crate::transfer::inventory::tokenize_nginx::tokenize_nginx;

/// Reads the servers of `text`.
fn servers_of(
    text: &str,
) -> Result<Vec<crate::transfer::inventory::model::nginx_server::NginxServer>, NginxSyntaxError> {
    read_nginx_servers(&tokenize_nginx(text).unwrap())
}

/// A server block's names, root and PHP target are collected.
#[test]
fn a_server_block_is_collected() {
    let servers = servers_of(
        "http { server { server_name a.example.com b.example.com; root /home/a/www; \
         location ~ \\.php$ { fastcgi_pass unix:/run/php/php8.2-fpm.sock; } } }",
    )
    .unwrap();

    assert_eq!(servers.len(), 1);
    assert_eq!(servers[0].server_names, ["a.example.com", "b.example.com"]);
    assert_eq!(servers[0].root.as_deref(), Some("/home/a/www"));
    assert_eq!(
        servers[0].fastcgi_pass.as_deref(),
        Some("unix:/run/php/php8.2-fpm.sock")
    );
    assert!(!servers[0].has_proxy_pass);
}

/// A root inside a location is used only as the fallback; the block's own wins.
#[test]
fn a_location_root_is_only_a_fallback() {
    let own = servers_of("server { location / { root /loc; } root /own; }").unwrap();
    let only_location = servers_of("server { location / { root /loc; } }").unwrap();

    assert_eq!(own[0].root.as_deref(), Some("/own"));
    assert_eq!(own[0].location_root.as_deref(), Some("/loc"));
    assert_eq!(only_location[0].root, None);
    assert_eq!(only_location[0].location_root.as_deref(), Some("/loc"));
}

/// A proxy_pass is noted.
#[test]
fn a_proxy_pass_is_noted() {
    let servers =
        servers_of("server { location / { proxy_pass http://127.0.0.1:3000; } }").unwrap();

    assert!(servers[0].has_proxy_pass);
}

/// Several servers are all returned and directives outside a server are ignored.
#[test]
fn several_servers_are_returned_and_outside_directives_are_ignored() {
    let servers =
        servers_of("root /etc/outside; server { server_name one; } server { server_name two; }")
            .unwrap();

    assert_eq!(servers.len(), 2);
    assert_eq!(servers[0].root, None);
}

/// A server inside a server is an ordinary block, not a second site.
#[test]
fn a_nested_server_is_not_a_second_site() {
    let servers =
        servers_of("server { server_name outer; server { server_name inner; } }").unwrap();

    assert_eq!(servers.len(), 1);
    assert_eq!(servers[0].server_names, ["outer"]);
}

/// Braces that do not balance are refused, in both directions.
#[test]
fn unbalanced_braces_are_refused() {
    assert_eq!(
        servers_of("server { server_name a;"),
        Err(NginxSyntaxError::UnbalancedBraces)
    );
    assert_eq!(
        servers_of("server { } }"),
        Err(NginxSyntaxError::UnbalancedBraces)
    );
}

/// Blocks nested past the bound are refused; the edge is the inverse control.
#[test]
fn nesting_past_the_bound_is_refused_and_the_edge_is_accepted() {
    let nested = |depth: usize| format!("{}{}", "a { ".repeat(depth), "} ".repeat(depth));

    assert!(servers_of(&nested(InventoryLimits::MAX_BLOCK_DEPTH)).is_ok());
    assert_eq!(
        servers_of(&nested(InventoryLimits::MAX_BLOCK_DEPTH + 1)),
        Err(NginxSyntaxError::TooDeep)
    );
}

/// A statement with more words than the bound is refused.
#[test]
fn a_statement_past_the_word_bound_is_refused() {
    let text = format!(
        "server {{ server_name {}; }}",
        "a ".repeat(InventoryLimits::MAX_STATEMENT_WORDS + 1)
    );

    assert_eq!(servers_of(&text), Err(NginxSyntaxError::StatementTooLong));
}

/// The names of one block stop at the name bound.
#[test]
fn names_stop_at_the_name_bound() {
    let mut text = String::from("server { ");
    for _ in 0..3 {
        text.push_str("server_name ");
        text.push_str(&"n ".repeat(200));
        text.push_str("; ");
    }
    text.push('}');

    let servers = servers_of(&text).unwrap();

    assert_eq!(
        servers[0].server_names.len(),
        InventoryLimits::MAX_SITE_NAMES
    );
}
