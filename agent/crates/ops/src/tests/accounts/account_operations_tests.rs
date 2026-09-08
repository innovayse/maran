//! Tests for the `account_operations` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `account_operations.rs` declares this file
//! with `#[path]`, which keeps it a child module and therefore able to reach private
//! items — a crate-level `tests/` directory sees only the public API.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashSet;
use std::sync::Mutex;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use maran_distro::{DistroFamily, adapter_for};

use crate::accounts::{
    AccountError, AccountOperations, CommandOutcome, StoredPassword, SystemHost,
};
use std::path::Path;

use crate::cron::recording_cron_host::RecordingCronHost;
use crate::db::create_database;
use crate::db::fake_db_host::FakeDbHost;
use crate::db::model::create_database_request::CreateDatabaseRequest;
use crate::php::fake_php_host::FakePhpHost;
use crate::php::model::pool_input::PoolInput;
use crate::php::write_pool;
use crate::sftp::fake_sftp_host::FakeSftpHost;
use crate::sftp::model::account_jail::AccountJail;
use crate::sites::disable_site;
use crate::sites::fake_site_host::{
    FakeSiteHost, create_test_site, distro as site_distro, php_input,
};
use crate::test_support::recording_commands::RecordingCommands;

/// A machine that records what it was asked to do instead of doing it.
///
/// Not a mock with expectations: the tests below assert on the recorded argv, which
/// is the thing worth pinning. `useradd --create-home` and `useradd -m` differ by
/// nothing a type system can see and by everything a customer's data can.
struct RecordingHost {
    existing: Mutex<HashSet<String>>,
    recording: RecordingCommands,
    statuses: Mutex<Vec<i32>>,
    stdout: Mutex<String>,
    /// What `getent shadow <name>` prints, when a test needs it to differ from
    /// everything else the host says.
    ///
    /// Present because one observation asks TWO programs about the same login:
    /// `passwd -S` for the flag the panel's suspension reads, and
    /// `getent shadow` for the field its reactivation reads. A fixture with one
    /// stdout for both would hand `passwd -S`'s status line to the classifier,
    /// which is not a shape any host produces.
    shadow: Mutex<Option<String>>,
    stderr: Mutex<String>,
    size: u64,
}

impl RecordingHost {
    fn new() -> Self {
        Self {
            existing: Mutex::new(HashSet::new()),
            recording: RecordingCommands::new(),
            statuses: Mutex::new(Vec::new()),
            stdout: Mutex::new("1001\n".to_owned()),
            shadow: Mutex::new(None),
            stderr: Mutex::new("refused\n".to_owned()),
            size: 0,
        }
    }

    fn with_user(self, username: &str) -> Self {
        self.existing
            .lock()
            .expect("the fixture lock is never poisoned")
            .insert(username.to_owned());
        self
    }

    fn with_size(mut self, size: u64) -> Self {
        self.size = size;
        self
    }

    fn with_stdout(self, stdout: &str) -> Self {
        *self
            .stdout
            .lock()
            .expect("the fixture lock is never poisoned") = stdout.to_owned();
        self
    }

    /// What every refusing program prints on standard error.
    ///
    /// Overridable because one decision in this area is made by READING that
    /// stream: an account with no crontab is a non-zero exit carrying the
    /// absent-table sentence, and a deletion that treated it as a refusal could
    /// never remove an ordinary account.
    /// Gives `getent shadow acme` a password field of its own.
    fn with_shadow_field(self, field: &str) -> Self {
        *self
            .shadow
            .lock()
            .expect("the fixture lock is never poisoned") = Some(shadow_entry(field));
        self
    }

    fn with_stderr(self, stderr: &str) -> Self {
        *self
            .stderr
            .lock()
            .expect("the fixture lock is never poisoned") = stderr.to_owned();
        self
    }

    fn failing_next(self, status: i32) -> Self {
        self.statuses
            .lock()
            .expect("the fixture lock is never poisoned")
            .push(status);
        self
    }

    fn calls(&self) -> Vec<Vec<String>> {
        self.recording.calls()
    }

    fn called(&self, program: &str) -> Vec<Vec<String>> {
        self.recording.calls_to(program)
    }

    /// What a program prints on standard error given the status it exited
    /// with: nothing when it succeeded, the configured reason when it
    /// refused.
    fn stderr_for(&self, status: i32) -> String {
        if status == 0 {
            String::new()
        } else {
            self.stderr
                .lock()
                .expect("the fixture lock is never poisoned")
                .clone()
        }
    }
}

impl SystemHost for RecordingHost {
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, AccountError> {
        let status = self
            .statuses
            .lock()
            .expect("the fixture lock is never poisoned")
            .pop()
            .unwrap_or(0);
        let stdout = match (
            arguments.first(),
            self.shadow
                .lock()
                .expect("the fixture lock is never poisoned")
                .clone(),
        ) {
            (Some(&"shadow"), Some(entry)) => entry,
            _ => self
                .stdout
                .lock()
                .expect("the fixture lock is never poisoned")
                .clone(),
        };
        self.recording
            .set_next(status, &stdout, &self.stderr_for(status));

        Ok(self.recording.record(program, arguments))
    }

