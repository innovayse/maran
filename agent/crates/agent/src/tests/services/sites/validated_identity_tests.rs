//! What the strict identity check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_identity;
use crate::proto::ErrorCode;

#[test]
fn an_ordinary_account_and_domain_are_accepted() {
    let (account, domain) = validated_identity("acme", "example.com").expect("accepted");

    assert_eq!(account.as_str(), "acme");
    assert_eq!(domain.as_str(), "example.com");
}

#[test]
fn a_reserved_domain_is_refused_as_invalid_input() {
    for reserved in ["203.0.113.7", "localhost", "x.local", "intranet.internal"] {
        let error = validated_identity("acme", reserved)
            .map(|_| ())
            .expect_err("a reserved name must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{reserved}");
    }
}

#[test]
fn a_malformed_domain_is_still_refused() {
    assert!(validated_identity("acme", "bad_name.example.com").is_err());
}
