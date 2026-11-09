//! The address and port the agent's fleet listener binds.

use std::net::SocketAddr;

use super::listen_address_error::ListenAddressError;

/// The lowest port a fleet listener may use.
const LOWEST_PORT: u16 = 1024;

/// A socket address for the fleet listener: an IP address and a port from 1024 up.
///
/// The IP may be unspecified (`0.0.0.0`, `[::]`): listening on every interface is the usual
/// choice, and the firewall rule the installer adds, not the bind address, restricts who may
/// connect. The port may not be 0 or privileged. Holding a value is the proof both were checked.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ListenAddress(SocketAddr);

impl ListenAddress {
    /// Parses `candidate` as `ip:port`.
    ///
    /// # Errors
    ///
    /// - [`ListenAddressError::NotASocketAddress`] when `candidate` is not an `ip:port` pair,
    ///   including a host name and an IPv6 address written without brackets.
    /// - [`ListenAddressError::PortNotAllowed`] when the port is below 1024, 0 included.
    pub fn parse(candidate: &str) -> Result<Self, ListenAddressError> {
        let address: SocketAddr =
            candidate
                .parse()
                .map_err(|_| ListenAddressError::NotASocketAddress {
                    value: candidate.to_owned(),
                })?;

        if address.port() < LOWEST_PORT {
            return Err(ListenAddressError::PortNotAllowed {
                port: address.port(),
            });
        }

        Ok(Self(address))
    }

    /// The validated socket address.
    #[must_use]
    pub fn socket_address(&self) -> SocketAddr {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fleet/listen_address_tests.rs"]
mod tests;
