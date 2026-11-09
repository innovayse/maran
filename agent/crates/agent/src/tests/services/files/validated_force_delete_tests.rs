//! Tests for `validated_force_delete`: the request is revalidated from the wire and the home and
//! the trash directory can never be the target.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_force_delete;
use crate::proto::{ErrorCode, ForceDeleteEntryRequest};

/// A valid request, for the tests that change one thing.
fn request() -> ForceDeleteEntryRequest {
    ForceDeleteEntryRequest {
        account_username: "alice_01".to_owned(),
        path: "sites/old".to_owned(),
    }
}

#[test]
fn a_valid_request_is_accepted() {
    let input = validated_force_delete(&request()).unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.path.file_name(), "old");
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_force_delete(&ForceDeleteEntryRequest {
        account_username: "../root".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_path_is_invalid_input() {
    let refused = validated_force_delete(&ForceDeleteEntryRequest {
        path: "../../etc".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_home_and_the_trash_directory_are_invalid_input() {
    for path in ["", ".maran-trash", ".maran-trash/entries/x"] {
        let refused = validated_force_delete(&ForceDeleteEntryRequest {
            path: path.to_owned(),
            ..request()
        })
        .unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    }
}