    fn user_exists(&self, username: &str) -> Result<bool, AccountError> {
        Ok(self
            .existing
            .lock()
            .expect("the fixture lock is never poisoned")
            .contains(username))
    }

    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        Ok(self.size)
    }
}

/// Operations bound to a recording host and the Debian adapter, which is what the
/// argv assertions below are written against.
fn debian(host: RecordingHost) -> AccountOperations<RecordingHost> {
    AccountOperations::new(host, adapter_for(DistroFamily::Debian))
}

fn name() -> AccountName {
    AccountName::parse("acme").expect("the fixture name is valid")
}

/// A database server this account has nothing on.
///
/// The account cascade's database half is exercised in the `db` area's own
/// tests; what these tests are about is the ORDER and the abort, so the hosts
/// they do not vary are empty.
fn no_databases() -> FakeDbHost {
    FakeDbHost::new()
}

/// A host this account has no SFTP login, jail or mount unit on.
fn no_sftp() -> FakeSftpHost {
    FakeSftpHost::new()
}

#[test]
fn creating_an_account_makes_the_user_its_home_and_its_own_group() {
    let operations = debian(RecordingHost::new());

    let created = operations
        .create(&name(), 1024 * 1024)
        .expect("creation succeeds");

    let useradd = operations_calls(&operations, "useradd");
    assert_eq!(
        useradd[0],
        vec![
            tool_path(&operations, "useradd").as_str(),
            "--create-home",
            "--home-dir",
            "/home/acme",
            "--shell",
            "/usr/sbin/nologin",
            "--user-group",
            "acme",
        ]
    );
    assert_eq!(created.home_directory, "/home/acme");
    assert_eq!(created.uid, 1001);
}

#[test]
fn a_new_account_gets_no_interactive_shell() {
    // A hosting account is not a person with a terminal: SFTP and cron work through
    // it, and an interactive login is exactly what must not.
    let operations = debian(RecordingHost::new());

    operations.create(&name(), 0).expect("creation succeeds");

    let useradd = operations_calls(&operations, "useradd");
    assert!(useradd[0].contains(&"/usr/sbin/nologin".to_owned()));
    assert!(!useradd[0].contains(&"/bin/bash".to_owned()));
}

#[test]
fn creating_an_account_that_already_exists_is_refused_and_touches_nothing() {
    let operations = debian(RecordingHost::new().with_user("acme"));

    let error = operations
        .create(&name(), 0)
        .expect_err("an existing account is refused");

    assert!(matches!(error, AccountError::AlreadyExists { .. }));
    // Nothing was run: a home directory this agent did not create may hold somebody
    // else's data, and re-owning it is the one mistake that cannot be undone.
    assert!(operations_calls(&operations, "useradd").is_empty());
}

#[test]
fn a_useradd_that_refuses_is_reported_with_its_own_stderr() {
    let operations = debian(RecordingHost::new().failing_next(9));

    let error = operations
        .create(&name(), 0)
        .expect_err("a refusing useradd fails");

    match error {
        AccountError::CommandFailed {
            program,
            status,
            stderr,
        } => {
            assert_eq!(program, tool_path(&operations, "useradd"));
            assert_eq!(status, 9);
            assert_eq!(stderr, "refused");
        }
        other => panic!("expected a command failure, got {other:?}"),
    }
}

#[test]
fn suspending_locks_the_password_and_takes_the_shell_away() {
    let operations = debian(RecordingHost::new().with_user("acme"));

    operations.suspend(&name()).expect("suspension succeeds");

    let usermod = operations_calls(&operations, "usermod");
    // Both: --lock stops any password matching, and the nologin shell stops the
    // authentication methods that never consult a password — an SSH key, say.
    assert!(
        usermod
            .iter()
            .any(|call| call.contains(&"--lock".to_owned()))
    );
    assert!(
        usermod
            .iter()
            .any(|call| call.contains(&"--shell".to_owned()))
    );
}

/// A shadow entry for `acme` whose password field is `field`.
///
/// `<name>:<password>:<last change>:…`, which is what `getent shadow acme`
/// prints on both families.
fn shadow_entry(field: &str) -> String {
    format!("acme:{field}:20704:0:99999:7:::\n")
}

/// A shadow password field holding a real hash, as either family writes one.
const HASHED_PASSWORD: &str = "$6$ou076yYZZcNDB1l3$9ClrBhB8kkMnQbmP16aIMTk3c0NcW..K8AxyIxIvLPY";

#[test]
fn unsuspending_reverses_exactly_what_suspending_did() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&shadow_entry(&format!("!{HASHED_PASSWORD}"))),
    );

    operations
        .unsuspend(&name())
        .expect("unsuspension succeeds");

    let usermod = operations_calls(&operations, "usermod");
    assert!(
        usermod
            .iter()
            .any(|call| call.contains(&"--unlock".to_owned()))
    );
}

