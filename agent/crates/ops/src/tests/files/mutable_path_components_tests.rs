//! The components of a mutable path, parents first.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::mutable_path_components;
use crate::files::home_fixture::mutable;

#[test]
fn a_nested_path_lists_its_parents_and_then_its_name() {
    let path = mutable("a/b/c.txt");

    assert_eq!(mutable_path_components(&path), vec!["a", "b", "c.txt"]);
}

#[test]
fn a_path_with_no_parents_is_its_name_alone() {
    let path = mutable("notes.txt");

    assert_eq!(mutable_path_components(&path), vec!["notes.txt"]);
}
