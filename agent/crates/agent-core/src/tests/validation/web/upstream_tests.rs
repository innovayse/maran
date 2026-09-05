//! Tests for the `upstream` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `upstream.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach private items — a crate-level `tests/`
//! directory sees only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::upstream::Upstream;
use super::super::upstream_error::UpstreamError;

#[test]
fn a_loopback_upstream_parses() {
    assert_eq!(
        Upstream::parse("127.0.0.1:3000").unwrap().as_str(),
        "127.0.0.1:3000"
    );
}

#[test]
fn a_private_upstream_parses() {
    assert_eq!(
        Upstream::parse("192.168.1.10:8080").unwrap().as_str(),
        "192.168.1.10:8080"
    );
}

#[test]
fn an_upstream_pointing_at_a_public_address_is_rejected() {
    // A reverse proxy at a public address turns the panel into an open proxy
    // for whoever asked for the site.
    assert!(matches!(
        Upstream::parse("8.8.8.8:80"),
        Err(UpstreamError::NotPrivate { .. })
    ));
}

#[test]
fn an_upstream_containing_a_newline_is_rejected() {
    // Written into an nginx `proxy_pass` line verbatim, a newline would end
    // the directive and start one of the caller's choosing.
    assert!(matches!(
        Upstream::parse("127.0.0.1:3000\nserver {\n  listen 80"),
        Err(UpstreamError::IllegalCharacter { .. })
    ));
}

#[test]
fn an_upstream_with_port_zero_is_rejected() {
    assert!(matches!(
        Upstream::parse("127.0.0.1:0"),
        Err(UpstreamError::InvalidPort)
    ));
}

#[test]
fn an_upstream_with_an_out_of_range_port_is_rejected() {
    assert!(matches!(
        Upstream::parse("127.0.0.1:70000"),
        Err(UpstreamError::Malformed { .. })
    ));
}

#[test]
fn an_upstream_missing_a_port_is_rejected() {
    assert!(matches!(
        Upstream::parse("127.0.0.1"),
        Err(UpstreamError::Malformed { .. })
    ));
}

#[test]
fn an_upstream_with_a_hostname_instead_of_an_ip_is_rejected() {
    // Only literal IP addresses are accepted: a hostname would need its own
    // DNS resolution step, another place for an attacker-controlled value to
    // reach the outside world.
    assert!(matches!(
        Upstream::parse("localhost:3000"),
        Err(UpstreamError::InvalidHost { .. })
    ));
}

#[test]
fn an_empty_upstream_is_rejected() {
    assert!(matches!(Upstream::parse(""), Err(UpstreamError::Empty)));
}

#[test]
fn the_ipv6_loopback_upstream_parses() {
    assert_eq!(Upstream::parse("::1:3000").unwrap().as_str(), "::1:3000");
}

#[test]
fn a_bracketed_ipv6_loopback_upstream_parses() {
    // The standard `[host]:port` notation, distinct from the degenerate bare
    // form above which only happens to work for `::1`.
    assert_eq!(
        Upstream::parse("[::1]:3000").unwrap().as_str(),
        "[::1]:3000"
    );
}

#[test]
fn a_bracketed_ipv6_public_address_is_rejected() {
    assert!(matches!(
        Upstream::parse("[2001:db8::1]:80"),
        Err(UpstreamError::NotPrivate { .. })
    ));
}

#[test]
fn an_upstream_pointing_at_an_ipv6_public_address_is_rejected() {
    assert!(matches!(
        Upstream::parse("2001:4860:4860::8888:80"),
        Err(UpstreamError::NotPrivate { .. })
    ));
}
