//! The firewall's address sets, as a layer that follows the host.

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_distro::DistroAdapter;
use maran_ops::firewall::{FirewallHost, SelfSetsOutcome, refresh_self_sets};

use crate::address_watch::host_address_layer::HostAddressLayer;
use crate::address_watch::host_address_layer_error::HostAddressLayerError;
use crate::address_watch::host_address_layer_outcome::HostAddressLayerOutcome;

/// The output chain of `table inet maran`, kept current by `refresh_self_sets`: its two
/// address sets in the kernel (one `nft -f` transaction) and in the ruleset file, and the
/// whole chain when more than the sets is stale.
pub(crate) struct FirewallSetsLayer {
    /// The machine the refresh runs against.
    host: Box<dyn FirewallHost>,
    /// The platform facts (the `nft` binary).
    distro: &'static dyn DistroAdapter,
}

impl FirewallSetsLayer {
    /// A layer over `host`.
    pub(crate) fn new(host: Box<dyn FirewallHost>, distro: &'static dyn DistroAdapter) -> Self {
        Self { host, distro }
    }
}

impl HostAddressLayer for FirewallSetsLayer {
    fn name(&self) -> &'static str {
        "firewall address sets"
    }

    fn follow(
        &self,
        addresses: &HostAddresses,
    ) -> Result<HostAddressLayerOutcome, HostAddressLayerError> {
        match refresh_self_sets(self.host.as_ref(), self.distro, addresses)? {
            SelfSetsOutcome::Refreshed => {
                tracing::info!(
                    "Refreshed the firewall's address sets: the ruleset file and the kernel follow the host's addresses"
                );

                Ok(HostAddressLayerOutcome::Followed)
            }
            SelfSetsOutcome::Repaired => {
                tracing::info!(
                    "Repaired the firewall's output chain: it was stale beyond its address sets and the whole ruleset was re-rendered for the host's facts"
                );

                Ok(HostAddressLayerOutcome::Followed)
            }
            SelfSetsOutcome::HostMoved => {
                tracing::info!(
                    "the host's addresses changed again while the firewall was being refreshed; it holds the newer ones and is checked again at the next interval"
                );

                Ok(HostAddressLayerOutcome::Pending)
            }
            SelfSetsOutcome::NotLoaded => {
                tracing::debug!(
                    "the firewall's output chain is not loaded, so there are no address sets to refresh yet"
                );

                Ok(HostAddressLayerOutcome::Pending)
            }
            SelfSetsOutcome::NotSeeded => {
                tracing::debug!(
                    "the firewall has no ruleset file yet, so there are no address sets to refresh"
                );

                Ok(HostAddressLayerOutcome::Pending)
            }
        }
    }
}
