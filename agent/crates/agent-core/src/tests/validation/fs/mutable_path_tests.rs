//! Tests for the path a mutation may name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MutablePath;
use crate::validation::fs::browse_path_error::BrowsePathError;
use crate::validation::fs::mutable_path_error::MutablePathError;

#[test]
fn a_nested_path_splits_into_parents_and_a_name() {
    let path = MutablePath::parse("sites/example.com/index.html").unwrap();

    assert_eq!(path.parent_components(), ["sites", "example.com"]);
    assert_eq!(path.file_name(), "index.html");
}

#[test]
fn an_entry_directly_in_the_home_has_no_parent_components() {
    let path = MutablePath::parse("notes").unwrap();

    assert!(path.parent_components().is_empty());
    assert_eq!(path.file_name(), "notes");
}

#[test]
fn the_home_itself_is_refused() {
    assert_eq!(MutablePath::parse("").unwrap_err(), MutablePathError::Home);
}

#[test]
fn the_trash_directory_is_refused_as_a_first_component() {
    for text in [".maran-trash", ".maran-trash/entries/x"] {
        assert_eq!(
            MutablePath::parse(text).unwrap_err(),
            MutablePathError::Reserved,
            "{text}"
        );
    }
}

#[test]
fn the_trash_name_deeper_in_the_tree_is_an_ordinary_name() {
    assert!(MutablePath::parse("sites/.maran-trash").is_ok());
}

#[test]
fn traversal_and_absolute_paths_are_refused() {
    assert_eq!(
        MutablePath::parse("a/../b").unwrap_err(),
        MutablePathError::Malformed(BrowsePathError::Traversal)
    );
    assert_eq!(
        MutablePath::parse("/etc").unwrap_err(),
        MutablePathError::Malformed(BrowsePathError::Absolute)
    );
    assert_eq!(
        MutablePath::parse("a//b").unwrap_err(),
        MutablePathError::Malformed(BrowsePathError::EmptyComponent)
    );
}

#[test]
fn a_control_character_and_an_over_deep_path_are_refused() {
    assert_eq!(
        MutablePath::parse("a\nb").unwrap_err(),
        MutablePathError::Malformed(BrowsePathError::ForbiddenCharacter)
    );
    let deep = vec!["d"; 65].join("/");
    assert_eq!(
        MutablePath::parse(&deep).unwrap_err(),
        MutablePathError::Malformed(BrowsePathError::TooDeep)
    );
}
