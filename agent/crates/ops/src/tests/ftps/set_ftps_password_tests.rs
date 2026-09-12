//! The one line that reaches `chpasswd`, the argument vector that must not, the
//! login that has to belong to the account, and the suspension that must not be
//! handed back.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;

use crate::ftps::fake_ftps_host::{FakeFtpsHost, distro};
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::set_ftps_password::{set_ftps_password, set_ftps_password_under_lock};

/// The login suffix every test here acts on.
const SUFFIX: &str = "files";

/// The password the tests set.
const NEW_PASSWORD: &str = "Str0ng-pass.word=+_";

/// A locked hash: a real password with the suspension marker in front of it.
const LOCKED_HASH: &str = "!$6$real$hash";

/// An unlocked hash: a login that can authenticate.
const OPEN_HASH: &str = "$6$real$hash";

/// A host holding `account`'s jail and its `files` login.
///
/// Each test names its OWN account, because the operation's entry point takes a
/// process-wide per-account lock that never waits: two tests sharing one account
/// name on the harness's threads would refuse each other, and the flake would
/// say nothing about the code.
fn host_with_login(account: &str) -> FakeFtpsHost {
    FakeFtpsHost::for_account(account, 1001, 1001)
        .with_jail()
        .with_existing_login(&login_name(account))
}

/// The login `account` holds, as the password database spells it.
fn login_name(account: &str) -> String {
    format!("{account}_{SUFFIX}")
}

/// The validated login name every test acts on.
fn login(host: &FakeFtpsHost) -> FtpsUserName {
    FtpsUserName::for_account(&host.account_name(), SUFFIX).expect("a valid name")
}

/// The validated password every test sets.
fn password() -> Password {
    Password::parse(NEW_PASSWORD).expect("a valid password")
}

#[test]
fn the_line_is_one_user_colon_password_pair_terminated_by_a_single_newline() {
    let host = host_with_login("pwline");

    set_ftps_password(
        &host,
        distro(),
        &host.account_name(),
        &login(&host),
        &password(),
    )
    .expect("the password must be set");

    let spawn = host
        .spawns()
        .into_iter()
        .find(|spawn| spawn.stdin.is_some())
        .expect("chpasswd ran");
    assert_eq!(
        spawn.stdin.as_deref(),
        Some("pwline_files:Str0ng-pass.word=+_\n"),
        "one line, and exactly one: a second user:password line is a password set \
         for a login the caller does not own"
    );
}

#[test]
fn the_argument_vector_carries_the_program_and_nothing_else() {
    let host = host_with_login("pwargv");

    set_ftps_password(
        &host,
        distro(),
        &host.account_name(),
        &login(&host),
        &password(),
    )
    .expect("the password must be set");

    let spawn = host
        .spawns()
        .into_iter()
        .find(|spawn| spawn.stdin.is_some())
        .expect("chpasswd ran");
    assert_eq!(
        spawn.argv,
        vec![distro().chpasswd_binary().to_owned()],
        "a command line is readable through /proc by every local user on the host"
    );
}

#[test]
fn a_refused_line_is_reported_and_nothing_pretends_the_password_was_set() {
    let host = host_with_login("pwrefused").refusing_passwords();

    let result = set_ftps_password(
        &host,
        distro(),
        &host.account_name(),
        &login(&host),
        &password(),
    );

    assert!(
        matches!(result, Err(FtpsError::PasswordRejected)),
        "{result:?}"
    );
}

#[test]
fn a_login_the_account_does_not_hold_is_refused_before_anything_is_written() {
    let login_name = login_name("pwforeign");
    // The cross-tenant hole: the login exists on the host, but its passwd
    // home is a NEIGHBOURING account's jail, so it is not this account's login
    // however the name decomposes.
    let host = FakeFtpsHost::for_account("pwforeign", 1001, 1001)
        .with_jail()
        .with_foreign_login(login_name.as_str(), "/var/lib/maran-ftps/pwforeign_f")
        .with_shadow_field(login_name.as_str(), OPEN_HASH);

    let result = set_ftps_password(
        &host,
        distro(),
        &host.account_name(),
        &login(&host),
        &password(),
    );

    assert!(matches!(result, Err(FtpsError::NotFound)), "{result:?}");
    assert_eq!(
        host.shadow_field(login_name.as_str()).as_deref(),
        Some(OPEN_HASH),
        "the neighbouring tenant's credential must be exactly as it was"
    );
    assert!(
        host.spawns().iter().all(|spawn| spawn.stdin.is_none()),
        "chpasswd must not have been reached at all"
    );
}

