//! What `delete_sftp_user` takes away, and everything it leaves alone.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sftp::delete_sftp_user::delete_sftp_user;
use crate::sftp::fake_sftp_host::{FakeSftpHost, distro, web_user};
use crate::sftp::sftp_error::SftpError;

/// The login goes and the files stay.
#[test]
fn deleting_an_sftp_user_removes_the_login_without_removing_its_files() {
    // `userdel -r` would walk into the bind mount and delete the customer's
    // whole website, for an operation that means "revoke one login".
    let host = FakeSftpHost::with_existing("alice_web");

    delete_sftp_user(&host, distro(), &web_user()).expect("deleted");

    let userdel = host.spawn_of("userdel").expect("userdel was run");
    assert_eq!(
        userdel.argv,
        vec!["/usr/sbin/userdel".to_owned(), "alice_web".to_owned()]
    );
    assert!(
        !userdel.argv.iter().any(|argument| argument == "-r"
            || argument == "--remove"
            || argument == "--remove-home"),
        "the home belongs to the account, not to this login: {:?}",
        userdel.argv
    );
    assert!(host.users().is_empty());
}

/// The account's jail survives the login, because other logins may use it.
#[test]
fn deleting_an_sftp_user_leaves_the_accounts_jail_and_mount_in_place() {
    let host = FakeSftpHost::with_existing("alice_web");

    delete_sftp_user(&host, distro(), &web_user()).expect("deleted");

    assert!(
        host.configs().is_empty() && host.directories().is_empty(),
        "the jail is an account resource with account lifetime"
    );
}

/// A repeat converges on `NotFound` instead of failing.
#[test]
fn deleting_a_user_that_is_not_there_reports_not_found() {
    let host = FakeSftpHost::new();

    let error = delete_sftp_user(&host, distro(), &web_user()).expect_err("must fail");

    assert!(matches!(error, SftpError::NotFound));
}

/// Any other refusal arrives as a status and nothing else.
#[test]
fn a_refusal_that_is_not_a_missing_user_carries_the_status_alone() {
    assert!(matches!(
        SftpError::from_userdel(8),
        SftpError::SpawnFailed { code: 8 }
    ));
    assert!(matches!(SftpError::from_userdel(6), SftpError::NotFound));
}
