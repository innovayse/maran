//! Where the password goes, and where it must never go.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::accounts::take_account_lock;
use crate::sftp::fake_sftp_host::{
    FakeSftpHost, TEST_PASSWORD, account, distro, web_request, web_user,
};
use crate::sftp::set_sftp_password::{set_sftp_password, set_sftp_password_under_lock};
use crate::sftp::sftp_error::SftpError;

/// The password reaches `chpasswd` on standard input and appears in no
/// argument vector.
#[test]
fn the_password_is_set_over_stdin_and_never_appears_in_an_argument_vector() {
    // The leak this closes: a password on a command line is visible in `ps` to
    // every local user on the host. chpasswd reads it from standard input; the
    // argv array carries only the program.
    let host = host_with_the_login();
    let request = web_request();

    set_sftp_password_under_lock(&host, distro(), &request.user, &request.password).expect("set");

    let spawn = host
        .spawn_of("chpasswd")
        .expect("a process was spawned to set the password");
    assert_eq!(spawn.argv, vec!["/usr/sbin/chpasswd".to_owned()]);
    assert!(spawn.stdin.contains("alice_web:"));
    assert!(
        !spawn
            .argv
            .iter()
            .any(|argument| argument.contains(TEST_PASSWORD)),
        "the password must not be in the argument vector: {:?}",
        spawn.argv
    );
}

/// Creating a login puts its password on standard input too, not just the
/// standalone operation.
#[test]
fn creating_a_user_also_keeps_the_password_out_of_every_argument_vector() {
    let host = FakeSftpHost::new();

    crate::sftp::create_sftp_user::create_sftp_user_under_lock(&host, distro(), &web_request())
        .expect("created");

    for spawn in host.spawns() {
        assert!(
            !spawn
                .argv
                .iter()
                .any(|argument| argument.contains(TEST_PASSWORD)),
            "a password reached an argument vector: {:?}",
            spawn.argv
        );
    }
    let chpasswd = host.spawn_of("chpasswd").expect("chpasswd was run");
    assert!(chpasswd.stdin.contains(TEST_PASSWORD));
}

/// Exactly one line reaches the tool, terminated so it is read at all.
#[test]
fn exactly_one_user_and_password_line_reaches_the_tool() {
    // Two lines would be two passwords set, which is the injection the Password
    // alphabet exists to make impossible. One line is what proves the format is
    // what the alphabet was designed against.
    let host = host_with_the_login();
    let request = web_request();

    set_sftp_password_under_lock(&host, distro(), &request.user, &request.password).expect("set");

    let spawn = host.spawn_of("chpasswd").expect("a spawn");
    assert_eq!(spawn.stdin, format!("alice_web:{TEST_PASSWORD}\n"));
    assert_eq!(spawn.stdin.lines().count(), 1);
}

/// A refused password is its own condition, and the tool's words do not travel.
#[test]
fn a_refused_password_is_reported_as_password_rejected() {
    let host = host_with_the_login();
    host.refuse_password_with(1);
    let password = Password::parse(TEST_PASSWORD).expect("valid");

    let error = set_sftp_password_under_lock(&host, distro(), &web_user(), &password)
        .expect_err("must fail");

    assert!(matches!(error, SftpError::PasswordRejected));
    let printed = format!("{error:?} {error}");
    assert!(!printed.contains(TEST_PASSWORD));
}

/// The error type has nowhere to put a password, whatever a caller formats.
#[test]
fn a_password_prints_as_a_placeholder_wherever_a_request_is_formatted() {
    let request = web_request();

    assert!(!format!("{request:?}").contains(TEST_PASSWORD));
}

/// A host holding `alice`'s `web` login, which is what every test below
/// re-credentials.
fn host_with_the_login() -> FakeSftpHost {
    FakeSftpHost::new().with_login("alice_web")
}

/// The password the tests below set, distinct from [`TEST_PASSWORD`] so a
/// re-credentialling can be told from the credential that was already there.
const NEW_PASSWORD: &str = "Rotated-2.pw";

/// The password value the tests below hand the operation.
fn new_password() -> Password {
    Password::parse(NEW_PASSWORD).expect("valid")
}

#[test]
fn a_locked_logins_password_change_leaves_it_locked() {
    // M-4, the half that is not a race. `chpasswd` REPLACES the shadow password
    // field, so before this the customer's own password change wiped the `!` a
    // suspension had written and handed a suspended account a live write
    // credential into its home — no interleaving, no attacker, and the panel
    // went on reporting the account as suspended.
    let host = host_with_the_login().with_locked("alice_web");

    set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password()).expect("set");

    assert!(
        host.is_locked("alice_web"),
        "the login was locked before the password change and must be locked after it: \
         a suspension that a password change lifts is not a suspension"
    );
    let chpasswd = host
        .spawn_of("chpasswd")
        .expect("the password was still set");
    assert!(
        chpasswd.stdin.contains(NEW_PASSWORD),
        "the new password must really have been set: the fix is to re-lock, not to refuse"
    );
}

