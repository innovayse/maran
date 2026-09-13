//! Tests for the typed input a `CreateDatabase` request becomes.
//!
//! Every field of the result is a validated type, and that is the whole
//! injection defence of the database area: the server's DDL takes no
//! placeholders, so the operation interpolates all three values, and what makes
//! that safe is that none of them can hold a quote, a backtick, a backslash, a
//! semicolon, a space or a newline.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_creation;
use crate::proto::ErrorCode;

/// A password made of every character class the type allows.
const GOOD_PASSWORD: &str = "Str0ng-pass.word=+_";

#[test]
fn the_names_are_rebuilt_under_the_account_and_the_password_is_carried_through() {
    let input = validated_creation("alice", "shop", "reader", GOOD_PASSWORD).expect("valid");

    assert_eq!(input.database.as_str(), "alice_shop");
    assert_eq!(input.user.as_str(), "alice_reader");
    assert_eq!(input.password.as_str(), GOOD_PASSWORD);
}

#[test]
fn a_password_carrying_a_quote_is_refused_so_it_cannot_close_the_identified_by_literal() {
    let refused = validated_creation("alice", "shop", "reader", "pw'; DROP DATABASE mysql; --")
        .expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_refusal_never_echoes_the_password_it_refused() {
    let secret = "pw'injected";
    let refused = validated_creation("alice", "shop", "reader", secret).expect_err("must refuse");

    assert!(
        !refused.message.contains(secret),
        "the message must name the condition, not the value: {}",
        refused.message
    );
    assert!(refused.tool_output.is_empty());
}

#[test]
fn an_empty_password_is_refused_rather_than_creating_a_user_anybody_can_reach() {
    let refused = validated_creation("alice", "shop", "reader", "").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
