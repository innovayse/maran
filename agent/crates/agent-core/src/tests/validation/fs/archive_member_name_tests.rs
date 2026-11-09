//! Tests for the name an archive member may carry.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::ArchiveMemberName;
use crate::validation::fs::archive_member_name_error::ArchiveMemberNameError;

fn refused(raw: &[u8]) -> ArchiveMemberNameError {
    ArchiveMemberName::parse(raw).unwrap_err()
}

#[test]
fn an_ordinary_nested_name_is_accepted_and_split() {
    let name = ArchiveMemberName::parse(b"site/css/main.css").unwrap();

    assert_eq!(name.as_str(), "site/css/main.css");
    assert_eq!(
        name.components().collect::<Vec<_>>(),
        ["site", "css", "main.css"]
    );
    assert_eq!(name.file_name(), "main.css");
    assert!(!name.had_trailing_separator());
}

#[test]
fn one_trailing_separator_is_accepted_and_remembered() {
    let name = ArchiveMemberName::parse(b"site/css/").unwrap();

    assert_eq!(name.as_str(), "site/css");
    assert_eq!(name.file_name(), "css");
    assert!(name.had_trailing_separator());
}

#[test]
fn a_single_component_is_its_own_file_name() {
    let name = ArchiveMemberName::parse(b"README").unwrap();

    assert_eq!(name.file_name(), "README");
    assert_eq!(name.components().count(), 1);
}

#[test]
fn an_empty_name_is_refused() {
    assert_eq!(refused(b""), ArchiveMemberNameError::Empty);
}

#[test]
fn a_parent_component_is_refused_at_every_position() {
    for raw in [&b"../x"[..], b"a/../x", b"a/..", b"a/b/../../../x"] {
        assert_eq!(refused(raw), ArchiveMemberNameError::Traversal, "{raw:?}");
    }
}

#[test]
fn a_current_directory_component_is_refused() {
    for raw in [&b"./x"[..], b"a/./x", b"."] {
        assert_eq!(refused(raw), ArchiveMemberNameError::Traversal, "{raw:?}");
    }
}

#[test]
fn an_absolute_name_is_refused_rather_than_stripped() {
    assert_eq!(refused(b"/etc/passwd"), ArchiveMemberNameError::Absolute);
    assert_eq!(refused(b"/"), ArchiveMemberNameError::Absolute);
}

#[test]
fn a_backslash_is_refused_so_a_windows_traversal_means_nothing_here() {
    assert_eq!(refused(b"..\\..\\x"), ArchiveMemberNameError::Backslash);
    assert_eq!(refused(b"a\\b"), ArchiveMemberNameError::Backslash);
}

#[test]
fn a_doubled_separator_is_refused() {
    assert_eq!(refused(b"a//b"), ArchiveMemberNameError::EmptyComponent);
    assert_eq!(refused(b"a//"), ArchiveMemberNameError::EmptyComponent);
}

#[test]
fn a_nul_and_other_control_characters_are_refused() {
    for raw in [&b"a\0b"[..], b"a\nb", b"a\x1fb", b"a\x7fb"] {
        assert_eq!(
            refused(raw),
            ArchiveMemberNameError::ForbiddenCharacter,
            "{raw:?}"
        );
    }
}

#[test]
fn a_name_that_is_not_utf8_is_refused() {
    assert_eq!(refused(b"a/\xff\xfe"), ArchiveMemberNameError::NotUtf8);
}

#[test]
fn a_component_over_255_bytes_is_refused_and_255_is_accepted() {
    let longest = "a".repeat(255);
    assert!(ArchiveMemberName::parse(longest.as_bytes()).is_ok());

    let too_long = "a".repeat(256);
    assert_eq!(
        refused(too_long.as_bytes()),
        ArchiveMemberNameError::ComponentTooLong
    );
}

#[test]
fn a_name_over_4096_bytes_is_refused() {
    let raw = vec![b'a'; 4097];

    assert_eq!(refused(&raw), ArchiveMemberNameError::TooLong);
}

#[test]
fn sixty_four_components_are_accepted_and_sixty_five_are_refused() {
    let deep = vec!["d"; 64].join("/");
    assert!(ArchiveMemberName::parse(deep.as_bytes()).is_ok());

    let too_deep = vec!["d"; 65].join("/");
    assert_eq!(
        refused(too_deep.as_bytes()),
        ArchiveMemberNameError::TooDeep
    );
}

#[test]
fn dots_inside_a_component_are_ordinary() {
    assert!(ArchiveMemberName::parse(b"...").is_ok());
    assert!(ArchiveMemberName::parse(b".htaccess").is_ok());
    assert!(ArchiveMemberName::parse(b"a/..b/c..").is_ok());
}
