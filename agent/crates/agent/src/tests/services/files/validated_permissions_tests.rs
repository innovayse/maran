//! Tests for `validated_permissions`: modes are refused when they carry more
//! than permission bits, and a zero directory mode means "the same mode".

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_permissions;
use crate::proto::{ChangePermissionsRequest, ErrorCode};

/// A valid request, for the tests that change one thing.
fn request() -> ChangePermissionsRequest {
    ChangePermissionsRequest {
        account_username: "alice_01".to_owned(),
        path: "sites/uploads".to_owned(),
        mode: 0o644,
        recursive: true,
        directory_mode: 0o755,
    }
}

#[test]
fn a_valid_change_carries_both_modes_and_the_recursive_flag() {
    let input = validated_permissions(&request()).unwrap();

    assert_eq!(input.mode.bits(), 0o644);
    assert_eq!(input.directory_mode.bits(), 0o755);
    assert!(input.recursive);
}

#[test]
fn a_zero_directory_mode_means_the_same_mode_for_directories() {
    let input = validated_permissions(&ChangePermissionsRequest {
        directory_mode: 0,
        ..request()
    })
    .unwrap();

    assert_eq!(input.directory_mode.bits(), 0o644);
}

#[test]
fn a_mode_with_the_setuid_bit_is_refused_not_masked() {
    let refused = validated_permissions(&ChangePermissionsRequest {
        mode: 0o4755,
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_directory_mode_with_the_sticky_bit_is_refused_not_masked() {
    let refused = validated_permissions(&ChangePermissionsRequest {
        directory_mode: 0o1777,
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_permissions(&ChangePermissionsRequest {
        account_username: String::new(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_path_is_invalid_input() {
    let refused = validated_permissions(&ChangePermissionsRequest {
        path: "../x".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
