//! The in-memory [`CronHost`] the cron tests decide against.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host installs tables into the host's cron spool and writes files inside
//! a real customer's home: a unit test can do neither, and a suite that tried
//! would pass or fail on whether it happened to run as root. What a unit test
//! CAN pin is the decision — what the installed table says, which files an
//! entry owns, what counts as a duplicate, and what is left behind when an
//! install is refused halfway.
//!
//! The fake models the two stores the design actually has, and keeps them
//! apart: a crontab, and a directory of per-entry files keyed by the very paths
//! `AgentPaths` builds. Keeping them apart is what lets a test assert the
//! property the whole area rests on — that the command is in the file store and
//! never in the table.
//!
//! It composes a command file's bytes through
//! [`CronEntry::file_contents`], the same function the real host uses, so a
//! test asserting "verbatim plus one newline" is pinning the shipped unit
//! rather than the fake's opinion of it.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::{BTreeMap, VecDeque};
use std::sync::Mutex;
use std::time::{Duration, UNIX_EPOCH};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::env_var_name::EnvVarName;
use maran_agent_core::validation::system::env_var_value::EnvVarValue;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_entry::CronEntry;
use crate::cron::model::cron_environment::CronEnvironment;
use crate::cron::model::cron_run_record::CronRunRecord;

/// The account every test in this folder works against.
pub(crate) const TEST_ACCOUNT: &str = "alice";

/// The first id the fake mints when a test has not queued one.
pub(crate) const FIRST_ID: &str = "11111111-1111-4111-8111-111111111111";

/// The second id the fake mints when a test has not queued one.
pub(crate) const SECOND_ID: &str = "22222222-2222-4222-8222-222222222222";

/// An id no test ever creates, for asking about an entry that is not there.
pub(crate) const ABSENT_ID: &str = "99999999-9999-4999-8999-999999999999";

/// The ids the fake hands out, in order, before it runs out.
const DEFAULT_IDS: [&str; 2] = [FIRST_ID, SECOND_ID];

/// A [`CronHost`] that keeps a crontab and a directory of entry files in
/// memory.
pub(crate) struct RecordingCronHost {
    /// The account's current crontab, or `None` for an account that has none.
    crontab: Mutex<Option<String>>,
    /// Every table the fake was asked to install, in order.
    installs: Mutex<Vec<String>>,
    /// The entry files the "home" holds, keyed by their full path.
    files: Mutex<BTreeMap<String, String>>,
    /// The ids the fake will hand out next.
    ids: Mutex<VecDeque<String>>,
    /// What the last run of each entry reported, keyed by the exit file's path.
    records: Mutex<BTreeMap<String, CronRunRecord>>,
    /// The status an install exits with, when a test installed a refusal.
    install_refusal: Mutex<Option<i32>>,
    /// The status a crontab read exits with, when a test installed a refusal.
    read_refusal: Mutex<Option<i32>>,
    /// Whether writing a command file refuses.
    write_refuses: Mutex<bool>,
    /// Whether reading an entry file refuses.
    entry_read_refuses: Mutex<bool>,
    /// Whether removing an entry's files refuses.
    remove_refuses: Mutex<bool>,
    /// Whether the host can mint an id at all.
    ids_unavailable: Mutex<bool>,
}

impl RecordingCronHost {
    /// A host with no crontab, no files and no refusals.
    pub(crate) fn new() -> Self {
        Self {
            crontab: Mutex::new(None),
            installs: Mutex::new(Vec::new()),
            files: Mutex::new(BTreeMap::new()),
            ids: Mutex::new(DEFAULT_IDS.iter().map(|id| (*id).to_owned()).collect()),
            records: Mutex::new(BTreeMap::new()),
            install_refusal: Mutex::new(None),
            read_refusal: Mutex::new(None),
            write_refuses: Mutex::new(false),
            entry_read_refuses: Mutex::new(false),
            remove_refuses: Mutex::new(false),
            ids_unavailable: Mutex::new(false),
        }
    }

