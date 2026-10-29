//! Tests for [`end_account_user_manager`]: the lingering and user-manager end
//! that runs before `userdel`.
//!
//! Driven by a host holding a set of present state paths, recording every spawn
//! and every pause, so the poll's bound is read from numbers and no test waits
//! (rules/testing.md "Determinism").

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashSet;
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};

use crate::accounts::end_account_user_manager::end_account_user_manager;
use crate::accounts::model::home_metadata::HomeMetadata;
use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;
use crate::accounts::{AccountError, SystemHost};

/// The uid the fixture account runs as.
const UID: u32 = 1001;

/// The linger file of the fixture account.
const LINGER: &str = "/var/lib/systemd/linger/acme";

/// The runtime directory of the fixture account.
const RUNTIME: &str = "/run/user/1001";

/// A host with a set of present paths and scripted `loginctl` behaviour.
struct StateHost {
    /// The paths that currently exist.
    present: Mutex<HashSet<String>>,
    /// Every spawn as argv without the program.
    spawns: Mutex<Vec<Vec<String>>>,
    /// Every interval paused for.
    pauses: Mutex<Vec<Duration>>,
    /// Whether `disable-linger` really removes the linger file.
    disable_removes: bool,
    /// How many looks at the runtime directory after `terminate-user` still
    /// find it; `None` means it never goes away.
    runtime_looks_until_gone: Option<u32>,
    /// Whether `terminate-user` has run.
    terminated: Mutex<bool>,
    /// Looks at the runtime directory since the termination.
    looks: Mutex<u32>,
    /// The exit status `loginctl` answers with.
    status: i32,
}

impl StateHost {
    /// A host on which `present` exists and `loginctl` succeeds.
    fn with(present: &[&str]) -> Self {
        Self {
            present: Mutex::new(present.iter().map(|p| (*p).to_owned()).collect()),
            spawns: Mutex::new(Vec::new()),
            pauses: Mutex::new(Vec::new()),
            disable_removes: true,
            runtime_looks_until_gone: Some(0),
            terminated: Mutex::new(false),
            looks: Mutex::new(0),
            status: 0,
        }
    }

    /// The argv of every spawn, in order.
    fn argvs(&self) -> Vec<Vec<String>> {
        self.spawns.lock().unwrap().clone()
    }

    /// Every interval paused for.
    fn pauses(&self) -> Vec<Duration> {
        self.pauses.lock().unwrap().clone()
    }
}

