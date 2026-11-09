//! The address a node is reached on, which its certificate names.

use std::net::IpAddr;

use super::node_address_error::NodeAddressError;
use crate::validation::web::domain::Domain;

/// The address the panel dials a node on: an IP address or a host name.
///
/// It is what goes into the node certificate's subject alternative name, so it is the one value
/// an operator types that becomes part of a signed object. Hence a closed type with two arms,
/// each already validated: an IP is parsed by the standard library, and a name goes through
/// [`Domain::parse_existing`] (the shape check without the reserved-name set, because a node on
/// a private network is legitimately called `node1.lan`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum NodeAddress {
    /// A literal IP address.
    Ip(IpAddr),
    /// A host name, lowercased.
    Host(Domain),
}

impl NodeAddress {
    /// Parses `candidate`.
    ///
    /// # Errors
    ///
    /// - [`NodeAddressError::NotAnAddress`] when `candidate` is neither an IP address nor a
    ///   well-formed host name (empty, a newline, a path, a scheme, a port, a malformed IPv4
    ///   literal such as `999.1.1.1`).
    /// - [`NodeAddressError::NotDialable`] for an unspecified or multicast IP address.
    pub fn parse(candidate: &str) -> Result<Self, NodeAddressError> {
        if let Ok(address) = candidate.parse::<IpAddr>() {
            if address.is_unspecified() || address.is_multicast() {
                return Err(NodeAddressError::NotDialable {
                    value: candidate.to_owned(),
                });
            }
            return Ok(Self::Ip(address));
        }

        let not_an_address = || NodeAddressError::NotAnAddress {
            value: candidate.to_owned(),
        };

        // A name whose last label is all digits is a malformed IPv4 literal (`999.1.1.1`,
        // `1.2.3`), not a host: DNS has no such top-level label, and a certificate naming one
        // would be a DNS name nobody can resolve.
        if candidate.rsplit('.').next().is_some_and(|label| {
            !label.is_empty() && label.bytes().all(|byte| byte.is_ascii_digit())
        }) {
            return Err(not_an_address());
        }

        Domain::parse_existing(candidate)
            .map(Self::Host)
            .map_err(|_| not_an_address())
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fleet/node_address_tests.rs"]
mod tests;