    /// A host whose account already has the crontab `text`.
    pub(crate) fn with_crontab(text: &str) -> Self {
        let host = Self::new();
        *host.crontab.lock().unwrap() = Some(text.to_owned());

        host
    }

    /// The account's crontab as it stands now.
    pub(crate) fn crontab(&self) -> Option<String> {
        self.crontab.lock().unwrap().clone()
    }

    /// Every table the fake was asked to install, in order.
    pub(crate) fn installs(&self) -> Vec<String> {
        self.installs.lock().unwrap().clone()
    }

    /// The contents of the entry file at `path`, if the "home" holds one.
    pub(crate) fn file(&self, path: &str) -> Option<String> {
        self.files.lock().unwrap().get(path).cloned()
    }

    /// Every entry file path the "home" holds, in order.
    pub(crate) fn file_paths(&self) -> Vec<String> {
        self.files.lock().unwrap().keys().cloned().collect()
    }

    /// Puts a file at `path` into the "home", as a run of the entry would.
    pub(crate) fn put_file(&self, path: &str, contents: &str) {
        self.files
            .lock()
            .unwrap()
            .insert(path.to_owned(), contents.to_owned());
    }

    /// Takes the file at `path` away, as a customer deleting it by hand would.
    pub(crate) fn take_file(&self, path: &str) {
        self.files.lock().unwrap().remove(path);
    }

    /// Records what an entry's last run reported.
    pub(crate) fn put_run_record(&self, path: &str, record: CronRunRecord) {
        self.records.lock().unwrap().insert(path.to_owned(), record);
    }

    /// Makes every install exit with `status`.
    pub(crate) fn refuse_install_with(&self, status: i32) {
        *self.install_refusal.lock().unwrap() = Some(status);
    }

    /// Makes reading the crontab exit with `status`.
    pub(crate) fn refuse_crontab_read_with(&self, status: i32) {
        *self.read_refusal.lock().unwrap() = Some(status);
    }

    /// Makes writing a command file refuse.
    pub(crate) fn refuse_writes(&self) {
        *self.write_refuses.lock().unwrap() = true;
    }

    /// Makes reading an entry file refuse.
    pub(crate) fn refuse_entry_reads(&self) {
        *self.entry_read_refuses.lock().unwrap() = true;
    }

    /// Makes removing an entry's files refuse.
    pub(crate) fn refuse_removals(&self) {
        *self.remove_refuses.lock().unwrap() = true;
    }

    /// Makes the host unable to mint an id.
    pub(crate) fn refuse_ids(&self) {
        *self.ids_unavailable.lock().unwrap() = true;
    }
}

impl CronHost for RecordingCronHost {
    fn read_crontab(&self, _account: &AccountName) -> Result<Option<String>, CronError> {
        if let Some(code) = *self.read_refusal.lock().unwrap() {
            return Err(CronError::CrontabRefused { code });
        }

        Ok(self.crontab.lock().unwrap().clone())
    }

    fn install_crontab(&self, _account: &AccountName, contents: &str) -> Result<(), CronError> {
        if let Some(code) = *self.install_refusal.lock().unwrap() {
            // Recorded even when refused, so a test can tell "the table was
            // never built" from "the table was built and the program said no".
            self.installs.lock().unwrap().push(contents.to_owned());

            return Err(CronError::CrontabRefused { code });
        }

        self.installs.lock().unwrap().push(contents.to_owned());
        *self.crontab.lock().unwrap() = Some(contents.to_owned());

        Ok(())
    }

    fn new_entry_id(&self) -> Result<CronEntryId, CronError> {
        if *self.ids_unavailable.lock().unwrap() {
            return Err(CronError::EntryIdUnavailable);
        }

        let next = self
            .ids
            .lock()
            .unwrap()
            .pop_front()
            .expect("the test queued fewer ids than the operation asked for");

        Ok(entry_id(&next))
    }

