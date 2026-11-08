//! Tests for `validated_zone_name`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_zone_name;
use crate::proto::ErrorCode;

#[test]
fn an_ordinary_zone_name_is_accepted() {
    assert_eq!(
        validated_zone_name("Example.com").unwrap().as_str(),
        "example.com"
    );
}

#[test]
fn hostile_and_malformed_names_are_invalid_input() {
    for hostile in [
        "",
        "com",
        "a\nb.com",
        "a/b.com",
        "../x.com",
        "a;b.com",
        "a b.com",
        "$INCLUDE.com",
    ] {
        let refused = validated_zone_name(hostile).unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32, "{hostile:?}");
    }
}
