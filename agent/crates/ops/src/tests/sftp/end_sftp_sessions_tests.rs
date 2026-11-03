//! What ending a login's sessions signals, whose login it may name, and what it
//! reports.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::accounts::take_account_lock;
use crate::sftp::end_sftp_sessions::end_sftp_sessions;
use crate::sftp::fake_sftp_host::{FakeSftpHost, distro};
use crate::sftp::sftp_error::SftpError;

fn account(name: &str) -> AccountName {
    AccountName::parse(name).expect("the fixture's own names are valid")
}

fn login_of(account: &AccountName) -> SftpUserName {
    SftpUserName::for_account(account, "web").expect("a valid login name")
}

fn host_with_login(account: &AccountName) -> FakeSftpHost {
    FakeSftpHost::new().with_login(login_of(account).as_str())
}

/// The sessions are found by sshd's title for exactly this login, with a signal
/// that cannot be ignored, and the pattern follows the end-of-options marker.
#[test]
fn ending_a_logins_sessions_signals_the_processes_titled_for_that_login() {
    let account = account("sftpendsig");
    let host = host_with_login(&account);
    host.pkill_answers(0, "2\n");

    let signalled = end_sftp_sessions(&host, distro(), &account, &login_of(&account)).unwrap();

    assert_eq!(signalled, 2);
    let pkill = host.spawn_of("pkill").expect("pkill was run");
    assert_eq!(
        pkill.argv[1..],
        [
            "--signal",
            "KILL",
            "--count",
            "--full",
            "--",
            "^sshd(-session)?: sftpendsig_web( \\[priv\\]|@)"
        ]
    );
    assert_eq!(pkill.class, CallClass::Mutation);
}

/// A login with nothing open answers zero: the operation is idempotent.
#[test]
fn a_login_with_no_open_session_answers_zero() {
    let account = account("sftpendnone");
    let host = host_with_login(&account);
    host.pkill_answers(1, "");

    let signalled = end_sftp_sessions(&host, distro(), &account, &login_of(&account)).unwrap();

    assert_eq!(signalled, 0);
}

/// The login must be the account's, and a refusal signals nothing.
#[test]
fn a_login_the_account_does_not_hold_is_not_found_and_nothing_is_signalled() {
    let account = account("sftpendforeign");
    let host = FakeSftpHost::new();

    let result = end_sftp_sessions(&host, distro(), &account, &login_of(&account));

    assert_eq!(result, Err(SftpError::NotFound));
    assert!(host.spawn_of("pkill").is_none());
}

/// A neighbouring hosting account spelled like the login is not the login.
#[test]
fn a_neighbouring_account_spelled_like_the_login_is_not_signalled() {
    let account = account("sftpendnear");
    let host = FakeSftpHost::new().with_hosting_account("sftpendnear_web");

    let result = end_sftp_sessions(&host, distro(), &account, &login_of(&account));

    assert_eq!(result, Err(SftpError::NotFound));
    assert!(host.spawn_of("pkill").is_none());
}

/// A refusal of the tool is an error and not a count.
#[test]
fn a_pkill_that_refuses_is_reported_with_its_status_and_not_as_a_count() {
    let account = account("sftpendrefuse");
    let host = host_with_login(&account);
    host.pkill_answers(2, "");

    let result = end_sftp_sessions(&host, distro(), &account, &login_of(&account));

    assert_eq!(result, Err(SftpError::SessionsNotEnded { code: 2 }));
}

/// A figure `pkill` printed unreadably does not fail a signal already sent.
#[test]
fn an_unreadable_count_after_a_signal_is_zero_and_not_an_error() {
    let account = account("sftpendcount");
    let host = host_with_login(&account);
    host.pkill_answers(0, "not a number\n");

    let signalled = end_sftp_sessions(&host, distro(), &account, &login_of(&account)).unwrap();

    assert_eq!(signalled, 0);
}

/// A deadline is a bound, never a verdict.
#[test]
fn a_pkill_that_times_out_is_a_bound() {
    let account = account("sftpendbound");
    let host = host_with_login(&account);
    host.time_out("pkill");

    let result = end_sftp_sessions(&host, distro(), &account, &login_of(&account));

    assert!(matches!(result, Err(SftpError::Bound(_))), "{result:?}");
}

/// A revocation is not refused because a backup holds the account's lock.
#[test]
fn ending_sessions_is_not_refused_while_the_accounts_lock_is_held() {
    let account = account("sftpendlock");
    let host = host_with_login(&account);
    let _held = take_account_lock(&account).expect("the lock was free");

    let result = end_sftp_sessions(&host, distro(), &account, &login_of(&account));

    assert!(result.is_ok(), "{result:?}");
}
