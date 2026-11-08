//! Tests for measuring the sources of an archive.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::symlink;
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;

use super::plan_sources;
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::member_kind::MemberKind;
use crate::files::archive::source_plan::SourcePlan;
use crate::files::open_home::open_home;

fn plan(home: &Path, sources: &[&str], entries: u64) -> Result<SourcePlan, ArchiveRefusal> {
    let uid = current_uid().unwrap();
    let paths: Vec<BrowsePath> = sources
        .iter()
        .map(|s| BrowsePath::parse(s).unwrap())
        .collect();
    plan_sources(
        &open_home(home, uid).unwrap(),
        uid,
        &paths,
        &CreateBounds::parse(entries, 1 << 30).unwrap(),
    )
}

fn listing(plan: &SourcePlan) -> Vec<(String, MemberKind)> {
    plan.entries
        .iter()
        .map(|entry| (entry.path.clone(), entry.kind))
        .collect()
}

#[test]
fn a_directory_is_listed_before_what_it_holds_and_names_are_sorted() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("site/css")).unwrap();
    fs::write(home.path().join("site/b.txt"), b"b").unwrap();
    fs::write(home.path().join("site/a.txt"), b"a").unwrap();
    fs::write(home.path().join("site/css/main.css"), b"c").unwrap();

    let planned = plan(home.path(), &["site"], 100).unwrap();

    let paths: Vec<String> = listing(&planned)
        .into_iter()
        .map(|(path, _)| path)
        .collect();
    assert_eq!(paths[0], "site");
    let position = |path: &str| paths.iter().position(|p| p == path).unwrap();
    assert!(position("site") < position("site/a.txt"));
    assert!(position("site/css") < position("site/css/main.css"));
    assert!(position("site/a.txt") < position("site/b.txt"));
    assert_eq!(paths.len(), 5);
}

#[test]
fn kinds_are_reported_for_files_and_directories() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("d/empty")).unwrap();
    fs::write(home.path().join("d/f"), b"x").unwrap();

    let kinds = listing(&plan(home.path(), &["d"], 100).unwrap());

    assert!(kinds.contains(&("d/empty".to_owned(), MemberKind::Directory)));
    assert!(kinds.contains(&("d/f".to_owned(), MemberKind::File)));
}

#[test]
fn archive_names_start_after_the_common_parent_of_the_sources() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("sites/a/public")).unwrap();
    fs::write(home.path().join("sites/a/public/index.html"), b"x").unwrap();
    fs::write(home.path().join("sites/a/notes.txt"), b"x").unwrap();

    let planned = plan(home.path(), &["sites/a/public", "sites/a/notes.txt"], 100).unwrap();

    let names: Vec<String> = planned
        .entries
        .iter()
        .map(|entry| planned.name_of(entry).unwrap().as_str().to_owned())
        .collect();
    assert_eq!(names, ["public", "public/index.html", "notes.txt"]);
}

#[test]
fn a_symlink_anywhere_in_a_source_refuses_the_whole_request() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("site/deep")).unwrap();
    fs::write(home.path().join("site/ok.txt"), b"x").unwrap();
    symlink("/etc/passwd", home.path().join("site/deep/passwd")).unwrap();

    assert_eq!(
        plan(home.path(), &["site"], 100).unwrap_err(),
        ArchiveRefusal::UnsupportedEntry
    );
}

#[test]
fn a_symlink_named_as_a_source_is_refused_and_not_followed() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::write(elsewhere.path().join("secret"), b"x").unwrap();
    symlink(elsewhere.path(), home.path().join("link")).unwrap();

    assert_eq!(
        plan(home.path(), &["link"], 100).unwrap_err(),
        ArchiveRefusal::UnsupportedEntry
    );
}

#[test]
fn a_fifo_in_a_source_refuses_the_request_without_blocking() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("site")).unwrap();
    let made = std::process::Command::new("mkfifo")
        .arg(home.path().join("site/pipe"))
        .status()
        .unwrap();
    assert!(made.success(), "the test needs mkfifo to build its fixture");

    assert_eq!(
        plan(home.path(), &["site"], 100).unwrap_err(),
        ArchiveRefusal::UnsupportedEntry
    );
}

#[test]
fn a_symlink_standing_at_a_level_above_the_source_is_not_followed() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::write(elsewhere.path().join("a.txt"), b"x").unwrap();
    symlink(elsewhere.path(), home.path().join("up")).unwrap();

    assert_eq!(
        plan(home.path(), &["up/a.txt"], 100).unwrap_err(),
        ArchiveRefusal::DirectoryUnusable
    );
}

#[test]
fn a_name_an_archive_cannot_hold_refuses_the_request() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("site")).unwrap();
    fs::write(home.path().join("site/back\\slash.txt"), b"x").unwrap();

    assert_eq!(
        plan(home.path(), &["site"], 100).unwrap_err(),
        ArchiveRefusal::UnsupportedEntry
    );
}

#[test]
fn more_entries_than_the_cap_refuse_the_request() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("site")).unwrap();
    for name in ["a", "b", "c"] {
        fs::write(home.path().join("site").join(name), b"x").unwrap();
    }

    // The directory and three files are four entries.
    assert!(plan(home.path(), &["site"], 4).is_ok());
    assert_eq!(
        plan(home.path(), &["site"], 3).unwrap_err(),
        ArchiveRefusal::EntryLimit
    );
}

#[test]
fn a_tree_deeper_than_a_name_may_be_refuses_the_request() {
    let home = tempfile::tempdir().unwrap();
    let deep = vec!["d"; 70].join("/");
    fs::create_dir_all(home.path().join(&deep)).unwrap();

    assert_eq!(
        plan(home.path(), &["d"], 1000).unwrap_err(),
        ArchiveRefusal::TooDeep
    );
}

#[test]
fn an_absent_source_is_not_found() {
    let home = tempfile::tempdir().unwrap();

    assert_eq!(
        plan(home.path(), &["nope"], 100).unwrap_err(),
        ArchiveRefusal::NotFound
    );
}

#[test]
fn a_single_file_is_a_plan_of_one() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("notes.txt"), b"x").unwrap();

    let planned = plan(home.path(), &["notes.txt"], 1).unwrap();

    assert_eq!(
        listing(&planned),
        [("notes.txt".to_owned(), MemberKind::File)]
    );
}
