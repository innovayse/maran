//! The escape a database-level `GRANT` needs, and reading it back.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{grant_pattern_for, unescaped_grant_pattern};

/// Every separator in a name is escaped, and nothing else is touched.
#[test]
fn every_separator_is_escaped_and_no_other_character_is() {
    assert_eq!(grant_pattern_for("h_stco_main"), "h\\_stco\\_main");
    assert_eq!(grant_pattern_for("alice9shop"), "alice9shop");
}

/// A pattern this panel wrote reads back as the name it was built from.
#[test]
fn an_escaped_pattern_reads_back_as_the_name_it_was_built_from() {
    let name = "h_stco_main";

    assert_eq!(unescaped_grant_pattern(&grant_pattern_for(name)), name);
}

/// A pattern escaping some OTHER character keeps its backslash when read back.
///
/// One of the two signals `repair_grants` refuses an unfamiliar row on: the
/// inverse is partial by design, so a leftover backslash says the stored form was
/// not written here. It is not the only signal — a name escaped in PART loses its
/// backslash and is caught by the re-escape below instead — and both are needed,
/// which is why they are two tests.
#[test]
fn a_pattern_escaping_another_character_keeps_a_backslash_when_read_back() {
    assert!(unescaped_grant_pattern("h\\%stco\\_main").contains('\\'));
}

/// A half-escaped name is not the escape of the name it reads back as.
///
/// The re-escape comparison is what makes the refusal decidable: reading
/// `h_stco\_main` back gives `h_stco_main`, whose escape is `h\_stco\_main` —
/// not the bytes that were stored.
#[test]
fn re_escaping_a_half_escaped_pattern_does_not_reproduce_it() {
    let stored = "h_stco\\_main";

    assert_ne!(grant_pattern_for(&unescaped_grant_pattern(stored)), stored);
}
