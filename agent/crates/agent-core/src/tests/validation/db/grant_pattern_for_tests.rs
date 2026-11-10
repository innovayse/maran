//! Tests for [`grant_pattern_for`]: what a database name becomes as a `GRANT` pattern.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::grant_pattern_for;

/// Every separator in a name is escaped, and nothing else is touched.
#[test]
fn every_separator_is_escaped_and_no_other_character_is() {
    assert_eq!(grant_pattern_for("h_stco_main"), "h\\_stco\\_main");
    assert_eq!(grant_pattern_for("alice9shop"), "alice9shop");
}

/// The any-sequence wildcard is escaped as well as the single-character one.
///
/// No `DatabaseName` can hold `%` today; the helper is the one place a name
/// becomes a pattern and must not be correct only for today's alphabet.
#[test]
fn a_percent_sign_is_escaped_like_an_underscore() {
    assert_eq!(grant_pattern_for("a%b_c"), "a\\%b\\_c");
}
