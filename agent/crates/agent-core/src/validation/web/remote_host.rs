//! The host a migration source is reached at: a DNS name or an IP address literal.

use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

use super::domain::Domain;
use super::ipv4_disguise::ipv4_in_disguise;
use super::remote_host_error::RemoteHostError;

/// The first-label bits of an IPv6 link-local address (`fe80::/10`).
const IPV6_LINK_LOCAL_MASK: u16 = 0xffc0;

/// The value of the first segment of an IPv6 link-local address once masked.
const IPV6_LINK_LOCAL_PREFIX: u16 = 0xfe80;

/// The DNS name or IP literal an administrator registered as the source of a
/// server migration, checked once at the boundary.
///
/// The agent opens an outbound SSH connection to this value, so the grammar is
/// closed: either a literal address that is not one of the kinds the agent never
/// connects to, or a host name that passes the same label rules every other host
/// name in the agent passes. A scheme, a path, a port, a space, a user part, a
/// leading `-` (which `ssh` would read as an option) and a zone identifier have
/// no place in either arm and are refused as [`RemoteHostError::NotAHost`].
///
/// What this type does NOT decide is where a NAME resolves. A name that resolves
/// to a loopback address, to a link-local one or to one of this host's own
/// addresses is refused later, on the RESOLVED address, by the operation that
/// resolves it once and connects to that literal (the grammar cannot know DNS).
/// Private-range addresses (`10.0.0.0/8`, `192.168.0.0/16`, `fc00::/7`) are
/// deliberately accepted: a source on the operator's own network is the ordinary
/// case, and the destination set is bounded by what the administrator typed.
///
/// A name is lowercased and an IPv6 literal is kept in its canonical text form,
/// so two spellings of one host compare equal.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct RemoteHost {
    /// The canonical text: a lowercased name, or an address in its canonical form.
    text: String,
    /// The address when the text is a literal one, so a caller connecting to it
    /// never resolves anything.
    address: Option<IpAddr>,
}

impl RemoteHost {
    /// Parses `candidate` as a DNS name or an IP address literal.
    ///
    /// # Errors
    ///
    /// - [`RemoteHostError::Empty`] when `candidate` is empty.
    /// - [`RemoteHostError::ForbiddenAddress`] for a loopback, link-local,
    ///   unspecified, multicast, broadcast or `0.0.0.0/8` literal, in IPv4 or in
    ///   an IPv6 literal that merely carries one.
    /// - [`RemoteHostError::NotAHost`] when the text is neither a literal nor a
    ///   well-formed name, or its last label is all digits (a malformed IPv4
    ///   literal such as `999.1.1.1`, `127.1` or `0177.0.0.1`, which a resolver
    ///   or `ssh` could read as an address of the caller's choosing).
    /// - [`RemoteHostError::LocalName`] for `localhost` and `*.localhost`.
    pub fn parse(candidate: &str) -> Result<Self, RemoteHostError> {
        if candidate.is_empty() {
            return Err(RemoteHostError::Empty);
        }

        if let Ok(address) = candidate.parse::<IpAddr>() {
            if is_never_connected_to(address) {
                return Err(RemoteHostError::ForbiddenAddress);
            }
            return Ok(Self {
                text: address.to_string(),
                address: Some(address),
            });
        }

        if ends_in_numeric_label(candidate) {
            return Err(RemoteHostError::NotAHost);
        }

        let name = Domain::parse_existing(candidate).map_err(|_| RemoteHostError::NotAHost)?;
        let lowered = name.as_str();
        if lowered == "localhost" || lowered.ends_with(".localhost") {
            return Err(RemoteHostError::LocalName);
        }

        Ok(Self {
            text: lowered.to_owned(),
            address: None,
        })
    }

    /// The canonical text of the host.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.text
    }

    /// Whether the host was typed as an IP address literal, in which case there
    /// is nothing to resolve.
    #[must_use]
    pub fn is_literal_address(&self) -> bool {
        self.address.is_some()
    }

    /// The address when the host is a literal one, so the caller connects to it
    /// directly.
    #[must_use]
    pub fn literal_address(&self) -> Option<IpAddr> {
        self.address
    }
}

/// Whether the last dot-separated label is non-empty and all ASCII digits.
///
/// DNS has no all-numeric top-level label, so such a name is a malformed or
/// shorthand IPv4 literal, and a resolver may read some of them as an address.
fn ends_in_numeric_label(candidate: &str) -> bool {
    candidate
        .rsplit('.')
        .next()
        .is_some_and(|label| !label.is_empty() && label.bytes().all(|byte| byte.is_ascii_digit()))
}

/// Whether the agent never connects to `address`, whichever way it is spelled.
fn is_never_connected_to(address: IpAddr) -> bool {
    match address {
        IpAddr::V4(v4) => is_refused_v4(v4),
        IpAddr::V6(v6) => is_refused_v6(v6),
    }
}

/// Whether an IPv4 address is loopback, link-local, unspecified, multicast, the
/// limited broadcast, or in `0.0.0.0/8` ("this network").
fn is_refused_v4(address: Ipv4Addr) -> bool {
    address.is_loopback()
        || address.is_link_local()
        || address.is_unspecified()
        || address.is_multicast()
        || address.is_broadcast()
        || address.octets()[0] == 0
}

/// Whether an IPv6 address is loopback, link-local (`fe80::/10`), unspecified or
/// multicast, or carries an IPv4 address of one of those kinds.
fn is_refused_v6(address: Ipv6Addr) -> bool {
    if let Some(embedded) = ipv4_in_disguise(address) {
        return is_refused_v4(embedded);
    }

    address.is_loopback()
        || address.is_unspecified()
        || address.is_multicast()
        || (address.segments()[0] & IPV6_LINK_LOCAL_MASK) == IPV6_LINK_LOCAL_PREFIX
}

#[cfg(test)]
#[path = "../../tests/validation/web/remote_host_tests.rs"]
mod tests;
