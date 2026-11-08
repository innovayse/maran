//! How many traffic summaries the agent reads at once.

use std::sync::Arc;

use tokio::sync::Semaphore;

use crate::sites::SitesOpError;
use crate::sites::traffic_run_slot::TrafficRunSlot;

/// The most traffic summaries read at once, across every account.
///
/// A summary holds one thread of tokio's blocking pool for as long as it reads
/// up to 64 MiB, and that pool is shared with every other rpc of the agent. The
/// only caller is the panel's hourly pass, which goes one site at a time, so two
/// leaves room for a second panel instance and a retry without letting the
/// summary take a meaningful share of the pool.
pub(crate) const HOST_TRAFFIC_SLOTS: usize = 2;

/// A counting semaphore for traffic summaries, taken without waiting.
///
/// **It refuses; it never queues.** The caller is an hourly pass that asks again
/// on its next turn; a queued read would hold the caller's request open and a
/// blocking thread with it for a time nobody chose.
///
/// **It is not one of the agent's locks** (rules/rust.md "What this agent
/// serialises"): it excludes nothing and orders nothing, so it is no wait-for
/// edge and no argument about lock order depends on it. It is process-local
/// like they are, and says nothing about a second agent; after an agent restart
/// the count is full again.
#[derive(Debug)]
pub(crate) struct TrafficRunSlots {
    /// The host-wide count.
    host: Arc<Semaphore>,
}

impl TrafficRunSlots {
    /// Slots for `host` summaries at once.
    pub(crate) fn new(host: usize) -> Self {
        Self {
            host: Arc::new(Semaphore::new(host)),
        }
    }

    /// Takes one summary's worth of the count, or refuses.
    ///
    /// # Errors
    ///
    /// [`SitesOpError::TrafficBusy`] when every slot is taken. Nothing is held
    /// on that path.
    pub(crate) fn try_acquire(&self) -> Result<TrafficRunSlot, SitesOpError> {
        Arc::clone(&self.host)
            .try_acquire_owned()
            .map(TrafficRunSlot::new)
            .map_err(|_| SitesOpError::TrafficBusy)
    }
}

#[cfg(test)]
#[path = "../tests/sites/traffic_run_slots_tests.rs"]
mod tests;
