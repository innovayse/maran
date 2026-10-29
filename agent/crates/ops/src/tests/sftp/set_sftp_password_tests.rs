//! Where the password goes, and where it must never go.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::accounts::take_account_lock;
use crate::sftp::fake_sftp_host::{
    ACCOUNT_GID, ACCOUNT_UID, FakeSftpHost, TEST_PASSWORD, account, distro, web_request, web_user,
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

/// A host holding `name`'s `web` login, and the request pieces for it.
///
/// Each refusal test below names its OWN account: the entry point takes the
/// process-wide per-account lock, which never waits, so two tests sharing a name
/// on the harness's threads would refuse each other with `AccountBusy` and hide
/// the refusal under test.
fn host_and_user_of(name: &str) -> (AccountName, SftpUserName, FakeSftpHost) {
    let account = AccountName::parse(name).expect("the fixture name is valid");
    let user = SftpUserName::for_account(&account, "web").expect("valid");
    let host = FakeSftpHost::new().with_login(user.as_str());

    (account, user, host)
}

/// Asserts that nothing at all was run or read-and-then-written for a refusal.
fn assert_nothing_ran(host: &FakeSftpHost) {
    assert!(
        host.spawns().is_empty(),
        "no chpasswd, no getent and no usermod may run for an unmanaged account: {:?}",
        host.spawns()
    );
}

#[test]
fn setting_an_sftp_password_for_a_reserved_account_name_spawns_nothing() {
    // The fake holds an ordinary row for this name (an ordinary uid, gid and
    // home), so only the name itself can refuse it: the answer must not depend
    // on which other fact the host happens to hold.
    let (account, user, host) = host_and_user_of("operator");

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountNotManaged)),
        "got {refused:?}"
    );
    assert_nothing_ran(&host);
}

#[test]
fn setting_an_sftp_password_for_the_panels_own_prefix_spawns_nothing() {
    let (account, user, host) = host_and_user_of("maran_pwprefix");

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountNotManaged)),
        "got {refused:?}"
    );
    assert_nothing_ran(&host);
}

#[test]
fn setting_an_sftp_password_for_an_account_with_a_system_user_id_spawns_nothing() {
    let (account, user, host) = host_and_user_of("sftppwsysuid");
    let host = host.with_account_ids(100, ACCOUNT_GID);

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountNotManaged)),
        "got {refused:?}"
    );
    assert_nothing_ran(&host);
}

#[test]
fn setting_an_sftp_password_for_an_account_with_a_system_group_id_spawns_nothing() {
    // The user id alone is not the test: an ordinary uid with a system primary
    // group is still a system identity.
    let (account, user, host) = host_and_user_of("sftppwsysgid");
    let host = host.with_account_ids(ACCOUNT_UID, 100);

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountNotManaged)),
        "got {refused:?}"
    );
    assert_nothing_ran(&host);
}

#[test]
fn setting_an_sftp_password_for_a_user_id_of_zero_spawns_nothing() {
    let (account, user, host) = host_and_user_of("sftppwzero");
    let host = host.with_account_ids(0, 0);

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountNotManaged)),
        "got {refused:?}"
    );
    assert_nothing_ran(&host);
}

#[test]
fn setting_an_sftp_password_for_an_account_with_another_home_spawns_nothing() {
    // An operator's own login with an ordinary uid and a name off the list, but
    // a home that is not `/home/<name>`.
    let (account, user, host) = host_and_user_of("sftppwhome");
    let host = host.with_account_home("/srv/sftppwhome");

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountNotManaged)),
        "got {refused:?}"
    );
    assert_nothing_ran(&host);
}

#[test]
fn setting_an_sftp_password_for_an_account_with_no_local_row_is_reported_as_missing() {
    // The answer this area always gave for an account the host does not hold is
    // kept, and nothing is run for it.
    let (account, user, host) = host_and_user_of("sftppwnorow");
    host.forget_the_account();

    let refused = set_sftp_password(&host, distro(), &account, &user, &new_password());

    assert!(
        matches!(refused, Err(SftpError::AccountMissing)),
        "got {refused:?}"
    );
    assert_nothing_ran(&host);
}

#[test]
fn the_refusal_for_an_unmanaged_account_does_not_name_which_check_failed() {
    // Reserved, system identity and wrong home are one answer, so a caller
    // probing the rules cannot tell them apart.
    let (account_a, user_a, host_a) = host_and_user_of("gopher");
    let (account_b, user_b, host_b) = host_and_user_of("sftppwsame");
    let host_b = host_b.with_account_home("/srv/sftppwsame");

    let reserved = set_sftp_password(&host_a, distro(), &account_a, &user_a, &new_password())
        .expect_err("must fail");
    let wrong_home = set_sftp_password(&host_b, distro(), &account_b, &user_b, &new_password())
        .expect_err("must fail");

    assert_eq!(format!("{reserved:?}"), format!("{wrong_home:?}"));
    assert_eq!(reserved.to_string(), wrong_home.to_string());
}

/// Each tool of the password change runs in the class of what it does.
#[test]
fn the_password_change_reads_as_a_query_and_writes_and_relocks_as_mutations() {
    let host = host_with_the_login().with_locked("alice_web");

    set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password()).expect("set");

    for spawn in host.spawns() {
        let expected = if spawn.argv[0].ends_with("getent") {
            CallClass::Query
        } else {
            CallClass::Mutation
        };
        assert_eq!(spawn.class, expected, "{:?}", spawn.argv);
    }
    assert!(
        host.spawn_of("usermod").is_some(),
        "the lock was restored, or the loop above did not cover the re-lock"
    );
}

/// `getent` and `usermod` put the login after the marker.
#[test]
fn getent_and_usermod_receive_the_end_of_options_marker_before_their_operands() {
    let host = host_with_the_login().with_locked("alice_web");

    set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password()).expect("set");

    let getent = host.spawn_of("getent").expect("getent was run").argv;
    assert_eq!(
        getent[1..],
        ["--", "shadow", "alice_web"].map(str::to_owned),
        "{getent:?}"
    );
    let usermod = host.spawn_of("usermod").expect("usermod was run").argv;
    assert_eq!(
        usermod[1..],
        ["--lock", "--", "alice_web"].map(str::to_owned),
        "{usermod:?}"
    );
    // Inverse control: `chpasswd` takes no operand at all, so it gets no marker.
    let chpasswd = host.spawn_of("chpasswd").expect("chpasswd was run").argv;
    assert_eq!(chpasswd.len(), 1, "{chpasswd:?}");
}

/// A `chpasswd` that outruns its deadline is not a rejected password.
#[test]
fn a_chpasswd_that_times_out_is_a_bound_and_not_a_rejected_password() {
    let host = host_with_the_login();
    host.time_out("chpasswd");

    let error = set_sftp_password_under_lock(&host, distro(), &web_user(), &new_password())
        .expect_err("must fail");

    assert!(matches!(error, SftpError::Bound(_)), "got {error:?}");
}
