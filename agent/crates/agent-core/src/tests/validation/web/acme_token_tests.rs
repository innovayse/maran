#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;
use crate::validation::web::acme_token_error::AcmeTokenError;

#[test]
fn a_base64url_token_is_accepted() {
    let token = AcmeToken::parse("evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ-PCt92wr-oA").unwrap();
    assert_eq!(
        token.as_str(),
        "evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ-PCt92wr-oA"
    );
}

#[test]
fn an_empty_token_is_refused() {
    assert_eq!(
        AcmeToken::parse(""),
        Err(AcmeTokenError::WrongLength {
            maximum: 128,
            actual: 0
        })
    );
}

#[test]
fn a_token_longer_than_the_ceiling_is_refused() {
    let long = "a".repeat(129);
    assert!(matches!(
        AcmeToken::parse(&long),
        Err(AcmeTokenError::WrongLength { actual: 129, .. })
    ));
    assert!(AcmeToken::parse(&"a".repeat(128)).is_ok());
}

#[test]
fn a_token_with_a_path_separator_or_dot_is_refused() {
    for (candidate, character) in [("../etc", '.'), ("a/b", '/'), ("a\0b", '\0'), ("a+b", '+')] {
        assert_eq!(
            AcmeToken::parse(candidate),
            Err(AcmeTokenError::IllegalCharacter { character })
        );
    }
}
