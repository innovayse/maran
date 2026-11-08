//! What the override revalidation accepts and refuses as a SET.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_overrides;
use crate::proto::{ErrorCode, PhpSetting};

/// A setting as it arrives on the wire.
fn setting(name: &str, value: &str) -> PhpSetting {
    PhpSetting {
        name: name.to_owned(),
        value: value.to_owned(),
    }
}

#[test]
fn a_post_size_at_least_the_upload_size_is_accepted() {
    let accepted = validated_overrides(&[
        setting("upload_max_filesize", "64M"),
        setting("post_max_size", "64M"),
    ])
    .unwrap();

    assert_eq!(accepted.len(), 2);
}

#[test]
fn a_post_size_below_the_upload_size_is_refused_as_invalid_input() {
    let refused = validated_overrides(&[
        setting("upload_max_filesize", "64M"),
        setting("post_max_size", "16M"),
    ])
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_single_bad_setting_is_still_refused_before_the_set_is_compared() {
    let refused = validated_overrides(&[setting("open_basedir", "/")]).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
