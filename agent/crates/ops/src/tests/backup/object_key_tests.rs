//! How a backup's key in a destination is spelled, and from what.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// The account these tests build keys for.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The backup id these tests build keys for.
fn backup_id() -> BackupId {
    BackupId::parse("0f4b8e1a-7c2d-4f5e-9a3b-6d8c1e2f0a9b").expect("the fixture id is valid")
}

/// A prefix, parsed.
fn prefix(candidate: &str) -> S3ObjectPrefix {
    S3ObjectPrefix::parse(candidate).expect("the fixture prefix is valid")
}

/// The key is exactly the prefix, the account and the id — three parts, from
/// three validated values, and no fourth thing.
///
/// The assertion is written as a whole-string equality rather than as three
/// `contains` checks on purpose: `contains` passes for a key that also carries
/// something else, and something else in a key is a write aimed somewhere this
/// product did not choose.
#[test]
fn a_key_is_built_from_the_prefix_the_account_and_the_id_and_nothing_else() {
    let key = object_key(&prefix("maran/backups"), &account(), &backup_id());

    assert_eq!(
        key,
        "maran/backups/alice/0f4b8e1a-7c2d-4f5e-9a3b-6d8c1e2f0a9b.tar.gz"
    );

    // And, stated as provenance rather than as spelling: strip the three parts
    // and the suffix, and there is nothing left. A key that grew a fourth
    // segment would leave one behind.
    let remainder = key
        .replacen("maran/backups", "", 1)
        .replacen("alice", "", 1)
        .replacen("0f4b8e1a-7c2d-4f5e-9a3b-6d8c1e2f0a9b", "", 1)
        .replacen(".tar.gz", "", 1);
    assert_eq!(remainder, "//", "left over: {remainder:?}");
}

/// An empty prefix is legal, and produces a key with no leading separator.
///
/// A key beginning with `/` names an object whose first path segment is empty,
/// which is a different object from the one every other part of this product
/// expects — and one a listing under `alice/` would never find.
#[test]
fn the_empty_prefix_produces_a_key_with_no_leading_separator() {
    let key = object_key(&prefix(""), &account(), &backup_id());

    assert_eq!(key, "alice/0f4b8e1a-7c2d-4f5e-9a3b-6d8c1e2f0a9b.tar.gz");
    assert!(!key.starts_with('/'));
}

/// The listing prefix and the keys agree, because one function builds both.
///
/// The failure this pins is silent: a listing prefix that drifted from the key
/// builder returns nothing, and retention reads "nothing" as "this account has
/// no backups to prune".
#[test]
fn every_key_starts_with_the_prefix_a_listing_asks_for() {
    for configured in ["", "maran/backups", "b"] {
        let parsed = prefix(configured);
        let listing = account_key_prefix(&parsed, &account());

        let key = object_key(&parsed, &account(), &backup_id());

        assert!(
            key.starts_with(&listing),
            "{key} is not under {listing} (prefix {configured:?})"
        );
        assert!(listing.ends_with('/'), "{listing}");
        assert!(!listing.contains("//"), "{listing}");
    }
}
