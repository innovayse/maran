//! Tests for [`prefix_within`]: cutting a text to a byte bound.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::prefix_within::prefix_within;

/// A text within the bound is returned whole.
#[test]
fn a_text_within_the_bound_is_returned_whole() {
    assert_eq!(prefix_within("abc", 3), "abc");
}

/// A text over the bound is cut to it.
#[test]
fn a_text_over_the_bound_is_cut_to_it() {
    assert_eq!(prefix_within("abcdef", 4), "abcd");
}

/// A cut that would fall inside a multi-byte character backs up to its start.
#[test]
fn a_cut_inside_a_character_backs_up_to_a_boundary() {
    // U+00E9 is two bytes; a bound of 2 lands in the middle of it.
    assert_eq!(prefix_within("a\u{e9}b", 2), "a");
}

/// A bound of zero yields the empty text and never panics.
#[test]
fn a_zero_bound_yields_nothing() {
    assert_eq!(prefix_within("abc", 0), "");
}
