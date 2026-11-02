//! One source network of an accept rule, in the shape the ruleset renders it.

/// A source network and the address-family keyword that precedes it.
///
/// The two travel together because the keyword is the family of the network:
/// a mismatched pair renders a family error `nft` refuses, and an empty pair
/// renders `saddr  accept`, which it refuses to parse. Both fail safe, since
/// `nft -f` is transactional, but the operator's apply fails for a reason no
/// message explains, so the caller builds the pair correctly rather than
/// having it caught later.
pub struct NftablesNetwork {
    /// The address-family keyword that precedes `saddr`: `ip` for an IPv4
    /// network, `ip6` for an IPv6 one.
    ///
    /// `&'static str` rather than `String` because the caller picks one of two
    /// constants from the address family it already parsed: no byte derived
    /// from a request can become this keyword.
    pub family_keyword: &'static str,
    /// The network in CIDR form, such as `10.0.0.0/8`.
    ///
    /// The only value here whose bytes a caller composes, and that matters
    /// because a ruleset is line-oriented: a newline in it renders a further
    /// rule line of somebody else's choosing into a file `nft -f` applies as
    /// root (rules/security.md §4). The caller fills it from a validated
    /// `SourceCidr` and never from a request field — the value is validated,
    /// not escaped, because there is no escaping in this grammar to fall back
    /// on.
    pub cidr: String,
}
