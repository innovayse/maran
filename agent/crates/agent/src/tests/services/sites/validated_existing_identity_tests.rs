//! What the cleanup identity check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_existing_identity;
use crate::proto::ErrorCode;

#[test]
fn a_domain_that_is_reserved_today_is_accepted_so_its_site_can_be_wound_down() {
    for reserved in ["203.0.113.7", "localhost", "x.local", "intranet.internal"] {
        let (account, domain) =
            validated_existing_identity("acme", reserved).expect("a legacy name stays reachable");

        assert_eq!(account.as_str(), "acme");
        assert_eq!(domain.as_str(), reserved);
    }
}

#[test]
fn an_ordinary_domain_is_accepted() {
    let (_, domain) = validated_existing_identity("acme", "example.com").expect("accepted");

    assert_eq!(domain.as_str(), "example.com");
}

#[test]
fn a_malformed_domain_is_still_refused_as_invalid_input() {
    for malformed in [
        "",
        "bad_name.example.com",
        "-a.example.com",
        "a\nb.example.com",
    ] {
        let error = validated_existing_identity("acme", malformed)
            .map(|_| ())
            .expect_err("the shape checks still apply in full");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{malformed:?}");
    }
}

#[test]
fn a_malformed_account_is_still_refused() {
    assert!(validated_existing_identity("Not Valid", "example.com").is_err());
}
