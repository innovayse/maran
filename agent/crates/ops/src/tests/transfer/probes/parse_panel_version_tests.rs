//! Tests for [`parse_panel_version`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_panel_version;

#[test]
fn the_version_of_the_release_state_file_is_read() {
    assert_eq!(
        parse_panel_version("VERSION=1.0.0-beta.4\n").as_deref(),
        Some("1.0.0-beta.4")
    );
    assert_eq!(
        parse_panel_version("VERSION=\"1.2.3+build.5\"\n").as_deref(),
        Some("1.2.3+build.5")
    );
}

#[test]
fn a_line_that_is_not_a_version_assignment_is_nothing() {
    for text in [
        "",
        "CHANNEL=stable\n",
        "VERSION=\n",
        "VERSION=\"\"\n",
        "version=1.0\n",
    ] {
        assert_eq!(parse_panel_version(text), None, "{text:?}");
    }
}

#[test]
fn a_version_with_a_character_outside_the_alphabet_or_over_the_bound_is_nothing() {
    for text in [
        "VERSION=1.0 beta\n",
        "VERSION=$(id)\n",
        "VERSION=1.0;rm\n",
        "VERSION=a\u{1b}b\n",
    ] {
        assert_eq!(parse_panel_version(text), None, "{text:?}");
    }
    let longest = "1".repeat(64);
    assert_eq!(
        parse_panel_version(&format!("VERSION={longest}\n")).as_deref(),
        Some(longest.as_str())
    );
    assert_eq!(parse_panel_version(&format!("VERSION={longest}1\n")), None);
}
