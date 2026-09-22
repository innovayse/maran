//! The pair of raw values a licence fingerprint is computed from.

use super::machine_identity::MachineIdentity;
use super::primary_interface::PrimaryInterface;

/// The two raw inputs a server fingerprint is built from.
///
/// Each is independently typed as present-or-not
/// ([`MachineIdentity::NotAvailable`] / [`PrimaryInterface::NotAvailable`]):
/// a fingerprint consumer needs to know which half of the pair it actually
/// has, not just "something was reported".
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ServerFingerprintInputs {
    /// This host's `machine-id`, or the fact that none exists.
    pub machine_id: MachineIdentity,
    /// The interface carrying this host's IPv4 default route, or the fact
    /// that none exists.
    pub primary_interface: PrimaryInterface,
}
