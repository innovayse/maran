//! Tests for [`parse_vhost_file_list`]: reading the listing of configuration files.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::inventory_fixtures::kinds_of;
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::parse_vhost_file_list::parse_vhost_file_list;

/// Paths are read in order, repeated paths once.
#[test]
fn paths_are_read_in_order_without_repeats() {
    let parsed = parse_vhost_file_list(
        "/etc/nginx/conf.d/a.conf\n/etc/nginx/sites-enabled/b\n/etc/nginx/conf.d/a.conf\n",
    );

    let paths: Vec<&str> = parsed.items.iter().map(|path| path.as_str()).collect();
    assert_eq!(
        paths,
        ["/etc/nginx/conf.d/a.conf", "/etc/nginx/sites-enabled/b"]
    );
    assert!(parsed.warnings.is_empty());
}

/// A path the grammar refuses is never asked for, and costs one warning however
/// many there are; the others are still read.
#[test]
fn refused_paths_cost_one_warning_and_the_rest_is_read() {
    let parsed = parse_vhost_file_list(
        "/etc/nginx/conf.d/ok.conf\n/etc/nginx/with space.conf\nrelative.conf\n/etc/../etc/x\n\"/q\"\n",
    );

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(
        kinds_of(&parsed.warnings),
        [SourceWarningKind::SiteUnreadable]
    );
}

/// Blank lines are ignored without a warning.
#[test]
fn blank_lines_are_ignored() {
    let parsed = parse_vhost_file_list("\n  \n/etc/nginx/conf.d/a.conf\n\n");

    assert_eq!(parsed.items.len(), 1);
    assert!(parsed.warnings.is_empty());
}

/// A NUL byte or an endless line is refused, not read.
#[test]
fn hostile_lines_are_refused() {
    let listing = format!(
        "/etc/nginx/conf.d/ok.conf\n/etc/nginx/a\0b\n/{}\n",
        "a".repeat(InventoryLimits::MAX_LINE_BYTES + 1)
    );

    let parsed = parse_vhost_file_list(&listing);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(
        kinds_of(&parsed.warnings),
        [SourceWarningKind::SiteUnreadable]
    );
}

/// More files than the bound stops the read with Truncated.
#[test]
fn too_many_files_truncate() {
    let mut listing = String::new();
    for index in 0..(InventoryLimits::MAX_VHOST_FILES + 2) {
        listing.push_str(&format!("/etc/nginx/conf.d/{index}.conf\n"));
    }

    let parsed = parse_vhost_file_list(&listing);

    assert_eq!(parsed.items.len(), InventoryLimits::MAX_VHOST_FILES);
    assert_eq!(kinds_of(&parsed.warnings), [SourceWarningKind::Truncated]);
}
