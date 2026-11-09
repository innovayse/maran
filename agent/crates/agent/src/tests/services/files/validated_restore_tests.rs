//! Tests for `validated_restore`: the trash id is strict and an empty
//! destination means the original place.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_restore;
use crate::proto::{ErrorCode, RestoreTrashEntryRequest};

/// A valid request, for the tests that change one thing.
fn request() -> RestoreTrashEntryRequest {
    RestoreTrashEntryRequest {
        account_username: "alice_01".to_owned(),
        trash_id: "1700000000-0123456789abcdef".to_owned(),
        destination_path: String::new(),
    }
}

#[test]
fn an_empty_destination_restores_to_the_original_place() {
    let input = validated_restore(&request()).unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.id.as_str(), "1700000000-0123456789abcdef");
    assert!(input.destination.is_none());
}

#[test]
fn a_stated_destination_is_carried() {
    let input = validated_restore(&RestoreTrashEntryRequest {
        destination_path: "sites/back".to_owned(),
        ..request()
    })
    .unwrap();

    assert_eq!(input.destination.unwrap().file_name(), "back");
}

#[test]
fn a_trash_id_that_is_a_path_is_invalid_input() {
    for id in ["../x", "a/b", "", "not-an-id", ".."] {
        let refused = validated_restore(&RestoreTrashEntryRequest {
            trash_id: id.to_owned(),
            ..request()
        })
        .unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn a_traversal_destination_is_invalid_input() {
    let refused = validated_restore(&RestoreTrashEntryRequest {
        destination_path: "../x".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_restore(&RestoreTrashEntryRequest {
        account_username: "../root".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
