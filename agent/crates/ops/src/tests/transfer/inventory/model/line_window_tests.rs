//! Tests for [`LineWindow`]: the line and byte bounds every line parser shares.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::line_window::LineWindow;

/// Ordinary lines pass through without their terminators.
#[test]
fn ordinary_lines_pass_through() {
    let window = LineWindow::of("one\ntwo\r\nthree");

    assert_eq!(window.lines, ["one", "two", "three"]);
    assert_eq!(window.rejected, 0);
    assert!(!window.truncated);
}

/// A line over the length bound is refused and counted, not cut.
#[test]
fn a_huge_line_is_refused_not_cut() {
    let text = format!(
        "ok\n{}\nafter\n",
        "x".repeat(InventoryLimits::MAX_LINE_BYTES + 1)
    );

    let window = LineWindow::of(&text);

    assert_eq!(window.lines, ["ok", "after"]);
    assert_eq!(window.rejected, 1);
}

/// A line of exactly the bound is the inverse control: accepted.
#[test]
fn a_line_of_exactly_the_bound_is_accepted() {
    let text = "y".repeat(InventoryLimits::MAX_LINE_BYTES);

    let window = LineWindow::of(&text);

    assert_eq!(window.lines.len(), 1);
    assert_eq!(window.rejected, 0);
}

/// A line holding a NUL byte is refused and counted.
#[test]
fn a_line_with_a_nul_byte_is_refused() {
    let window = LineWindow::of("fine\nbro\0ken\nalso fine\n");

    assert_eq!(window.lines, ["fine", "also fine"]);
    assert_eq!(window.rejected, 1);
}

/// More lines than the bound stops the window and marks it truncated.
#[test]
fn too_many_lines_truncate_the_window() {
    let text = "a\n".repeat(InventoryLimits::MAX_LINES + 5);

    let window = LineWindow::of(&text);

    assert_eq!(window.lines.len(), InventoryLimits::MAX_LINES);
    assert!(window.truncated);
}

/// Text past the byte bound is not read and marks the window truncated.
#[test]
fn text_past_the_byte_bound_is_not_read() {
    let text = "z\n".repeat(InventoryLimits::MAX_OUTPUT_BYTES);

    let window = LineWindow::of(&text);

    assert!(window.truncated);
    assert!(window.lines.len() <= InventoryLimits::MAX_LINES);
}
