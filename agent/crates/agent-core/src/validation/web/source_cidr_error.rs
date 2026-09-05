//! Why a candidate source network was refused.

/// Reasons [`super::source_cidr::SourceCidr::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SourceCidrError {
    /// The candidate was empty.
    #[error("a source network cannot be empty")]
    Empty,

    /// The candidate carried no `/prefix`.
    ///
    /// A bare address is refused rather than read as a host route, because
    /// `10.0.0.1` and `10.0.0.1/32` look alike and mean different things to a
    /// reader: one of them states the intent and the other leaves it to whoever
    /// reads the rule next.
    #[error("`{candidate}` is not `address/prefix`")]
    MissingPrefix {
        /// What was offered.
        candidate: String,
    },

    /// The address half did not parse as an IPv4 or IPv6 address.
    ///
    /// A hostname lands here, and deliberately: the agent resolves nothing. A
    /// firewall rule built from a name would mean whatever DNS said at the
    /// moment it was written, which is not a property a rule may have.
    #[error("`{address}` is not an IP address")]
    InvalidAddress {
        /// The offending address half.
        address: String,
    },

    /// The prefix half was empty, over three digits, or not decimal.
    #[error("`{prefix}` is not a decimal prefix length")]
    InvalidPrefix {
        /// The offending prefix half.
        prefix: String,
    },

    /// The prefix was longer than the address family allows.
    ///
    /// `/33` on IPv4 and `/129` on IPv6. Bounded per family rather than at 128
    /// for both: a `/64` is an ordinary IPv6 network and a nonsense IPv4 one.
    #[error("`/{prefix}` exceeds the longest prefix for this address family, /{maximum}")]
    PrefixTooLong {
        /// The offending prefix, as written.
        prefix: String,
        /// The longest prefix this address family allows.
        maximum: u8,
    },

    /// The address is an IPv4 address wearing IPv6 notation.
    ///
    /// `::ffff:1.2.3.4` (v4-mapped) and `::1.2.3.4` (v4-compatible, deprecated
    /// by RFC 4291) both name an IPv4 host in a form `std::net` parses as IPv6.
    /// The firewall keeps one rule path per family and asks
    /// [`super::source_cidr::SourceCidr::is_v4`] which one to use, so such a
    /// value would be rendered as an IPv6 rule and would then match nothing an
    /// IPv4 packet carries — a source restriction that silently restricts
    /// nothing. `::` and `::1` share the compatible shape and are ordinary IPv6
    /// addresses, so they are not refused.
    #[error("write an IPv4 network in IPv4 notation — this address is `{as_v4}`")]
    Ipv4InIpv6Notation {
        /// The same address written as IPv4.
        as_v4: String,
    },

    /// Bits below the prefix were set — `203.0.113.7/24`.
    ///
    /// Refused rather than masked, and the reason is the size of the mistake:
    /// `203.0.113.7/24` is either one host or 256 of them, the two differ by a
    /// factor of 256 in what they let through, and masking would silently
    /// choose the wider one on the caller's behalf. The error carries both
    /// spellings so the caller says which they meant.
    ///
    /// This is also what completes the promise
    /// [`SourceCidrError::NotCanonical`] makes. Without it, `10.0.0.0/8` has
    /// 2^24 accepted spellings, every one of which renders a different rule
    /// text — and "is this rule already present?" then has more than one
    /// answer, which is how a delete leaves a rule live.
    #[error("write this either as the network `{network}` or as the single address `{host}`")]
    HostBitsSet {
        /// The candidate masked down to its prefix.
        network: String,
        /// The candidate as a single-address network.
        host: String,
    },

    /// The candidate parsed, but is not how the value is written back.
    ///
    /// This is the check that refuses leading-zero octets (`010.0.0.1`), an
    /// uppercase or uncompressed IPv6 address (`2001:0DB8::`), and a padded
    /// prefix (`/032`). Each of those is a second spelling of a network that
    /// already has one, and a firewall whose rules can be spelled two ways is a
    /// firewall where "is this rule already present?" has two answers — which
    /// is how a delete leaves a rule live.
    ///
    /// The error carries the canonical spelling so the caller can send that.
    #[error("write this network as `{canonical}`")]
    NotCanonical {
        /// How the value is written.
        canonical: String,
    },
}
