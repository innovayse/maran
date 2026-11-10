//! Tests for [`parse_php_versions`]: reading a source's PHP installations.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::inventory_fixtures::kinds_of;
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::parse_php_versions::parse_php_versions;

/// Hand-written listing of both families' directories (synthetic).
const LISTING: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/php_versions.txt");

/// Distinct versions are read in order, from both directory spellings.
#[test]
fn distinct_versions_are_read_from_both_spellings() {
    let parsed = parse_php_versions(LISTING);

    assert_eq!(parsed.items, ["7.4", "8.2", "8.3"]);
    assert!(parsed.warnings.is_empty());
}

/// A directory above the installation cannot supply a version: only the last
/// component is read.
#[test]
fn only_the_last_component_is_read() {
    assert!(
        parse_php_versions("/opt/php8.2/composer\n")
            .items
            .is_empty()
    );
}

/// More versions than the bound stops the read with Truncated.
#[test]
fn too_many_versions_truncate() {
    let mut listing = String::new();
    for major in 1..=9 {
        for minor in 0..=9 {
            listing.push_str(&format!("/etc/php/{major}.{minor}\n"));
        }
    }

    let parsed = parse_php_versions(&listing);

    assert_eq!(parsed.items.len(), InventoryLimits::MAX_PHP_VERSIONS);
    assert_eq!(kinds_of(&parsed.warnings), [SourceWarningKind::Truncated]);
}

/// An empty listing yields nothing and no warning.
#[test]
fn an_empty_listing_yields_nothing() {
    let parsed = parse_php_versions("");

    assert!(parsed.items.is_empty());
    assert!(parsed.warnings.is_empty());
}
