//! Tests for where an archive name begins.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::common_parent_length;

fn length(sources: &[&str]) -> usize {
    let paths: Vec<BrowsePath> = sources
        .iter()
        .map(|s| BrowsePath::parse(s).unwrap())
        .collect();
    common_parent_length(&paths)
}

#[test]
fn a_single_source_keeps_its_own_name() {
    assert_eq!(length(&["sites/a"]), "sites/".len());
    assert_eq!(length(&["a"]), 0);
}

#[test]
fn siblings_share_their_parent() {
    assert_eq!(
        length(&["sites/a/public", "sites/a/notes.txt"]),
        "sites/a/".len()
    );
}

#[test]
fn sources_in_different_places_share_only_what_they_share() {
    assert_eq!(length(&["sites/a/x", "sites/b/y"]), "sites/".len());
    assert_eq!(length(&["sites/a", "docs"]), 0);
}

#[test]
fn a_shared_prefix_of_letters_is_not_a_shared_directory() {
    assert_eq!(length(&["site/x", "site2/y"]), 0);
}
