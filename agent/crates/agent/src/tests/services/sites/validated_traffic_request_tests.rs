//! What a traffic summary request is checked for before it reaches the host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_traffic_request;
use crate::proto::{ErrorCode, SummariseSiteTrafficRequest, TrafficCursor};

fn request(
    account: &str,
    domain: &str,
    cursor: Option<TrafficCursor>,
) -> SummariseSiteTrafficRequest {
    SummariseSiteTrafficRequest {
        account_username: account.to_owned(),
        domain: domain.to_owned(),
        cursor,
    }
}

#[test]
fn a_request_without_a_cursor_starts_from_the_beginning() {
    let input = validated_traffic_request(&request("acme", "example.com", None)).unwrap();

    assert_eq!(input.account.as_str(), "acme");
    assert_eq!(input.domain.as_str(), "example.com");
    assert!(input.cursor.is_none());
}

#[test]
fn a_cursor_is_carried_over_unchanged() {
    let cursor = TrafficCursor {
        file_id: "7:42:00ff00ff00ff00ff".to_owned(),
        offset: 1_234,
    };

    let input = validated_traffic_request(&request("acme", "example.com", Some(cursor))).unwrap();

    let carried = input.cursor.unwrap();
    assert_eq!(carried.file_id, "7:42:00ff00ff00ff00ff");
    assert_eq!(carried.offset, 1_234);
}

#[test]
fn a_cursor_identity_over_the_bound_is_refused_as_invalid_input() {
    let cursor = TrafficCursor {
        file_id: "x".repeat(129),
        offset: 0,
    };

    let error = validated_traffic_request(&request("acme", "example.com", Some(cursor)))
        .map(|_| ())
        .unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_cursor_identity_at_the_bound_is_accepted() {
    let cursor = TrafficCursor {
        file_id: "x".repeat(128),
        offset: 0,
    };

    assert!(validated_traffic_request(&request("acme", "example.com", Some(cursor))).is_ok());
}

#[test]
fn a_malformed_account_or_domain_is_refused_as_invalid_input() {
    for (account, domain) in [
        ("Not An Account", "example.com"),
        ("acme", "bad_name.example.com"),
        ("acme", "a\nb.example.com"),
        ("acme", ""),
        ("", "example.com"),
    ] {
        let error = validated_traffic_request(&request(account, domain, None))
            .map(|_| ())
            .unwrap_err();

        assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "{account:?} {domain:?}"
        );
    }
}
