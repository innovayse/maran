//! Tests for the `caa_value` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::caa_value::CaaValue;
use super::super::caa_value_error::CaaValueError;

#[test]
fn an_issuer_with_parameters_is_accepted() {
    assert!(CaaValue::parse("letsencrypt.org; accounturi=https://x.example/acct/1").is_ok());
}

#[test]
fn a_quote_a_backslash_and_a_newline_are_refused() {
    for hostile in ["a\"b", "a\\b", "a\nb", "a\0b"] {
        assert!(matches!(
            CaaValue::parse(hostile),
            Err(CaaValueError::IllegalCharacter { .. })
        ));
    }
}

#[test]
fn empty_and_256_bytes_are_refused() {
    assert_eq!(CaaValue::parse(""), Err(CaaValueError::Empty));
    assert!(CaaValue::parse(&"a".repeat(255)).is_ok());
    assert_eq!(
        CaaValue::parse(&"a".repeat(256)),
        Err(CaaValueError::TooLong)
    );
}
