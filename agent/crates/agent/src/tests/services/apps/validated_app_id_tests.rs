//! Tests for the revalidation of an app id.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_app_id;
use crate::proto::ErrorCode;

#[test]
fn a_catalogue_id_is_accepted() {
    assert_eq!(validated_app_id("php-8.3").unwrap().as_id(), "php-8.3");
}

#[test]
fn a_package_name_or_an_option_is_invalid_input() {
    for candidate in ["apache2", "-oDpkg::Pre-Invoke=sh", "", "nginx\n"] {
        let error = validated_app_id(candidate).unwrap_err();
        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{candidate:?}");
    }
}
