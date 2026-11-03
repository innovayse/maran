//! Which key files the start-up sweep takes away, and which it must not.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sftp::fake_sftp_host::{FakeSftpHost, distro};
use crate::sftp::reconcile_orphan_key_files::reconcile_orphan_key_files;
use crate::sftp::sftp_error::SftpError;

const DIRECTORY: &str = "/etc/ssh/maran-sftp-keys";

fn key(name: &str) -> String {
    format!("{DIRECTORY}/{name}")
}

#[test]
fn a_key_file_whose_login_is_gone_is_removed_under_both_names() {
    let host = FakeSftpHost::new()
        .with_path(&key("alice_gone"))
        .with_path(&key("alice_gone.suspended"));

    let removed = reconcile_orphan_key_files(&host, distro()).unwrap();

    assert_eq!(removed, 2);
    assert!(host.paths().is_empty());
}

#[test]
fn a_key_file_whose_login_exists_is_kept_under_both_names() {
    // The inverse control: a sweep that removed everything would pass every test
    // that only ever hands it orphans.
    let host = FakeSftpHost::with_existing("alice_web")
        .with_path(&key("alice_web"))
        .with_path(&key("alice_web.suspended"));

    let removed = reconcile_orphan_key_files(&host, distro()).unwrap();

    assert_eq!(removed, 0);
    assert_eq!(host.paths().len(), 2);
}

#[test]
fn only_the_orphans_among_a_mixed_directory_are_removed() {
    let host = FakeSftpHost::with_existing("alice_web")
        .with_path(&key("alice_web"))
        .with_path(&key("bob_gone"));

    let removed = reconcile_orphan_key_files(&host, distro()).unwrap();

    assert_eq!(removed, 1);
    assert_eq!(host.paths(), vec![key("alice_web")]);
}

#[test]
fn a_file_that_is_not_the_shape_of_a_logins_key_file_is_left_alone() {
    let host = FakeSftpHost::new()
        .with_path(&key("README"))
        .with_path(&key(".tmpAb12Cd"))
        .with_path(&key("alice_web.bak"));

    let removed = reconcile_orphan_key_files(&host, distro()).unwrap();

    assert_eq!(removed, 0);
    assert_eq!(host.paths().len(), 3);
}

#[test]
fn a_login_of_another_account_still_keeps_its_file() {
    // Existence is the question, not ownership: the sweep has no account.
    let host = FakeSftpHost::with_existing("bob_deploy").with_path(&key("bob_deploy"));

    reconcile_orphan_key_files(&host, distro()).unwrap();

    assert_eq!(host.paths(), vec![key("bob_deploy")]);
}

#[test]
fn an_unreadable_password_database_removes_nothing() {
    let host = FakeSftpHost::new().with_path(&key("alice_gone"));
    host.make_passwd_unreadable();

    let result = reconcile_orphan_key_files(&host, distro());

    assert_eq!(result, Err(SftpError::PasswdUnreadable));
    assert_eq!(host.paths(), vec![key("alice_gone")]);
}

#[test]
fn an_empty_key_directory_does_not_even_read_the_password_database() {
    // A host that never held an SFTP login must start whether or not the
    // password database can be read here.
    let host = FakeSftpHost::new();
    host.make_passwd_unreadable();

    assert_eq!(reconcile_orphan_key_files(&host, distro()), Ok(0));
}

#[test]
fn a_file_that_cannot_be_removed_is_a_key_file_failure() {
    let host = FakeSftpHost::new()
        .with_path(&key("alice_gone"))
        .refuse_removal_of(&key("alice_gone"));

    let result = reconcile_orphan_key_files(&host, distro());

    assert_eq!(result, Err(SftpError::KeyFileFailed));
}

#[test]
fn a_second_sweep_finds_nothing_more_to_remove() {
    let host = FakeSftpHost::new().with_path(&key("alice_gone"));

    assert_eq!(reconcile_orphan_key_files(&host, distro()), Ok(1));
    assert_eq!(reconcile_orphan_key_files(&host, distro()), Ok(0));
}
