//! Which refusals the login area's proof of a hosting account maps to, and that
//! a refused account is never culled.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use crate::logins::fake_logins_host::{ACCOUNT_UID, FakeLoginsHost, account, account_row, distro};
use crate::logins::logins_error::LoginsError;
use crate::logins::resolve_managed_account::resolve_managed_account;
use crate::logins::set_account_logins_locked::set_account_logins_locked_under_lock;

#[test]
fn an_ordinary_hosting_account_resolves_with_its_own_ids() {
    // The accepting control: without it every refusal below could be a function
    // that refuses everything.
    let host = FakeLoginsHost::with_passwd(&[account_row()]);

    let managed = resolve_managed_account(&host, distro(), &account()).expect("a hosting account");

    assert_eq!(managed.uid(), ACCOUNT_UID);
    assert_eq!(managed.name(), &account());
}

#[test]
fn an_account_with_no_row_is_missing() {
    let host = FakeLoginsHost::with_passwd(&[("bob", 1002, "/home/bob")]);

    let refused = resolve_managed_account(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
}

#[test]
fn an_unreadable_password_database_is_refused_rather_than_trusted() {
    let host = FakeLoginsHost::with_passwd(&[account_row()]);
    host.refuse_to_be_read();

    let refused = resolve_managed_account(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
}

#[test]
fn a_row_with_uid_zero_is_not_a_managed_account() {
    let host = FakeLoginsHost::with_passwd(&[("alice", 0, "/home/alice")]);

    let refused = resolve_managed_account(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountNotManaged)),
        "got {refused:?}"
    );
}

#[test]
fn a_row_whose_home_is_not_the_managed_home_is_not_a_managed_account() {
    let host = FakeLoginsHost::with_passwd(&[("alice", ACCOUNT_UID, "/srv/alice")]);

    let refused = resolve_managed_account(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountNotManaged)),
        "got {refused:?}"
    );
}

#[test]
fn a_reserved_name_is_not_a_managed_account_even_with_an_ordinary_row() {
    let host = FakeLoginsHost::with_passwd(&[("ubuntu", ACCOUNT_UID, "/home/ubuntu")]);
    let reserved = AccountName::parse("ubuntu").expect("the fixture name is valid");

    let refused = resolve_managed_account(&host, distro(), &reserved);

    assert!(
        matches!(refused, Err(LoginsError::AccountNotManaged)),
        "got {refused:?}"
    );
}

#[test]
fn a_refused_account_is_never_locked_and_never_has_its_sessions_ended() {
    // The consequence the mapping exists for: a refusal must stop the operation
    // before pkill or usermod is spawned, so an operator's own login survives a
    // suspension aimed at its name.
    let host = FakeLoginsHost::with_passwd(&[("ubuntu", ACCOUNT_UID, "/home/ubuntu")])
        .with_process(ACCOUNT_UID);
    let reserved = AccountName::parse("ubuntu").expect("the fixture name is valid");

    let refused = set_account_logins_locked_under_lock(&host, distro(), &reserved, true);

    assert!(
        matches!(refused, Err(LoginsError::AccountNotManaged)),
        "got {refused:?}"
    );
    assert!(
        host.spawns().is_empty(),
        "nothing may be spawned for a refused account"
    );
    assert_eq!(host.running_processes(), vec![ACCOUNT_UID]);
}
