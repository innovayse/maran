//! The transport protocol an allow rule opens a port for.

use std::fmt;

/// The transport protocol of one firewall allow rule, rendered as the keyword
/// `nft` expects immediately before `dport`.
///
/// A closed set of two rather than a number or a free string on purpose: the
/// rendered file is a grammar `nft` parses, so anything reaching it has to be
/// a value this crate itself chose. The panel offers TCP and UDP and nothing
/// else, and a third protocol is a new variant here rather than a caller
/// passing a keyword through.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum NftablesProtocol {
    /// TCP, rendered as `tcp`.
    Tcp,
    /// UDP, rendered as `udp`.
    Udp,
}

impl fmt::Display for NftablesProtocol {
    /// Writes the `nft` keyword for the protocol — `tcp` or `udp`.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        let keyword = match *self {
            Self::Tcp => "tcp",
            Self::Udp => "udp",
        };

        formatter.write_str(keyword)
    }
}