    fn write_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        command: &CronCommand,
    ) -> Result<(), CronError> {
        if *self.write_refuses.lock().unwrap() {
            return Err(CronError::EntryFileUnwritable);
        }

        self.files.lock().unwrap().insert(
            cmd_path(account, entry),
            // The shipped composition, not the fake's own: the file's format is
            // one unit and both sides of it use the same function.
            CronEntry::file_contents(command),
        );

        Ok(())
    }

    fn read_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<String>, CronError> {
        if *self.entry_read_refuses.lock().unwrap() {
            return Err(CronError::EntryFileUnreadable);
        }

        Ok(self.file(&cmd_path(account, entry)))
    }

    fn remove_entry_files(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<(), CronError> {
        if *self.remove_refuses.lock().unwrap() {
            return Err(CronError::EntryFileUnremovable);
        }

        let mut files = self.files.lock().unwrap();
        for path in [
            cmd_path(account, entry),
            log_path(account, entry),
            exit_path(account, entry),
        ] {
            files.remove(&path);
        }

        Ok(())
    }

    fn read_run_record(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<CronRunRecord>, CronError> {
        if *self.entry_read_refuses.lock().unwrap() {
            return Err(CronError::EntryFileUnreadable);
        }

        Ok(self
            .records
            .lock()
            .unwrap()
            .get(&exit_path(account, entry))
            .cloned())
    }

    fn read_output_tail(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        max_bytes: usize,
    ) -> Result<Option<String>, CronError> {
        if *self.entry_read_refuses.lock().unwrap() {
            return Err(CronError::EntryFileUnreadable);
        }

        Ok(self.file(&log_path(account, entry)).map(|text| {
            // The tail, as the real host reads it: the end of the file is what
            // a failed run's error is at.
            let start = text.len().saturating_sub(max_bytes);
            text[start..].to_owned()
        }))
    }
}

/// The path of the entry's command file, as `AgentPaths` builds it.
pub(crate) fn cmd_path(account: &AccountName, entry: &CronEntryId) -> String {
    AgentPaths::cron_cmd_path(account, entry)
        .display()
        .to_string()
}

/// The path of the entry's output file.
pub(crate) fn log_path(account: &AccountName, entry: &CronEntryId) -> String {
    AgentPaths::cron_log_path(account, entry)
        .display()
        .to_string()
}

/// The path of the entry's exit-status file.
pub(crate) fn exit_path(account: &AccountName, entry: &CronEntryId) -> String {
    AgentPaths::cron_exit_path(account, entry)
        .display()
        .to_string()
}

/// The Debian-family adapter, for the `sh` path the render needs.
pub(crate) fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// The account every test works against.
pub(crate) fn account() -> AccountName {
    AccountName::parse(TEST_ACCOUNT).expect("a valid account name")
}

/// A validated id from its text.
pub(crate) fn entry_id(text: &str) -> CronEntryId {
    CronEntryId::parse(text).expect("a valid entry id")
}

/// A schedule of five already-valid fields.
pub(crate) fn schedule(
    minute: &str,
    hour: &str,
    day_of_month: &str,
    month: &str,
    day_of_week: &str,
) -> CronSchedule {
    CronSchedule::parse(minute, hour, day_of_month, month, day_of_week).expect("a valid schedule")
}

/// The schedule every test uses when the schedule itself is not the subject.
pub(crate) fn every_five_minutes() -> CronSchedule {
    schedule("*/5", "*", "*", "*", "*")
}

/// A validated command from its text.
pub(crate) fn command(text: &str) -> CronCommand {
    CronCommand::parse(text).expect("a valid command")
}

/// A validated environment assignment from its two halves.
pub(crate) fn assignment(name: &str, value: &str) -> CronEnvironment {
    CronEnvironment {
        name: EnvVarName::parse(name).expect("a valid name"),
        value: EnvVarValue::parse(value).expect("a valid value"),
    }
}

/// A run record at a fixed instant, so no test depends on the clock.
pub(crate) fn run_record(exit_code: Option<i32>, seconds_since_epoch: u64) -> CronRunRecord {
    CronRunRecord {
        exit_code,
        ran_at: UNIX_EPOCH + Duration::from_secs(seconds_since_epoch),
    }
}
