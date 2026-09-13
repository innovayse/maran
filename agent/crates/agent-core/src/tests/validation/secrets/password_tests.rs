//! Tests for the `password` module.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md); the
//! source file declares this module with `#[path]`, keeping it a child able to
//! reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::secrets::password_error::PasswordError;

use super::Password;

#[test]
fn letters_digits_and_the_safe_symbols_are_accepted() {
    let password = Password::parse("Ab3-_.=+xyz").expect("the documented alphabet");

    assert_eq!(password.as_str(), "Ab3-_.=+xyz");
}

#[test]
fn a_quote_is_rejected_because_it_would_close_the_sql_string() {
    assert!(matches!(
        Password::parse("abc'def"),
        Err(PasswordError::UnexpectedCharacter { character: '\'' })
    ));
}

#[test]
fn a_colon_is_rejected_because_it_splits_a_chpasswd_line() {
    assert!(matches!(
        Password::parse("abc:def"),
        Err(PasswordError::UnexpectedCharacter { character: ':' })
    ));
}

#[test]
fn a_newline_is_rejected_because_it_would_forge_a_second_chpasswd_line() {
    assert!(matches!(
        Password::parse("abc\ndef"),
        Err(PasswordError::UnexpectedCharacter { character: '\n' })
    ));
}

#[test]
fn an_empty_password_is_rejected() {
    assert!(matches!(Password::parse(""), Err(PasswordError::Empty)));
}

#[test]
fn a_password_over_the_ceiling_is_rejected() {
    assert!(matches!(
        Password::parse(&"a".repeat(129)),
        Err(PasswordError::TooLong { length: 129 })
    ));
}

#[test]
fn debug_prints_a_placeholder_and_never_the_value() {
    let password = Password::parse("topsecret1").expect("a valid password");

    assert_eq!(format!("{password:?}"), "<password>");
}

#[test]
fn a_backslash_a_backtick_a_quote_and_a_space_are_rejected() {
    // Each would either close or re-open the quoting of `IDENTIFIED BY '<value>'`, or split a
    // `chpasswd` line. Without this the alphabet could be widened by one character and no
    // named test would notice.
    for injecting in ["abc\\def", "abc`def", "abc\"def", "abc def", "abc\tdef"] {
        assert!(
            Password::parse(injecting).is_err(),
            "{injecting:?} must be rejected"
        );
    }
}

#[test]
fn a_password_of_exactly_the_ceiling_is_accepted() {
    // 128 bytes exactly. Without this the `>` in the ceiling could become `>=` unnoticed.
    let candidate = "a".repeat(128);

    assert_eq!(
        Password::parse(&candidate)
            .expect("a valid password")
            .as_str(),
        candidate
    );
}

#[test]
fn a_password_inside_a_struct_does_not_leak_through_the_derived_debug() {
    // The realistic leak is not `{password:?}` written on purpose. It is a request struct with
    // `#[derive(Debug)]` reaching a tracing macro; the derived Debug prints each field through
    // the field's own Debug, so this is the case the hand-written impl actually has to cover.
    // The fields are read only through the derived Debug, which dead-code analysis
    // deliberately ignores — and that derived Debug is the whole subject of this test.
    #[expect(dead_code, reason = "read through the derived Debug under test")]
    #[derive(Debug)]
    struct Request {
        user: String,
        password: Password,
    }

    let printed = format!(
        "{:?}",
        Request {
            user: "acme".to_owned(),
            password: Password::parse("topsecret1").expect("a valid password"),
        }
    );

    assert!(printed.contains("acme"));
    assert!(!printed.contains("topsecret1"));
}
