//! Tests for the child body of a listing, against a real temporary directory.
//!
//! Needs no root (see `open_directory_in_home_tests`). What is observed here is
//! what the child would hand the parent: the entries, how they are described,
//! and what is left out.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsString;
use std::fs;
use std::os::unix::ffi::OsStringExt;
use std::os::unix::fs::symlink;
use std::os::unix::net::UnixListener;
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::list_in_home;
use crate::files::FilesOpError;
use crate::files::model::entry_kind::EntryKind;
use crate::files::model::listed_entries::ListedEntries;
use crate::files::model::listing_answer::ListingAnswer;

/// Lists `path` under `home` as the test's own uid.
fn list(home: &Path, path: &str, include_hidden: bool) -> Result<ListingAnswer, FilesOpError> {
    list_in_home(
        home,
        &BrowsePath::parse(path).unwrap(),
        include_hidden,
        current_uid().unwrap(),
    )
}

/// The entries of a successful listing.
fn listed(home: &Path, path: &str, include_hidden: bool) -> ListedEntries {
    match list(home, path, include_hidden).unwrap() {
        ListingAnswer::Entries(listed) => listed,
        ListingAnswer::TooLarge => panic!("the directory was reported too large"),
    }
}

/// The sorted names of a listing.
fn names(listed: &ListedEntries) -> Vec<&str> {
    let mut names: Vec<&str> = listed.entries.iter().map(|e| e.name.as_str()).collect();
    names.sort_unstable();
    names
}

#[test]
fn files_and_directories_are_listed_with_their_own_facts() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("a.txt"), b"hello").unwrap();
    fs::create_dir(home.path().join("sub")).unwrap();

    let result = listed(home.path(), "", true);

    assert_eq!(names(&result), ["a.txt", "sub"]);
    let file = result.entries.iter().find(|e| e.name == "a.txt").unwrap();
    assert_eq!(file.kind, EntryKind::File);
    assert_eq!(file.size, 5);
    assert_eq!(file.uid, current_uid().unwrap());
    assert_eq!(file.link_count, 1);
    assert_eq!(file.mode & !0o7777, 0);
    assert!(file.modified_unix > 0);
    let directory = result.entries.iter().find(|e| e.name == "sub").unwrap();
    assert_eq!(directory.kind, EntryKind::Directory);
    assert_eq!(result.skipped, 0);
}

#[test]
fn a_nested_directory_is_listed() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("sites/example.com")).unwrap();
    fs::write(home.path().join("sites/example.com/index.html"), b"").unwrap();

    assert_eq!(
        names(&listed(home.path(), "sites/example.com", true)),
        ["index.html"]
    );
}

#[test]
fn an_empty_directory_lists_nothing() {
    let home = tempfile::tempdir().unwrap();

    assert!(listed(home.path(), "", true).entries.is_empty());
}

#[test]
fn hidden_entries_are_left_out_unless_asked_for() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join(".env"), b"").unwrap();
    fs::write(home.path().join("visible"), b"").unwrap();

    assert_eq!(names(&listed(home.path(), "", false)), ["visible"]);
    assert_eq!(names(&listed(home.path(), "", true)), [".env", "visible"]);
}

#[test]
fn a_symlink_is_listed_as_a_link_with_its_own_size_and_never_its_targets() {
    let home = tempfile::tempdir().unwrap();
    let target = tempfile::tempdir().unwrap();
    let big = target.path().join("big");
    fs::write(&big, vec![0_u8; 100_000]).unwrap();
    symlink(&big, home.path().join("link")).unwrap();
    symlink(target.path(), home.path().join("dirlink")).unwrap();

    let result = listed(home.path(), "", true);

    let link = result.entries.iter().find(|e| e.name == "link").unwrap();
    assert_eq!(link.kind, EntryKind::Symlink);
    assert_eq!(link.size, big.as_os_str().len() as u64);
    assert_ne!(link.size, 100_000);
    let dirlink = result.entries.iter().find(|e| e.name == "dirlink").unwrap();
    assert_eq!(dirlink.kind, EntryKind::Symlink);
}

#[test]
fn a_socket_is_listed_as_other_without_being_opened() {
    let home = tempfile::tempdir().unwrap();
    let _listener = UnixListener::bind(home.path().join("sock")).unwrap();

    let result = listed(home.path(), "", true);

    assert_eq!(result.entries[0].kind, EntryKind::Other);
}

#[test]
fn a_name_that_is_not_utf8_or_holds_a_control_character_is_skipped_and_counted() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("fine"), b"").unwrap();
    fs::write(
        home.path().join(OsString::from_vec(vec![b'b', 0xff, b'd'])),
        b"",
    )
    .unwrap();
    fs::write(home.path().join("line\nbreak"), b"").unwrap();

    let result = listed(home.path(), "", true);

    assert_eq!(names(&result), ["fine"]);
    assert_eq!(result.skipped, 2);
}

#[test]
fn a_symlinked_directory_in_the_path_is_refused() {
    let home = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    fs::write(outside.path().join("secret"), b"x").unwrap();
    symlink(outside.path(), home.path().join("escape")).unwrap();
    fs::create_dir(home.path().join("real")).unwrap();
    fs::write(home.path().join("real/ok"), b"").unwrap();

    assert_eq!(names(&listed(home.path(), "real", true)), ["ok"]);
    assert_eq!(
        list(home.path(), "escape", true).unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
}

#[test]
fn listing_a_file_instead_of_a_directory_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("file"), b"").unwrap();

    assert_eq!(
        list(home.path(), "file", true).unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
}

#[test]
fn a_directory_of_exactly_the_ceiling_lists_and_one_more_entry_is_too_large() {
    let home = tempfile::tempdir().unwrap();
    for number in 0..100_000 {
        fs::File::create(home.path().join(format!("f{number}"))).unwrap();
    }

    assert_eq!(listed(home.path(), "", true).entries.len(), 100_000);

    fs::File::create(home.path().join("one-too-many")).unwrap();

    assert_eq!(
        list(home.path(), "", true).unwrap(),
        ListingAnswer::TooLarge
    );
}
