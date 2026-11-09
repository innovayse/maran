//! What a log prune request is checked for before it reaches the host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_prune_request;
use crate::proto::{ErrorCode, PruneSiteLogsRequest};

fn request(account: &str, domain: &str, retain: u32) -> PruneSiteLogsRequest {
    PruneSiteLogsRequest {
        account_username: account.to_owned(),
        domain: domain.to_owned(),
        retain_rotations: retain,
    }
}

#[test]
fn every_retention_from_one_to_fourteen_is_accepted() {
    for retain in 1..=14 {
        let input = validated_prune_request(&request("acme", "example.com", retain)).unwrap();

        assert_eq!(input.retain.value(), retain);
        assert_eq!(input.account.as_str(), "acme");
        assert_eq!(input.domain.as_str(), "example.com");
    }
}

#[test]
fn zero_is_refused_because_an_absent_field_would_otherwise_remove_every_archive() {
    let error = validated_prune_request(&request("acme", "example.com", 0))
        .map(|_| ())
        .unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_retention_above_the_installers_policy_is_refused() {
    for retain in [15, 30, u32::MAX] {
        let error = validated_prune_request(&request("acme", "example.com", retain))
            .map(|_| ())
            .unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{retain}");
    }
}

#[test]
fn a_malformed_account_or_domain_is_refused_as_invalid_input() {
    for (account, domain) in [
        ("Not An Account", "example.com"),
        ("acme", "-a.example.com"),
    ] {
        let error = validated_prune_request(&request(account, domain, 3))
            .map(|_| ())
            .unwrap_err();

        assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "{account:?} {domain:?}"
        );
    }
}
