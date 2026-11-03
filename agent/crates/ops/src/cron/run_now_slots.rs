//! How many entries the agent runs by hand at once.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, PoisonError};

use maran_agent_core::validation::system::name::AccountName;
use tokio::sync::Semaphore;

use crate::cron::cron_error::CronError;
use crate::cron::run_now_slot::RunNowSlot;

/// The most entries the agent runs by hand at once, across every account.
///
/// A run holds one thread of tokio's blocking pool from the moment it starts
/// until the job ends, which is up to `RUN_NOW_RUNTIME_CEILING` (two minutes).
/// That pool is shared with every other rpc of the agent — file operations,
/// every `userdel`, every backup — and its default ceiling is 512 threads.
/// Sixteen leaves a customer's "run now" button no way to take a meaningful
/// share of it, and is still more than a hosting server has reason to run at
/// once: the runs are for checking that a job works.
pub(crate) const HOST_RUN_NOW_SLOTS: usize = 16;

/// The most entries one account may have running by hand at once.
///
/// Two, so that a customer who starts a second while the first is still going
/// is not told no, and one who scripts the button cannot occupy more than two
/// threads. The host-wide count is what bounds the sum of accounts.
pub(crate) const ACCOUNT_RUN_NOW_SLOTS: usize = 2;

/// Counting semaphores for the runs started by hand: one for the host and one
/// per account, both taken without waiting.
///
/// **It refuses; it never queues.** A queued run would hold the caller's
/// request open and a thread of the panel's with it for a time nobody chose, and
/// the panel's own wait for an answer (200 s) is shorter than two full queues of
/// two-minute runs. A refusal is immediate and says "try again later"
/// ([`CronError::RunCapacityReached`]), which is the answer a button needs.
///
/// **It is not one of the agent's locks** (rules/rust.md "What this agent
/// serialises"): it excludes nothing and orders nothing, so it cannot be a
/// wait-for edge and no argument about lock order depends on it. It is
/// process-local like they are, and says nothing about a second agent.
///
/// The per-account map only grows, one small entry per account name that ever
/// ran something by hand, as `cron_lock`'s does.
#[derive(Debug)]
pub(crate) struct RunNowSlots {
    /// The host-wide count.
    host: Arc<Semaphore>,
    /// How many each account may hold at once.
    per_account: usize,
    /// Each account's count, made the first time the account asks.
    accounts: Mutex<HashMap<String, Arc<Semaphore>>>,
}

impl RunNowSlots {
    /// Slots for `host` runs at once on the host and `per_account` for each
    /// account.
    pub(crate) fn new(host: usize, per_account: usize) -> Self {
        Self {
            host: Arc::new(Semaphore::new(host)),
            per_account,
            accounts: Mutex::new(HashMap::new()),
        }
    }

    /// Takes one run's worth of both counts, or refuses.
    ///
    /// The account's count is asked first: a customer at their own ceiling is
    /// refused without having looked at, or taken from, the host's.
    ///
    /// # Errors
    ///
    /// [`CronError::RunCapacityReached`] when the account already has its share
    /// running or the host has none left. Nothing is held on that path.
    pub(crate) fn try_acquire(&self, account: &AccountName) -> Result<RunNowSlot, CronError> {
        let account_count = {
            let mut accounts = self.accounts.lock().unwrap_or_else(PoisonError::into_inner);
            Arc::clone(
                accounts
                    .entry(account.as_str().to_owned())
                    .or_insert_with(|| Arc::new(Semaphore::new(self.per_account))),
            )
        };

        let account_permit = account_count
            .try_acquire_owned()
            .map_err(|_| CronError::RunCapacityReached)?;
        let host_permit = Arc::clone(&self.host)
            .try_acquire_owned()
            .map_err(|_| CronError::RunCapacityReached)?;

        Ok(RunNowSlot::new(account_permit, host_permit))
    }
}

#[cfg(test)]
#[path = "../tests/cron/run_now_slots_tests.rs"]
mod tests;
