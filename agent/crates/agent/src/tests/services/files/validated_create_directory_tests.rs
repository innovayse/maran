//! Tests for `validated_create_directory`: the request is revalidated from the
//! wire and the mode is refused, never masked.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_create_directory;
use crate::proto::{CreateDirectoryRequest, ErrorCode};

/// A valid request, for the tests that change one thing.
fn request() -> CreateDirectoryRequest {
    CreateDirectoryRequest {
        account_username: "alice_01".to_owned(),
        path: "sites/uploads".to_owned(),
        mode: 0o755,
    }
}

#[test]
fn a_valid_creation_is_accepted() {
    let input = validated_create_directory(&request()).unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.path.file_name(), "uploads");
    assert_eq!(input.mode.bits(), 0o755);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_create_directory(&CreateDirectoryRequest {
        account_username: "../root".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_path_is_invalid_input() {
    let refused = validated_create_directory(&CreateDirectoryRequest {
        path: "../../etc".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_home_itself_is_invalid_input() {
    let refused = validated_create_directory(&CreateDirectoryRequest {
        path: String::new(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_mode_with_the_setuid_bit_is_refused_not_masked() {
    let refused = validated_create_directory(&CreateDirectoryRequest {
        mode: 0o4755,
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
