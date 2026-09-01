//! Tests for the descriptor walk into a customer's home.
//!
//! None of these needs root, and that is not a compromise. A hosting customer
//! owns their home and everything the walk descends through; a test owns a
//! `tempfile::TempDir` and runs as its own uid, which is the same relationship.
//! `open_parent_directory` takes the home and the uid as arguments precisely so
//! that they can come from `getpwnam_r` in production and from a temporary
//! directory here, with every line below the split identical.
//!
//! Each attack the threat note names has a test of its own: a symlink at a
//! level, a symlink where a level is about to be created, a plain file where a
//! directory should be, a directory belonging to somebody else, and a home that
//! is a symlink.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::{PermissionsExt, symlink};
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::relative_path::RelativePath;

use super::open_parent_directory;
use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;

/// The challenge path every test here walks.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// Walks to the parent of `CHALLENGE`, creating what is missing.
fn creating(home: &Path) -> Result<(), FilesOpError> {
    walk(home, CHALLENGE, MissingParents::Create)
}

/// Walks to the parent of `CHALLENGE`, requiring every level to be there.
fn requiring(home: &Path) -> Result<(), FilesOpError> {
    walk(home, CHALLENGE, MissingParents::Require)
}

/// The walk itself, discarding the descriptor a test does not need.
fn walk(home: &Path, path: &str, missing: MissingParents) -> Result<(), FilesOpError> {
    open_parent_directory(
        home,
        &RelativePath::parse(path).unwrap(),
        current_uid().unwrap(),
        missing,
    )
    .map(|_| ())
}

#[test]
fn a_missing_challenge_directory_is_created_level_by_level() {
    let home = tempfile::tempdir().unwrap();

    creating(home.path()).unwrap();

    assert!(
        home.path()
            .join("sites/example.com/.well-known/acme-challenge")
            .is_dir()
    );
}

#[test]
fn creating_a_chain_that_is_already_there_succeeds_and_changes_nothing() {
    let home = tempfile::tempdir().unwrap();

    creating(home.path()).unwrap();
    creating(home.path()).unwrap();

    assert!(
        home.path()
            .join("sites/example.com/.well-known/acme-challenge")
            .is_dir()
    );
}

#[test]
fn a_symlinked_level_is_refused_rather_than_followed_out_of_the_home() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    symlink(elsewhere.path(), home.path().join("sites")).unwrap();

    let refused = creating(home.path()).unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
    assert!(
        !elsewhere.path().join("example.com").exists(),
        "nothing may be created through a link out of the home"
    );
}

#[test]
fn a_symlink_planted_at_a_level_that_is_about_to_be_created_is_refused() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    std::fs::create_dir(home.path().join("sites")).unwrap();
    // The name `mkdirat` is about to try. It already exists, as a link, so
    // `mkdirat` answers EEXIST — and the whole safety of treating EEXIST as
    // "fine, it was already there" rests on the open that follows refusing it.
    symlink(elsewhere.path(), home.path().join("sites/example.com")).unwrap();

    let refused = creating(home.path()).unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
    assert!(
        !elsewhere.path().join(".well-known").exists(),
        "an existing name is not a directory just because mkdirat said EEXIST"
    );
}

