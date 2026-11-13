//! One layer of the port-25 lockdown that is filled from the host's addresses.

use maran_agent_core::utils::host_addresses::HostAddresses;

use crate::address_watch::host_address_layer_error::HostAddressLayerError;
use crate::address_watch::host_address_layer_outcome::HostAddressLayerOutcome;

/// A layer of the lockdown that holds a copy of the host's addresses and has to
/// be told when they change.
///
/// Implementations are synchronous and run on the blocking pool: they spawn
/// programs and write files.
pub(crate) trait HostAddressLayer: Send {
    /// A short name for the log line that reports this layer.
    fn name(&self) -> &'static str;

    /// Brings the layer to `addresses`. Idempotent: a layer that already holds
    /// them writes nothing.
    ///
    /// Answers [`HostAddressLayerOutcome::Followed`] only when the layer really holds the
    /// addresses; a layer that had nothing it could bring current answers
    /// [`HostAddressLayerOutcome::Pending`], and is asked again.
    ///
    /// # Errors
    ///
    /// Returns the layer's own failure; the layer is then left as it was.
    fn follow(
        &self,
        addresses: &HostAddresses,
    ) -> Result<HostAddressLayerOutcome, HostAddressLayerError>;
}
