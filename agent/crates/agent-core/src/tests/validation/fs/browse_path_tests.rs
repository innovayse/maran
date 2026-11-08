//! Tests for the path a browse may name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use super::BrowsePath;
use crate::validation::fs::browse_path_error::BrowsePathError;

#[test]
fn the_empty_path_names_the_home() {
    let path = BrowsePath::parse("").unwrap();

    assert!(path.is_home());
    assert!(path.components().is_empty());
    assert_eq!(path.file_name(), "");
    assert_eq!(path.parent(), None);
    assert_eq!(path.as_path(), PathBuf::new());
}

#[test]
fn a_nested_path_splits_into_components_and_has_a_parent() {
    let path = BrowsePath::parse("sites/example.com/public").unwrap();

    assert!(!path.is_home());
    assert_eq!(path.components(), ["sites", "example.com", "public"]);
    assert_eq!(path.file_name(), "public");
    assert_eq!(path.as_path(), PathBuf::from("sites/example.com/public"));
    assert_eq!(
        path.parent().unwrap().components(),
        ["sites", "example.com"]
    );
    assert!(
        BrowsePath::parse("sites")
            .unwrap()
            .parent()
            .unwrap()
            .is_home()
    );
}

#[test]
fn an_absolute_path_is_refused_rather_than_stripped() {
    assert_eq!(
        BrowsePath::parse("/etc").unwrap_err(),
        BrowsePathError::Absolute
    );
    assert_eq!(
        BrowsePath::parse("/").unwrap_err(),
        BrowsePathError::Absolute
    );
}

#[test]
fn traversal_is_refused_wherever_it_appears() {
    for text in ["..", "../etc", "a/../b", "a/..", ".", "./a", "a/./b"] {
        assert_eq!(
            BrowsePath::parse(text).unwrap_err(),
            BrowsePathError::Traversal,
            "{text}"
        );
    }
}

#[test]
fn a_doubled_or_trailing_separator_is_refused() {
    assert_eq!(
        BrowsePath::parse("a//b").unwrap_err(),
        BrowsePathError::EmptyComponent
    );
    assert_eq!(
        BrowsePath::parse("a/").unwrap_err(),
        BrowsePathError::EmptyComponent
    );
}

#[test]
fn a_control_character_is_refused() {
    for text in ["a\0b", "a\nb", "a\rb", "a\x1fb"] {
        assert_eq!(
            BrowsePath::parse(text).unwrap_err(),
            BrowsePathError::ForbiddenCharacter
        );
    }
}

#[test]
fn a_component_over_255_bytes_is_refused_and_255_is_accepted() {
    assert_eq!(
        BrowsePath::parse(&"a".repeat(256)).unwrap_err(),
        BrowsePathError::ComponentTooLong
    );
    assert!(BrowsePath::parse(&"a".repeat(255)).is_ok());
}

#[test]
fn sixty_four_components_are_accepted_and_sixty_five_are_refused() {
    assert!(BrowsePath::parse(&["a"; 64].join("/")).is_ok());
    assert_eq!(
        BrowsePath::parse(&["a"; 65].join("/")).unwrap_err(),
        BrowsePathError::TooDeep
    );
}

#[test]
fn a_path_over_4096_bytes_is_refused_before_it_is_split() {
    // 17 components of 250 bytes is under the depth ceiling but over the byte one.
    let long = vec!["a".repeat(250); 17].join("/");

    assert_eq!(
        BrowsePath::parse(&long).unwrap_err(),
        BrowsePathError::TooLong
    );
    // Inverse control: 16 of them (4015 bytes) are fine.
    assert!(BrowsePath::parse(&vec!["a".repeat(250); 16].join("/")).is_ok());
}

#[test]
fn a_dotfile_is_an_ordinary_component() {
    let path = BrowsePath::parse(".config/app").unwrap();

    assert_eq!(path.components(), [".config", "app"]);
}
