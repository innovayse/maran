//! One "let this in" rule, in the shape the ruleset template renders.

use crate::nftables::nftables_protocol::NftablesProtocol;

/// A single accept rule of the agent's nftables ruleset: one port, one
/// protocol, and either every source or one network.
///
/// Every field is a plain value the CALLER fills. This crate renders text and
/// validates nothing — the validated types live in `agent-core`
/// (`Port`, `SourceCidr`) and the conversion into this shape belongs to
/// `ops::firewall`, so a rule that reaches here has already been checked by
/// the layer whose job that is.
///
/// Two invariants the caller owns, because nothing here can enforce them:
///
/// 1. `source_cidr` is a validated `SourceCidr`, never a request field. See
///    the field's own note — a newline in it appends a rule of somebody
///    else's choosing to a file applied as root.
/// 2. When `source_is_any` is false, `source_cidr` AND `family_keyword` are
///    both non-empty, and the keyword matches the address family of the
///    network. An empty pair renders `tcp dport 8080  saddr  accept`, which
///    `nft` refuses to parse; a mismatched pair renders a family error. Both
///    fail safe — `nft -f` is transactional, so the live ruleset is untouched
///    — but the operator's apply fails for a reason no message explains, so
///    the value is built correctly rather than caught later.
pub struct NftablesAllow {
    /// Destination port the rule opens.
    pub port: u16,
    /// Transport protocol the rule names.
    pub protocol: NftablesProtocol,
    /// The source network in CIDR form, such as `10.0.0.0/8`.
    ///
    /// The only field here whose bytes a caller composes, and that matters
    /// because a ruleset is line-oriented: a newline in this value renders a
    /// further rule line of somebody else's choosing into a file `nft -f`
    /// applies as root (rules/security.md §4). The caller fills it from a
    /// validated `SourceCidr` and never from a request field — the value is
    /// validated, not escaped, because there is no escaping in this grammar to
    /// fall back on.
    ///
    /// Read only when `source_is_any` is false; a rule open to everyone leaves
    /// it empty, because the template renders no `saddr` clause at all in that
    /// branch.
    pub source_cidr: String,
    /// Whether the rule accepts from every source.
    ///
    /// True renders the rule without a `saddr` clause; false renders
    /// `<family_keyword> saddr <source_cidr>`. It is a separate flag rather
    /// than an empty `source_cidr` so that "open to the world" is a decision
    /// the caller states, never one an empty string falls into.
    pub source_is_any: bool,
    /// The address-family keyword that precedes `saddr` — `ip` for an IPv4
    /// network, `ip6` for an IPv6 one.
    ///
    /// `&'static str` rather than `String` because the caller picks one of two
    /// constants from the address family it already parsed: no byte derived
    /// from a request can become this keyword.
    pub family_keyword: &'static str,
}
