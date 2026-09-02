//! A reverse-proxy target that is safe to write into a web-server configuration.

use std::net::IpAddr;

use crate::validation::web::upstream_error::UpstreamError;

/// A validated `host:port` reverse-proxy target, checked once at the boundary
/// so no later caller has to remember to re-check it.
///
/// The host is guaranteed loopback or an RFC1918 private address and the port
/// is guaranteed to be in `1..=65535`: a reverse proxy pointing anywhere else
/// on the internet would turn the panel into an open proxy for whoever asked
/// for the site. Construction is the only way to obtain one, so an `Upstream`
/// in a signature is a promise that the value has been through
/// [`Upstream::parse`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Upstream(String);

impl Upstream {
    /// Parses `candidate` as a `host:port` reverse-proxy target.
    ///
    /// # Errors
    ///
    /// - [`UpstreamError::Empty`] when `candidate` is empty.
    /// - [`UpstreamError::IllegalCharacter`] for a newline, carriage return or
    ///   other control character — which is what stops the value from ending
    ///   the config line it is written into and starting a directive of the
    ///   caller's choosing.
    /// - [`UpstreamError::Malformed`] when the candidate is not `host:port`,
    ///   or the port half is not a number.
    /// - [`UpstreamError::InvalidPort`] when the port is `0` or above `65535`.
    /// - [`UpstreamError::InvalidHost`] when the host half does not parse as
    ///   an IP address.
    /// - [`UpstreamError::NotPrivate`] when the host parses but is neither
    ///   loopback (`127.0.0.0/8`, `::1`) nor RFC1918 private (`10/8`,
    ///   `172.16/12`, `192.168/16`).
    pub fn parse(candidate: &str) -> Result<Self, UpstreamError> {
        if candidate.is_empty() {
            return Err(UpstreamError::Empty);
        }

        if let Some(character) = candidate.chars().find(|c| c.is_control()) {
            return Err(UpstreamError::IllegalCharacter { character });
        }

        // One closure for the one error every structural check below reports.
        let malformed = || UpstreamError::Malformed {
            candidate: candidate.to_owned(),
        };

        let (host, port) = if let Some(rest) = candidate.strip_prefix('[') {
            // Bracketed IPv6, `[host]:port` — the standard notation, needed
            // because a bare IPv6 address already contains colons and cannot
            // otherwise be told apart from the port separator.
            let (host, after) = rest.split_once(']').ok_or_else(malformed)?;
            let port = after.strip_prefix(':').ok_or_else(malformed)?;
            (host, port)
        } else {
            candidate.rsplit_once(':').ok_or_else(malformed)?
        };

        if host.is_empty() {
            return Err(malformed());
        }

        let port: u16 = port.parse().map_err(|_| malformed())?;
        if port == 0 {
            return Err(UpstreamError::InvalidPort);
        }

        let address: IpAddr = host.parse().map_err(|_| UpstreamError::InvalidHost {
            host: host.to_owned(),
        })?;

        // IPv6 unique-local (`fc00::/7`) is deliberately NOT allowed, and the
        // contract says so (`proto/agent/v1/sites.proto`, `proxy_upstream`)
        // rather than promising a "private address" this refuses. A
        // customer-supplied upstream fails closed; nothing on a supported host
        // needs a ULA target, and widening this later is a contract change, not
        // a quiet edit here.
        let is_allowed = match address {
            IpAddr::V4(v4) => v4.is_loopback() || v4.is_private(),
            IpAddr::V6(v6) => v6.is_loopback(),
        };
        if !is_allowed {
            return Err(UpstreamError::NotPrivate {
                host: host.to_owned(),
            });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated `host:port` upstream.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/upstream_tests.rs"]
mod tests;
