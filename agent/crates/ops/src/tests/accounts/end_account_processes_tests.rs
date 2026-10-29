//! Tests for [`end_account_processes`]: the bounded kill-and-verify loop that
//! runs before `userdel`.
//!
//! Driven by a scripted host that answers each `pkill` spawn from a queue and
//! records the argv and every pause, so the loop's bound is read from numbers
//! and no test waits (rules/testing.md "Determinism").

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::VecDeque;
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};

use crate::accounts::end_account_processes::end_account_processes;
use crate::accounts::model::home_metadata::HomeMetadata;
use crate::accounts::{AccountError, SystemHost};

/// The uid the fixture account runs as.
const UID: u32 = 1001;

/// A host that answers every spawn from a script of exit statuses.
struct ScriptedHost {
    /// The exit statuses the next spawns answer with; `1` ("nothing matched")
    /// once the script is empty.
    statuses: Mutex<VecDeque<i32>>,
    /// Every spawn as `(argv without the program, call class)`.
    spawns: Mutex<Vec<(Vec<String>, CallClass)>>,
    /// Every program spawned, in order.
    programs: Mutex<Vec<String>>,
    /// Every interval paused for.
    pauses: Mutex<Vec<Duration>>,
    /// Whether every spawn fails to start.
    unavailable: bool,
}

impl ScriptedHost {
    /// A host whose first spawns answer `statuses` in order.
    fn answering(statuses: &[i32]) -> Self {
        Self {
            statuses: Mutex::new(statuses.iter().copied().collect()),
            spawns: Mutex::new(Vec::new()),
            programs: Mutex::new(Vec::new()),
            pauses: Mutex::new(Vec::new()),
            unavailable: false,
        }
    }

    /// A host on which `pkill` cannot be started.
    fn without_pkill() -> Self {
        Self {
            unavailable: true,
            ..Self::answering(&[])
        }
    }

    /// The argv of every spawn, in order.
    fn argvs(&self) -> Vec<Vec<String>> {
        self.spawns
            .lock()
            .unwrap()
            .iter()
            .map(|(argv, _)| argv.clone())
            .collect()
    }

    /// How many spawns were the kill (signal `KILL`).
    fn kills(&self) -> usize {
        self.argvs()
            .iter()
            .filter(|a| a.get(1).is_some_and(|s| s == "KILL"))
            .count()
    }

    /// Every interval paused for.
    fn pauses(&self) -> Vec<Duration> {
        self.pauses.lock().unwrap().clone()
    }
}

