//! Tests for the `s3_bucket` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_LENGTH, MINIMUM_LENGTH, S3Bucket, S3BucketError};

#[test]
fn the_dns_grammar_is_enforced() {
    for candidate in ["maran-backups", "acme.backups.eu", "b1", "12345"]
        .into_iter()
        .filter(|candidate| candidate.len() >= MINIMUM_LENGTH)
    {
        assert_eq!(S3Bucket::parse(candidate).unwrap().as_str(), candidate);
    }

    assert_eq!(
        S3Bucket::parse("-maran"),
        Err(S3BucketError::EdgeNotAlphanumeric)
    );
    assert_eq!(
        S3Bucket::parse("maran-"),
        Err(S3BucketError::EdgeNotAlphanumeric)
    );
    assert_eq!(
        S3Bucket::parse("maran..backups"),
        Err(S3BucketError::EmptyLabel)
    );
    assert_eq!(
        S3Bucket::parse("maran_backups"),
        Err(S3BucketError::IllegalCharacter { character: '_' })
    );
    assert_eq!(
        S3Bucket::parse("192.168.0.1"),
        Err(S3BucketError::LooksLikeIpAddress)
    );
}

#[test]
fn an_uppercase_bucket_is_refused() {
    // Refused rather than lowercased: the bucket the operator typed and the
    // bucket the agent writes to must be the same bucket, and a bucket name is
    // a DNS label that never holds an uppercase letter.
    assert_eq!(
        S3Bucket::parse("Maran-Backups"),
        Err(S3BucketError::IllegalCharacter { character: 'M' })
    );
}

#[test]
fn bounds_are_enforced() {
    let short = "ab";
    assert_eq!(short.len(), MINIMUM_LENGTH - 1);
    assert_eq!(
        S3Bucket::parse(short),
        Err(S3BucketError::WrongLength {
            minimum: MINIMUM_LENGTH,
            maximum: MAXIMUM_LENGTH,
            actual: short.len(),
        })
    );

    let long = "a".repeat(MAXIMUM_LENGTH + 1);
    assert_eq!(
        S3Bucket::parse(&long),
        Err(S3BucketError::WrongLength {
            minimum: MINIMUM_LENGTH,
            maximum: MAXIMUM_LENGTH,
            actual: long.len(),
        })
    );

    assert!(S3Bucket::parse(&"a".repeat(MAXIMUM_LENGTH)).is_ok());
}
