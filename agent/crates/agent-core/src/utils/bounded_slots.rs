//! How many operations of one kind run at once, for the host and for each account.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, PoisonError};

use tokio::sync::Semaphore;

use crate::utils::bounded_slot::BoundedSlot;
use crate::utils::slots_full::SlotsFull;
use crate::validation::system::name::AccountName;

/// Counting semaphores for one kind of operation: one for the host and one per
/// account, both taken without waiting.
///
/// Shared by the runs started by hand (`ops::cron`) and by the file manager's
/// browse, archive and bulk operations (`ops::files`), which differ only in their
/// numbers and in the error they answer a refusal with.
///
/// **It refuses; it never queues.** A queued operation would hold the caller's
/// request open, and a thread of the agent's blocking pool with it, for a time
/// nobody chose. A refusal is immediate and says "try again later".
///
/// **It is not one of the agent's locks** (rules/rust.md "What this agent
/// serialises"): it excludes nothing and orders nothing, so it is no wait-for edge
/// and no argument about lock order depends on it. The counts live in this
/// process's memory only: after an agent restart every count is full again, and a
/// second agent has counts of its own. The per-account map only grows, one small
/// entry per account name that ever asked.
#[derive(Debug)]
pub struct BoundedSlots {
    /// The host-wide count.
    host: Arc<Semaphore>,
    /// How many each account may hold at once.
    per_account: usize,
    /// Each account's count, made the first time the account asks.
    accounts: Mutex<HashMap<String, Arc<Semaphore>>>,
}

impl BoundedSlots {
    /// Slots for `host` operations at once on the host and `per_account` for each
    /// account.
    pub fn new(host: usize, per_account: usize) -> Self {
        Self {
            host: Arc::new(Semaphore::new(host)),
            per_account,
            accounts: Mutex::new(HashMap::new()),
        }
    }

    /// Takes one operation's worth of both counts, or refuses.
    ///
    /// The account's count is asked first: a customer at their own ceiling is
    /// refused without having looked at, or taken from, the host's. A refusal by
    /// the host gives the account's permit back (it is dropped on that path).
    ///
    /// # Errors
    ///
    /// [`SlotsFull`] when the account already has its share running or the host
    /// has none left. Nothing is held on that path.
    pub fn try_acquire(&self, account: &AccountName) -> Result<BoundedSlot, SlotsFull> {
        let account_count = {
            let mut accounts = self.accounts.lock().unwrap_or_else(PoisonError::into_inner);
            Arc::clone(
                accounts
                    .entry(account.as_str().to_owned())
                    .or_insert_with(|| Arc::new(Semaphore::new(self.per_account))),
            )
        };

        let account_permit = account_count.try_acquire_owned().map_err(|_| SlotsFull)?;
        let host_permit = Arc::clone(&self.host)
            .try_acquire_owned()
            .map_err(|_| SlotsFull)?;

        Ok(BoundedSlot::new(account_permit, host_permit))
    }
}

#[cfg(test)]
#[path = "../tests/utils/bounded_slots_tests.rs"]
mod tests;
