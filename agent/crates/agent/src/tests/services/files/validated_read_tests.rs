//! Tests for `validated_read`: a read is revalidated from the wire, and the
//! limit is refused at both ends before a child is forked.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_read;
use crate::proto::{ErrorCode, ReadFileRequest};

/// A request that is valid, for the tests that change one thing.
fn request() -> ReadFileRequest {
    ReadFileRequest {
        account_username: "alice_01".to_owned(),
        path: "sites/index.html".to_owned(),
        max_bytes: 1024,
    }
}

#[test]
fn a_valid_read_is_accepted_with_its_limit() {
    let input = validated_read(&request()).unwrap();

    assert_eq!(input.account().as_str(), "alice_01");
    assert_eq!(input.path().file_name(), "index.html");
    assert_eq!(input.max_bytes(), 1024);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_read(&ReadFileRequest {
        account_username: "../root".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_path_is_invalid_input() {
    let refused = validated_read(&ReadFileRequest {
        path: "../../etc/passwd".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_limit_of_zero_is_invalid_input() {
    let refused = validated_read(&ReadFileRequest {
        max_bytes: 0,
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_limit_above_the_agents_ceiling_is_invalid_input_and_one_at_it_is_accepted() {
    let ceiling = 512 * 1024 * 1024;

    let above = validated_read(&ReadFileRequest {
        max_bytes: ceiling + 1,
        ..request()
    })
    .unwrap_err();
    let at = validated_read(&ReadFileRequest {
        max_bytes: ceiling,
        ..request()
    });

    assert_eq!(above.code, ErrorCode::InvalidInput as i32);
    assert!(at.is_ok(), "inverse control: the ceiling itself is allowed");
}

#[test]
fn a_refusal_carries_no_tool_output() {
    let refused = validated_read(&ReadFileRequest {
        max_bytes: 0,
        ..request()
    })
    .unwrap_err();

    assert!(refused.tool_output.is_empty());
}
