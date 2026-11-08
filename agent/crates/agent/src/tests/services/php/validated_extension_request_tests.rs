//! Tests for the checks an extension request passes before `ops` is asked.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_extension_request;
use crate::proto::ErrorCode;

#[test]
fn a_supported_version_and_a_catalogue_name_become_their_validated_types() {
    let (version, extension) = validated_extension_request("8.3", "mysql").unwrap();

    assert_eq!(version.as_str(), "8.3");
    assert_eq!(extension.as_str(), "mysql");
}

#[test]
fn a_malformed_version_is_invalid_input() {
    let error = validated_extension_request("8", "gd").unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_name_that_is_empty_or_off_the_catalogue_or_carries_a_control_character_is_invalid_input() {
    for hostile in [
        "",
        "GD",
        "php8.3-gd",
        "gd\n",
        "gd\0",
        "gd --enable",
        "../gd",
    ] {
        let error = validated_extension_request("8.3", hostile).unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{hostile:?}");
        assert!(error.tool_output.is_empty(), "{hostile:?}");
    }
}
