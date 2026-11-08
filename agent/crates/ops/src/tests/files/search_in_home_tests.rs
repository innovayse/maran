//! Tests for the name search inside a home.
//!
//! None needs root: the uid is an argument and a test owns its temporary
//! directory as a customer owns their home. Every bound and every refusal has
//! a positive control beside it, so a search that found nothing, or refused
//! everything, cannot pass. What these cannot observe is a mount point inside a
//! home (it takes root); the real-host suite holds that one.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::os::unix::ffi::OsStrExt;
use std::os::unix::fs::symlink;
use std::path::Path;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::search_query::SearchQuery;
use maran_agent_core::validation::system::name::AccountName;

use super::{search_in_home, search_within};
use crate::files::FilesOpError;
use crate::files::model::search_budget::SearchBudget;
use crate::files::model::search_input::SearchInput;
use crate::files::model::search_outcome::SearchOutcome;

/// A request from `start` for `query` with the given bounds.
fn request(start: &str, query: &str, depth: u32, results: u32, hidden: bool) -> SearchInput {
    SearchInput::new(
        AccountName::parse("alice1").unwrap(),
        BrowsePath::parse(start).unwrap(),
        SearchQuery::parse(query).unwrap(),
        depth,
        results,
        hidden,
    )
}

/// Searches `home` as the current user.
fn search(home: &Path, input: &SearchInput) -> Result<SearchOutcome, FilesOpError> {
    search_in_home(home, input, current_uid().unwrap())
}

/// The matched paths, in order.
fn paths(outcome: &SearchOutcome) -> Vec<&str> {
    outcome
        .matches
        .iter()
        .map(|found| found.path.as_str())
        .collect()
}

/// Creates an empty file, making its parents.
fn touch(home: &Path, relative: &str) {
    let path = home.join(relative);
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    std::fs::write(path, b"x").unwrap();
}

#[test]
fn a_nested_match_is_found_with_its_path_from_the_home() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), "a/b/report.txt");
    touch(home.path(), "a/other.txt");

    let outcome = search(home.path(), &request("", "report", 0, 0, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["a/b/report.txt"]);
    assert_eq!(outcome.matches[0].kind, EntryKind::File);
    assert_eq!(outcome.matches[0].size, 1);
    assert!(!outcome.truncated);
}

#[test]
fn matching_ignores_case_in_the_name_and_in_the_query() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), "README.md");

    let outcome = search(home.path(), &request("", "ReadMe", 0, 0, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["README.md"]);
}

#[test]
fn a_directory_whose_name_matches_is_reported_and_searched() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), "logs/inner.txt");

    let outcome = search(home.path(), &request("", "log", 0, 0, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["logs"]);
    assert_eq!(outcome.matches[0].kind, EntryKind::Directory);
}

#[test]
fn the_search_starts_in_the_directory_asked_for_and_paths_stay_relative_to_the_home() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), "a/target.txt");
    touch(home.path(), "b/target.txt");

    let outcome = search(home.path(), &request("a", "target", 0, 0, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["a/target.txt"]);
}

#[test]
fn depth_one_searches_only_the_start_directory() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), "top.txt");
    touch(home.path(), "d/deep.txt");

    let shallow = search(home.path(), &request("", "txt", 1, 0, false)).unwrap();
    let deeper = search(home.path(), &request("", "txt", 2, 0, false)).unwrap();

    assert_eq!(paths(&shallow), vec!["top.txt"]);
    assert_eq!(paths(&deeper), vec!["top.txt", "d/deep.txt"]);
}

#[test]
fn a_match_below_the_requested_depth_is_not_reported() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), "1/2/3/needle");

    assert!(
        search(home.path(), &request("", "needle", 3, 0, false))
            .unwrap()
            .matches
            .is_empty()
    );
    assert_eq!(
        paths(&search(home.path(), &request("", "needle", 4, 0, false)).unwrap()),
        vec!["1/2/3/needle"]
    );
}

#[test]
fn a_request_for_more_than_sixteen_levels_searches_sixteen() {
    let home = tempfile::tempdir().unwrap();
    let levels = |count: u32| {
        (1..=count)
            .map(|n| n.to_string())
            .collect::<Vec<_>>()
            .join("/")
    };
    // An entry inside N directories is at depth N + 1.
    touch(home.path(), &format!("{}/needle", levels(16)));
    touch(home.path(), &format!("a-{}/needle", levels(15)));

    let outcome = search(home.path(), &request("", "needle", 99, 0, false)).unwrap();

    assert_eq!(
        outcome.matches.len(),
        1,
        "depth 17 is out of reach, depth 16 is not"
    );
    assert!(outcome.matches[0].path.starts_with("a-"));
}

