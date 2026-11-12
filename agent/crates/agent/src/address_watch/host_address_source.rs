//! Where the periodic check reads the host's addresses from.

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::host_addresses_error::HostAddressesError;
use maran_agent_core::utils::read_host_addresses::read_host_addresses;

/// The one reading of the host's addresses the check makes per interval.
pub(crate) trait HostAddressSource: Send {
    /// The addresses configured on this host and the ranges it answers on.
    ///
    /// # Errors
    ///
    /// Returns [`HostAddressesError`] when the tables cannot be known. The check
    /// then changes nothing: a layer filled from a guess would leave an address of
    /// the host open on one side of the lockdown.
    fn read(&self) -> Result<HostAddresses, HostAddressesError>;
}

/// The kernel's own tables, through the reader the mail stage, the firewall's
/// startup reconcile and the migration policy share.
pub(crate) struct KernelAddressSource;

impl HostAddressSource for KernelAddressSource {
    fn read(&self) -> Result<HostAddresses, HostAddressesError> {
        read_host_addresses()
    }
}
