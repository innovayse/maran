//! Turning one rule of the agent's ruleset into the wire's rule message.

use maran_ops::firewall::{FirewallRule, NftablesProtocol};

use crate::proto::FirewallRule as WireRule;
use crate::proto::Protocol;

/// Builds the wire message for one rule the agent's ruleset holds.
///
/// The source is written by [`SourceCidr`](maran_agent_core::validation::web::source_cidr::SourceCidr)'s
/// own `Display` rather than echoed from whatever the caller once sent, so a
/// listing reports the network the firewall is actually running — the
/// canonical spelling the rule was stored under, which is what a panel has to
/// send back to deny it again.
#[must_use]
pub fn listed_rule(rule: &FirewallRule) -> WireRule {
    let protocol = match rule.protocol {
        NftablesProtocol::Tcp => Protocol::Tcp,
        NftablesProtocol::Udp => Protocol::Udp,
    };

    WireRule {
        port: u32::from(rule.ports.lower().value()),
        // Absent for a single port, which is what every rule written before the
        // field existed is — and what a panel too old to read the field sees
        // for every rule, since an unknown field is ignored rather than
        // refused. A newer panel reading an absent bound reads "the single port
        // `port`", which is the same rule it would have shown before.
        port_to: rule.ports.upper().map(|upper| u32::from(upper.value())),
        protocol: protocol as i32,
        source_cidr: rule.source.to_string(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/firewall/listed_rule_tests.rs"]
mod tests;