/// A login that never had a password is unsuspended WITHOUT the call that
/// refuses it.
///
/// The defect this replaces: every hosting account is passwordless, and
/// `usermod --unlock` on such a login exits **0 on the Debian family and 1 on
/// the RHEL one** (measured on both polygon images, unchanged under `LC_ALL=C`).
/// An unconditional unlock whose non-zero status is a failure therefore made
/// reactivation impossible on the entire RHEL half of the supported matrix.
///
/// If the fix were broken, this line would see a `--unlock` in the argv — the
/// exact call that cannot succeed there.
#[test]
fn unsuspending_a_login_with_no_password_never_runs_the_unlock_that_refuses_it() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&shadow_entry("!")),
    );

    operations
        .unsuspend(&name())
        .expect("unsuspension succeeds");

    let usermod = operations_calls(&operations, "usermod");
    assert!(
        !usermod
            .iter()
            .any(|call| call.contains(&"--unlock".to_owned())),
        "a login with no password has nothing to unlock: {usermod:?}"
    );
    assert!(
        usermod
            .iter()
            .any(|call| call.contains(&"--shell".to_owned())),
        "the shell is still restored: {usermod:?}"
    );
}

/// The same, on the family the defect was fatal on, with that family's spelling.
///
/// `useradd` writes `!` on the Debian family and `!!` on the RHEL one. Both mean
/// "no password", and the RHEL spelling is the one that must not reach
/// `usermod --unlock`.
#[test]
fn on_the_rhel_family_the_double_marker_login_is_unsuspended_without_an_unlock() {
    let operations = AccountOperations::new(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&shadow_entry("!!")),
        adapter_for(DistroFamily::Rhel),
    );

    operations
        .unsuspend(&name())
        .expect("unsuspension succeeds on the RHEL family");

    assert!(
        !operations_calls(&operations, "usermod")
            .iter()
            .any(|call| call.contains(&"--unlock".to_owned()))
    );
}

/// An account whose password is already usable is not unlocked again.
#[test]
fn unsuspending_an_already_unlocked_login_runs_no_unlock() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&shadow_entry(HASHED_PASSWORD)),
    );

    operations
        .unsuspend(&name())
        .expect("unsuspension succeeds");

    assert!(
        !operations_calls(&operations, "usermod")
            .iter()
            .any(|call| call.contains(&"--unlock".to_owned()))
    );
}

/// A real refusal of a real unlock is still a failure.
///
/// The inverse control for the two tests above: the repair must not become
/// "ignore what `usermod --unlock` says", which would swallow "cannot update
/// the password file" along with the harmless refusal.
#[test]
fn unsuspending_fails_when_the_unlock_it_did_run_was_refused() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&shadow_entry(&format!("!{HASHED_PASSWORD}")))
            .failing_next(1)
            .failing_next(0),
    );

    let error = operations
        .unsuspend(&name())
        .expect_err("a refused unlock is a refusal");

    assert!(matches!(error, AccountError::CommandFailed { .. }));
}

/// The shadow entry is read for this account and for no other.
#[test]
fn unsuspending_asks_getent_for_this_accounts_shadow_entry_only() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&shadow_entry("!")),
    );

    operations
        .unsuspend(&name())
        .expect("unsuspension succeeds");

    let getent = operations_calls(&operations, "getent");
    assert_eq!(getent.len(), 1);
    assert_eq!(
        getent[0],
        vec![
            operations.distro().getent_binary().to_owned(),
            "shadow".to_owned(),
            "acme".to_owned(),
        ]
    );
}

/// An entry naming another account is refused rather than classified.
#[test]
fn unsuspending_refuses_a_shadow_entry_that_names_a_different_account() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout("root:!:20704:0:99999:7:::\n"),
    );

    let error = operations
        .unsuspend(&name())
        .expect_err("another account's entry is not an answer about this one");

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}

/// A shadow lookup that fails is not read as "no password".
///
/// "The agent could not tell" and "there is nothing to unlock" are different
/// facts, and only one of them may quietly skip a step.
#[test]
fn unsuspending_refuses_when_the_shadow_lookup_itself_failed() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&shadow_entry("!"))
            .failing_next(2),
    );

    let error = operations
        .unsuspend(&name())
        .expect_err("an unreadable shadow database is a refusal");

    assert!(matches!(error, AccountError::CommandFailed { .. }));
}

/// A `passwd -S` status line for `acme` in `state`.
fn password_status(state: &str) -> String {
    format!("acme {state} 2026-01-01 0 99999 7 -1\n")
}

#[test]
fn a_locked_login_is_observed_as_locked_rather_than_assumed_from_the_suspend_call() {
    let host = RecordingHost::new()
        .with_user("acme")
        .with_stdout(&password_status("L"))
        .with_shadow_field(&format!("!{HASHED_PASSWORD}"));
    let operations = debian(host);

    let state = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect("the state is readable");

    assert!(state.login_locked);
    assert_eq!(state.login_password, StoredPassword::Locked);
    assert_eq!(
        operations
            .host()
            .called(operations.distro().passwd_binary()),
        vec![vec![
            operations.distro().passwd_binary().to_owned(),
            "-S".to_owned(),
            "acme".to_owned(),
        ]],
        "the state is READ with -S; anything else would be setting a password",
    );
}

