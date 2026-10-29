//! Tests for [`remove_spool_in`]: only a uid-owned regular file at the account's name is removed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::{MetadataExt, symlink};

use super::remove_spool_in;
use crate::accounts::AccountError;

#[test]
fn an_owned_spool_file_is_removed() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("acme"), b"mail").unwrap();
    let uid = fs::metadata(dir.path()).unwrap().uid();

    remove_spool_in(dir.path().to_str().unwrap(), "acme", uid).unwrap();

    assert!(!dir.path().join("acme").exists());
}

#[test]
fn another_accounts_spool_is_left_alone() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("acme"), b"mail").unwrap();
    fs::write(dir.path().join("other"), b"mail").unwrap();
    let uid = fs::metadata(dir.path()).unwrap().uid();

    remove_spool_in(dir.path().to_str().unwrap(), "acme", uid).unwrap();

    assert!(dir.path().join("other").exists());
}

#[test]
fn a_spool_owned_by_another_uid_is_left_alone() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("acme"), b"mail").unwrap();
    let stranger = fs::metadata(dir.path()).unwrap().uid().wrapping_add(1);

    remove_spool_in(dir.path().to_str().unwrap(), "acme", stranger).unwrap();

    assert!(dir.path().join("acme").exists());
}

#[test]
fn a_missing_spool_is_success() {
    let dir = tempfile::tempdir().unwrap();
    let uid = fs::metadata(dir.path()).unwrap().uid();

    assert!(remove_spool_in(dir.path().to_str().unwrap(), "acme", uid).is_ok());
}

#[test]
fn a_missing_mail_directory_is_skipped() {
    let dir = tempfile::tempdir().unwrap();
    let absent = dir.path().join("absent");

    assert!(remove_spool_in(absent.to_str().unwrap(), "acme", 4242).is_ok());
}

#[test]
fn a_symlink_at_the_name_is_refused_and_its_target_survives() {
    let dir = tempfile::tempdir().unwrap();
    let target = tempfile::tempdir().unwrap();
    fs::write(target.path().join("precious"), b"x").unwrap();
    symlink(target.path().join("precious"), dir.path().join("acme")).unwrap();
    let uid = fs::metadata(dir.path()).unwrap().uid();

    let refusal = remove_spool_in(dir.path().to_str().unwrap(), "acme", uid);

    assert!(
        matches!(refusal, Err(AccountError::MailSpoolRemains { .. })),
        "{refusal:?}"
    );
    assert!(target.path().join("precious").exists());
}

#[test]
fn a_directory_at_the_name_is_refused_and_untouched() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir(dir.path().join("acme")).unwrap();
    fs::write(dir.path().join("acme/inner"), b"x").unwrap();
    let uid = fs::metadata(dir.path()).unwrap().uid();

    let refusal = remove_spool_in(dir.path().to_str().unwrap(), "acme", uid);

    assert!(
        matches!(refusal, Err(AccountError::MailSpoolRemains { .. })),
        "{refusal:?}"
    );
    assert!(dir.path().join("acme/inner").exists());
}

#[test]
fn a_mail_directory_that_is_a_symlink_is_refused_not_followed() {
    let target = tempfile::tempdir().unwrap();
    fs::write(target.path().join("acme"), b"mail").unwrap();
    let holder = tempfile::tempdir().unwrap();
    let link = holder.path().join("mail");
    symlink(target.path(), &link).unwrap();
    let uid = fs::metadata(target.path()).unwrap().uid();

    let refusal = remove_spool_in(link.to_str().unwrap(), "acme", uid);

    assert!(
        matches!(refusal, Err(AccountError::HomeInspection { .. })),
        "{refusal:?}"
    );
    assert!(target.path().join("acme").exists());
}
