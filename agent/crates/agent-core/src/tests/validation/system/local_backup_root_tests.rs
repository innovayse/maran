//! Tests for the `local_backup_root` module.
//!
//! A backup holds the customer's files AND their database, so the directory it
//! rests in is the most sensitive location this product writes. These tests
//! state the two halves of that separately: what can be answered by reading the
//! operator's string, and the one question — is a component a symlink — that
//! can only be answered by asking the filesystem.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::symlink;

use tempfile::tempdir;

use super::{
    DEFAULT_LOCAL_BACKUP_ROOT, LocalBackupRoot, LocalBackupRootError, resolve_without_symlinks,
};

#[test]
fn the_default_root_parses() {
    let root = LocalBackupRoot::parse(DEFAULT_LOCAL_BACKUP_ROOT).unwrap();
    assert_eq!(root.as_str(), "/var/backups/maran");
}

#[test]
fn a_path_under_home_is_refused() {
    // The refusal that matters most and is least obvious: under a home the
    // directory is writable by the CUSTOMER, so the artifact a later root-side
    // restore reads is an artifact the customer chose.
    for candidate in [
        "/home",
        "/home/acme",
        "/home/acme/backups",
        "/home/.maran-restore/backups",
    ] {
        assert_eq!(
            LocalBackupRoot::parse(candidate),
            Err(LocalBackupRootError::UnderAccountHomes),
            "`{candidate}` puts the artifact where the customer can replace it"
        );
    }
}

#[test]
fn a_path_under_a_site_root_is_refused() {
    for candidate in [
        "/var/www",
        "/var/www/html/backups",
        "/srv/www/backups",
        "/usr/share/nginx/html",
    ] {
        assert_eq!(
            LocalBackupRoot::parse(candidate),
            Err(LocalBackupRootError::UnderSiteRoot),
            "`{candidate}` is one `curl` away from being public"
        );
    }
}

#[test]
fn tmp_and_var_tmp_and_dev_shm_are_refused() {
    for candidate in [
        "/tmp",
        "/tmp/maran",
        "/var/tmp/maran",
        "/dev/shm",
        "/dev/shm/maran",
    ] {
        assert_eq!(
            LocalBackupRoot::parse(candidate),
            Err(LocalBackupRootError::WorldWritableAncestor),
            "`{candidate}` lets another local user pre-plant the name root writes"
        );
    }
}

#[test]
fn a_relative_path_is_refused() {
    for candidate in ["backups", "./backups", "../backups", ""] {
        assert_eq!(
            LocalBackupRoot::parse(candidate),
            Err(LocalBackupRootError::NotAbsolute),
            "`{candidate}` means whatever the process's working directory means"
        );
    }
}

#[test]
fn a_path_that_is_not_spelled_one_way_is_refused() {
    for candidate in [
        "/",
        "/var/backups/",
        "/var//backups",
        "/var/./backups",
        "/var/backups/../backups",
    ] {
        assert_eq!(
            LocalBackupRoot::parse(candidate),
            Err(LocalBackupRootError::NotCanonical),
            "`{candidate}` is a second spelling of a directory that already has one"
        );
    }
}

#[test]
fn a_candidate_longer_than_a_path_is_refused() {
    let candidate = format!("/{}", "a".repeat(4096));
    assert_eq!(
        LocalBackupRoot::parse(&candidate),
        Err(LocalBackupRootError::TooLong {
            maximum: 4096,
            actual: candidate.len(),
        })
    );
}

#[test]
fn a_component_that_is_a_symlink_is_refused() {
    // The string check cannot see this, and this check cannot be done on a
    // string: it is `resolve` asking the filesystem what the path really is.
    // Driven through the injected core, because every directory a test may
    // create is under `/tmp` or under a home — the two places `parse` refuses —
    // so a `LocalBackupRoot` value can never be made to point at one.
    let directory = tempdir().unwrap();
    let real = directory.path().canonicalize().unwrap().join("real");
    let link = directory.path().canonicalize().unwrap().join("link");
    std::fs::create_dir(&real).unwrap();
    symlink(&real, &link).unwrap();

    assert_eq!(resolve_without_symlinks(&real), Ok(real.clone()));
    assert_eq!(
        resolve_without_symlinks(&link),
        Err(LocalBackupRootError::SymlinkComponent)
    );
    assert_eq!(
        resolve_without_symlinks(&real.join("absent")),
        Err(LocalBackupRootError::Unresolvable)
    );
}

#[test]
fn parse_answers_a_string_question_and_resolve_is_the_one_that_asks_the_disk() {
    // The type's contract, stated as a test: a path that does not exist at all
    // still parses, because parsing reads text. Existence, ownership and mode
    // are the inode's answers and are taken at write time.
    let candidate = "/var/backups/maran-that-does-not-exist";
    let root = LocalBackupRoot::parse(candidate).unwrap();
    assert_eq!(root.as_str(), candidate);
    assert_eq!(root.resolve(), Err(LocalBackupRootError::Unresolvable));
}
