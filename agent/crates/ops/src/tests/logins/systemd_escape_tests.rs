//! The rule that turns a mount point into the one unit name systemd accepts —
//! and the four inputs it is deliberately not the rule for.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::systemd_escape;

/// A path separator becomes `-`, and the leading one is dropped.
#[test]
fn a_path_separator_becomes_a_dash_and_the_leading_one_is_dropped() {
    assert_eq!(systemd_escape("/var/lib/x/home"), "var-lib-x-home");
}

/// Anything systemd would escape is escaped, not passed through.
#[test]
fn a_character_systemd_escapes_becomes_its_hexadecimal_form() {
    // No path either jail builds contains one today. The general loop is kept
    // anyway, so a widened account alphabet still derives the name systemd
    // would: all three of these were compared against `systemd-escape --path`
    // and agree.
    assert_eq!(systemd_escape("/var/lib/a b"), "var-lib-a\\x20b");
    assert_eq!(systemd_escape("/var/lib/a-b"), "var-lib-a\\x2db");
    assert_eq!(systemd_escape("/var/lib/a.b_c"), "var-lib-a.b_c");
}

/// The two jail roots escape to what systemd derives from their mount points.
#[test]
fn both_jail_roots_escape_to_the_names_systemd_derives_from_them() {
    // The `-` inside `maran-sftp` and `maran-ftps` is NOT a path separator, so
    // it escapes to `\x2d` while the separators around it become `-`. The two
    // must agree because they come from one rule; asserting them side by side
    // is what would go red if a second copy of the rule ever appeared. Both
    // rows are `systemd-escape --path`'s own output for those paths.
    assert_eq!(
        systemd_escape("/var/lib/maran-sftp/alice/home"),
        "var-lib-maran\\x2dsftp-alice-home"
    );
    assert_eq!(
        systemd_escape("/var/lib/maran-ftps/alice/home"),
        "var-lib-maran\\x2dftps-alice-home"
    );
}

/// Every byte outside the kept alphabet gets the same two-digit hex form.
#[test]
fn every_byte_outside_the_kept_alphabet_is_written_as_two_hex_digits() {
    // The boundary the rule exists for: a newline, a NUL and a high byte are
    // all names a unit file could otherwise be given, and systemd would derive
    // a different one from the same `Where=`.
    assert_eq!(systemd_escape("/a\nb"), "a\\x0ab");
    assert_eq!(systemd_escape("/a\0b"), "a\\x00b");
    assert_eq!(systemd_escape("/a\u{7f}b"), "a\\x7fb");
    // Multi-byte UTF-8 escapes per byte, as systemd does — `é` is 0xc3 0xa9.
    assert_eq!(systemd_escape("/é"), "\\xc3\\xa9");
}

/// The four inputs where this rule and `systemd-escape --path` disagree, pinned
/// as the restriction they are.
///
/// Measured against the real tool, not read out of its documentation. None can
/// reach here: both callers build `<literal root>/<AccountName>[/home]`, so the
/// path is absolute and non-empty, its separators are single and never
/// trailing, it holds no `:`, and its first component is `var`.
///
/// The assertions are on THIS rule's answers, and each names systemd's beside
/// it. That is deliberate: a later change that made any of them match the tool
/// would be a widening of the contract, and it should arrive as a red test and
/// a rewritten doc comment rather than as a silent improvement — the four rows
/// on `systemd_escape` are the reason a caller may not pass a free-form path.
#[test]
fn the_four_inputs_this_rule_is_not_for_are_pinned_rather_than_claimed_to_agree() {
    // systemd answers `-`; the root path is not a jail path.
    assert_eq!(systemd_escape("/"), "");
    // systemd keeps `:`; no account name can contain one.
    assert_eq!(systemd_escape("/a:b/c"), "a\\x3ab-c");
    // systemd escapes a LEADING dot; every path here begins with `var`.
    assert_eq!(systemd_escape("/.hidden/x"), ".hidden-x");
    // systemd collapses repeated separators and drops a trailing one; these
    // paths are built by `format!` from a literal root and one name.
    assert_eq!(systemd_escape("//a//b/"), "a--b-");
}
