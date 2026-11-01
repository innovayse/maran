#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;
use crate::validation::web::acme_key_authorization_error::AcmeKeyAuthorizationError;

fn token() -> AcmeToken {
    AcmeToken::parse("tok-EN_1").unwrap()
}

#[test]
fn the_token_a_dot_and_a_thumbprint_are_accepted() {
    let value = AcmeKeyAuthorization::parse(
        &token(),
        "tok-EN_1.9jg46WB3rR_AHD-EBXdN7cBkH1WOu0tA3M9fm21mqTI",
    )
    .unwrap();
    assert_eq!(
        value.as_str(),
        "tok-EN_1.9jg46WB3rR_AHD-EBXdN7cBkH1WOu0tA3M9fm21mqTI"
    );
}

#[test]
fn an_answer_for_another_token_is_refused() {
    assert_eq!(
        AcmeKeyAuthorization::parse(&token(), "other.abc"),
        Err(AcmeKeyAuthorizationError::NotForToken)
    );
    assert_eq!(
        AcmeKeyAuthorization::parse(&token(), "tok-EN_1abc"),
        Err(AcmeKeyAuthorizationError::NotForToken)
    );
}

#[test]
fn a_thumbprint_carrying_a_newline_or_nothing_is_refused() {
    for candidate in ["tok-EN_1.", "tok-EN_1.abc\nX-Header: 1", "tok-EN_1.a.b"] {
        assert_eq!(
            AcmeKeyAuthorization::parse(&token(), candidate),
            Err(AcmeKeyAuthorizationError::MalformedThumbprint)
        );
    }
}

#[test]
fn a_key_authorization_longer_than_the_ceiling_is_refused() {
    let long = format!("tok-EN_1.{}", "a".repeat(300));
    assert!(matches!(
        AcmeKeyAuthorization::parse(&token(), &long),
        Err(AcmeKeyAuthorizationError::WrongLength { maximum: 256, .. })
    ));
}