#[test]
fn reaching_the_result_limit_with_more_to_find_sets_truncated() {
    let home = tempfile::tempdir().unwrap();
    for name in ["a1", "a2", "a3"] {
        touch(home.path(), name);
    }

    let outcome = search(home.path(), &request("", "a", 0, 2, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["a1", "a2"]);
    assert!(outcome.truncated);
}

#[test]
fn finding_exactly_the_result_limit_is_not_truncated() {
    let home = tempfile::tempdir().unwrap();
    for name in ["a1", "a2"] {
        touch(home.path(), name);
    }

    let outcome = search(home.path(), &request("", "a", 0, 2, false)).unwrap();

    assert_eq!(outcome.matches.len(), 2);
    assert!(!outcome.truncated);
}

#[test]
fn the_entry_read_budget_stops_the_search_and_marks_it_truncated() {
    let home = tempfile::tempdir().unwrap();
    for n in 0..10 {
        touch(home.path(), &format!("file{n}"));
    }
    let input = request("", "file", 0, 0, false);

    let cut = search_within(
        home.path(),
        &input,
        current_uid().unwrap(),
        SearchBudget::of(4),
    )
    .unwrap();
    let enough = search_within(
        home.path(),
        &input,
        current_uid().unwrap(),
        SearchBudget::of(1_000),
    )
    .unwrap();

    assert!(cut.truncated);
    assert!(
        cut.matches.len() <= 4,
        "no more entries than the budget were read"
    );
    assert!(!cut.matches.is_empty());
    assert!(
        !enough.truncated,
        "a budget that is not reached is not exceeded"
    );
    assert_eq!(enough.matches.len(), 10);
}

#[test]
fn the_budget_counts_names_that_do_not_match() {
    let home = tempfile::tempdir().unwrap();
    for n in 0..10 {
        touch(home.path(), &format!("noise{n}"));
    }
    let input = request("", "needle", 0, 0, false);

    let cut = search_within(
        home.path(),
        &input,
        current_uid().unwrap(),
        SearchBudget::of(5),
    )
    .unwrap();
    let enough = search_within(
        home.path(),
        &input,
        current_uid().unwrap(),
        SearchBudget::of(1_000),
    )
    .unwrap();

    assert!(cut.truncated, "five of ten names read, none matching");
    assert!(cut.matches.is_empty());
    assert!(!enough.truncated);
}

#[test]
fn a_symlink_to_a_directory_is_reported_as_a_link_and_not_entered() {
    let home = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    std::fs::write(outside.path().join("needle-outside"), b"s").unwrap();
    symlink(outside.path(), home.path().join("needle-link")).unwrap();
    touch(home.path(), "real/needle-inside");

    let outcome = search(home.path(), &request("", "needle", 0, 0, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["needle-link", "real/needle-inside"]);
    assert_eq!(outcome.matches[0].kind, EntryKind::Symlink);
    assert_eq!(outcome.matches[1].kind, EntryKind::File);
}

#[test]
fn a_symlink_as_the_starting_directory_is_refused() {
    let home = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    symlink(outside.path(), home.path().join("escape")).unwrap();
    touch(home.path(), "real/file");

    assert_eq!(
        search(home.path(), &request("escape", "f", 0, 0, false)).unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
    assert!(search(home.path(), &request("real", "f", 0, 0, false)).is_ok());
}

#[test]
fn hidden_names_are_searched_and_reported_only_when_asked_for() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), ".secret-note");
    touch(home.path(), ".cache/inner-note");
    touch(home.path(), "plain-note");

    let without = search(home.path(), &request("", "note", 0, 0, false)).unwrap();
    let with = search(home.path(), &request("", "note", 0, 0, true)).unwrap();

    assert_eq!(paths(&without), vec!["plain-note"]);
    assert_eq!(
        paths(&with),
        vec![".secret-note", "plain-note", ".cache/inner-note"]
    );
}

#[test]
fn a_name_that_cannot_be_sent_is_counted_and_not_reported() {
    let home = tempfile::tempdir().unwrap();
    std::fs::write(home.path().join(OsStr::from_bytes(b"bad-\xff-name")), b"x").unwrap();
    std::fs::write(home.path().join("line\nbreak-name"), b"x").unwrap();
    touch(home.path(), "good-name");

    let outcome = search(home.path(), &request("", "name", 0, 0, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["good-name"]);
    assert_eq!(outcome.skipped_entries, 2);
}

#[test]
fn a_fifo_is_reported_as_other_without_being_opened() {
    let home = tempfile::tempdir().unwrap();
    let made = std::process::Command::new("mkfifo")
        .arg(home.path().join("pipe-entry"))
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()
        .unwrap();
    assert!(made.success(), "the test needs mkfifo to build its fixture");

    // Opening the FIFO would block this test forever: it being listed is the
    // proof the search only `lstat`s what it does not descend into.
    let outcome = search(home.path(), &request("", "pipe", 0, 0, false)).unwrap();

    assert_eq!(paths(&outcome), vec!["pipe-entry"]);
    assert_eq!(outcome.matches[0].kind, EntryKind::Other);
}

#[test]
fn a_home_owned_by_another_uid_is_refused_and_its_owner_can_search_it() {
    let home = tempfile::tempdir().unwrap();
    touch(home.path(), "dir/needle-inside");

    // Searched as a uid that owns nothing here: the walk refuses the home.
    let foreign = current_uid().unwrap() + 1;
    let refused = search_in_home(home.path(), &request("", "needle", 0, 0, false), foreign);

    assert_eq!(refused.unwrap_err(), FilesOpError::HomeUnusable);
    // Control: as the owner the same tree is searched.
    assert_eq!(
        paths(&search(home.path(), &request("", "needle", 0, 0, false)).unwrap()),
        vec!["dir/needle-inside"]
    );
}

#[test]
fn an_empty_home_gives_an_empty_untruncated_outcome() {
    let home = tempfile::tempdir().unwrap();

    let outcome = search(home.path(), &request("", "x", 0, 0, false)).unwrap();

    assert_eq!(outcome, SearchOutcome::default());
}
