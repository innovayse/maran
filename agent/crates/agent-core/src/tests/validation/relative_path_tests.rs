//! Tests for the relative path a customer file operation is allowed to name.
//!
//! Every rejection has its own test, because they are the reason the type
//! exists: once a `RelativePath` is constructed, the descriptor walk hands its
//! components straight to `openat`, and anything this constructor lets through
//! is something a syscall is handed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use super::RelativePath;
use crate::validation::relative_path_error::RelativePathError;

/// The challenge path a real ACME issuance asks for.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

#[test]
fn the_acme_challenge_path_is_accepted_and_splits_into_its_components() {
    let path = RelativePath::parse(CHALLENGE).unwrap();

    assert_eq!(path.file_name(), "token123");
    assert_eq!(
        path.parent_components(),
        ["sites", "example.com", ".well-known", "acme-challenge"]
    );
    assert_eq!(path.as_path(), PathBuf::from(CHALLENGE));
    assert_eq!(
        path.parent_as_path(),
        PathBuf::from("sites/example.com/.well-known/acme-challenge")
    );
}

#[test]
fn a_single_component_path_has_no_parent_components_at_all() {
    let path = RelativePath::parse("token123").unwrap();

    assert_eq!(path.file_name(), "token123");
    assert!(path.parent_components().is_empty());
    assert_eq!(path.parent_as_path(), PathBuf::new());
}

#[test]
fn an_empty_path_is_refused() {
    assert_eq!(
        RelativePath::parse("").unwrap_err(),
        RelativePathError::Empty
    );
}

#[test]
fn an_absolute_path_is_refused_rather_than_stripped() {
    assert_eq!(
        RelativePath::parse("/etc/shadow").unwrap_err(),
        RelativePathError::Absolute
    );
}

#[test]
fn a_parent_traversal_is_refused_wherever_it_appears() {
    assert_eq!(
        RelativePath::parse("..").unwrap_err(),
        RelativePathError::Traversal
    );
    assert_eq!(
        RelativePath::parse("../../etc/shadow").unwrap_err(),
        RelativePathError::Traversal
    );
    assert_eq!(
        RelativePath::parse("sites/../../etc/shadow").unwrap_err(),
        RelativePathError::Traversal
    );
    assert_eq!(
        RelativePath::parse("sites/example.com/..").unwrap_err(),
        RelativePathError::Traversal
    );
}

#[test]
fn a_current_directory_component_is_refused_rather_than_normalised_away() {
    assert_eq!(
        RelativePath::parse("./token").unwrap_err(),
        RelativePathError::Traversal
    );
    assert_eq!(
        RelativePath::parse("sites/./token").unwrap_err(),
        RelativePathError::Traversal
    );
}

#[test]
fn a_doubled_or_trailing_separator_is_refused() {
    assert_eq!(
        RelativePath::parse("sites//token").unwrap_err(),
        RelativePathError::EmptyComponent
    );
    assert_eq!(
        RelativePath::parse("sites/token/").unwrap_err(),
        RelativePathError::EmptyComponent
    );
}

#[test]
fn a_nul_byte_is_refused_so_no_name_is_truncated_at_the_c_boundary() {
    assert_eq!(
        RelativePath::parse("token\0.txt").unwrap_err(),
        RelativePathError::ForbiddenCharacter
    );
}

#[test]
fn a_newline_or_another_control_character_is_refused() {
    assert_eq!(
        RelativePath::parse("token\nname").unwrap_err(),
        RelativePathError::ForbiddenCharacter
    );
    assert_eq!(
        RelativePath::parse("token\rname").unwrap_err(),
        RelativePathError::ForbiddenCharacter
    );
}

#[test]
fn a_component_longer_than_a_filesystem_accepts_is_refused() {
    let long = "a".repeat(256);

    assert_eq!(
        RelativePath::parse(&long).unwrap_err(),
        RelativePathError::ComponentTooLong
    );
    assert!(RelativePath::parse(&"a".repeat(255)).is_ok());
}

#[test]
fn a_path_deeper_than_the_agent_will_walk_is_refused() {
    let deep = ["a"; 9].join("/");

    assert_eq!(
        RelativePath::parse(&deep).unwrap_err(),
        RelativePathError::TooDeep
    );
    assert!(RelativePath::parse(&["a"; 8].join("/")).is_ok());
}

#[test]
fn a_dotfile_is_a_perfectly_ordinary_component() {
    let path = RelativePath::parse(".well-known/acme-challenge/token").unwrap();

    assert_eq!(path.parent_components(), [".well-known", "acme-challenge"]);
}
