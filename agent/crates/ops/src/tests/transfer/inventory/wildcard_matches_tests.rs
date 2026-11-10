//! Tests for [`wildcard_matches`]: matching a file name against an include pattern.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::wildcard_matches::wildcard_matches;

/// A star matches any run, including none.
#[test]
fn a_star_matches_any_run() {
    assert!(wildcard_matches("*", "anything"));
    assert!(wildcard_matches("*.conf", "site.conf"));
    assert!(wildcard_matches("a*c", "ac"));
    assert!(wildcard_matches("a*c", "abbbc"));
}

/// A question mark matches exactly one byte.
#[test]
fn a_question_mark_matches_one_byte() {
    assert!(wildcard_matches("a?c", "abc"));
    assert!(!wildcard_matches("a?c", "ac"));
}

/// A name that does not fit the pattern is the inverse control.
#[test]
fn a_name_that_does_not_fit_is_refused() {
    assert!(!wildcard_matches("*.conf", "site.conf.bak"));
    assert!(!wildcard_matches("site.conf", "site.conf2"));
    assert!(wildcard_matches("site.conf", "site.conf"));
}

/// A literal star in the text is matched by the pattern star.
#[test]
fn a_star_in_the_text_is_matched() {
    assert!(wildcard_matches("*", "a*b"));
}

/// A hostile pattern matches nothing instead of running long.
#[test]
fn a_hostile_pattern_matches_nothing() {
    assert!(!wildcard_matches("*a*a*a*a*b", &"a".repeat(2000)));
    assert!(!wildcard_matches(&"a".repeat(300), &"a".repeat(300)));
}
