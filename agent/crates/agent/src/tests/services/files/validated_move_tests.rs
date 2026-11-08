//! Tests for `validated_move`: both ends are revalidated from the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_move;
use crate::proto::{ErrorCode, MoveEntryRequest};

/// A valid request, for the tests that change one thing.
fn request() -> MoveEntryRequest {
    MoveEntryRequest {
        account_username: "alice_01".to_owned(),
        source_path: "sites/a.txt".to_owned(),
        destination_path: "sites/b.txt".to_owned(),
    }
}

#[test]
fn a_valid_move_is_accepted() {
    let input = validated_move(&request()).unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.source.file_name(), "a.txt");
    assert_eq!(input.destination.file_name(), "b.txt");
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_move(&MoveEntryRequest {
        account_username: "Alice!".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_source_is_invalid_input() {
    let refused = validated_move(&MoveEntryRequest {
        source_path: "../x".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_destination_is_invalid_input() {
    let refused = validated_move(&MoveEntryRequest {
        destination_path: "sites/../../x".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_trash_directory_cannot_be_a_source_or_a_destination() {
    for (source, destination) in [(".maran-trash", "x"), ("x", ".maran-trash/entries")] {
        let refused = validated_move(&MoveEntryRequest {
            source_path: source.to_owned(),
            destination_path: destination.to_owned(),
            ..request()
        })
        .unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    }
}
