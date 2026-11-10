//! Tests for [`BoundedText`]: the output bound applied to one command's stdout.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::model::bounded_text::BoundedText;
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;

/// Output within the bound is kept whole and not marked truncated.
#[test]
fn output_within_the_bound_is_kept_whole() {
    let text = BoundedText::of("one\ntwo\n".to_owned());

    assert_eq!(text.text, "one\ntwo\n");
    assert!(!text.truncated);
}

/// Output over the bound is cut back to the last whole line and marked.
#[test]
fn output_over_the_bound_is_cut_to_whole_lines() {
    let line = format!("{}\n", "a".repeat(1023));
    let output = line.repeat(1025);

    let text = BoundedText::of(output);

    assert!(text.truncated);
    assert!(text.text.len() <= InventoryLimits::MAX_OUTPUT_BYTES);
    assert!(text.text.ends_with('\n'));
    assert_eq!(text.text.len() % line.len(), 0);
}

/// Output exactly at the bound is the inverse control: not truncated.
#[test]
fn output_exactly_at_the_bound_is_not_truncated() {
    let output = "a".repeat(InventoryLimits::MAX_OUTPUT_BYTES);

    let text = BoundedText::of(output);

    assert!(!text.truncated);
    assert_eq!(text.text.len(), InventoryLimits::MAX_OUTPUT_BYTES);
}

/// One endless line with no newline is cut at the bound without panicking.
#[test]
fn one_endless_line_is_cut_at_the_bound() {
    let output = "b".repeat(InventoryLimits::MAX_OUTPUT_BYTES + 10);

    let text = BoundedText::of(output);

    assert!(text.truncated);
    assert_eq!(text.text.len(), InventoryLimits::MAX_OUTPUT_BYTES);
}
