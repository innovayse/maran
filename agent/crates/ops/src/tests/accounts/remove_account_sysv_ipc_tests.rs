//! Tests for [`remove_account_sysv_ipc`]: select the uid's objects, `ipcrm`
//! each, and prove none is left.
//!
//! Driven by a host whose three tables are scripted per read, recording every
//! spawn, so no test touches a real kernel table.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::VecDeque;
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};

use crate::accounts::model::home_metadata::HomeMetadata;
use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;
use crate::accounts::remove_account_sysv_ipc::remove_account_sysv_ipc;
use crate::accounts::{AccountError, SystemHost};

/// The uid the fixture account runs as.
const UID: u32 = 1001;

/// A host with per-kind scripted table reads and a scripted `ipcrm` status.
struct IpcHost {
    /// For each kind, the texts successive reads answer; the last repeats.
    tables: Mutex<Vec<(SysvIpcKind, VecDeque<String>)>>,
    /// The exit status every `ipcrm` answers.
    status: i32,
    /// Every spawn's argv (without the program).
    spawns: Mutex<Vec<Vec<String>>>,
}

impl IpcHost {
    /// A host whose reads of each kind answer the given texts in order.
    fn new(shm: &[&str], msg: &[&str], sem: &[&str], status: i32) -> Self {
        let queue = |texts: &[&str]| texts.iter().map(|t| (*t).to_owned()).collect();
        Self {
            tables: Mutex::new(vec![
                (SysvIpcKind::Shm, queue(shm)),
                (SysvIpcKind::Msg, queue(msg)),
                (SysvIpcKind::Sem, queue(sem)),
            ]),
            status,
            spawns: Mutex::new(Vec::new()),
        }
    }

    /// Every spawn's argv, in order.
    fn argvs(&self) -> Vec<Vec<String>> {
        self.spawns.lock().unwrap().clone()
    }
}

