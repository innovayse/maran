//! One TCP socket the kernel reports as listening.

/// A listening TCP socket, reduced to the two facts the status needs.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ListeningSocket {
    /// The local port.
    pub port: u16,
    /// Whether the socket is bound to a loopback address and so unreachable from
    /// outside the host.
    pub loopback_only: bool,
}