/// The RHEL family's own spelling of "locked" is recognised.
///
/// `passwd -S` prints `L`/`P` on the Debian family and `LK`/`PS` on the RHEL
/// one — measured on both polygon images, not assumed. An exact comparison
/// against `"L"` was true on Debian and false on every RHEL host, which made
/// this observation report every account there as unlocked and made the panel
/// refuse every suspension on the whole family. Both spellings are pinned here
/// because a test carrying only one of them is what let that ship.
#[test]
fn the_rhel_familys_own_spelling_of_a_locked_password_is_recognised() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&password_status("LK"))
            .with_shadow_field(&format!("!{HASHED_PASSWORD}")),
    );

    let state = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect("the state is readable");

    assert!(state.login_locked);
    assert_eq!(state.login_password, StoredPassword::Locked);
}

/// And its spelling of "not locked" is NOT mistaken for one.
///
/// The inverse control the test above owes: matching a first letter is only
/// safe while no OTHER state begins with it, and `PS` is the state a perfectly
/// usable RHEL password is reported in.
#[test]
fn the_rhel_familys_own_spelling_of_a_usable_password_is_not_read_as_locked() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&password_status("PS"))
            .with_shadow_field(HASHED_PASSWORD),
    );

    let state = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect("the state is readable");

    assert!(!state.login_locked);
    assert_eq!(state.login_password, StoredPassword::Usable);
}

#[test]
fn a_login_with_a_usable_password_is_not_reported_as_locked() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&password_status("P"))
            .with_shadow_field(HASHED_PASSWORD),
    );

    let state = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect("the state is readable");

    assert!(!state.login_locked);
    assert_eq!(state.login_password, StoredPassword::Usable);
}

#[test]
fn a_login_with_no_password_at_all_is_not_reported_as_locked() {
    // `NP` is an account with no password, which is NOT a locked one: any
    // authentication method that does not consult the password — an SSH key
    // already in place — still works against it. Reporting it as locked would
    // certify a suspension that never happened.
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&password_status("NP"))
            .with_shadow_field(""),
    );

    let state = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect("the state is readable");

    assert!(!state.login_locked);
    // The pair that `passwd -S` cannot express: `NP` and an EMPTY shadow field
    // are the same login, and it authenticates with no password at all.
    assert_eq!(state.login_password, StoredPassword::Empty);
}

#[test]
fn a_password_status_line_naming_another_login_is_refused_rather_than_believed() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout("root L 2026-01-01 0 99999 7 -1\n"),
    );

    let error = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect_err("a line about another login says nothing about this one");

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}

#[test]
fn a_password_status_that_cannot_be_read_is_an_error_and_never_an_open_login() {
    // "The agent could not tell" and "the login is open" are different facts,
    // and only one of them may reach a caller deciding whether an account is
    // suspended. Answering `false` here would make an unreadable host look
    // like an un-suspended one, which is the direction that gets acted on.
    let operations = debian(RecordingHost::new().with_user("acme").with_stdout(""));

    let error = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect_err("an unreadable status line is not an answer");

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}

#[test]
fn the_suspension_state_of_an_account_that_does_not_exist_is_not_found() {
    let operations = debian(RecordingHost::new());

    let error = operations
        .suspension_state(
            &FakeSiteHost::passing(),
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect_err("there is no such account");

    assert!(matches!(error, AccountError::NotFound { .. }));
}

#[test]
fn the_suspension_state_carries_the_sites_the_host_actually_serves() {
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout(&password_status("L"))
            .with_shadow_field("!"),
    );
    let site_host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&site_host, &input).expect("the site is created");
    disable_site(&site_host, site_distro(), &input).expect("the site is disabled");

    let state = operations
        .suspension_state(
            &site_host,
            &quiet_cron_host(),
            &FakeSftpHost::new(),
            &name(),
        )
        .expect("the state is readable");

    assert!(state.sites_directory_readable);
    assert_eq!(state.sites.len(), 1);
    assert!(state.sites[0].serving_stub);
    // The ordinary hosting account, and the whole reason the field exists:
    // `passwd -S` says LOCKED while the shadow field says there is no password
    // to unlock. A panel deciding a reactivation from the first refuses every
    // account there is.
    assert!(state.login_locked);
    assert_eq!(state.login_password, StoredPassword::Absent);
}

#[test]
fn suspending_an_account_that_does_not_exist_is_not_found() {
    let operations = debian(RecordingHost::new());

    let error = operations
        .suspend(&name())
        .expect_err("an unknown account is not found");

    assert!(matches!(error, AccountError::NotFound { .. }));
}

#[test]
fn deleting_removes_the_home_tree_and_reports_what_it_freed() {
    let operations = debian(RecordingHost::new().with_user("acme").with_size(4096));

    let freed = operations
        .delete(&FakePhpHost::empty(), &no_databases(), &no_sftp(), &name())
        .expect("deletion succeeds");

    assert_eq!(freed, 4096);
    assert_eq!(
        operations_calls(&operations, "userdel")[0],
        vec![
            tool_path(&operations, "userdel").as_str(),
            "--remove",
            "acme"
        ]
    );
}