impl SystemHost for IpcHost {
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
        Ok(CommandOutcome {
            status: self.status,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    fn user_exists(&self, _username: &str) -> Result<bool, AccountError> {
        unreachable!("the IPC removal asks nothing about users")
    }

    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        unreachable!("the IPC removal measures nothing")
    }

    fn read_password_database(&self, _path: &str) -> Result<String, AccountError> {
        unreachable!("the IPC removal is handed its account resolved")
    }

    fn read_mounts(&self) -> Result<String, AccountError> {
        unreachable!("the IPC removal reads no mounts")
    }

    fn home_metadata(&self, _path: &str) -> Result<Option<HomeMetadata>, AccountError> {
        unreachable!("the IPC removal inspects no home")
    }

    fn read_sysv_ipc_table(&self, kind: SysvIpcKind) -> Result<String, AccountError> {
        let mut tables = self.tables.lock().unwrap();
        let queue = &mut tables.iter_mut().find(|(k, _)| *k == kind).unwrap().1;
        if queue.len() > 1 {
            Ok(queue.pop_front().unwrap())
        } else {
            Ok(queue.front().cloned().unwrap_or_default())
        }
    }

    fn pause(&self, _interval: Duration) {
        unreachable!("the IPC removal never waits")
    }

    fn available_bytes(&self, _directory: &std::path::Path) -> Result<u64, std::io::Error> {
        Ok(u64::MAX)
    }
}

/// The distribution the program is resolved against.
fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The proof the removal is handed, resolved from the account's one row.
fn managed() -> ManagedAccount {
    let name = AccountName::parse("acme").expect("the fixture name is valid");
    let passwd = format!("acme:x:{UID}:{UID}::/home/acme:/usr/sbin/nologin\n");

    ManagedAccount::resolve(name, &passwd).expect("acme is a hosting account")
}

/// A shared-memory table with one row `(id, uid, cuid)`.
fn shm(rows: &[(u32, u32, u32)]) -> String {
    let mut text = String::from("key shmid perms size uid gid cuid cgid\n");
    for (id, uid, cuid) in rows {
        text.push_str(&format!("0 {id} 600 4096 {uid} {uid} {cuid} {cuid}\n"));
    }
    text
}

/// A message-queue table with one row `(id, uid, cuid)`.
fn msg(rows: &[(u32, u32, u32)]) -> String {
    let mut text = String::from("key msqid perms cbytes uid gid cuid cgid\n");
    for (id, uid, cuid) in rows {
        text.push_str(&format!("0 {id} 600 0 {uid} {uid} {cuid} {cuid}\n"));
    }
    text
}

/// A semaphore table with one row `(id, uid, cuid)`.
fn sem(rows: &[(u32, u32, u32)]) -> String {
    let mut text = String::from("key semid perms nsems uid gid cuid cgid\n");
    for (id, uid, cuid) in rows {
        text.push_str(&format!("0 {id} 600 1 {uid} {uid} {cuid} {cuid}\n"));
    }
    text
}

/// The argv of one expected `ipcrm`.
fn argv(selector: &str, id: &str) -> Vec<String> {
    vec![selector.to_owned(), id.to_owned()]
}

#[test]
fn each_kind_is_removed_with_its_own_selector_and_id() {
    let host = IpcHost::new(
        &[&shm(&[(5, UID, UID)]), &shm(&[])],
        &[&msg(&[(9, UID, UID)]), &msg(&[])],
        &[&sem(&[(3, UID, UID)]), &sem(&[])],
        0,
    );

    remove_account_sysv_ipc(&host, distro(), &managed()).expect("all removed");

    assert_eq!(
        host.argvs(),
        vec![
            argv("--shmem-id", "5"),
            argv("--queue-id", "9"),
            argv("--semaphore-id", "3"),
        ]
    );
}

#[test]
fn an_object_owned_by_the_uid_is_removed() {
    let host = IpcHost::new(&[&shm(&[(5, UID, 0)]), &shm(&[])], &[""], &[""], 0);

    remove_account_sysv_ipc(&host, distro(), &managed()).expect("removed");

    assert_eq!(host.argvs(), vec![argv("--shmem-id", "5")]);
}

#[test]
fn an_object_created_by_the_uid_but_owned_by_another_is_removed() {
    let host = IpcHost::new(&[&shm(&[(6, 0, UID)]), &shm(&[])], &[""], &[""], 0);

    remove_account_sysv_ipc(&host, distro(), &managed()).expect("removed");

    assert_eq!(host.argvs(), vec![argv("--shmem-id", "6")]);
}

#[test]
fn an_object_of_an_unrelated_owner_is_left_alone() {
    let host = IpcHost::new(&[&shm(&[(7, 2002, 2002)])], &[""], &[""], 0);

    remove_account_sysv_ipc(&host, distro(), &managed()).expect("nothing to remove");

    assert!(host.argvs().is_empty());
}

#[test]
fn empty_tables_need_no_removal() {
    let host = IpcHost::new(&[""], &[""], &[""], 0);

    remove_account_sysv_ipc(&host, distro(), &managed()).expect("nothing to remove");

    assert!(host.argvs().is_empty());
}

#[test]
fn an_object_that_survives_the_reread_is_ipc_remain() {
    let host = IpcHost::new(&[&shm(&[(5, UID, UID)])], &[""], &[""], 0);

    let error = remove_account_sysv_ipc(&host, distro(), &managed()).unwrap_err();

    assert!(matches!(error, AccountError::IpcRemain { username } if username == "acme"));
}

#[test]
fn a_semaphore_that_survives_the_reread_is_ipc_remain() {
    let host = IpcHost::new(&[""], &[""], &[&sem(&[(3, UID, UID)])], 0);

    let error = remove_account_sysv_ipc(&host, distro(), &managed()).unwrap_err();

    assert!(matches!(error, AccountError::IpcRemain { .. }));
}

#[test]
fn a_failing_ipcrm_is_a_command_failure_not_a_success() {
    let host = IpcHost::new(&[&shm(&[(5, UID, UID)]), &shm(&[])], &[""], &[""], 1);

    let error = remove_account_sysv_ipc(&host, distro(), &managed()).unwrap_err();

    assert!(matches!(error, AccountError::CommandFailed { .. }));
}

#[test]
fn an_unparseable_table_is_unreadable_output() {
    let host = IpcHost::new(&["key shmid\n1 2\n"], &[""], &[""], 0);

    let error = remove_account_sysv_ipc(&host, distro(), &managed()).unwrap_err();

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}
