//! Tests for the byte cap of an upload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::UploadLimit;
use crate::validation::fs::upload_limit_error::UploadLimitError;

#[test]
fn a_cap_that_is_not_stated_is_one_mebibyte() {
    assert_eq!(UploadLimit::parse(0).unwrap().bytes(), 1_048_576);
}

#[test]
fn a_stated_cap_is_carried_as_it_is() {
    assert_eq!(UploadLimit::parse(1).unwrap().bytes(), 1);
    assert_eq!(UploadLimit::parse(50_000_000).unwrap().bytes(), 50_000_000);
}

#[test]
fn the_ceiling_is_two_hundred_fifty_six_mebibytes_and_is_itself_accepted() {
    assert_eq!(
        UploadLimit::parse(268_435_456).unwrap().bytes(),
        268_435_456
    );
}

#[test]
fn one_byte_over_the_ceiling_is_refused_rather_than_clamped() {
    assert_eq!(
        UploadLimit::parse(268_435_457).unwrap_err(),
        UploadLimitError::AboveCeiling
    );
    assert_eq!(
        UploadLimit::parse(u64::MAX).unwrap_err(),
        UploadLimitError::AboveCeiling
    );
}
