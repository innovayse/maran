//! Tests for [`parse_crontab`]: counting the entries of one crontab.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::parse_crontab::parse_crontab;

/// A crontab with comments, environment lines and four entries (synthetic).
const BASIC: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/crontab_basic.txt");

/// A crontab of lines that are not entries, and one that is (synthetic).
const HOSTILE: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/crontab_hostile.txt");

/// Comments, blanks and environment settings are not entries.
#[test]
fn comments_blanks_and_environment_lines_are_not_entries() {
    let reading = parse_crontab(BASIC);

    assert_eq!(reading.entry_count, 4);
    assert_eq!(reading.skipped_lines, 0);
    assert!(!reading.truncated);
}

/// Lines that are not entries are skipped and counted, not counted as entries.
#[test]
fn lines_that_are_not_entries_are_skipped_and_counted() {
    let reading = parse_crontab(HOSTILE);

    assert_eq!(reading.entry_count, 1);
    assert_eq!(reading.skipped_lines, 3);
}

/// Inverse control: a command that itself contains an equals sign is still an
/// entry, not an environment setting.
#[test]
fn a_command_with_an_equals_sign_is_an_entry() {
    let reading = parse_crontab("* * * * * FOO=bar /home/acme/run.sh\n");

    assert_eq!(reading.entry_count, 1);
}

/// An empty crontab has no entries.
#[test]
fn an_empty_crontab_has_no_entries() {
    let reading = parse_crontab("");

    assert_eq!(reading.entry_count, 0);
    assert_eq!(reading.skipped_lines, 0);
}

/// A NUL byte and an endless line are skipped, not read.
#[test]
fn hostile_lines_are_skipped() {
    let listing = format!(
        "0 1 * * * /ok\n0 2 * * * /ev\0il\n{}\n",
        "x".repeat(InventoryLimits::MAX_LINE_BYTES + 1)
    );

    let reading = parse_crontab(&listing);

    assert_eq!(reading.entry_count, 1);
    assert_eq!(reading.skipped_lines, 2);
}

/// More lines than the bound set `truncated`.
#[test]
fn too_many_lines_truncate() {
    let listing = "0 1 * * * /x\n".repeat(InventoryLimits::MAX_LINES + 1);

    let reading = parse_crontab(&listing);

    assert!(reading.truncated);
    assert_eq!(reading.entry_count as usize, InventoryLimits::MAX_LINES);
}
