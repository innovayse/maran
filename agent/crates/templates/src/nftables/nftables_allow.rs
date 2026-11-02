//! One "let this in" rule, in the shape the ruleset template renders.

use crate::nftables::nftables_allow_source::NftablesAllowSource;
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
/// 1. The network's CIDR is a validated `SourceCidr`, never a request field.
///    See [`NftablesNetwork`](crate::nftables::nftables_network::NftablesNetwork)
///    — a newline in it appends a rule of somebody else's choosing to a file
///    applied as root.
/// 2. The network's family keyword matches the address family of its CIDR. A
///    mismatched pair renders a family error. It fails safe — `nft -f` is
///    transactional, so the live ruleset is untouched — but the operator's
///    apply fails for a reason no message explains, so the value is built
///    correctly rather than caught later.
pub struct NftablesAllow {
    /// Destination port the rule opens, or the LOWER bound when
    /// [`Self::port_to`] is present.
    pub port: u16,
    /// Inclusive upper bound of a port range, or `None` for a single port.
    ///
    /// `Some(upper)` renders `dport <port>-<upper>`; `None` renders
    /// `dport <port>` and is byte-identical to what this type rendered before
    /// the field existed. That is the whole compatibility claim of the field,
    /// and the committed `ruleset.nft` golden — which carries no range — is
    /// what holds it up.
    ///
    /// A third invariant the caller owns, beside the two on the type: `upper`
    /// is strictly ABOVE `port`. Nothing here can enforce it, and both ways of
    /// getting it wrong are silent in a different way — `nft` refuses
    /// `dport 30099-30000` and aborts the whole transactional load, while
    /// `dport 30000-30000` loads happily as a second spelling of a single port
    /// that a later deny for `30000` would not match. `ops::firewall`'s
    /// `PortSpan` is the validated type that decides both, and it is the only
    /// thing that fills this field.
    pub port_to: Option<u16>,
    /// Transport protocol the rule names.
    pub protocol: NftablesProtocol,
    /// Where the rule lets traffic in from: every source, or one network.
    ///
    /// [`NftablesAllowSource::Anyone`] renders the rule without a `saddr`
    /// clause; a network renders `<family_keyword> saddr <cidr>`. The choice is
    /// stated by the caller, never one an empty string falls into.
    pub source: NftablesAllowSource,
}