#[test]
fn a_plain_file_where_a_directory_belongs_is_refused() {
    let home = tempfile::tempdir().unwrap();
    std::fs::write(home.path().join("sites"), b"not a directory").unwrap();

    assert_eq!(
        creating(home.path()).unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
}

#[test]
fn a_missing_level_is_refused_when_the_caller_required_it_rather_than_being_created() {
    let home = tempfile::tempdir().unwrap();

    let refused = requiring(home.path()).unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
    assert!(
        !home.path().join("sites").exists(),
        "a walk that requires its levels must never create one"
    );
}

#[test]
fn a_home_that_is_not_there_is_refused_as_an_unusable_home_and_not_as_a_bad_level() {
    let home = tempfile::tempdir().unwrap();
    let absent = home.path().join("no-such-account");

    assert_eq!(creating(&absent).unwrap_err(), FilesOpError::HomeUnusable);
}

#[test]
fn a_home_that_is_a_symlink_is_refused() {
    let parent = tempfile::tempdir().unwrap();
    let real = tempfile::tempdir().unwrap();
    let linked = parent.path().join("acme");
    symlink(real.path(), &linked).unwrap();

    assert_eq!(creating(&linked).unwrap_err(), FilesOpError::HomeUnusable);
}

#[test]
fn a_home_owned_by_somebody_else_is_refused() {
    let home = tempfile::tempdir().unwrap();

    // The uid the walk is told to expect is not the uid that owns the
    // directory, which is the same asymmetry a home belonging to another
    // account produces — and the only way to produce it without root.
    let refused = open_parent_directory(
        home.path(),
        &RelativePath::parse(CHALLENGE).unwrap(),
        current_uid().unwrap() + 1,
        MissingParents::Create,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::HomeUnusable);
}

// There is deliberately NO unit test for "a LEVEL below the home is owned by
// somebody else". It cannot be built without root — a test cannot chown a
// directory to another user, and handing the walk a uid that does not own the
// home makes the home check fire first, so a test written that way would be
// named for the level check and killed by the home check. That is precisely the
// shape of test this plan has been burned by. The level's ownership check is
// covered instead by `tests/privileges_on_a_real_host.rs`, which runs as root in
// the polygon and can create the second account it needs.

#[test]
fn a_single_component_path_needs_no_descent_at_all_and_lands_in_the_home_itself() {
    let home = tempfile::tempdir().unwrap();

    walk(home.path(), "token123", MissingParents::Require).unwrap();
}

#[test]
fn a_plain_file_at_the_last_level_is_refused_by_the_walk_and_not_by_a_later_syscall() {
    let home = tempfile::tempdir().unwrap();
    std::fs::create_dir_all(home.path().join("sites/example.com/.well-known")).unwrap();
    // A plain file where the challenge DIRECTORY belongs. This is the position
    // that makes the refusal observable: at any higher level the walk's next
    // syscall on the opened descriptor returns `ENOTDIR` and produces the same
    // error, so `O_DIRECTORY` and `is_dir()` together are invisible. Here the
    // walk itself must answer, because there is no later syscall inside it.
    std::fs::write(
        home.path()
            .join("sites/example.com/.well-known/acme-challenge"),
        b"not a directory",
    )
    .unwrap();

    let refused = requiring(home.path()).unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
}

#[test]
fn a_created_level_is_traversable_by_the_web_server_whatever_the_daemons_umask_is() {
    let home = tempfile::tempdir().unwrap();

    creating(home.path()).unwrap();

    // Asserted exactly, not as "at least", because the mode is applied with an
    // explicit `fchmod` on the created directory rather than left to `mkdirat`
    // and the umask. A challenge directory the web server cannot traverse is an
    // issuance that fails validation with nothing in any log to explain it.
    for level in [
        "sites",
        "sites/example.com",
        "sites/example.com/.well-known",
        "sites/example.com/.well-known/acme-challenge",
    ] {
        let mode = std::fs::metadata(home.path().join(level))
            .unwrap()
            .permissions()
            .mode();
        assert_eq!(mode & 0o777, 0o755, "the level {level} must be traversable");
    }
}

#[test]
fn a_level_that_was_already_there_keeps_the_mode_it_had() {
    let home = tempfile::tempdir().unwrap();
    std::fs::create_dir(home.path().join("sites")).unwrap();
    std::fs::set_permissions(
        home.path().join("sites"),
        std::fs::Permissions::from_mode(0o700),
    )
    .unwrap();

    creating(home.path()).unwrap();

    // `files.proto` promises it: a directory that already exists is left exactly
    // as it is. Widening one the customer narrowed on purpose would be the agent
    // opening a customer's directory to the world on their behalf.
    assert_eq!(
        std::fs::metadata(home.path().join("sites"))
            .unwrap()
            .permissions()
            .mode()
            & 0o777,
        0o700
    );
}
