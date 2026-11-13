//! Reads one rendered rule line back into a firewall rule.

use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::port_span::PortSpan;

/// The keyword `nft` expects between a protocol and a port number.
const DPORT: &str = "dport";

/// The verdict every rule this agent renders ends in.
const ACCEPT: &str = "accept";

/// The keyword `nft` expects before a source network.
const SADDR: &str = "saddr";

/// Token count of a rule line open to every source: `tcp dport 80 accept`.
const OPEN_RULE_TOKENS: usize = 4;

/// Token count of a source-restricted rule line:
/// `tcp dport 3306 ip saddr 10.0.0.0/8 accept`.
const RESTRICTED_RULE_TOKENS: usize = 7;

/// Reads one rendered rule line back into a [`FirewallRule`].
///
/// `None` for anything this agent's template does not render, which the
/// caller turns into [`FirewallError::ForeignRuleset`]. Every value goes
/// through its validated type — [`PortSpan`] and [`SourceCidr`] — so a rule
/// that comes out of this function is a rule that could be rendered back
/// unchanged, and the address-family keyword has to agree with the network it
/// precedes. A range whose bounds this agent would refuse to render is refused
/// here too, by the same constructor.
pub(crate) fn parse_rule(line: &str) -> Option<FirewallRule> {
    let tokens: Vec<&str> = line.split_whitespace().collect();

    let protocol = match *tokens.first()? {
        "tcp" => NftablesProtocol::Tcp,
        "udp" => NftablesProtocol::Udp,
        _ => return None,
    };
    if *tokens.get(1)? != DPORT {
        return None;
    }
    let ports = PortSpan::parse_rendered(tokens.get(2)?)?;

    let source = match tokens.len() {
        OPEN_RULE_TOKENS => SourceCidr::any_v4(),
        RESTRICTED_RULE_TOKENS => {
            if *tokens.get(4)? != SADDR {
                return None;
            }
            let source = SourceCidr::parse(tokens.get(5)?).ok()?;
            if *tokens.get(3)? != FirewallRule::keyword_for(&source) {
                return None;
            }

            source
        }
        _ => return None,
    };

    if *tokens.last()? != ACCEPT {
        return None;
    }

    Some(FirewallRule {
        ports,
        protocol,
        source,
    })
}
