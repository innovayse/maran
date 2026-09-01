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

use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;

use maran_distro::{DistroFamily, adapter_for};

use crate::accounts::{AccountError, AccountOperations, CommandOutcome, SystemHost};
use std::path::Path;

use crate::php::fake_php_host::FakePhpHost;
use crate::php::model::pool_input::PoolInput;
use crate::php::write_pool;

/// A machine that records what it was asked to do instead of doing it.
///
/// Not a mock with expectations: the tests below assert on the recorded argv, which
/// is the thing worth pinning. `useradd --create-home` and `useradd -m` differ by
/// nothing a type system can see and by everything a customer's data can.
struct RecordingHost {
    existing: Mutex<HashSet<String>>,
    calls: Mutex<Vec<Vec<String>>>,
    statuses: Mutex<Vec<i32>>,
    stdout: Mutex<String>,
    size: u64,
}

impl RecordingHost {
    fn new() -> Self {
        Self {
            existing: Mutex::new(HashSet::new()),
            calls: Mutex::new(Vec::new()),
            statuses: Mutex::new(Vec::new()),
            stdout: Mutex::new("1001\n".to_owned()),
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

    fn failing_next(self, status: i32) -> Self {
        self.statuses
            .lock()
            .expect("the fixture lock is never poisoned")
            .push(status);
        self
    }

    fn calls(&self) -> Vec<Vec<String>> {
        self.calls
            .lock()
            .expect("the fixture lock is never poisoned")
            .clone()
    }

    fn called(&self, program: &str) -> Vec<Vec<String>> {
        self.calls()
            .into_iter()
            .filter(|call| call[0] == program)
            .collect()
    }
}

impl SystemHost for RecordingHost {
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, AccountError> {
        let mut call = vec![program.to_owned()];
        call.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls
            .lock()
            .expect("the fixture lock is never poisoned")
            .push(call);

        let status = self
            .statuses
            .lock()
            .expect("the fixture lock is never poisoned")
            .pop()
            .unwrap_or(0);
        Ok(CommandOutcome {
            status,
            stdout: self
                .stdout
                .lock()
                .expect("the fixture lock is never poisoned")
                .clone(),
            stderr: if status == 0 {
                String::new()
            } else {
                "refused\n".to_owned()
            },
        })
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
            "useradd",
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
            assert_eq!(program, "useradd");
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

#[test]
fn unsuspending_reverses_exactly_what_suspending_did() {
    let operations = debian(RecordingHost::new().with_user("acme"));

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
        .delete(&FakePhpHost::empty(), &name())
        .expect("deletion succeeds");

    assert_eq!(freed, 4096);
    assert_eq!(
        operations_calls(&operations, "userdel")[0],
        vec!["userdel", "--remove", "acme"]
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
        vec!["setquota", "-u", "acme", "2", "2", "0", "0", "/home"]
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
    operations.host().called(program)
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
        vec!["chgrp", "--no-dereference", "www-data", "/home/acme"],
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
    assert_eq!(chmod[0], vec!["chmod", "0750", "/home/acme"]);
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
        vec!["chgrp", "--no-dereference", "nginx", "/home/acme"]
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
        .delete(&php_host, &name())
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

    let refusal = operations.delete(&php_host, &name());

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
        .delete(&php_host, &name())
        .expect("deletion succeeds");

    assert_eq!(php_host.removals(), 0);
    assert_eq!(php_host.commands(), Vec::<Vec<String>>::new());
}