#[test]
fn a_quota_is_set_in_kibibyte_blocks_rounded_up() {
    // Rounding down would hand out less than the plan was sold with, and the
    // difference would only ever surface as an unexplained write failure.
    let operations = debian(RecordingHost::new().with_user("acme"));

    operations
        .set_quota(&name(), 1025)
        .expect("the quota is set");

    let setquota = operations_calls(&operations, "setquota");
    assert_eq!(
        setquota[0],
        vec![
            tool_path(&operations, "setquota").as_str(),
            "-u",
            "acme",
            "2",
            "2",
            "0",
            "0",
            "/home"
        ]
    );
}

#[test]
fn usage_reports_the_measured_tree_and_the_hard_limit() {
    let host = RecordingHost::new()
        .with_user("acme")
        .with_size(2048)
        .with_stdout("/dev/sda1 100 5120 5120 0 0 0\n");
    let operations = debian(host);

    let usage = operations.usage(&name()).expect("usage is read");

    assert_eq!(usage.used_bytes, 2048);
    assert_eq!(usage.quota_bytes, 5120 * 1024);
}

#[test]
fn a_filesystem_without_quotas_reports_no_limit_rather_than_failing() {
    let host = RecordingHost::new()
        .with_user("acme")
        .with_size(2048)
        .with_stdout("");
    let operations = debian(host);

    let usage = operations.usage(&name()).expect("usage is read");

    assert_eq!(usage.quota_bytes, 0);
}

/// Reads back what the operations asked the host to run.
fn operations_calls(
    operations: &AccountOperations<RecordingHost>,
    program: &str,
) -> Vec<Vec<String>> {
    operations.host().called(&tool_path(operations, program))
}

/// The absolute path the operations' own adapter names for a tool.
///
/// The tests address tools by their short names, which is how a reader thinks
/// of them, while the operations spawn them by absolute path — a root daemon
/// resolving `useradd` through `PATH` would run whichever one a writable
/// directory earlier in that variable happened to hold. Translating here rather
/// than repeating the literal in each assertion means the tests keep failing if
/// an operation ever goes back to a bare name: the recorded program would be
/// `useradd`, and nothing would be found at the adapter's path.
///
/// # Panics
///
/// Panics on a tool this mapping does not know, which is a test asking about
/// something the accounts area never runs.
fn tool_path(operations: &AccountOperations<RecordingHost>, program: &str) -> String {
    let distro = operations.distro();
    match program {
        "useradd" => distro.useradd_binary(),
        "usermod" => distro.usermod_binary(),
        "userdel" => distro.userdel_binary(),
        "setquota" => distro.setquota_binary(),
        "quota" => distro.quota_binary(),
        "id" => distro.id_binary(),
        "chmod" => distro.chmod_binary(),
        "chgrp" => distro.chgrp_binary(),
        "crontab" => distro.crontab_binary(),
        "passwd" => distro.passwd_binary(),
        "getent" => distro.getent_binary(),
        other => panic!("the accounts area never runs {other}"),
    }
    .to_owned()
}

#[test]
fn the_nologin_shell_comes_from_the_distribution_and_not_from_a_literal() {
    // Debian ships it at /usr/sbin/nologin and RHEL documents /sbin/nologin. A literal in
    // the operation would create every RHEL account with a shell path this agent invented
    // (rules/rust.md "Distro adapter": ops never hard-codes a platform path).
    let on_debian = debian(RecordingHost::new());
    let on_rhel = AccountOperations::new(RecordingHost::new(), adapter_for(DistroFamily::Rhel));

    on_debian.create(&name(), 0).expect("creation succeeds");
    on_rhel.create(&name(), 0).expect("creation succeeds");

    assert!(operations_calls(&on_debian, "useradd")[0].contains(&"/usr/sbin/nologin".to_owned()));
    assert!(operations_calls(&on_rhel, "useradd")[0].contains(&"/sbin/nologin".to_owned()));
}

#[test]
fn a_new_accounts_home_is_group_owned_by_the_web_server_so_a_site_can_be_served() {
    // The defect: `useradd --create-home` leaves the home 0750 acme:acme, and the web
    // server is in no group that can enter it — so a real nginx logs
    // `stat() ... failed (13: Permission denied)` for every document root the agent
    // creates, and no site this panel makes can be served at all.
    let operations = debian(RecordingHost::new());

    operations.create(&name(), 0).expect("creation succeeds");

    let chgrp = operations_calls(&operations, "chgrp");
    assert_eq!(
        chgrp[0],
        vec![
            tool_path(&operations, "chgrp").as_str(),
            "--no-dereference",
            "www-data",
            "/home/acme"
        ],
        "the home must be group-owned by the web server's group, by name from the adapter"
    );
}

#[test]
fn a_new_accounts_home_is_not_opened_to_every_other_local_user() {
    // A traversal bit would fix serving too, and would open the home to every other
    // customer's PHP worker, every FTP session and every cron job on the machine.
    // "Other" is not a principal; it is everyone. So the mode stays 0750 and the
    // traversal is granted to the web server's group alone.
    let operations = debian(RecordingHost::new());

    operations.create(&name(), 0).expect("creation succeeds");

    let chmod = operations_calls(&operations, "chmod");
    assert_eq!(
        chmod[0],
        vec![
            tool_path(&operations, "chmod").as_str(),
            "0750",
            "/home/acme"
        ]
    );
    for call in operations.host().calls() {
        assert!(
            !call.iter().any(|argument| argument.contains("o+")
                || argument == "0751"
                || argument == "0755"),
            "no step of creation may grant anything to other: {call:?}"
        );
    }
}

