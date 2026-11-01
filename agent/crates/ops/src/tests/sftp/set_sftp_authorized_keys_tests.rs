//! Where an SFTP login's keys go, and the names they must never be under.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;

use crate::accounts::take_account_lock;
use crate::sftp::fake_sftp_host::{FakeSftpHost, account, distro, web_user};
use crate::sftp::model::authorized_key_set::AuthorizedKeySet;
use crate::sftp::set_sftp_authorized_keys::{
    set_sftp_authorized_keys, set_sftp_authorized_keys_under_lock,
};
use crate::sftp::sftp_error::SftpError;

const ED25519: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIG7OpD9/qnELjIQS7Z+hv9E4OBM+3IqncDtZr2x3fz0s";

/// Where sshd reads `alice_web`'s keys.
const ACTIVE: &str = "/etc/ssh/maran-sftp-keys/alice_web";

/// Where `alice_web`'s keys wait out a suspension.
const SUSPENDED: &str = "/etc/ssh/maran-sftp-keys/alice_web.suspended";

fn one_key() -> AuthorizedKeySet {
    AuthorizedKeySet::new(vec![
        SshPublicKey::parse(&format!("{ED25519} me@laptop")).unwrap(),
    ])
    .unwrap()
}

fn host_with_the_login() -> FakeSftpHost {
    FakeSftpHost::new().with_login("alice_web")
}

#[test]
fn keys_are_written_restricted_and_without_their_comment_where_sshd_reads_them() {
    let host = host_with_the_login();

    let count =
        set_sftp_authorized_keys_under_lock(&host, distro(), &account(), &web_user(), &one_key())
            .unwrap();

    assert_eq!(count, 1);
    let configs = host.configs();
    assert_eq!(configs.len(), 1);
    assert_eq!(configs[0].target, ACTIVE);
    assert!(
        configs[0]
            .contents
            .contains(&format!("\nrestrict {ED25519}\n"))
    );
    assert!(!configs[0].contents.contains("me@laptop"));
    assert_eq!(
        configs[0].validator,
        vec!["/usr/bin/ssh-keygen", "-l", "-f", ACTIVE]
    );
    assert_eq!(configs[0].validator_class, CallClass::Validate);
    assert!(configs[0].reload.is_empty(), "nothing is reloaded");
}

#[test]
fn a_locked_login_gets_its_keys_where_sshd_never_reads_them() {
    // Adding a key must not be a way past a suspension.
    let host = host_with_the_login()
        .with_locked("alice_web")
        .with_path(ACTIVE);

    set_sftp_authorized_keys_under_lock(&host, distro(), &account(), &web_user(), &one_key())
        .unwrap();

    assert_eq!(host.configs()[0].target, SUSPENDED);
    assert_eq!(host.paths(), vec![SUSPENDED.to_owned()]);
}

#[test]
fn an_unlocked_login_drops_a_leftover_suspended_copy() {
    let host = host_with_the_login().with_path(SUSPENDED);

    set_sftp_authorized_keys_under_lock(&host, distro(), &account(), &web_user(), &one_key())
        .unwrap();

    assert_eq!(host.paths(), vec![ACTIVE.to_owned()]);
}

#[test]
fn an_empty_set_removes_both_names_and_writes_nothing() {
    let host = host_with_the_login().with_path(ACTIVE).with_path(SUSPENDED);
    let none = AuthorizedKeySet::new(Vec::new()).unwrap();

    let count =
        set_sftp_authorized_keys_under_lock(&host, distro(), &account(), &web_user(), &none)
            .unwrap();

    assert_eq!(count, 0);
    assert!(host.configs().is_empty());
    assert!(host.paths().is_empty());
}

#[test]
fn a_login_the_account_does_not_hold_is_not_found_and_nothing_is_written() {
    // `alice_web` homed in another account's jail: the same spelling, another
    // tenant's login.
    let host = FakeSftpHost::new().with_foreign_login("alice_web", "/var/lib/maran-sftp/alicex");

    let result =
        set_sftp_authorized_keys_under_lock(&host, distro(), &account(), &web_user(), &one_key());

    assert!(matches!(result, Err(SftpError::NotFound)));
    assert!(host.configs().is_empty());
}

#[test]
fn a_refused_write_is_a_key_file_failure() {
    let host = host_with_the_login();
    host.refuse_config_writes();

    let result =
        set_sftp_authorized_keys_under_lock(&host, distro(), &account(), &web_user(), &one_key());

    assert!(matches!(result, Err(SftpError::KeyFileFailed)));
}

#[test]
fn a_held_account_lock_refuses_before_anything_is_read() {
    let other = maran_agent_core::validation::system::name::AccountName::parse("keysbusy").unwrap();
    let user = maran_agent_core::validation::system::sftp_user_name::SftpUserName::for_account(
        &other, "web",
    )
    .unwrap();
    let host = FakeSftpHost::new();
    let _held = take_account_lock(&other).expect("the lock is free at the start of this test");

    let result = set_sftp_authorized_keys(&host, distro(), &other, &user, &one_key());

    assert!(matches!(result, Err(SftpError::AccountBusy)));
    assert!(host.spawns().is_empty());
}
