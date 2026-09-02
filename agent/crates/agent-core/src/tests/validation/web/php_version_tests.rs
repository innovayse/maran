//! Tests for the `php_version` module.
//!
//! The happy path is the least interesting test here. This value reaches a
//! root-written nginx directive through a template with escaping deliberately
//! off, so what matters is the set of things it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::php_version::PhpVersion;
use super::super::php_version_error::PhpVersionError;

#[test]
fn an_ordinary_version_parses() {
    assert_eq!(PhpVersion::parse("8.3").unwrap().as_str(), "8.3");
}

#[test]
fn the_oldest_supported_version_parses() {
    assert_eq!(PhpVersion::parse("7.4").unwrap().as_str(), "7.4");
}

#[test]
fn a_version_containing_a_newline_is_rejected() {
    // The attack this type exists for. Written into
    // `fastcgi_pass unix:/run/maran/php/acme-<version>.sock;` verbatim, the
    // rest of the value becomes directives of the caller's choosing inside a
    // config file root wrote — and `nginx -t` accepts it, because it is valid.
    let refused = PhpVersion::parse("8.3\n    root /etc;\n");

    assert_eq!(refused, Err(PhpVersionError::ControlCharacter));
}

#[test]
fn a_version_that_closes_the_directive_is_rejected() {
    let refused = PhpVersion::parse("8.3.sock; root /etc; #");

    assert!(matches!(refused, Err(PhpVersionError::Malformed { .. })));
}

#[test]
fn a_version_that_closes_the_server_block_is_rejected() {
    // `}` would end the server block and let a whole second `server { … }`
    // follow, serving any document root on any hostname.
    let refused = PhpVersion::parse("8.3}");

    assert!(matches!(refused, Err(PhpVersionError::Malformed { .. })));
}

#[test]
fn a_version_that_traverses_out_of_the_socket_directory_is_rejected() {
    // The same value names the pool directory and the socket path, so a
    // traversal here escapes both.
    let refused = PhpVersion::parse("../../etc/nginx");

    assert!(matches!(refused, Err(PhpVersionError::Malformed { .. })));
}

#[test]
fn a_three_component_version_is_rejected() {
    // `8.3.2` names no package, no service unit and no pool directory: the
    // repositories the spec fixes publish two-component versions only.
    let refused = PhpVersion::parse("8.3.2");

    assert!(matches!(refused, Err(PhpVersionError::Malformed { .. })));
}

#[test]
fn a_version_with_a_non_numeric_component_is_rejected() {
    assert!(matches!(
        PhpVersion::parse("8.x"),
        Err(PhpVersionError::Malformed { .. })
    ));
}

#[test]
fn an_empty_version_is_rejected() {
    assert_eq!(PhpVersion::parse(""), Err(PhpVersionError::Empty));
}

#[test]
fn an_empty_component_is_rejected() {
    assert!(matches!(
        PhpVersion::parse("8."),
        Err(PhpVersionError::Malformed { .. })
    ));
}

#[test]
fn an_absurdly_long_component_is_rejected() {
    assert_eq!(
        PhpVersion::parse("8.30000000"),
        Err(PhpVersionError::ComponentTooLong)
    );
}
