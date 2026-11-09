//! How a mail port is bound, as the kernel reports it.

/// What the kernel says about one port.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PortBinding {
    /// Nothing listens on it.
    NotBound,
    /// Something listens on a loopback address only.
    LoopbackOnly,
    /// Something listens on an address other hosts can reach.
    Public,
}
