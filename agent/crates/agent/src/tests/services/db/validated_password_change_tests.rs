//! Tests for the user and password a `SetDatabasePassword` is rebuilt into.
//!
//! Two properties carry this rpc's whole safety. The user name is rebuilt from
//! the account the panel authorised, so a request cannot re-credential another
//! tenant's login — the prize for getting past that would be a working password
//! on somebody else's data. And the password cannot hold a quote or a
//! backslash, so it cannot break out of the `IDENTIFIED BY '<value>'` literal
//! the operation interpolates it into, under root.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_password_change;
use crate::proto::ErrorCode;

#[test]
fn the_user_is_rebuilt_under_the_account_the_panel_authorised() {
    let (user, _) = validated_password_change("alice", "shop", "Replaced-2026").expect("valid");

    assert_eq!(user.as_str(), "alice_shop");
}

#[test]
fn the_password_survives_the_rebuild_exactly_as_it_was_sent() {
    let (_, password) = validated_password_change("alice", "shop", "Replaced-2026").expect("valid");

    assert_eq!(password.as_str(), "Replaced-2026");
}

#[test]
fn a_suffix_naming_another_tenant_produces_a_name_under_the_callers_own_account() {
    // `bob_admin` cannot smuggle in a second prefix: the separator is outside
    // the suffix alphabet, so this is refused rather than becoming a name that
    // reads as bob's in every grant listing.
    let refused =
        validated_password_change("alice", "bob_admin", "Replaced-2026").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_account_name_the_agent_will_not_accept_is_refused_before_anything_is_built() {
    let refused = validated_password_change("", "shop", "Replaced-2026").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_password_is_refused_rather_than_meaning_leave_it_unchanged() {
    // A silent no-op would report success for a credential that was never
    // rotated, and the customer would be shown a password that does not work.
    let refused = validated_password_change("alice", "shop", "").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_password_carrying_a_quote_is_refused_before_it_reaches_root_sql() {
    // The one that matters: `IDENTIFIED BY '<value>'` takes no placeholder, so a
    // quote that got this far would close the literal inside a root session.
    let refused = validated_password_change("alice", "shop", "a'; DROP DATABASE x; --")
        .expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_password_carrying_a_backslash_is_refused_too() {
    // MySQL's string escape. Refusing the quote alone would leave a value that
    // can re-open the literal a character later.
    let refused =
        validated_password_change("alice", "shop", "back\\slash").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_error_text_never_carries_the_password_that_was_refused() {
    let refused =
        validated_password_change("alice", "shop", "secret'value").expect_err("must refuse");

    assert!(
        !refused.message.contains("secret"),
        "a refusal must name the condition and never the value: {}",
        refused.message
    );
}
