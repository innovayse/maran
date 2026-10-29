//! Tests for [`remove_account_at_jobs`], driven by a scripted host.

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
use crate::accounts::remove_account_at_jobs::remove_account_at_jobs;
use crate::accounts::{AccountError, SystemHost};

/// A host answering `atq`/`atrm` spawns from queues and lstat from flags.
struct ScriptedHost {
    /// Whether the atq binary exists.
    atq_present: bool,
    /// Whether the at spool directory exists.
    spool_present: bool,
    /// Successive `atq` listings; the last one repeats.
    listings: Mutex<VecDeque<String>>,
    /// Exit status of every `atrm`.
    atrm_status: i32,
    /// Every spawn as (program, argv).
    spawns: Mutex<Vec<(String, Vec<String>)>>,
}

impl ScriptedHost {
    /// An installed `at` whose queue shows `listings` in turn.
    fn with_listings(listings: &[&str]) -> Self {
        Self {
            atq_present: true,
            spool_present: true,
            listings: Mutex::new(listings.iter().map(|s| (*s).to_owned()).collect()),
            atrm_status: 0,
            spawns: Mutex::new(Vec::new()),
        }
    }

    /// The `atrm` argvs seen, in order.
    fn removed(&self) -> Vec<Vec<String>> {
        self.spawns
            .lock()
            .unwrap()
            .iter()
            .filter(|(p, _)| p.ends_with("atrm"))
            .map(|(_, a)| a.clone())
            .collect()
    }

    /// How many spawns happened in total.
    fn spawn_count(&self) -> usize {
        self.spawns.lock().unwrap().len()
    }
}

/// Metadata of a plain existing file.
fn present() -> HomeMetadata {
    HomeMetadata {
        is_symlink: false,
        is_directory: false,
        owner_uid: 0,
        group_gid: 0,
        device: 1,
    }
}

impl SystemHost for ScriptedHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, AccountError> {
        let program = program.to_string();
        self.spawns.lock().unwrap().push((
            program.clone(),
            arguments.iter().map(|a| (*a).to_owned()).collect(),
        ));
        if program.ends_with("atrm") {
            return Ok(CommandOutcome {
                status: self.atrm_status,
                stdout: String::new(),
                stderr: String::new(),
            });
        }
        let mut listings = self.listings.lock().unwrap();
        let stdout = if listings.len() > 1 {
            listings.pop_front().unwrap()
        } else {
            listings.front().cloned().unwrap_or_default()
        };
        Ok(CommandOutcome {
            status: 0,
            stdout,
            stderr: String::new(),
        })
    }

    fn user_exists(&self, _username: &str) -> Result<bool, AccountError> {
        unreachable!("at removal asks nothing about users")
    }

    fn directory_size(&self, _path: &str) -> Result<u64, AccountError> {
        unreachable!("at removal measures nothing")
    }

    fn read_password_database(&self, _path: &str) -> Result<String, AccountError> {
        unreachable!("at removal is handed its account")
    }

    fn read_mounts(&self) -> Result<String, AccountError> {
        unreachable!("at removal reads no mounts")
    }

    fn home_metadata(&self, path: &str) -> Result<Option<HomeMetadata>, AccountError> {
        let exists = if path.ends_with("atq") {
            self.atq_present
        } else {
            self.spool_present
        };
        Ok(exists.then(present))
    }

    fn read_sysv_ipc_table(&self, _kind: SysvIpcKind) -> Result<String, AccountError> {
        unreachable!("at removal reads no IPC table")
    }

    fn pause(&self, _interval: Duration) {}
}

/// The distribution the binaries come from.
fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The account `acme`, resolved through the real managed-account type.
fn managed() -> ManagedAccount {
    let name = AccountName::parse("acme").expect("valid");
    ManagedAccount::resolve(name, "acme:x:1001:1001::/home/acme:/usr/sbin/nologin\n")
        .expect("hosting account")
}

/// One listing line for `user` with job `id`.
fn line(id: u32, user: &str) -> String {
    format!("{id}\tThu Oct 29 10:00:00 2026 a {user}\n")
}

/// The account's job is removed with its id as the only argument.
#[test]
fn the_accounts_job_is_removed_by_id() {
    let host = ScriptedHost::with_listings(&[&line(5, "acme"), ""]);
    remove_account_at_jobs(&host, distro(), &managed()).unwrap();
    assert_eq!(host.removed(), vec![vec!["5".to_owned()]]);
}

/// Another user's job is left alone.
#[test]
fn another_users_job_is_not_removed() {
    let host = ScriptedHost::with_listings(&[&line(5, "other")]);
    remove_account_at_jobs(&host, distro(), &managed()).unwrap();
    assert!(host.removed().is_empty());
}

/// A longer name sharing the prefix does not match.
#[test]
fn a_name_sharing_the_prefix_is_not_matched() {
    let host = ScriptedHost::with_listings(&[&line(5, "acme2")]);
    remove_account_at_jobs(&host, distro(), &managed()).unwrap();
    assert!(host.removed().is_empty());
}

/// An unparseable line refuses.
#[test]
fn an_unparseable_listing_is_refused() {
    let host = ScriptedHost::with_listings(&["nonsense\n"]);
    let err = remove_account_at_jobs(&host, distro(), &managed()).unwrap_err();
    assert!(matches!(err, AccountError::UnreadableOutput { .. }));
}

/// An empty queue succeeds without any atrm.
#[test]
fn an_empty_queue_succeeds() {
    let host = ScriptedHost::with_listings(&[""]);
    remove_account_at_jobs(&host, distro(), &managed()).unwrap();
    assert!(host.removed().is_empty());
}

/// No atq and no spool means at was never installed: nothing is spawned.
#[test]
fn missing_atq_without_a_spool_is_skipped() {
    let mut host = ScriptedHost::with_listings(&[]);
    host.atq_present = false;
    host.spool_present = false;
    remove_account_at_jobs(&host, distro(), &managed()).unwrap();
    assert_eq!(host.spawn_count(), 0);
}

/// No atq but a spool present is refused.
#[test]
fn missing_atq_with_a_spool_is_refused() {
    let mut host = ScriptedHost::with_listings(&[]);
    host.atq_present = false;
    let err = remove_account_at_jobs(&host, distro(), &managed()).unwrap_err();
    assert!(matches!(err, AccountError::CommandUnavailable { .. }));
}

/// A failing atrm is a command failure.
#[test]
fn a_failing_atrm_is_refused() {
    let mut host = ScriptedHost::with_listings(&[&line(5, "acme")]);
    host.atrm_status = 1;
    let err = remove_account_at_jobs(&host, distro(), &managed()).unwrap_err();
    assert!(matches!(err, AccountError::CommandFailed { .. }));
}

/// A job still listed after atrm is reported as remaining.
#[test]
fn a_surviving_job_reports_at_jobs_remain() {
    let host = ScriptedHost::with_listings(&[&line(5, "acme")]);
    let err = remove_account_at_jobs(&host, distro(), &managed()).unwrap_err();
    assert!(matches!(err, AccountError::AtJobsRemain { ref username } if username == "acme"));
}
