//! Tests for the reading of the site web server marker.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::SiteWebServer;

use super::parse_marker;
use crate::apps::marker_text::marker_text;

#[test]
fn the_marker_names_the_web_server_and_nothing_else_does() {
    assert_eq!(parse_marker(Some("apache\n")), Some(SiteWebServer::Apache));
    assert_eq!(parse_marker(Some("nginx")), Some(SiteWebServer::Nginx));
}

#[test]
fn no_marker_and_an_unrecognised_marker_name_no_server() {
    // Inverse control: neither nginx's binary being present nor Apache's is a marker. Only the
    // file's text decides, so a host that has the packages and has chosen nothing names no server.
    assert_eq!(parse_marker(None), None);
    assert_eq!(parse_marker(Some("")), None);
    assert_eq!(parse_marker(Some("Apache2")), None);
    assert_eq!(parse_marker(Some("apache nginx")), None);
}

#[test]
fn what_is_written_is_what_is_read() {
    for server in [SiteWebServer::Nginx, SiteWebServer::Apache] {
        assert_eq!(parse_marker(Some(marker_text(server))), Some(server));
    }
}