#[test]
fn an_sftp_login_of_the_same_account_is_not_reachable_through_this_operation() {
    let login_name = login_name("pwsftp");
    // Same account, same uid, same name shape — told apart only by the jail its
    // home is under, which is what stops an FTPS rpc re-credentialling an SFTP
    // login.
    let host = FakeFtpsHost::for_account("pwsftp", 1001, 1001)
        .with_jail()
        .with_foreign_login(login_name.as_str(), "/var/lib/maran-sftp/pwsftp")
        .with_shadow_field(login_name.as_str(), OPEN_HASH);

    let result = set_ftps_password(
        &host,
        distro(),
        &host.account_name(),
        &login(&host),
        &password(),
    );

    assert!(matches!(result, Err(FtpsError::NotFound)), "{result:?}");
}

#[test]
fn a_login_that_is_not_on_the_host_at_all_is_reported_as_not_found() {
    let host = FakeFtpsHost::for_account("pwabsent", 1001, 1001).with_jail();

    let result = set_ftps_password(
        &host,
        distro(),
        &host.account_name(),
        &login(&host),
        &password(),
    );

    assert!(matches!(result, Err(FtpsError::NotFound)), "{result:?}");
}

#[test]
fn a_password_database_that_cannot_be_read_refuses_rather_than_reporting_no_logins() {
    let host = host_with_login("pwunreadable").unreadable_passwd();

    let result = set_ftps_password(
        &host,
        distro(),
        &host.account_name(),
        &login(&host),
        &password(),
    );

    assert!(
        matches!(result, Err(FtpsError::AccountMissing)),
        "{result:?}"
    );
}

#[test]
fn a_login_that_could_authenticate_before_is_left_unlocked_afterwards() {
    let login_name = login_name("pwopen");
    let host = host_with_login("pwopen").with_shadow_field(login_name.as_str(), OPEN_HASH);

    set_ftps_password_under_lock(&host, distro(), &login(&host), &password())
        .expect("the password must be set");

    let field = host
        .shadow_field(login_name.as_str())
        .expect("the entry is still there");
    assert!(
        !field.starts_with('!'),
        "an ordinary password change must not lock a working login, got {field:?}"
    );
}

#[test]
fn a_suspended_login_is_locked_again_after_its_password_is_set() {
    let login_name = login_name("pwlocked");
    let host = host_with_login("pwlocked").with_shadow_field(login_name.as_str(), LOCKED_HASH);

    set_ftps_password_under_lock(&host, distro(), &login(&host), &password())
        .expect("the password must be set");

    let field = host
        .shadow_field(login_name.as_str())
        .expect("the entry is still there");
    assert!(
        field.starts_with('!'),
        "chpasswd REPLACES the shadow field, so a suspended customer changing \
         their password would otherwise unlock their own login; got {field:?}"
    );
    assert!(
        field.contains("fake$hash"),
        "the password the customer chose must be the one the login has once the \
         suspension is lifted; got {field:?}"
    );
}

#[test]
fn a_login_that_never_had_a_password_is_locked_again_after_its_password_is_set() {
    let login_name = login_name("pwabsentfield");
    // `useradd` writes `!` on the Debian family and `!!` on the RHEL one, and
    // `logins::set_account_logins_locked` leaves such an entry alone. Either
    // way nothing could authenticate before, so nothing may afterwards.
    let host = host_with_login("pwabsentfield").with_shadow_field(login_name.as_str(), "!!");

    set_ftps_password_under_lock(&host, distro(), &login(&host), &password())
        .expect("the password must be set");

    let field = host
        .shadow_field(login_name.as_str())
        .expect("the entry is still there");
    assert!(field.starts_with('!'), "{field:?}");
}

#[test]
fn an_empty_password_field_is_deliberately_not_restored() {
    let login_name = login_name("pwempty");
    // An empty field authenticates with the empty password. Restoring it would
    // be restoring an open door, so it counts as "could authenticate": the new
    // password is set and nothing is locked.
    let host = host_with_login("pwempty").with_shadow_field(login_name.as_str(), "");

    set_ftps_password_under_lock(&host, distro(), &login(&host), &password())
        .expect("the password must be set");

    let field = host
        .shadow_field(login_name.as_str())
        .expect("the entry is still there");
    assert!(
        !field.starts_with('!'),
        "an empty field must not be put back, and must not be replaced by a lock; got {field:?}"
    );
}

