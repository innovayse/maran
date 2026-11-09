//! Why a candidate node address was refused.

/// Reasons [`super::node_address::NodeAddress::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum NodeAddressError {
    /// The candidate is neither an IP address nor a well-formed host name.
    #[error("'{value}' is neither an IP address nor a host name")]
    NotAnAddress {
        /// What was offered.
        value: String,
    },
    /// The candidate is an IP address no peer can dial: unspecified or multicast.
    ///
    /// A certificate naming such an address would be valid for nothing, so it is refused
    /// before a key is generated.
    #[error("'{value}' is not an address a peer can dial")]
    NotDialable {
        /// What was offered.
        value: String,
    },
}
