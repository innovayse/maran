//! Tests for the login and key set a `SetSftpAuthorizedKeys` is rebuilt into.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_authorized_keys;
use crate::proto::ErrorCode;

const ED25519: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIG7OpD9/qnELjIQS7Z+hv9E4OBM+3IqncDtZr2x3fz0s";

#[test]
fn the_login_is_rebuilt_under_the_account_and_duplicates_collapse() {
    let keys = vec![format!("{ED25519} a"), format!("{ED25519} b")];

    let (account, user, set) = validated_authorized_keys("alice", "web", &keys).unwrap();

    assert_eq!(account.as_str(), "alice");
    assert_eq!(user.as_str(), "alice_web");
    assert_eq!(set.len(), 1);
}

#[test]
fn a_key_carrying_an_option_refuses_the_whole_request() {
    let keys = vec![ED25519.to_owned(), format!("command=\"id\" {ED25519}")];

    let refused = validated_authorized_keys("alice", "web", &keys).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.starts_with("public key 1:"));
    assert!(
        !refused.message.contains("AAAA"),
        "the key text is never echoed"
    );
}

#[test]
fn more_keys_than_the_ceiling_are_refused_before_any_is_parsed() {
    let keys = vec!["not a key".to_owned(); 33];

    let refused = validated_authorized_keys("alice", "web", &keys).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.contains("ceiling"));
}

#[test]
fn an_empty_list_is_accepted_and_means_no_keys() {
    let (_, _, set) = validated_authorized_keys("alice", "web", &[]).unwrap();

    assert!(set.is_empty());
}