#[test]
fn the_web_server_group_is_the_familys_own_never_a_literal() {
    // The RHEL family's web server is `nginx`, not `www-data`. An account created on
    // AlmaLinux with a Debian group name is created successfully — `chgrp` is the only
    // thing that would refuse — and the customer finds out when their site 403s.
    let on_rhel = AccountOperations::new(RecordingHost::new(), adapter_for(DistroFamily::Rhel));

    on_rhel.create(&name(), 0).expect("creation succeeds");

    let chgrp = operations_calls(&on_rhel, "chgrp");
    assert_eq!(
        chgrp[0],
        vec![
            tool_path(&on_rhel, "chgrp").as_str(),
            "--no-dereference",
            "nginx",
            "/home/acme"
        ]
    );
}

#[test]
fn deleting_an_account_takes_its_php_pools_with_it() {
    // The trap this closes: a pool file names the account it runs as, php-fpm
    // resolves that name at startup, and once the account is gone `php-fpm -t`
    // answers `cannot get uid for user '<account>'` and the master refuses to
    // start or reload AT ALL. One deleted customer therefore left a file that
    // took PHP down for every tenant on the server at the next unrelated
    // reload, hours or days later.
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    write_pool(
        &php_host,
        adapter_for(DistroFamily::Debian),
        &PoolInput {
            account: name(),
            version: PhpVersion::parse("8.3").expect("a supported version"),
            max_children: 5,
            overrides: Vec::new(),
        },
    )
    .expect("the fixture pool is written");
    let operations = debian(RecordingHost::new().with_user("acme"));

    operations
        .delete(&php_host, &no_databases(), &no_sftp(), &name())
        .expect("deletion succeeds");

    assert!(
        php_host
            .config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
            .is_none(),
        "the account's pool must be gone once the account is"
    );
}

#[test]
fn a_pool_that_cannot_be_removed_stops_the_deletion_rather_than_orphaning_the_pool() {
    // This is also where the ORDER is pinned, and the order is the whole of the
    // risk. A refused pool removal can only stop `userdel` if the removal comes
    // FIRST; were `userdel` to run first, this assertion could not hold. And
    // first is the only safe order: while the account still exists every pool
    // file is valid, so `php-fpm -t` passes and each master reloads cleanly,
    // whereas after `userdel` every remaining pool names a user that no longer
    // resolves — the removal protocol validates AFTER unlinking, so it would put
    // the file back and the pool would become unremovable by the very operation
    // meant to remove it.
    //
    // The recoverable half is chosen deliberately: an account that is still
    // there can be deleted again once whatever refused is fixed, whereas an
    // account that is gone with its pool left behind cannot be repaired by any
    // operation this agent has.
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    write_pool(
        &php_host,
        adapter_for(DistroFamily::Debian),
        &PoolInput {
            account: name(),
            version: PhpVersion::parse("8.3").expect("a supported version"),
            max_children: 5,
            overrides: Vec::new(),
        },
    )
    .expect("the fixture pool is written");
    php_host.reject_validation("php-fpm will not have it");
    let operations = debian(RecordingHost::new().with_user("acme"));

    let refusal = operations.delete(&php_host, &no_databases(), &no_sftp(), &name());

    assert!(
        matches!(refusal, Err(AccountError::PoolRemoval { .. })),
        "expected PoolRemoval, got {refusal:?}"
    );
    assert!(
        operations_calls(&operations, "userdel").is_empty(),
        "userdel must NOT have run: the account stays, which is the state that can be retried"
    );
}

#[test]
fn deleting_an_account_that_never_ran_php_reloads_nothing() {
    // A static-only customer is the common case, and a deletion that restarted
    // six php-fpm masters to remove nothing would make every such deletion a
    // small outage for every other tenant on the box.
    let php_host = FakePhpHost::with_installed(&["8.3"]);
    let operations = debian(RecordingHost::new().with_user("acme"));

    operations
        .delete(&php_host, &no_databases(), &no_sftp(), &name())
        .expect("deletion succeeds");

    assert_eq!(php_host.removals(), 0);
    assert_eq!(php_host.commands(), Vec::<Vec<String>>::new());
}

/// `acme`'s jail, derived exactly as the deletion derives it.
fn acme_jail() -> AccountJail {
    AccountJail::for_account(
        &name(),
        adapter_for(DistroFamily::Debian).systemd_unit_directory(),
    )
}

/// A database server holding `acme`'s `shop` database and its user.
fn databases_of_acme() -> FakeDbHost {
    let host = FakeDbHost::new();
    create_database(
        &host,
        &CreateDatabaseRequest {
            database: DatabaseName::for_account(&name(), "shop").expect("a valid database name"),
            user: DbUserName::for_account(&name(), "shopuser").expect("a valid user name"),
            password: Password::parse("Gen3rated-pw").expect("a valid password"),
        },
    )
    .expect("the fixture database is created");

    host
}

/// A host holding `acme`'s login, her jail and her mount unit.
fn sftp_of_acme() -> FakeSftpHost {
    let jail = acme_jail();

    FakeSftpHost::new()
        .with_login("acme_web")
        .with_path(jail.mount_point())
        .with_path(jail.directory())
        .with_path(jail.unit_path())
}

