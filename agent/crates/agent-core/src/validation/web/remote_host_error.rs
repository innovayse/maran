//! Why a remote host was refused.

/// Reasons [`super::remote_host::RemoteHost::parse`] refuses a candidate.
///
/// No variant carries the refused text: the value is an administrator's input and
/// an operator log line needs to know WHICH rule refused it, not the bytes.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RemoteHostError {
    /// The candidate was empty.
    #[error("a remote host cannot be empty")]
    Empty,

    /// The candidate is neither an IP address literal nor a well-formed host
    /// name: a character outside the host grammar (a scheme, a path, a port, a
    /// space, a control character), a malformed label, a name over the DNS
    /// limit, or a malformed IPv4 literal such as `999.1.1.1` or `127.1`.
    #[error("a remote host must be a host name or an IP address")]
    NotAHost,

    /// The name is `localhost` or ends in `.localhost`, which always names this
    /// machine.
    #[error("a remote host cannot be a localhost name")]
    LocalName,

    /// The candidate is an IP address literal this agent never connects to:
    /// loopback, link-local, unspecified, multicast, or the IPv4 broadcast and
    /// "this network" ranges, including the same address spelled inside an IPv6
    /// literal.
    #[error("a remote host cannot be a loopback, link-local, unspecified or multicast address")]
    ForbiddenAddress,
}
