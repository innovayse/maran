//! Tests for [`parse_web_server`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_web_server;
use crate::transfer::model::source_web_server::SourceWebServer;

#[test]
fn the_two_banners_are_told_apart() {
    assert_eq!(
        parse_web_server("nginx version: nginx/1.24.0 (Ubuntu)\n"),
        Some(SourceWebServer::Nginx)
    );
    assert_eq!(
        parse_web_server("Server version: Apache/2.4.58 (Ubuntu)\nServer built:   2024-04-10\n"),
        Some(SourceWebServer::Apache)
    );
}

#[test]
fn text_that_names_neither_is_unknown() {
    for banner in ["", "lighttpd/1.4.71\n", "\n"] {
        assert_eq!(parse_web_server(banner), None, "{banner:?}");
    }
}