#[test]
fn deleting_an_account_takes_its_databases_with_it() {
    // `userdel` touches neither MySQL nor sshd. Before this, deleting `acme`
    // left `acme_shop` on the server with the customer's rows in it AND
    // `acme_shopuser` able to reach them — and system user names are recycled,
    // so the next account created as `acme` inherited both.
    let db_host = databases_of_acme();
    let operations = debian(RecordingHost::new().with_user("acme"));

    operations
        .delete(&FakePhpHost::empty(), &db_host, &no_sftp(), &name())
        .expect("deletion succeeds");

    assert!(db_host.databases().is_empty());
    assert!(db_host.users().is_empty());
}

#[test]
fn a_database_that_cannot_be_dropped_stops_the_deletion_rather_than_orphaning_it() {
    // The order is pinned here as much as the abort is: a refused drop can only
    // stop `userdel` if the drop comes FIRST. The recoverable half is chosen
    // deliberately — an account that is still there can be deleted again, while
    // an orphaned database handed to the next tenant of that name cannot be
    // repaired by any operation this agent has.
    let db_host = FakeDbHost::failing_with(2013, "Lost connection to server");
    let operations = debian(RecordingHost::new().with_user("acme"));

    let refusal = operations.delete(&FakePhpHost::empty(), &db_host, &no_sftp(), &name());

    assert!(
        matches!(refusal, Err(AccountError::DatabaseRemoval { .. })),
        "expected DatabaseRemoval, got {refusal:?}"
    );
    assert!(
        operations_calls(&operations, "userdel").is_empty(),
        "userdel must NOT have run: the account stays, which is the state that can be retried"
    );
}

#[test]
fn deleting_an_account_takes_its_sftp_logins_and_its_jail_with_it() {
    // The login is a working credential into the account's home, and the jail
    // holds a bind mount of that home. Left behind, both are inherited by a
    // re-created account of the same name.
    let sftp_host = sftp_of_acme();
    let operations = debian(RecordingHost::new().with_user("acme"));

    operations
        .delete(&FakePhpHost::empty(), &no_databases(), &sftp_host, &name())
        .expect("deletion succeeds");

    assert!(sftp_host.users().is_empty());
    assert!(
        sftp_host.paths().is_empty(),
        "the jail and its unit must be gone: {:?}",
        sftp_host.paths()
    );
}

#[test]
fn a_jail_that_cannot_be_taken_down_stops_the_deletion_before_userdel_removes_the_home() {
    // The sharpest ordering claim in the cascade. `userdel --remove` on an
    // account whose home is still bind-mounted into its jail would delete the
    // customer's files from inside the mount, and a mount that outlived the
    // account points at a home that no longer exists — which the uninstaller
    // refuses to clean up and a re-created account would inherit.
    let sftp_host = sftp_of_acme().refuse_removal_of(acme_jail().mount_point());
    let operations = debian(RecordingHost::new().with_user("acme"));

    let refusal = operations.delete(&FakePhpHost::empty(), &no_databases(), &sftp_host, &name());

    assert!(
        matches!(refusal, Err(AccountError::SftpRemoval { .. })),
        "expected SftpRemoval, got {refusal:?}"
    );
    assert!(
        operations_calls(&operations, "userdel").is_empty(),
        "userdel must NOT have run while the account's home is still mounted into its jail"
    );
}

#[test]
fn every_program_the_accounts_area_runs_is_named_by_an_absolute_path() {
    // The rule this asserts is not "useradd lives in /usr/sbin" — the tests above
    // already pin each argv. It is the property those tests share and none of them
    // states: a root daemon that spawns a program by a BARE name resolves it
    // through PATH, and runs whichever binary the first writable directory in that
    // variable happens to hold. Every one of these tools is run as uid 0, so the
    // first such name is a local root escalation, and it would be added by someone
    // doing the obvious thing — copying the line above it.
    //
    // Written against a single sweep of every operation rather than per call site,
    // because a per-site assertion is exactly what a new call site does not have.
    // Two hosts, because creation refuses an account that is already there while
    // every other operation refuses one that is not: a single fixture could only
    // ever sweep half of them.
    let creating = debian(RecordingHost::new());
    creating.create(&name(), 1024).expect("creation succeeds");

    // The shadow entry is what `unsuspend` reads first, so this fixture's stdout
    // has to be one; the sweep is about the argv of every program, not about what
    // any single one of them printed.
    let existing = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_size(4096)
            .with_stdout(&shadow_entry("!")),
    );
    existing.suspend(&name()).expect("suspension succeeds");
    existing.unsuspend(&name()).expect("unsuspension succeeds");
    existing.set_quota(&name(), 2048).expect("the quota is set");
    let _ = existing.usage(&name());
    // Its stdout is not what this sweep is about — the call is recorded either
    // way, and `passwd` is a program run as root like every other one here.
    let _ = existing.suspension_state(
        &FakeSiteHost::passing(),
        &quiet_cron_host(),
        &FakeSftpHost::new(),
        &name(),
    );
    existing
        .delete(&FakePhpHost::empty(), &no_databases(), &no_sftp(), &name())
        .expect("deletion succeeds");

    let mut calls = creating.host().calls();
    calls.extend(existing.host().calls());
    assert!(
        !calls.is_empty(),
        "the sweep ran no programs at all, so it proves nothing about how they are named",
    );

    for call in &calls {
        let program = call
            .first()
            .expect("a recorded call always carries its program");
        assert!(
            program.starts_with('/'),
            "{program} is run by a bare name: as root, PATH decides which binary that is",
        );
    }
}

