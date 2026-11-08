//! One admitted operation, held until it ends.

use tokio::sync::OwnedSemaphorePermit;

/// The right to have one operation running, against the account's count and the
/// host's.
///
/// Released by being dropped, which is the point of making it a value: every path
/// out of the operation — its answer, an error, a panic in the blocking thread —
/// gives both permits back, and no code has to remember to.
///
/// Built only by [`BoundedSlots::try_acquire`](super::bounded_slots::BoundedSlots).
#[derive(Debug)]
pub struct BoundedSlot {
    /// The permit against this account's own count. Held for its drop alone.
    _account: OwnedSemaphorePermit,
    /// The permit against the host-wide count. Held for its drop alone.
    _host: OwnedSemaphorePermit,
}

impl BoundedSlot {
    /// Pairs the two permits a grant consists of.
    pub(super) fn new(account: OwnedSemaphorePermit, host: OwnedSemaphorePermit) -> Self {
        Self {
            _account: account,
            _host: host,
        }
    }
}
