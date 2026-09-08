//! Tests for the `s3_region` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_LENGTH, S3Region, S3RegionError};

#[test]
fn the_region_grammar_is_enforced() {
    for candidate in ["us-east-1", "eu-central-1", "auto", "fra1"] {
        assert_eq!(S3Region::parse(candidate).unwrap().as_str(), candidate);
    }

    assert_eq!(S3Region::parse(""), Err(S3RegionError::Empty));
    assert_eq!(
        S3Region::parse("US-EAST-1"),
        Err(S3RegionError::IllegalCharacter { character: 'U' })
    );
    assert_eq!(
        S3Region::parse("us_east_1"),
        Err(S3RegionError::IllegalCharacter { character: '_' })
    );
    assert_eq!(
        S3Region::parse("us east 1"),
        Err(S3RegionError::IllegalCharacter { character: ' ' })
    );

    let long = "a".repeat(MAXIMUM_LENGTH + 1);
    assert_eq!(
        S3Region::parse(&long),
        Err(S3RegionError::TooLong {
            maximum: MAXIMUM_LENGTH,
            actual: long.len(),
        })
    );
}
