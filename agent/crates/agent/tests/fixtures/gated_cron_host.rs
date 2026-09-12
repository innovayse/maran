//! A real [`ProcessCronHost`] with one rendezvous inside it, so a race against
//! a real crontab happens on every run instead of on the lucky ones.
//!
//! Everything this host does, it does by delegating to `ProcessCronHost`: the
//! spool is the host's real spool, `crontab(1)` is the real program, and the
//! entry files are real files in a real home. The ONLY thing added is a pause
//! immediately after `read_crontab` has answered — which is exactly the window
//! the per-account cron lock exists to close. A caller parked there is holding
//! a document it parsed from a table another caller may still replace.
//!
//! Two threads that merely start at the same time may or may not interleave,
//! and a concurrency test that depends on which one wins reports "serialised"
//! for a build that is not — the direction that manufactures confidence
//! (rules/testing.md). The gate removes the scheduler from the question: with
//! the lock in force the second caller is parked outside its own read and the
//! first waits out its budget alone; without the lock the second walks straight
//! in and the two meet.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic, dead_code)]

use std::sync::{Condvar, Mutex};
use std::time::Duration;

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;
use maran_ops::cron::{CronError, CronHost, CronRunRecord, ProcessCronHost};

/// How long the first caller waits at the gate for a second one.
///
/// It is only ever paid when the lock is doing its job, because then the
/// partner never arrives — it is parked on the account's lock until the first
/// caller's install has landed. With the lock removed the partner arrives in
/// microseconds and nothing waits at all, so only the passing run pays the
/// budget. A second is orders of magnitude more than a thread needs to reach
/// the gate even on a loaded container, so an unlocked build cannot escape the
/// race by being slow.
const ARRIVAL_BUDGET: Duration = Duration::from_secs(1);

/// A [`CronHost`] that really touches the machine and pauses in one place.
pub struct GatedCronHost {
    /// The host every method delegates to.
    inner: ProcessCronHost,
    /// How many callers have reached the gate.
    arrived: Mutex<usize>,
    /// Signalled by the second arrival.
    partner: Condvar,
    /// Whether the first caller's wait ended on the budget rather than on a
    /// partner turning up.
    ///
    /// This is the axis that says which build is under test, and the arrival
    /// COUNT cannot: a count of two is reached either way, because a caller
    /// parked on a lock still arrives once the lock is released. A rendezvous
    /// that COMPLETED means both callers were inside the read-modify-write at
    /// once, which is the race; one that TIMED OUT means the second was held
    /// outside it, which is the lock.
    timed_out: Mutex<bool>,
}

impl GatedCronHost {
    /// A gated host around a real one for this family.
    pub fn new(distro: &'static dyn DistroAdapter) -> Self {
        Self {
            inner: ProcessCronHost::new(distro),
            arrived: Mutex::new(0),
            partner: Condvar::new(),
            timed_out: Mutex::new(false),
        }
    }

    /// How many callers reached the gate in total.
    pub fn arrivals(&self) -> usize {
        *self.arrived.lock().unwrap()
    }

    /// Whether the first caller waited out its budget alone.
    pub fn rendezvous_timed_out(&self) -> bool {
        *self.timed_out.lock().unwrap()
    }

    /// Blocks the first caller until a second arrives, or until the budget runs
    /// out. Later callers pass straight through.
    fn arrive(&self) {
        let mut arrived = self.arrived.lock().unwrap();
        *arrived += 1;

        if *arrived >= 2 {
            self.partner.notify_all();

            return;
        }

        let (_arrived, outcome) = self
            .partner
            .wait_timeout(arrived, ARRIVAL_BUDGET)
            .expect("the gate's own mutex is never poisoned");
        if outcome.timed_out() {
            *self.timed_out.lock().unwrap() = true;
        }
    }
}

impl CronHost for GatedCronHost {
    fn read_crontab(&self, account: &AccountName) -> Result<Option<String>, CronError> {
        let text = self.inner.read_crontab(account)?;

        // After the real program answered and before this caller can install
        // what it renders from that answer. Nothing else in this file waits.
        self.arrive();

        Ok(text)
    }

    fn install_crontab(&self, account: &AccountName, contents: &str) -> Result<(), CronError> {
        self.inner.install_crontab(account, contents)
    }

    fn new_entry_id(&self) -> Result<CronEntryId, CronError> {
        self.inner.new_entry_id()
    }

    fn write_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        command: &CronCommand,
    ) -> Result<(), CronError> {
        self.inner.write_command_file(account, entry, command)
    }

    fn read_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<String>, CronError> {
        self.inner.read_command_file(account, entry)
    }

    fn remove_entry_files(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<(), CronError> {
        self.inner.remove_entry_files(account, entry)
    }

    fn read_run_record(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<CronRunRecord>, CronError> {
        self.inner.read_run_record(account, entry)
    }

    fn read_output_tail(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        max_bytes: usize,
    ) -> Result<Option<String>, CronError> {
        self.inner.read_output_tail(account, entry, max_bytes)
    }
}
