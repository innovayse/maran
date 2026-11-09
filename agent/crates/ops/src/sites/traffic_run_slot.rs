//! One admitted traffic summary, held until it ends.

use tokio::sync::OwnedSemaphorePermit;

/// The right to have one traffic summary being read, against the host-wide
/// count.
///
/// Released by being dropped, so every path out of the call — its answer, an
/// error, a panic in the blocking thread — gives the permit back and no code
/// has to remember to. Built only by
/// [`TrafficRunSlots::try_acquire`](super::traffic_run_slots::TrafficRunSlots).
#[derive(Debug)]
pub(crate) struct TrafficRunSlot {
    /// The permit against the host-wide count. Held for its drop alone.
    _host: OwnedSemaphorePermit,
}

impl TrafficRunSlot {
    /// Wraps the permit a grant consists of.
    pub(crate) fn new(host: OwnedSemaphorePermit) -> Self {
        Self { _host: host }
    }
}
