//! Tests for [`parse_mysql_listing`]: reading a source's database listing.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::inventory_fixtures::{count_of, kinds_of};
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::parse_mysql_listing::parse_mysql_listing;

/// Tab-separated names and sizes, with a system schema and refusable names
/// (synthetic).
const LISTING: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/mysql_listing.txt");

/// Names and sizes are read; the system schema is skipped and a name with a
/// space is refused without text.
#[test]
fn names_and_sizes_are_read_and_system_schemas_skipped() {
    let parsed = parse_mysql_listing(LISTING);

    let names: Vec<&str> = parsed.items.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(
        names,
        [
            "acme_wp",
            "acme_shop",
            "shopsite_blog",
            "acme_Bad",
            "orphan"
        ]
    );
    assert_eq!(parsed.items[0].size_bytes, Some(1_048_576));
    assert_eq!(parsed.items[1].size_bytes, Some(2_097_152));
    assert_eq!(parsed.items[2].size_bytes, None);
    assert_eq!(kinds_of(&parsed.warnings), [SourceWarningKind::NameRefused]);
    assert_eq!(parsed.warnings[0].subject(), "");
}

/// A fraction that is not zero, a negative figure and text are not sizes.
#[test]
fn unreadable_sizes_are_not_measured() {
    let parsed = parse_mysql_listing("a1\t12.5\nb1\t-4\nc1\tlots\nd1\n e1\t7\n");

    let sizes: Vec<Option<u64>> = parsed.items.iter().map(|d| d.size_bytes).collect();
    assert_eq!(sizes, vec![None; 4]);
}

/// A repeated name keeps its first line.
#[test]
fn a_repeated_name_keeps_its_first_line() {
    let parsed = parse_mysql_listing("a_x\t1\na_x\t2\n");

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(parsed.items[0].size_bytes, Some(1));
}

/// A name over 64 bytes and a name with a quote are refused; one at 64 bytes is
/// the inverse control.
#[test]
fn name_length_and_alphabet_are_held() {
    let long = "a".repeat(65);
    let edge = "a".repeat(64);
    let listing = format!("{long}\t1\n{edge}\t1\nq`uote\t1\nsemi;colon\t1\n");

    let parsed = parse_mysql_listing(&listing);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::NameRefused),
        3
    );
}

/// A NUL byte or an endless line is refused.
#[test]
fn hostile_lines_are_refused() {
    let listing = format!(
        "ok_one\t1\nev\0il\t2\n{}\t3\n",
        "x".repeat(InventoryLimits::MAX_LINE_BYTES + 1)
    );

    let parsed = parse_mysql_listing(&listing);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::NameRefused),
        2
    );
}

/// More lines than the bound stop the read with Truncated, and the databases
/// kept are the ones inside it.
#[test]
fn too_many_databases_truncate() {
    let mut listing = String::new();
    for index in 0..(InventoryLimits::MAX_LINES + 2) {
        listing.push_str(&format!("d{index}\t1\n"));
    }

    let parsed = parse_mysql_listing(&listing);

    assert_eq!(parsed.items.len(), InventoryLimits::MAX_LINES);
    assert_eq!(count_of(&parsed.warnings, SourceWarningKind::Truncated), 1);
}

/// An empty listing yields nothing.
#[test]
fn an_empty_listing_yields_nothing() {
    let parsed = parse_mysql_listing("");

    assert!(parsed.items.is_empty());
    assert!(parsed.warnings.is_empty());
}
