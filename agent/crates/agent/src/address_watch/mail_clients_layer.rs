//! The mail server's refused-client table, as a layer that follows the host.

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_distro::DistroAdapter;
use maran_ops::mail::{MailHost, SelfClientsOutcome, refresh_self_clients};

use crate::address_watch::host_address_layer::HostAddressLayer;
use crate::address_watch::host_address_layer_error::HostAddressLayerError;
use crate::address_watch::host_address_layer_outcome::HostAddressLayerOutcome;

/// Postfix's `self_clients.cidr`, rewritten by `refresh_self_clients` when the host's
/// addresses no longer match it, and reloaded when Postfix is running. A Postfix that is
/// stopped is never started by it.
pub(crate) struct MailClientsLayer {
    /// The machine the refresh runs against.
    host: Box<dyn MailHost>,
    /// The platform facts (Postfix's binary and unit).
    distro: &'static dyn DistroAdapter,
}

impl MailClientsLayer {
    /// A layer over `host`.
    pub(crate) fn new(host: Box<dyn MailHost>, distro: &'static dyn DistroAdapter) -> Self {
        Self { host, distro }
    }
}

impl HostAddressLayer for MailClientsLayer {
    fn name(&self) -> &'static str {
        "mail refused-client table"
    }

    fn follow(
        &self,
        addresses: &HostAddresses,
    ) -> Result<HostAddressLayerOutcome, HostAddressLayerError> {
        match refresh_self_clients(self.host.as_ref(), self.distro, addresses)? {
            SelfClientsOutcome::Rewritten => {
                tracing::info!(
                    "the mail server's refused-client table follows the host's addresses and Postfix was reloaded"
                );

                Ok(HostAddressLayerOutcome::Followed)
            }
            SelfClientsOutcome::Unchanged => Ok(HostAddressLayerOutcome::Followed),
            SelfClientsOutcome::NotRunning => {
                tracing::debug!(
                    "Postfix is not running: its refused-client table holds the host's addresses and is read when it starts, but nothing was reloaded"
                );

                Ok(HostAddressLayerOutcome::Pending)
            }
            SelfClientsOutcome::NotEnabled => {
                tracing::debug!(
                    "mail was never enabled, so there is no refused-client table to refresh"
                );

                Ok(HostAddressLayerOutcome::Pending)
            }
        }
    }
}