#[test]
fn deleting_an_account_takes_its_crontab_with_it_before_userdel_runs() {
    // `userdel` removes neither family's cron spool file — measured on both
    // polygon images — and cron keys that file by the account's NAME, which the
    // host recycles. So a deletion that skipped this step leaves a schedule
    // behind that the next account of the same name inherits whole, and that
    // the panel then renders on that account's own scheduled-tasks screen.
    let operations = debian(RecordingHost::new().with_user("acme"));

    operations
        .delete(&FakePhpHost::empty(), &no_databases(), &no_sftp(), &name())
        .expect("deletion succeeds");

    assert_eq!(
        operations_calls(&operations, "crontab")[0],
        vec![
            tool_path(&operations, "crontab").as_str(),
            "-u",
            "acme",
            "-r"
        ],
        "the table must be removed through crontab(1), by the account's name"
    );

    // Order, and it is the whole of the safety argument. `crontab -u <name>`
    // refuses a name the password database no longer holds, so the removal has
    // to happen while the account is still there — and doing it then is also
    // what makes the name unambiguous: it still resolves to THIS account, not
    // to whoever takes its uid afterwards.
    let calls = operations.host().calls();
    let crontab = calls
        .iter()
        .position(|call| call[0] == tool_path(&operations, "crontab"))
        .expect("the crontab removal must have run");
    let userdel = calls
        .iter()
        .position(|call| call[0] == tool_path(&operations, "userdel"))
        .expect("userdel must have run");
    assert!(
        crontab < userdel,
        "the crontab must be removed while the account still exists to name"
    );
}

#[test]
fn an_account_that_never_had_a_crontab_is_still_deleted() {
    // The normal case, and the one that would turn this cleanup into a worse
    // defect than the leak. Both cron lineages exit non-zero and print
    // `no crontab for <account>` for an account with no table, so a step that
    // read that as a refusal would make deleting an ordinary account impossible.
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stderr("no crontab for acme\n")
            .failing_next(1),
    );

    operations
        .delete(&FakePhpHost::empty(), &no_databases(), &no_sftp(), &name())
        .expect("an account with no crontab is deleted normally");

    assert!(
        !operations_calls(&operations, "userdel").is_empty(),
        "userdel must still run for an account that never had a crontab"
    );
}

#[test]
fn a_crontab_that_cannot_be_removed_stops_the_deletion_before_userdel() {
    // The recoverable half of the failure, the same choice every other step in
    // this cascade makes: an account that is still there can be deleted again,
    // while a crontab orphaned under a name the host will recycle cannot be
    // repaired by any operation this agent has — nothing points at it any more.
    let operations = debian(RecordingHost::new().with_user("acme").failing_next(15));

    let refusal = operations.delete(&FakePhpHost::empty(), &no_databases(), &no_sftp(), &name());

    match refusal {
        Err(AccountError::CommandFailed {
            program,
            status,
            stderr,
        }) => {
            assert_eq!(program, tool_path(&operations, "crontab"));
            assert_eq!(status, 15);
            assert_eq!(stderr, "refused");
        }
        other => panic!("expected a refusing crontab to fail the deletion, got {other:?}"),
    }

    assert!(
        operations_calls(&operations, "userdel").is_empty(),
        "userdel must NOT have run while the account's crontab is still in the spool"
    );
}

#[test]
fn the_absent_crontab_sentence_is_believed_only_on_the_stream_the_account_cannot_write() {
    // `crontab -l` prints the account's OWN table on standard output, so an
    // account that put `no crontab for acme` in its crontab could otherwise
    // decide what this step concludes — and what it would decide is "there was
    // nothing to remove", which is the fail-open direction. The sentence is
    // matched in standard error and nowhere else.
    let operations = debian(
        RecordingHost::new()
            .with_user("acme")
            .with_stdout("no crontab for acme\n")
            .failing_next(1),
    );

    let refusal = operations.delete(&FakePhpHost::empty(), &no_databases(), &no_sftp(), &name());

    assert!(
        matches!(refusal, Err(AccountError::CommandFailed { .. })),
        "a customer's own bytes must not turn a refusal into a success: {refusal:?}"
    );
    assert!(
        operations_calls(&operations, "userdel").is_empty(),
        "userdel must not run when the crontab removal was only believed to have worked"
    );
}

/// A cron host holding no crontab at all.
///
/// The cron half of the suspension state has its own tests against a fake
/// carrying real crontab text (`inspect_account_cron_tests.rs`); what the tests
/// in this file are about is the LOGIN half and the site half, so cron answers
/// the state every account has before the panel installs anything.
fn quiet_cron_host() -> RecordingCronHost {
    RecordingCronHost::new()
}
