//! Tests for `validated_copy`: a copy always runs under stated, bounded caps.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_copy;
use crate::proto::{CopyEntryRequest, ErrorCode};

/// A valid request, for the tests that change one thing.
fn request() -> CopyEntryRequest {
    CopyEntryRequest {
        account_username: "alice_01".to_owned(),
        source_path: "sites/a".to_owned(),
        destination_path: "sites/b".to_owned(),
        max_entries: 1000,
        max_bytes: 1024 * 1024,
    }
}

#[test]
fn a_valid_copy_is_accepted() {
    let input = validated_copy(&request()).unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.source.file_name(), "a");
    assert_eq!(input.destination.file_name(), "b");
}

#[test]
fn a_cap_the_request_does_not_state_is_invalid_input() {
    for (max_entries, max_bytes) in [(0, 1024), (1000, 0)] {
        let refused = validated_copy(&CopyEntryRequest {
            max_entries,
            max_bytes,
            ..request()
        })
        .unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn a_cap_above_the_agents_ceiling_is_invalid_input() {
    let refused = validated_copy(&CopyEntryRequest {
        max_entries: u64::MAX,
        max_bytes: u64::MAX,
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_copy(&CopyEntryRequest {
        account_username: "../root".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_destination_is_invalid_input() {
    let refused = validated_copy(&CopyEntryRequest {
        destination_path: "../x".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
