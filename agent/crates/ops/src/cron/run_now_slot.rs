//! One admitted run by hand, held until it ends.

use tokio::sync::OwnedSemaphorePermit;

/// The right to have one entry running by hand, for the account it was granted
/// to and against the host-wide count (issue #333).
///
/// Released by being dropped, which is the point of making it a value: every
/// path out of the run — the run's own answer, an error, a panic in the blocking
/// thread — gives both permits back, and no code has to remember to.
///
/// Built only by [`RunNowSlots::try_acquire`](super::run_now_slots::RunNowSlots).
#[derive(Debug)]
pub(crate) struct RunNowSlot {
    /// The permit against this account's own count. Held for its drop alone.
    _account: OwnedSemaphorePermit,
    /// The permit against the host-wide count. Held for its drop alone.
    _host: OwnedSemaphorePermit,
}

impl RunNowSlot {
    /// Pairs the two permits a grant consists of.
    pub(crate) fn new(account: OwnedSemaphorePermit, host: OwnedSemaphorePermit) -> Self {
        Self {
            _account: account,
            _host: host,
        }
    }
}
