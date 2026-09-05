//! Why a candidate ban address was refused.

/// Reasons [`super::ban_address::BanAddress::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum BanAddressError {
    /// The candidate was empty.
    #[error("a ban address cannot be empty")]
    Empty,

    /// The candidate did not parse as a single IPv4 or IPv6 address.
    ///
    /// A network (`10.0.0.0/8`), a hostname and a scoped address
    /// (`fe80::1%eth0`) all land here. A ban is an element of an address set,
    /// not a rule, so it is one address and never a range — and the agent
    /// resolves nothing, so a name has no address to be.
    #[error("`{candidate}` is not a single IP address")]
    Invalid {
        /// What was offered.
        candidate: String,
    },

    /// The address is an IPv4 address wearing IPv6 notation.
    ///
    /// `::ffff:1.2.3.4` (v4-mapped) and `::1.2.3.4` (v4-compatible, deprecated
    /// by RFC 4291) both name an IPv4 host in a form `std::net` parses as IPv6.
    /// The firewall keeps one ban set per family and asks
    /// [`super::ban_address::BanAddress::is_v4`] which one to add to, so such a
    /// value would be added to the IPv6 set and would then never match the IPv4
    /// packets the ban was ordered against — a ban that bans nothing. `::` and
    /// `::1` share the compatible shape and are ordinary IPv6 addresses, so
    /// they are not refused.
    ///
    /// [`super::source_cidr_error::SourceCidrError`] carries the same refusal
    /// for the same reason, and both are decided by the one predicate in
    /// `super::ipv4_disguise`: each type words its own refusal, neither owns
    /// the answer.
    ///
    /// This is the refusal a caller building a ban from a socket-reported peer
    /// address will meet, because a dual-stack listener reports an IPv4 client
    /// in exactly this form. Such a caller must map the address to IPv4 before
    /// parsing; `as_v4` is the address to retry with, and
    /// [`super::ban_address::BanAddress`]'s own documentation says when to do
    /// it rather than leaving it to be discovered here.
    #[error("write an IPv4 address in IPv4 notation — this address is `{as_v4}`")]
    Ipv4InIpv6Notation {
        /// The same address written as IPv4.
        as_v4: String,
    },

    /// The address is a loopback address, which no ban can ever block.
    ///
    /// `127.0.0.0/8` and `::1` are refused. Both nftables tables the agent
    /// renders accept `iif "lo"` before either ban set is consulted — the bans
    /// table at hook priority -5 and the rules table at 0 — because the panel's
    /// own web-server-to-application hop lives on loopback and nothing, not
    /// even a ban, may sever it. An element added to a ban set for such an
    /// address therefore matches no packet: the ban is installed, reported as
    /// placed, and blocks nothing.
    ///
    /// This is the same failure the
    /// [`BanAddressError::Ipv4InIpv6Notation`] refusal exists to prevent — a
    /// ban that silently does nothing — and it is refused for the same reason:
    /// an operator reading an audit journal that says an address was banned
    /// must be able to believe it.
    ///
    /// The whole `127.0.0.0/8` block is refused rather than the single address
    /// `127.0.0.1`, because every address in it is loopback and `iif "lo"`
    /// exempts the interface, not one address on it.
    #[error(
        "`{address}` is a loopback address: loopback traffic is accepted before any ban is consulted, so such a ban would block nothing"
    )]
    Loopback {
        /// The loopback address that was offered.
        address: String,
    },

    /// The candidate parsed, but is not how the address is written back.
    ///
    /// Bans are added and deleted by their text, and the panel keeps the
    /// durable record of what is banned. An address with two spellings is a ban
    /// that can be added under one and left behind under the other, so the one
    /// spelling this type writes is the only one it accepts.
    #[error("write this address as `{canonical}`")]
    NotCanonical {
        /// How the address is written.
        canonical: String,
    },
}
