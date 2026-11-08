//! Tests for the `set_account_open_files_limit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::open_files_limit::OpenFilesLimit;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};

use super::{set_account_open_files_limit, set_account_open_files_limit_under_lock};
use crate::accounts::model::home_metadata::HomeMetadata;
use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;
use crate::accounts::{
    AccountError, AccountOperations, CommandOutcome, ResourceCeiling, SystemHost, take_account_lock,
};
use crate::safe_write::model::{ConfigFile, Validator};

/// One file the host was asked to write, and the validator that came with it.
#[derive(Debug, Clone, PartialEq, Eq)]
struct Written {
    target: PathBuf,
    contents: String,
    mode: u32,
    validator_program: String,
    validator_arguments: Vec<String>,
    validator_class: CallClass,
}

/// A host that holds a fixed password database and records every drop-in write
/// and removal it is asked for.
struct LimitsHost {
    passwd: String,
    writes: Mutex<Vec<Written>>,
    removals: Mutex<Vec<PathBuf>>,
    write_fails: bool,
}

impl LimitsHost {
    fn holding(passwd: &str) -> Self {
        Self {
            passwd: passwd.to_owned(),
            writes: Mutex::new(Vec::new()),
            removals: Mutex::new(Vec::new()),
            write_fails: false,
        }
    }

    fn with_failing_write(mut self) -> Self {
        self.write_fails = true;
        self
    }

    fn writes(&self) -> Vec<Written> {
        self.writes.lock().unwrap().clone()
    }

    fn removals(&self) -> Vec<PathBuf> {
        self.removals.lock().unwrap().clone()
    }
}

impl SystemHost for LimitsHost {
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, AccountError> {
        panic!("setting an open-files limit spawns nothing itself; the validator is the protocol's")
    }

    fn user_exists(&self, _username: &str) -> Result<bool, AccountError> {
        panic!("setting an open-files limit never asks whether a user exists")
    }

    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        panic!("setting an open-files limit measures nothing")
    }

    fn read_password_database(&self, _path: &str) -> Result<String, AccountError> {
        Ok(self.passwd.clone())
    }

    fn read_mounts(&self) -> Result<String, AccountError> {
        panic!("setting an open-files limit reads no mount table")
    }

    fn home_metadata(&self, _path: &str) -> Result<Option<HomeMetadata>, AccountError> {
        panic!("setting an open-files limit inspects no home")
    }

    fn read_sysv_ipc_table(&self, _kind: SysvIpcKind) -> Result<String, AccountError> {
        panic!("setting an open-files limit reads no IPC table")
    }

    fn pause(&self, _interval: Duration) {
        panic!("setting an open-files limit never waits")
    }

    fn available_bytes(&self, _directory: &Path) -> Result<u64, std::io::Error> {
        panic!("setting an open-files limit does not consult the free-space floor")
    }

    fn write_limits_file(
        &self,
        file: &ConfigFile<'_>,
        validator: &Validator<'_>,
    ) -> Result<(), AccountError> {
        if self.write_fails {
            return Err(AccountError::LimitsFileFailed);
        }
        self.writes.lock().unwrap().push(Written {
            target: file.target.to_path_buf(),
            contents: file.contents.to_owned(),
            mode: file.mode,
            validator_program: validator.program.as_str().to_owned(),
            validator_arguments: validator
                .arguments
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
            validator_class: validator.class,
        });
        Ok(())
    }

    fn remove_limits_file(&self, path: &Path) -> Result<(), AccountError> {
        self.removals.lock().unwrap().push(path.to_path_buf());
        Ok(())
    }
}

fn name(raw: &str) -> AccountName {
    AccountName::parse(raw).unwrap()
}

fn row(raw: &str, uid: u32) -> String {
    format!("{raw}:x:{uid}:{uid}::/home/{raw}:/usr/sbin/nologin\n")
}

fn operations_over(passwd: &str) -> AccountOperations<LimitsHost> {
    AccountOperations::new(
        LimitsHost::holding(passwd),
        adapter_for(DistroFamily::Debian),
        FreeSpaceFloor::default_floor(),
    )
}

fn limited(figure: u32) -> ResourceCeiling<OpenFilesLimit> {
    ResourceCeiling::Limited(OpenFilesLimit::parse(figure).unwrap())
}

#[test]
fn a_figure_is_written_as_the_accounts_own_drop_in_at_mode_0644() {
    let operations = operations_over(&row("alice", 2001));

    set_account_open_files_limit_under_lock(&operations, &name("alice"), limited(4096)).unwrap();

    let writes = operations.host().writes();
    assert_eq!(writes.len(), 1);
    assert_eq!(
        writes[0].target,
        Path::new("/etc/security/limits.d/maran-alice.conf")
    );
    assert_eq!(writes[0].mode, 0o644);
    assert!(operations.host().removals().is_empty());
}

