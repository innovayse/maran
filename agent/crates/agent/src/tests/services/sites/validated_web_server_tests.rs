//! Tests for the reading of a request's site web server.

use maran_distro::SiteWebServer;

use super::validated_web_server;
use crate::proto::SiteWebServer as WireWebServer;

#[test]
fn apache_is_apache() {
    assert_eq!(
        validated_web_server(WireWebServer::Apache as i32),
        SiteWebServer::Apache
    );
}

#[test]
fn nginx_the_unset_value_and_an_unknown_one_are_nginx() {
    // Inverse control of the test above: only the explicit Apache value reaches Apache.
    for value in [
        WireWebServer::Nginx as i32,
        WireWebServer::Unspecified as i32,
        77,
    ] {
        assert_eq!(validated_web_server(value), SiteWebServer::Nginx);
    }
}
