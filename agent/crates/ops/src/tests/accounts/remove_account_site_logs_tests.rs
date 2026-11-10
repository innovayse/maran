//! Tests for [`remove_logs_in`]: the account's log directory goes, and nothing else does.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::symlink;

use super::remove_logs_in;
use crate::accounts::AccountError;

#[test]
fn the_accounts_log_directory_and_every_log_in_it_are_removed() {
    let root = tempfile::tempdir().unwrap();
    let account = root.path().join("acme");
    fs::create_dir(&account).unwrap();
    fs::write(
        account.join("shop.example.access.log"),
        b"203.0.113.9 GET /?sid=1",
    )
    .unwrap();
    fs::write(account.join("shop.example.error.log"), b"error").unwrap();
    fs::write(account.join("shop.example.access.log.2.gz"), b"old").unwrap();

    remove_logs_in(root.path().to_str().unwrap(), "acme").unwrap();

    assert!(!account.exists());
}

#[test]
fn another_accounts_logs_are_left_alone() {
    let root = tempfile::tempdir().unwrap();
    for name in ["acme", "other", "acme2"] {
        fs::create_dir(root.path().join(name)).unwrap();
        fs::write(root.path().join(name).join("a.example.access.log"), b"x").unwrap();
    }

    remove_logs_in(root.path().to_str().unwrap(), "acme").unwrap();

    assert!(!root.path().join("acme").exists());
    assert!(root.path().join("other/a.example.access.log").exists());
    assert!(root.path().join("acme2/a.example.access.log").exists());
}

#[test]
fn an_account_without_a_log_directory_is_success() {
    let root = tempfile::tempdir().unwrap();

    assert!(remove_logs_in(root.path().to_str().unwrap(), "acme").is_ok());
}

#[test]
fn a_missing_log_root_is_skipped() {
    let root = tempfile::tempdir().unwrap();
    let absent = root.path().join("absent");

    assert!(remove_logs_in(absent.to_str().unwrap(), "acme").is_ok());
}

#[test]
fn a_second_removal_converges() {
    let root = tempfile::tempdir().unwrap();
    fs::create_dir(root.path().join("acme")).unwrap();
    fs::write(root.path().join("acme/a.example.access.log"), b"x").unwrap();

    remove_logs_in(root.path().to_str().unwrap(), "acme").unwrap();

    assert!(remove_logs_in(root.path().to_str().unwrap(), "acme").is_ok());
}

#[test]
fn a_symlink_at_the_accounts_name_is_refused_and_its_target_untouched() {
    let root = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::write(elsewhere.path().join("keep.log"), b"keep").unwrap();
    symlink(elsewhere.path(), root.path().join("acme")).unwrap();

    let refusal = remove_logs_in(root.path().to_str().unwrap(), "acme");

    assert!(matches!(refusal, Err(AccountError::SiteLogsRemain { .. })));
    assert!(elsewhere.path().join("keep.log").exists());
}

#[test]
fn a_directory_inside_the_accounts_logs_is_refused_and_nothing_is_removed() {
    let root = tempfile::tempdir().unwrap();
    let account = root.path().join("acme");
    fs::create_dir_all(account.join("nested")).unwrap();
    fs::write(account.join("a.example.access.log"), b"x").unwrap();

    let refusal = remove_logs_in(root.path().to_str().unwrap(), "acme");

    assert!(matches!(refusal, Err(AccountError::SiteLogsRemain { .. })));
    assert!(account.join("a.example.access.log").exists());
    assert!(account.join("nested").exists());
}

#[test]
fn a_symlink_inside_the_accounts_logs_is_refused_and_its_target_untouched() {
    let root = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::write(elsewhere.path().join("keep.log"), b"keep").unwrap();
    let account = root.path().join("acme");
    fs::create_dir(&account).unwrap();
    symlink(
        elsewhere.path().join("keep.log"),
        account.join("a.example.access.log"),
    )
    .unwrap();

    let refusal = remove_logs_in(root.path().to_str().unwrap(), "acme");

    assert!(matches!(refusal, Err(AccountError::SiteLogsRemain { .. })));
    assert!(elsewhere.path().join("keep.log").exists());
}