impl SystemHost for StateHost {
    fn run(
        &self,
        _program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, AccountError> {
        self.spawns
            .lock()
            .unwrap()
            .push(arguments.iter().map(|a| (*a).to_owned()).collect());
        if self.status == 0 {
            match arguments.first().copied() {
                Some("disable-linger") if self.disable_removes => {
                    self.present.lock().unwrap().remove(LINGER);
                }
                Some("terminate-user") => *self.terminated.lock().unwrap() = true,
                _ => {}
            }
        }
        Ok(CommandOutcome {
            status: self.status,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    fn user_exists(&self, _username: &str) -> Result<bool, AccountError> {
        unreachable!("the user manager end asks nothing about users")
    }

    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        unreachable!("the user manager end measures nothing")
    }

    fn read_password_database(&self, _path: &str) -> Result<String, AccountError> {
        unreachable!("the user manager end is handed its account already resolved")
    }

    fn read_mounts(&self) -> Result<String, AccountError> {
        unreachable!("the user manager end reads no mounts")
    }

    fn home_metadata(&self, path: &str) -> Result<Option<HomeMetadata>, AccountError> {
        if path == RUNTIME && *self.terminated.lock().unwrap() {
            let mut looks = self.looks.lock().unwrap();
            *looks += 1;
            if self
                .runtime_looks_until_gone
                .is_some_and(|until| *looks > until)
            {
                self.present.lock().unwrap().remove(RUNTIME);
            }
        }
        Ok(self
            .present
            .lock()
            .unwrap()
            .contains(path)
            .then_some(HomeMetadata {
                is_symlink: false,
                is_directory: true,
                owner_uid: UID,
                group_gid: UID,
                device: 1,
            }))
    }

    fn read_sysv_ipc_table(&self, _kind: SysvIpcKind) -> Result<String, AccountError> {
        unreachable!("the user manager end reads no IPC table")
    }

    fn pause(&self, interval: Duration) {
        self.pauses.lock().unwrap().push(interval);
    }
}

/// The distribution the argv is pinned against.
fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The proof the operation is handed, resolved through the real managed-account type.
fn managed() -> ManagedAccount {
    let name = AccountName::parse("acme").expect("the fixture name is valid");
    let passwd = format!("acme:x:{UID}:{UID}::/home/acme:/usr/sbin/nologin\n");

    ManagedAccount::resolve(name, &passwd).expect("acme is a hosting account")
}

/// The argv of the linger removal.
fn disable_argv() -> Vec<String> {
    vec!["disable-linger".to_owned(), "acme".to_owned()]
}

/// The argv of the manager termination.
fn terminate_argv() -> Vec<String> {
    vec!["terminate-user".to_owned(), UID.to_string()]
}

#[test]
fn an_account_with_no_linger_file_and_no_runtime_directory_runs_nothing() {
    let host = StateHost::with(&[]);

    end_account_user_manager(&host, distro(), &managed()).expect("nothing to end");

    assert!(host.argvs().is_empty());
    assert!(host.pauses().is_empty());
}

#[test]
fn a_linger_file_alone_is_disabled_with_the_account_name_and_nothing_else_runs() {
    let host = StateHost::with(&[LINGER]);

    end_account_user_manager(&host, distro(), &managed()).expect("the linger file is gone");

    assert_eq!(host.argvs(), vec![disable_argv()]);
}

#[test]
fn a_runtime_directory_alone_is_terminated_by_uid_and_waited_for() {
    let host = StateHost::with(&[RUNTIME]);

    end_account_user_manager(&host, distro(), &managed()).expect("the runtime directory is gone");

    assert_eq!(host.argvs(), vec![terminate_argv()]);
}

#[test]
fn both_states_are_ended_linger_first_then_the_manager() {
    let host = StateHost::with(&[LINGER, RUNTIME]);

    end_account_user_manager(&host, distro(), &managed()).expect("both are gone");

    assert_eq!(host.argvs(), vec![disable_argv(), terminate_argv()]);
}

#[test]
fn a_runtime_directory_that_goes_away_on_the_third_look_succeeds_after_two_pauses() {
    let mut host = StateHost::with(&[RUNTIME]);
    host.runtime_looks_until_gone = Some(2);

    end_account_user_manager(&host, distro(), &managed()).expect("it went away in time");

    assert_eq!(host.pauses(), vec![Duration::from_millis(200); 2]);
}

#[test]
fn a_runtime_directory_that_never_goes_away_refuses_after_ten_looks_and_nine_pauses() {
    let mut host = StateHost::with(&[RUNTIME]);
    host.runtime_looks_until_gone = None;

    let error =
        end_account_user_manager(&host, distro(), &managed()).expect_err("the manager never stops");

    assert!(
        matches!(error, AccountError::UserManagerRemains { ref username } if username == "acme"),
        "{error:?}"
    );
    assert_eq!(host.pauses().len(), 9, "no pause after the last look");
}

#[test]
fn a_linger_file_that_survives_disable_linger_refuses_without_terminating() {
    let mut host = StateHost::with(&[LINGER, RUNTIME]);
    host.disable_removes = false;

    let error = end_account_user_manager(&host, distro(), &managed())
        .expect_err("the linger file is still there");

    assert!(
        matches!(error, AccountError::UserManagerRemains { ref username } if username == "acme"),
        "{error:?}"
    );
    assert_eq!(host.argvs(), vec![disable_argv()]);
}

#[test]
fn a_loginctl_that_exits_non_zero_while_state_exists_is_a_command_failure() {
    let mut host = StateHost::with(&[LINGER]);
    host.status = 1;

    let error =
        end_account_user_manager(&host, distro(), &managed()).expect_err("loginctl refused");

    assert!(
        matches!(error, AccountError::CommandFailed { status: 1, .. }),
        "{error:?}"
    );
}

#[test]
fn a_failing_terminate_user_is_a_command_failure_and_is_not_polled() {
    let mut host = StateHost::with(&[RUNTIME]);
    host.status = 1;

    let error =
        end_account_user_manager(&host, distro(), &managed()).expect_err("loginctl refused");

    assert!(
        matches!(error, AccountError::CommandFailed { status: 1, .. }),
        "{error:?}"
    );
    assert!(host.pauses().is_empty());
}
