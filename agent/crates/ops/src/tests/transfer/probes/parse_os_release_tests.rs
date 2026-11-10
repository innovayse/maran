//! Tests for [`parse_os_release`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_os_release;

#[test]
fn the_pretty_name_is_read_with_one_pair_of_quotes_removed() {
    assert_eq!(
        parse_os_release("PRETTY_NAME=\"Ubuntu 24.04 LTS\"\n").as_deref(),
        Some("Ubuntu 24.04 LTS")
    );
    assert_eq!(
        parse_os_release("PRETTY_NAME='AlmaLinux 9.4'\n").as_deref(),
        Some("AlmaLinux 9.4")
    );
    assert_eq!(
        parse_os_release("PRETTY_NAME=Plain\n").as_deref(),
        Some("Plain")
    );
}

#[test]
fn a_line_that_is_not_the_pretty_name_is_nothing() {
    for text in [
        "",
        "NAME=\"Ubuntu\"\n",
        "PRETTY_NAME=\n",
        "PRETTY_NAME=\"\"\n",
        " PRETTY_NAME=x\n",
    ] {
        assert_eq!(parse_os_release(text), None, "{text:?}");
    }
}

#[test]
fn a_name_with_a_control_character_or_over_the_bound_is_nothing() {
    assert_eq!(parse_os_release("PRETTY_NAME=\"Evil\u{1b}[2J\"\n"), None);
    assert_eq!(parse_os_release("PRETTY_NAME=\"a\tb\"\n"), None);
    let longest = "x".repeat(128);
    assert_eq!(
        parse_os_release(&format!("PRETTY_NAME={longest}\n")).as_deref(),
        Some(longest.as_str())
    );
    assert_eq!(parse_os_release(&format!("PRETTY_NAME={longest}x\n")), None);
}

#[test]
fn only_the_first_line_is_read() {
    assert_eq!(parse_os_release("NAME=x\nPRETTY_NAME=y\n"), None);
}
