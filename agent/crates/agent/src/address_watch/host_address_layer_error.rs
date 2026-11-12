//! Why a layer could not follow the host's addresses.

use maran_ops::firewall::FirewallError;
use maran_ops::mail::MailError;

/// A layer of the port-25 lockdown that could not be brought to the host's
/// present addresses. It stays as it was and is tried again at the next interval.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub(crate) enum HostAddressLayerError {
    /// The firewall's address sets could not be refreshed.
    #[error("the firewall's address sets: {0}")]
    Firewall(#[from] FirewallError),
    /// The mail server's refused-client table could not be refreshed.
    #[error("the mail server's refused-client table: {0}")]
    Mail(#[from] MailError),
}
