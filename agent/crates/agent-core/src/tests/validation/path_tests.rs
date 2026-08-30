//! Tests for the `path` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `path.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};

use super::{PathError, resolve_under};

/// Builds a temporary home directory and returns it with its owning handle,
/// which must stay alive for as long as the directory is used.
fn temporary_home() -> (tempfile::TempDir, PathBuf) {
    let root = tempfile::tempdir().unwrap();
    let home = root.path().join("home");
    std::fs::create_dir(&home).unwrap();
    (root, home)
}

#[test]
fn directory_inside_the_home_resolves_within_it() {
    let (root, home) = temporary_home();
    std::fs::create_dir(home.join("public_html")).unwrap();

    let resolved = resolve_under(&home, Path::new("public_html")).unwrap();

    assert!(resolved.starts_with(home.canonicalize().unwrap()));
    assert!(resolved.ends_with("public_html"));
    drop(root);
}

#[test]
fn parent_traversal_out_of_the_home_is_rejected() {
    let (root, home) = temporary_home();

    // Either rejection is correct here: the escape target may or may not
    // exist on the host, which decides between NotFound and EscapesHome.
    assert!(resolve_under(&home, Path::new("../../etc/passwd")).is_err());
    drop(root);
}

#[test]
fn symlink_pointing_outside_the_home_is_rejected() {
    let (root, home) = temporary_home();
    let outside = root.path().join("outside");
    std::fs::create_dir(&outside).unwrap();
    std::os::unix::fs::symlink(&outside, home.join("escape")).unwrap();

    assert_eq!(
        resolve_under(&home, Path::new("escape")),
        Err(PathError::EscapesHome)
    );
    drop(root);
}

#[test]
fn path_that_does_not_exist_is_rejected() {
    let (root, home) = temporary_home();

    assert_eq!(
        resolve_under(&home, Path::new("missing")),
        Err(PathError::NotFound)
    );
    drop(root);
}
