//! One "let this in" rule of the agent-managed firewall.

use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_templates::nftables::nftables_allow::NftablesAllow;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;

/// The address-family keyword `nft` expects before `saddr` for an IPv4
/// network.
const IPV4_KEYWORD: &str = "ip";

/// The address-family keyword `nft` expects before `saddr` for an IPv6
/// network.
const IPV6_KEYWORD: &str = "ip6";

/// One port the operator has opened: a port, a protocol, and the source
/// network it is open to.
///
/// Every field is a validated type and none of them is a `String`, which is
/// this area's whole injection defence. The rendered ruleset is a grammar
/// `nft` parses as root, and `maran-templates` validates nothing by design —
/// so the guarantee that no caller-supplied byte reaches that grammar has to
/// live here, in the type the render is built from. [`SourceCidr`] carries an
/// `IpAddr` and a prefix length and writes its own text back, so there is no
/// spelling of a source network in a rendered ruleset that this agent did not
/// write, and no newline can reach a file applied as root
/// (rules/security.md §4).
///
/// **"Open to everyone" is `0.0.0.0/0` and only that.** It is the value
/// [`SourceCidr::any_v4`] exists to produce and the value the wire contract
/// documents for an unrestricted rule, and it renders with no `saddr` clause
/// at all — so an unrestricted rule covers IPv6 as well, because the table is
/// in the `inet` family. `::/0` is NOT the same thing: it parses, it is a
/// legitimate value, and it renders as `ip6 saddr ::/0`, which matches IPv6
/// traffic and nothing else.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FirewallRule {
    /// The destination port the rule opens.
    pub port: Port,
    /// The transport protocol the rule opens it for.
    pub protocol: NftablesProtocol,
    /// The source network the rule is open to; `0.0.0.0/0` means every
    /// source.
    pub source: SourceCidr,
}

impl FirewallRule {
    /// Whether this rule is open to every source.
    ///
    /// True exactly for `0.0.0.0/0` — see the note on the type for why `::/0`
    /// is an IPv6 restriction rather than a second spelling of "anyone".
    #[must_use]
    pub fn is_open_to_anyone(&self) -> bool {
        self.source == SourceCidr::any_v4()
    }

    /// Turns the rule into the shape the ruleset template renders.
    ///
    /// The `source_cidr` string is written by [`SourceCidr`]'s own `Display`,
    /// never by a caller, and it is left empty for an unrestricted rule
    /// because the template renders no `saddr` clause in that branch. The
    /// family keyword is one of two constants chosen from the parsed address
    /// family, so no byte derived from a request can become it.
    #[must_use]
    pub fn to_allow(&self) -> NftablesAllow {
        let open_to_anyone = self.is_open_to_anyone();

        NftablesAllow {
            port: self.port.value(),
            protocol: self.protocol,
            source_cidr: if open_to_anyone {
                String::new()
            } else {
                self.source.to_string()
            },
            source_is_any: open_to_anyone,
            family_keyword: if self.source.is_v4() {
                IPV4_KEYWORD
            } else {
                IPV6_KEYWORD
            },
        }
    }

    /// The address-family keyword a source network of this family renders
    /// with.
    ///
    /// Read back by the ruleset parser, so that a rendered line whose keyword
    /// and network disagree is refused rather than read as a rule this agent
    /// could have written.
    #[must_use]
    pub(crate) fn keyword_for(source: &SourceCidr) -> &'static str {
        if source.is_v4() {
            IPV4_KEYWORD
        } else {
            IPV6_KEYWORD
        }
    }
}