#[test]
fn an_unlocked_logins_password_change_leaves_it_usable() {
    // The inverse control, and it is not optional: an implementation that
    // locked unconditionally would satisfy the assertion above and would
    // suspend every customer who changed their own password.
    let host = host_with_the_login();

    set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password()).expect("set");

    assert!(
        !host.is_locked("alice_web"),
        "an ordinary password change must not lock the login"
    );
    assert!(
        host.spawn_of("usermod").is_none(),
        "nothing was locked before, so there is nothing to restore: {:?}",
        host.spawns()
    );
}

#[test]
fn a_login_with_no_password_at_all_is_left_unable_to_authenticate() {
    // A hand-made login the agent never credited. `usermod --lock` on it is a
    // measured no-op and the attestation reports it as locked, so a password
    // change that left it usable would open a login the panel says is shut.
    let host = host_with_the_login().with_passwordless("alice_web");

    set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password()).expect("set");

    assert!(
        host.is_locked("alice_web"),
        "a login that could not authenticate before must not be able to after"
    );
}

#[test]
fn the_field_is_read_before_the_password_is_written_and_again_after_the_lock() {
    // The order is the whole mechanism: `chpasswd` destroys the answer, so a
    // read taken afterwards would report the state this operation just created.
    // And the second read is what makes the restore an observation rather than
    // a hope — `usermod --lock` exits zero on a login it did nothing to.
    let host = host_with_the_login().with_locked("alice_web");

    set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password()).expect("set");

    let programs: Vec<String> = host
        .spawns()
        .into_iter()
        .filter_map(|spawn| spawn.argv.first().cloned())
        .map(|program| program.rsplit('/').next().unwrap_or_default().to_owned())
        .collect();
    assert_eq!(
        programs,
        vec!["getent", "chpasswd", "usermod", "getent"],
        "expected read, write, re-lock, read back"
    );
}

#[test]
fn a_lock_that_did_not_take_is_reported_rather_than_reported_as_success() {
    // `usermod --lock` answering zero is not evidence, so the operation reads
    // the field back. This is the fake refusing to lock while still exiting
    // zero — the shape of a tool that did nothing.
    let host = host_with_the_login().with_locked("alice_web");
    host.ignore_locking();

    let refused = set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password());

    assert!(
        matches!(refused, Err(SftpError::SuspensionNotRestored)),
        "got {refused:?}"
    );
}

#[test]
fn a_refused_password_leaves_the_lock_where_it_was_and_never_runs_usermod() {
    let host = host_with_the_login().with_locked("alice_web");
    host.refuse_password_with(1);

    let refused = set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password());

    assert!(
        matches!(refused, Err(SftpError::PasswordRejected)),
        "got {refused:?}"
    );
    assert!(
        host.spawn_of("usermod").is_none(),
        "nothing was changed, so nothing may be restored"
    );
}

#[test]
fn a_login_the_account_does_not_hold_is_refused_before_anything_is_written() {
    // The collision this area has already been burned by: a request authorised
    // for `alice` naming login `bob` addresses the system user `alice_bob`,
    // which may be a NEIGHBOURING account's own entry or another account's
    // login. `useradd` refuses a name in use, so a creation cannot make this
    // mistake; setting a password had no such gate and would have written a
    // credential onto somebody else's login.
    let host = FakeSftpHost::new()
        .with_hosting_account("alice_bob")
        .with_login("alice_web");
    let neighbour = SftpUserName::for_account(&account(), "bob").expect("valid");

    let refused = set_sftp_password(&host, distro(), &account(), &neighbour, &new_password());

    assert!(
        matches!(refused, Err(SftpError::NotFound)),
        "got {refused:?}"
    );
    assert!(
        host.spawn_of("chpasswd").is_none(),
        "no password may be set on a login this account does not hold: {:?}",
        host.spawns()
    );
}

/// An account no other test names, so the process-wide account lock this suite
/// takes cannot be contended by the harness's own threads.
fn contended_account() -> AccountName {
    AccountName::parse("sftppwcontended").expect("the fixture name is valid")
}

#[test]
fn a_password_change_is_refused_while_the_hosting_accounts_lock_is_held() {
    // M-4, the race half. Without the lock, a suspension landing between this
    // operation's read of the shadow field and its `chpasswd` is undone by a
    // password change acting on a fact that has expired.
    let account = contended_account();
    let user = SftpUserName::for_account(&account, "web").expect("valid");
    let held = take_account_lock(&account).expect("the lock is free at the start of this test");
    let host = FakeSftpHost::new().with_login(user.as_str());

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountBusy)),
        "got {refused:?}"
    );
    assert!(
        host.spawns().is_empty(),
        "the operation never started: {:?}",
        host.spawns()
    );
    drop(held);
}

#[test]
fn a_password_change_that_finished_leaves_the_hosting_accounts_lock_free() {
    // The inverse control. A guard leaked by the entry point would make the
    // refusal above pass forever and every later password change for that
    // account impossible — a worse defect than the race being closed.
    let account = AccountName::parse("sftppwreleases").expect("the fixture name is valid");
    let user = SftpUserName::for_account(&account, "web").expect("valid");
    let host = FakeSftpHost::new().with_login(user.as_str());

    set_sftp_password(&host, distro(), &account, &user, &new_password()).expect("set");

    let free = take_account_lock(&account);
    assert!(
        free.is_some(),
        "the lock must be free once the operation returned"
    );
}
