//! Tests for the `secret` module.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md); the
//! source file declares this module with `#[path]`, keeping it a child able to
//! reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::Secret;

#[test]
fn debug_prints_a_placeholder_and_never_the_value() {
    let secret = Secret::new("token-123".to_owned());

    assert_eq!(format!("{secret:?}"), "<secret>");
}

#[test]
fn expose_returns_the_wrapped_value() {
    let secret = Secret::new("token-123".to_owned());

    assert_eq!(secret.expose(), "token-123");
}

#[test]
fn a_secret_inside_a_struct_does_not_leak_through_the_derived_debug() {
    // The realistic leak is not `{secret:?}` written on purpose. It is a request struct with
    // `#[derive(Debug)]` reaching a tracing macro, which is how a password ends up in a log
    // that somebody later pastes into an issue. The derived Debug prints each field through
    // the field's own Debug, so this is the case the hand-written impl actually has to cover.
    // The fields are read only through the derived Debug, which dead-code analysis
    // deliberately ignores — and that derived Debug is the whole subject of this test.
    #[expect(dead_code, reason = "read through the derived Debug under test")]
    #[derive(Debug)]
    struct Request {
        user: String,
        password: Secret,
    }

    let printed = format!(
        "{:?}",
        Request {
            user: "acme".to_owned(),
            password: Secret::new("token-123".to_owned()),
        }
    );

    assert!(printed.contains("acme"));
    assert!(!printed.contains("token-123"));
}
