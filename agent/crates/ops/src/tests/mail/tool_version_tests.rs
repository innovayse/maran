//! Reading a daemon's version out of what it printed.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::model::tool_version::ToolVersion;

#[test]
fn a_bare_version_is_read_with_its_first_two_numbers() {
    let version = ToolVersion::parse_output("3.8.6\n").unwrap();

    assert_eq!(version.text(), "3.8.6");
    assert_eq!((version.major(), version.minor()), (3, 8));
}

#[test]
fn a_version_followed_by_a_build_is_read_without_the_build() {
    let version = ToolVersion::parse_output("2.3.19.1 (9b53102964)\n").unwrap();

    assert_eq!(version.text(), "2.3.19.1");
    assert_eq!((version.major(), version.minor()), (2, 3));
}

#[test]
fn a_banner_word_before_the_version_is_skipped() {
    let version = ToolVersion::parse_output("Rspamadm 3.8.4\n").unwrap();

    assert_eq!(version.text(), "3.8.4");
}

#[test]
fn a_distribution_suffix_does_not_disturb_the_numbers() {
    let version = ToolVersion::parse_output("2.4.1-4+deb13u1").unwrap();

    assert_eq!((version.major(), version.minor()), (2, 4));
}

#[test]
fn output_with_no_version_in_it_reads_as_nothing() {
    for output in [
        "",
        "\n",
        "command not found",
        "doveadm: Fatal: unknown option",
        "7",
        ".5.1",
        "x.y.z",
    ] {
        assert_eq!(ToolVersion::parse_output(output), None, "{output:?}");
    }
}

#[test]
fn a_word_too_long_to_be_a_version_is_refused() {
    let long = format!("1.{}", "2".repeat(40));

    assert_eq!(ToolVersion::parse_output(&long), None);
}

#[test]
fn a_word_with_a_stray_character_is_refused() {
    assert_eq!(ToolVersion::parse_output("2.3;rm"), None);
}
