//! Tests for the `fanout_recipient` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::FanoutRecipient;

#[test]
fn an_alias_is_keyed_by_its_lower_cased_address() {
    let recipient = FanoutRecipient::parse("Sales@Example.test").unwrap();

    assert!(matches!(recipient, FanoutRecipient::Alias(_)));
    assert_eq!(recipient.to_key(), "sales@example.test");
    assert_eq!(recipient.domain().as_str(), "example.test");
}

#[test]
fn a_catch_all_is_keyed_by_at_and_its_domain() {
    let recipient = FanoutRecipient::parse("@Example.test").unwrap();

    assert!(matches!(recipient, FanoutRecipient::CatchAll(_)));
    assert_eq!(recipient.to_key(), "@example.test");
}

#[test]
fn a_key_that_is_neither_is_refused() {
    for bad in [
        "",
        "@",
        "plain",
        "a@b@c.test",
        "@a@b.test",
        "a b@example.test",
    ] {
        assert!(FanoutRecipient::parse(bad).is_err(), "{bad:?}");
    }
}
