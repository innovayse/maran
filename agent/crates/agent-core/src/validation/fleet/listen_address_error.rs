//! Why a candidate fleet listen address was refused.

/// Reasons [`super::listen_address::ListenAddress::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ListenAddressError {
    /// The candidate is not an `ip:port` pair (an IPv6 address is written `[::1]:8444`).
    ///
    /// A host name is refused on purpose: a listener binds an address, and resolving a name at
    /// start-up would make what the root daemon listens on depend on a resolver.
    #[error("'{value}' is not an ip:port address")]
    NotASocketAddress {
        /// What was offered.
        value: String,
    },
    /// The port is below 1024, which includes 0 ("let the kernel choose").
    ///
    /// The panel has to be told where to dial, so the port is chosen, and a privileged port is
    /// one a service of this kind has no business on.
    #[error("the fleet port must be between 1024 and 65535, got {port}")]
    PortNotAllowed {
        /// The port that was offered.
        port: u16,
    },
}
