//! What a `SourceEndpoint` must carry to be served.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;
use crate::proto::{ErrorCode, SourceFailureReason};

fn endpoint(host: &str, port: u32, user: &str) -> Option<proto::SourceEndpoint> {
    Some(proto::SourceEndpoint {
        host: host.to_owned(),
        port,
        user: user.to_owned(),
    })
}

#[test]
fn a_well_formed_endpoint_yields_the_host_the_port_and_the_login() {
    for host in ["example.com", "10.0.0.5", "192.168.1.20", "2001:db8::1"] {
        let accepted = validated_source_endpoint(endpoint(host, 2222, "migrator")).unwrap();

        assert_eq!(accepted.host.as_str(), host);
        assert_eq!(accepted.port.value(), 2222);
        assert_eq!(accepted.user.as_str(), "migrator");
    }
}

#[test]
fn an_absent_endpoint_is_invalid_input_with_no_connection_reason() {
    let failure = validated_source_endpoint(None).unwrap_err();

    assert_eq!(failure.error.code, ErrorCode::InvalidInput as i32);
    assert_eq!(failure.reason, SourceFailureReason::Unspecified);
}

#[test]
fn a_forbidden_address_is_invalid_input_with_the_address_refused_reason() {
    for host in [
        "127.0.0.1",
        "::1",
        "169.254.169.254",
        "0.0.0.0",
        "224.0.0.1",
        "localhost",
    ] {
        let failure = validated_source_endpoint(endpoint(host, 22, "root")).unwrap_err();

        assert_eq!(failure.error.code, ErrorCode::InvalidInput as i32, "{host}");
        assert_eq!(
            failure.reason,
            SourceFailureReason::AddressRefused,
            "{host}"
        );
    }
}

#[test]
fn text_that_is_not_a_host_is_invalid_input_and_not_an_address_refusal() {
    for host in [
        "",
        "-oProxyCommand=x",
        "ssh://example.com",
        "a b",
        "example.com:22",
        "999.1.1.1",
    ] {
        let failure = validated_source_endpoint(endpoint(host, 22, "root")).unwrap_err();

        assert_eq!(
            failure.error.code,
            ErrorCode::InvalidInput as i32,
            "{host:?}"
        );
        assert_eq!(failure.reason, SourceFailureReason::Unspecified, "{host:?}");
    }
}

#[test]
fn a_port_outside_the_range_and_a_login_outside_the_grammar_are_invalid_input() {
    for case in [
        endpoint("example.com", 0, "root"),
        endpoint("example.com", 65_536, "root"),
        endpoint("example.com", 22, ""),
        endpoint("example.com", 22, "-oProxyCommand=x"),
        endpoint("example.com", 22, "Root"),
        endpoint("example.com", 22, "a b"),
    ] {
        let failure = validated_source_endpoint(case).unwrap_err();

        assert_eq!(failure.error.code, ErrorCode::InvalidInput as i32);
        assert_eq!(failure.reason, SourceFailureReason::Unspecified);
    }
}