#[test]
fn the_file_carries_one_rule_line_naming_the_account_and_the_figure() {
    let operations = operations_over(&row("alice", 2001));

    set_account_open_files_limit_under_lock(&operations, &name("alice"), limited(4096)).unwrap();

    let contents = operations.host().writes()[0].contents.clone();
    let rules: Vec<&str> = contents
        .lines()
        .filter(|line| !line.starts_with('#'))
        .collect();
    assert_eq!(rules, ["alice - nofile 4096"]);
    assert!(contents.ends_with('\n'));
}

#[test]
fn the_validator_asks_the_host_whether_the_named_account_resolves() {
    let operations = operations_over(&row("alice", 2001));

    set_account_open_files_limit_under_lock(&operations, &name("alice"), limited(4096)).unwrap();

    let writes = operations.host().writes();
    let write = &writes[0];
    assert_eq!(
        write.validator_program,
        adapter_for(DistroFamily::Debian).id_binary().as_str()
    );
    assert_eq!(write.validator_arguments, ["-u", "--", "alice"]);
    assert_eq!(write.validator_class, CallClass::Validate);
}

#[test]
fn no_ceiling_removes_the_drop_in_and_writes_nothing() {
    let operations = operations_over(&row("alice", 2001));

    set_account_open_files_limit_under_lock(
        &operations,
        &name("alice"),
        ResourceCeiling::Unlimited,
    )
    .unwrap();

    assert_eq!(
        operations.host().removals(),
        [PathBuf::from("/etc/security/limits.d/maran-alice.conf")]
    );
    assert!(operations.host().writes().is_empty());
}

#[test]
fn the_same_request_twice_writes_the_same_bytes_to_the_same_place() {
    let operations = operations_over(&row("alice", 2001));

    set_account_open_files_limit_under_lock(&operations, &name("alice"), limited(8192)).unwrap();
    set_account_open_files_limit_under_lock(&operations, &name("alice"), limited(8192)).unwrap();

    let writes = operations.host().writes();
    assert_eq!(writes.len(), 2);
    assert_eq!(writes[0], writes[1]);
}

#[test]
fn a_user_that_is_not_a_hosting_account_is_refused_before_anything_is_written() {
    let passwd = "root:x:0:0::/root:/bin/bash\n";
    let operations = operations_over(passwd);

    let refusal = set_account_open_files_limit_under_lock(&operations, &name("root"), limited(1));

    assert!(
        matches!(refusal, Err(AccountError::NotManaged { .. })),
        "expected NotManaged, got {refusal:?}"
    );
    assert!(operations.host().writes().is_empty());
    assert!(operations.host().removals().is_empty());
}

#[test]
fn an_account_the_host_does_not_hold_is_not_found_and_not_removed_either() {
    let operations = operations_over(&row("alice", 2001));

    let refusal = set_account_open_files_limit_under_lock(
        &operations,
        &name("bob"),
        ResourceCeiling::Unlimited,
    );

    assert!(
        matches!(refusal, Err(AccountError::NotFound { .. })),
        "expected NotFound, got {refusal:?}"
    );
    assert!(operations.host().removals().is_empty());
}

#[test]
fn a_refused_write_is_the_limits_file_failure() {
    let operations = AccountOperations::new(
        LimitsHost::holding(&row("alice", 2001)).with_failing_write(),
        adapter_for(DistroFamily::Debian),
        FreeSpaceFloor::default_floor(),
    );

    let refusal = set_account_open_files_limit_under_lock(&operations, &name("alice"), limited(10));

    assert!(
        matches!(refusal, Err(AccountError::LimitsFileFailed)),
        "expected LimitsFileFailed, got {refusal:?}"
    );
}

#[test]
fn an_account_another_operation_holds_is_busy_and_nothing_is_written() {
    let operations = operations_over(&row("limitsbusy", 2002));
    let held = take_account_lock(&name("limitsbusy")).expect("the lock is free");

    let refusal = set_account_open_files_limit(&operations, &name("limitsbusy"), limited(10));

    assert!(
        matches!(refusal, Err(AccountError::Busy { .. })),
        "expected Busy, got {refusal:?}"
    );
    assert!(operations.host().writes().is_empty());
    drop(held);
}

#[test]
fn once_the_lock_is_free_the_public_entry_writes() {
    // The inverse control of the case above: a gate that refused everything would
    // pass it, so the same call with the lock free must get through.
    let operations = operations_over(&row("limitsfree", 2003));

    set_account_open_files_limit(&operations, &name("limitsfree"), limited(10)).unwrap();

    assert_eq!(operations.host().writes().len(), 1);
}
