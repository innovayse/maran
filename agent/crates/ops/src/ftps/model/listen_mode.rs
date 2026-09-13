//! Which pair of listening options the FTPS daemon was configured with.

/// The listening socket the daemon binds: one dual-stack socket, or an IPv4-only
/// one.
///
/// Not a setting. It is an answer the agent works out by probing the host — see
/// [`probe_listen_mode`](crate::ftps::probe_listen_mode) — and then reports, so
/// a screen can state which mode a server ended up in. There is nothing for an
/// operator to choose here: the two options are mutually exclusive in vsftpd
/// (setting both `listen` and `listen_ipv6` is a refusal), and which one works
/// is decided by whether the kernel has IPv6 at all.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ListenMode {
    /// One `AF_INET6` socket, serving IPv4 clients through it.
    ///
    /// The default, and what both distributions' own shipped configuration
    /// does: with the Linux default `net.ipv6.bindv6only=0` an `AF_INET6`
    /// listening socket accepts IPv4 connections too, so one daemon serves both
    /// families of address.
    DualStack,
    /// One `AF_INET` socket, because this kernel has no IPv6.
    ///
    /// Chosen only when a listening bind of `[::]:0` was refused with an error
    /// that is evidence about address families. A host in this mode serves no
    /// IPv6 client, which is not a downgrade the agent chose — it is the only
    /// socket the kernel will give it.
    Ipv4Only,
}