impl SystemHost for ScriptedHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, AccountError> {
        if self.unavailable {
            return Err(AccountError::CommandUnavailable {
                program: program.to_string(),
                reason: "not installed".to_owned(),
            });
        }
        self.programs.lock().unwrap().push(program.to_string());
        self.spawns
            .lock()
            .unwrap()
            .push((arguments.iter().map(|a| (*a).to_owned()).collect(), class));
        let status = self.statuses.lock().unwrap().pop_front().unwrap_or(1);
        Ok(CommandOutcome {
            status,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    fn user_exists(&self, _username: &str) -> Result<bool, AccountError> {
        unreachable!("the process cull asks nothing about users")
    }

    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        unreachable!("the process cull measures nothing")
    }

    fn read_password_database(&self, _path: &str) -> Result<String, AccountError> {
        unreachable!("the process cull is handed its account already resolved")
    }

    fn read_mounts(&self) -> Result<String, AccountError> {
        unreachable!("the process cull reads no mounts")
    }

    fn home_metadata(&self, _path: &str) -> Result<Option<HomeMetadata>, AccountError> {
        unreachable!("the process cull inspects no home")
    }

    fn pause(&self, interval: Duration) {
        self.pauses.lock().unwrap().push(interval);
    }
}

/// The distribution the argv is pinned against.
fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The proof the cull is handed, resolved through the real managed-account type
/// from the one password row the account has.
fn managed() -> ManagedAccount {
    let name = AccountName::parse("acme").expect("the fixture name is valid");
    let passwd = format!("acme:x:{UID}:{UID}::/home/acme:/usr/sbin/nologin\n");

    ManagedAccount::resolve(name, &passwd).expect("acme is a hosting account")
}

/// The argv `pkill` is expected to be given to kill, after the program.
fn kill_argv() -> Vec<String> {
    ["--signal", "KILL", "--count", "--uid", &UID.to_string()]
        .map(str::to_owned)
        .to_vec()
}

/// The argv `pgrep` is expected to be given to check, after the program: every
/// state but zombie.
fn probe_argv() -> Vec<String> {
    [
        "--count",
        "--runstates",
        "D,R,S,T,t,W,X,I,P",
        "--uid",
        &UID.to_string(),
    ]
    .map(str::to_owned)
    .to_vec()
}

#[test]
fn the_cull_kills_then_checks_with_the_exact_argv_of_the_managed_uid() {
    let host = ScriptedHost::answering(&[0, 1]);

    end_account_processes(&host, distro(), &managed()).expect("nothing is left");

    assert_eq!(host.argvs(), vec![kill_argv(), probe_argv()]);
}

#[test]
fn an_account_with_no_processes_is_done_after_one_round_without_pausing() {
    let host = ScriptedHost::answering(&[1, 1]);

    end_account_processes(&host, distro(), &managed()).expect("nothing is left");

    assert_eq!(host.argvs().len(), 2);
    assert!(host.pauses().is_empty());
}

#[test]
fn every_pkill_runs_in_the_mutation_class() {
    let host = ScriptedHost::answering(&[0, 1]);

    end_account_processes(&host, distro(), &managed()).expect("nothing is left");

    assert!(
        host.spawns
            .lock()
            .unwrap()
            .iter()
            .all(|(_, class)| *class == CallClass::Mutation)
    );
}

#[test]
fn a_survivor_that_is_gone_on_the_second_round_is_killed_again_and_the_cull_completes() {
    // Round 1: kill, check finds one. Round 2: kill, check finds none.
    let host = ScriptedHost::answering(&[0, 0, 0, 1]);

    end_account_processes(&host, distro(), &managed()).expect("the second round is clean");

    assert_eq!(host.kills(), 2, "the kill is repeated every round");
    assert_eq!(host.pauses(), vec![Duration::from_millis(200)]);
}

#[test]
fn survivors_in_every_round_refuse_with_processes_remain_after_five_kills_and_four_pauses() {
    let host = ScriptedHost::answering(&[0; 10]);

    let error =
        end_account_processes(&host, distro(), &managed()).expect_err("the survivor is never gone");

    assert!(
        matches!(error, AccountError::ProcessesRemain { ref username } if username == "acme"),
        "{error:?}"
    );
    assert_eq!(host.kills(), 5);
    assert_eq!(host.pauses().len(), 4, "no pause after the last check");
}

#[test]
fn a_kill_that_exits_with_an_unknown_status_is_a_refusal_not_an_empty_account() {
    let host = ScriptedHost::answering(&[2]);

    let error = end_account_processes(&host, distro(), &managed())
        .expect_err("status 2 says nothing about what was signalled");

    assert!(
        matches!(error, AccountError::CommandFailed { status: 2, .. }),
        "{error:?}"
    );
    assert_eq!(host.argvs().len(), 1, "no check follows a refused kill");
}

#[test]
fn a_check_that_exits_with_an_unknown_status_is_a_refusal_not_proof_of_absence() {
    let host = ScriptedHost::answering(&[0, 3]);

    let error = end_account_processes(&host, distro(), &managed())
        .expect_err("status 3 from the check proves nothing");

    assert!(
        matches!(error, AccountError::CommandFailed { status: 3, .. }),
        "{error:?}"
    );
}

#[test]
fn a_pkill_that_cannot_be_started_fails_the_cull() {
    let host = ScriptedHost::without_pkill();

    let error = end_account_processes(&host, distro(), &managed())
        .expect_err("no pkill means nothing was killed");

    assert!(
        matches!(error, AccountError::CommandUnavailable { .. }),
        "{error:?}"
    );
}

#[test]
fn the_kill_runs_pkill_and_the_check_runs_pgrep_so_a_zombie_is_not_counted() {
    let host = ScriptedHost::answering(&[0, 1]);

    end_account_processes(&host, distro(), &managed()).expect("nothing is left");

    assert_eq!(
        *host.programs.lock().unwrap(),
        vec![
            distro().pkill_binary().to_string(),
            distro().pgrep_binary().to_string()
        ]
    );
}
