//! Tests for the `s3_object_prefix` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_LENGTH, S3ObjectPrefix, S3ObjectPrefixError};

#[test]
fn the_empty_prefix_is_legal() {
    // A destination that writes at the root of its bucket is an ordinary
    // choice, so the empty prefix is a value and not a refusal.
    let prefix = S3ObjectPrefix::parse("").unwrap();
    assert_eq!(prefix.as_str(), "");
    assert!(prefix.is_empty());
}

#[test]
fn a_traversal_segment_is_refused() {
    // An object key is not a path, but the tools an operator points at a bucket
    // treat it as one, and a `..` segment is how a key climbs out of the prefix
    // an administrator confined an account to.
    for candidate in ["..", "../maran", "maran/../../etc", "maran/.."] {
        assert_eq!(
            S3ObjectPrefix::parse(candidate),
            Err(S3ObjectPrefixError::TraversalSegment),
            "`{candidate}` must not become part of an object key"
        );
    }
}

#[test]
fn a_leading_slash_is_refused() {
    // The key is built as `<prefix>/<account>/<id>.tar.gz`, so a leading slash
    // makes an empty first segment: one bucket, two spellings of one object.
    assert_eq!(
        S3ObjectPrefix::parse("/maran"),
        Err(S3ObjectPrefixError::LeadingSlash)
    );
}

#[test]
fn the_prefix_grammar_is_enforced() {
    for candidate in ["maran", "maran/nightly", "acme_backups-2026"] {
        assert_eq!(
            S3ObjectPrefix::parse(candidate).unwrap().as_str(),
            candidate
        );
    }

    assert_eq!(
        S3ObjectPrefix::parse("maran backups"),
        Err(S3ObjectPrefixError::IllegalCharacter { character: ' ' })
    );
    assert_eq!(
        S3ObjectPrefix::parse("maran/"),
        Err(S3ObjectPrefixError::EmptySegment)
    );
    assert_eq!(
        S3ObjectPrefix::parse("maran//nightly"),
        Err(S3ObjectPrefixError::EmptySegment)
    );

    let long = "a".repeat(MAXIMUM_LENGTH + 1);
    assert_eq!(
        S3ObjectPrefix::parse(&long),
        Err(S3ObjectPrefixError::TooLong {
            maximum: MAXIMUM_LENGTH,
            actual: long.len(),
        })
    );
}