#[test]
fn a_lock_that_the_tool_reports_as_done_and_did_not_do_is_reported_as_not_restored() {
    let login_name = login_name("pwnolock");
    // `usermod --lock` exits ZERO on a login it did nothing to, which is why the
    // restored lock is verified by re-reading the raw field rather than by
    // reading an exit status.
    let host = host_with_login("pwnolock")
        .with_shadow_field(login_name.as_str(), LOCKED_HASH)
        .usermod_lock_that_does_nothing();

    let result = set_ftps_password_under_lock(&host, distro(), &login(&host), &password());

    assert!(
        matches!(result, Err(FtpsError::SuspensionNotRestored)),
        "{result:?}"
    );
}

#[test]
fn a_usermod_that_refuses_outright_is_reported_as_a_spawn_failure() {
    let login_name = login_name("pwusermod");
    let host = host_with_login("pwusermod")
        .with_shadow_field(login_name.as_str(), LOCKED_HASH)
        .refusing_usermod();

    let result = set_ftps_password_under_lock(&host, distro(), &login(&host), &password());

    assert!(
        matches!(result, Err(FtpsError::SpawnFailed { code: 1 })),
        "{result:?}"
    );
}

#[test]
fn a_shadow_entry_that_cannot_be_parsed_refuses_before_the_password_is_written() {
    let host = host_with_login("pwgarbled").getent_printing("nonsense with no colons\n");

    let result = set_ftps_password_under_lock(&host, distro(), &login(&host), &password());

    assert!(
        matches!(result, Err(FtpsError::StatusUnreadable)),
        "{result:?}"
    );
    assert!(
        host.spawns().iter().all(|spawn| spawn.stdin.is_none()),
        "an unreadable field read as 'no password' would LOCK a working login, so \
         nothing may be written at all"
    );
}

#[test]
fn an_entry_printed_for_some_other_login_is_not_believed() {
    let host = host_with_login("pwotherlogin")
        .getent_printing("someone_else:$6$other$hash:19000:0:99999:7:::\n");

    let result = set_ftps_password_under_lock(&host, distro(), &login(&host), &password());

    assert!(
        matches!(result, Err(FtpsError::StatusUnreadable)),
        "{result:?}"
    );
}

#[test]
fn a_name_service_that_will_not_answer_is_a_failure_and_never_an_absent_password() {
    let host = host_with_login("pwnonameservice").getent_refusing(1);

    let result = set_ftps_password_under_lock(&host, distro(), &login(&host), &password());

    assert!(
        matches!(result, Err(FtpsError::SpawnFailed { code: 1 })),
        "an unreadable field must not be read as Absent, which would lock a \
         working login: {result:?}"
    );
}

#[test]
fn the_shadow_field_is_read_before_the_password_is_written_and_not_after() {
    let login_name = login_name("pworder");
    // The order is the whole operation: `chpasswd` destroys the answer, so a
    // read taken afterwards would always see an unlocked field and never restore
    // anything.
    let host = host_with_login("pworder").with_shadow_field(login_name.as_str(), LOCKED_HASH);

    set_ftps_password_under_lock(&host, distro(), &login(&host), &password())
        .expect("the password must be set");

    let programs: Vec<String> = host
        .spawns()
        .into_iter()
        .map(|spawn| spawn.argv[0].clone())
        .collect();
    let first_getent = programs
        .iter()
        .position(|program| program == distro().getent_binary())
        .expect("getent ran");
    let chpasswd = programs
        .iter()
        .position(|program| program == distro().chpasswd_binary())
        .expect("chpasswd ran");
    assert!(
        first_getent < chpasswd,
        "the field must be read before the write that replaces it, got {programs:?}"
    );
}

#[test]
fn a_second_operation_for_the_same_account_is_refused_rather_than_queued() {
    let host = host_with_login("pwbusy");
    let account = host.account_name();
    let guard = crate::accounts::take_account_lock(&account).expect("the lock is free");

    let result = set_ftps_password(&host, distro(), &account, &login(&host), &password());

    assert!(matches!(result, Err(FtpsError::AccountBusy)), "{result:?}");
    assert!(
        host.spawns().is_empty(),
        "nothing may be read and nothing written when the lock refuses"
    );
    drop(guard);
}
