//! Tests for the `set_account_resource_limits` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::cpu_quota_percent::CpuQuotaPercent;
use maran_agent_core::validation::system::memory_max_mib::MemoryMaxMib;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::tasks_max::TasksMax;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};

use super::set_account_resource_limits;
use crate::accounts::model::home_metadata::HomeMetadata;
use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;
use crate::accounts::{
    AccountError, AccountOperations, AccountResourceLimits, CommandOutcome, ResourceCeiling,
    SystemHost,
};

/// A host that records every spawn and answers each with one fixed status.
struct SliceHost {
    passwd: String,
    status: i32,
    spawns: Mutex<Vec<(String, Vec<String>, CallClass)>>,
}

impl SliceHost {
    fn new(passwd: &str, status: i32) -> Self {
        Self {
            passwd: passwd.to_owned(),
            status,
            spawns: Mutex::new(Vec::new()),
        }
    }
}

impl SystemHost for SliceHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, AccountError> {
        self.spawns.lock().unwrap().push((
            program.as_str().to_owned(),
            arguments
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
            class,
        ));
        Ok(CommandOutcome {
            status: self.status,
            stdout: String::new(),
            stderr: String::new(),
        })
    }

    fn user_exists(&self, _username: &str) -> Result<bool, AccountError> {
        panic!("setting a slice's ceilings never asks whether a user exists")
    }

    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        panic!("setting a slice's ceilings measures nothing")
    }

    fn read_password_database(&self, _path: &str) -> Result<String, AccountError> {
        Ok(self.passwd.clone())
    }

    fn read_mounts(&self) -> Result<String, AccountError> {
        panic!("setting a slice's ceilings reads no mount table")
    }

    fn home_metadata(&self, _path: &str) -> Result<Option<HomeMetadata>, AccountError> {
        panic!("setting a slice's ceilings inspects no home")
    }

    fn read_sysv_ipc_table(&self, _kind: SysvIpcKind) -> Result<String, AccountError> {
        panic!("setting a slice's ceilings reads no IPC table")
    }

    fn pause(&self, _interval: Duration) {
        panic!("setting a slice's ceilings never waits")
    }

    fn available_bytes(&self, _directory: &std::path::Path) -> Result<u64, std::io::Error> {
        panic!("setting a slice's ceilings writes nothing to disk")
    }
}

fn name(raw: &str) -> AccountName {
    AccountName::parse(raw).unwrap()
}

fn row(raw: &str, uid: u32) -> String {
    format!("{raw}:x:{uid}:{uid}::/home/{raw}:/usr/sbin/nologin\n")
}

fn capped() -> AccountResourceLimits {
    AccountResourceLimits {
        cpu: ResourceCeiling::Limited(CpuQuotaPercent::parse(150).unwrap()),
        memory: ResourceCeiling::Limited(MemoryMaxMib::parse(1024).unwrap()),
        tasks: ResourceCeiling::Limited(TasksMax::parse(64).unwrap()),
    }
}

#[test]
fn the_ceilings_are_set_on_the_slice_named_after_the_accounts_uid() {
    let operations = AccountOperations::new(
        SliceHost::new(&row("alice", 2001), 0),
        adapter_for(DistroFamily::Debian),
        FreeSpaceFloor::default_floor(),
    );

    set_account_resource_limits(&operations, &name("alice"), &capped()).unwrap();

    let spawns = operations.host().spawns.lock().unwrap();
    assert_eq!(spawns.len(), 1);
    let (program, arguments, class) = &spawns[0];
    assert_eq!(
        program,
        adapter_for(DistroFamily::Debian).service_manager().as_str()
    );
    assert_eq!(
        arguments,
        &[
            "set-property",
            "--",
            "user-2001.slice",
            "CPUQuota=150%",
            "MemoryMax=1024M",
            "TasksMax=64",
        ]
    );
    assert_eq!(*class, CallClass::Mutation);
}

#[test]
fn unlimited_ceilings_reset_every_property_rather_than_leaving_one_behind() {
    let operations = AccountOperations::new(
        SliceHost::new(&row("alice", 2001), 0),
        adapter_for(DistroFamily::Rhel),
        FreeSpaceFloor::default_floor(),
    );
    let unlimited = AccountResourceLimits {
        cpu: ResourceCeiling::Unlimited,
        memory: ResourceCeiling::Unlimited,
        tasks: ResourceCeiling::Unlimited,
    };

    set_account_resource_limits(&operations, &name("alice"), &unlimited).unwrap();

    let spawns = operations.host().spawns.lock().unwrap();
    assert_eq!(
        spawns[0].1[3..],
        ["CPUQuota=", "MemoryMax=infinity", "TasksMax=infinity"]
    );
}

#[test]
fn an_absent_account_is_not_found_and_nothing_runs() {
    let operations = AccountOperations::new(
        SliceHost::new(&row("bob", 2002), 0),
        adapter_for(DistroFamily::Debian),
        FreeSpaceFloor::default_floor(),
    );

    let refused = set_account_resource_limits(&operations, &name("alice"), &capped());

    assert!(matches!(refused, Err(AccountError::NotFound { .. })));
    assert!(operations.host().spawns.lock().unwrap().is_empty());
}

#[test]
fn a_system_identity_is_refused_so_roots_slice_is_never_capped() {
    let operations = AccountOperations::new(
        SliceHost::new("daemon:x:1:1::/home/daemon:/usr/sbin/nologin\n", 0),
        adapter_for(DistroFamily::Debian),
        FreeSpaceFloor::default_floor(),
    );

    let refused = set_account_resource_limits(&operations, &name("daemon"), &capped());

    assert!(matches!(refused, Err(AccountError::NotManaged { .. })));
    assert!(operations.host().spawns.lock().unwrap().is_empty());
}

#[test]
fn a_refusing_service_manager_is_a_command_failure() {
    let operations = AccountOperations::new(
        SliceHost::new(&row("alice", 2001), 1),
        adapter_for(DistroFamily::Debian),
        FreeSpaceFloor::default_floor(),
    );

    let refused = set_account_resource_limits(&operations, &name("alice"), &capped());

    assert!(matches!(
        refused,
        Err(AccountError::CommandFailed { status: 1, .. })
    ));
}
