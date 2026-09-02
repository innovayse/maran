//! Tests for the login and password a `SetSftpPassword` is rebuilt into.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_password_change;
use crate::proto::ErrorCode;

/// A password made of every character class the type allows.
const GOOD_PASSWORD: &str = "Str0ng-pass.word=+_";

#[test]
fn the_login_is_rebuilt_under_the_account_and_the_password_is_carried_through() {
    let (user, password) = validated_password_change("alice", "web", GOOD_PASSWORD).expect("valid");

    assert_eq!(user.as_str(), "alice_web");
    assert_eq!(password.as_str(), GOOD_PASSWORD);
}

#[test]
fn a_password_carrying_a_newline_is_refused_so_it_cannot_add_a_second_chpasswd_line() {
    // A second `user:password` line is a password set for a login the caller
    // does not own — `root:` included.
    let refused =
        validated_password_change("alice", "web", "pw\nroot:owned").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_password_carrying_a_colon_is_refused_so_it_cannot_move_the_field_boundary() {
    let refused = validated_password_change("alice", "web", "pw:extra").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_password_is_refused_rather_than_reported_as_a_rotation_that_happened() {
    let refused = validated_password_change("alice", "web", "").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_refusal_never_echoes_the_password_it_refused() {
    let secret = "pw:leaked";
    let refused = validated_password_change("alice", "web", secret).expect_err("must refuse");

    assert!(
        !refused.message.contains(secret),
        "the message must name the condition, not the value: {}",
        refused.message
    );
}
