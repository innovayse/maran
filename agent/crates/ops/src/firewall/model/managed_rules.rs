//! Separates the operator's rules from a ruleset's unconditional accepts.

use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::port_span::PortSpan;
use crate::firewall::model::ruleset_ports::RulesetPorts;

/// The fewest rule lines a ruleset this agent rendered can hold.
///
/// The SSH accept (the unconditional fallback, or the operator's own rules in
/// its place) and the panel port's accept. Both are unconditional, so a
/// rendered ruleset never has fewer.
const MINIMUM_RULE_LINES: usize = 2;

/// Separates the operator's rules from the unconditional accepts.
///
/// The rendered region is one block per SSH port, then the panel port's
/// accept, then everything else. The SSH region is the leading run of TCP
/// lines whose port sshd listens on — sound because a TCP rule for an SSH port
/// can never be rendered anywhere else, so everything after the panel line is
/// for some other port or protocol.
///
/// **It takes the ports rather than inferring them, and that is the whole of
/// what changed when one SSH port became a list.** The first line's port
/// used to delimit the block; with several ports the next block's fallback
/// sits exactly where the panel accept does, and inferring would report the
/// panel port as a rule nobody created.
///
/// A lone bare accept in a port's block is that port's FALLBACK and not a
/// rule: it is what the template renders when the operator has authored
/// nothing for it. Reporting it would show every host a rule nobody created,
/// and denying it would report success while the template rendered it straight
/// back. Each port is judged on its own block, so an explicit rule for one SSH
/// port leaves every other port's fallback exactly where it was.
///
/// # Errors
///
/// Returns [`FirewallError::PortsDisagree`] when the region is shorter than
/// the unconditional accepts, or does not carry the panel port's bare accept
/// where the render puts it. Not `ForeignRuleset`: the file has already proved
/// itself this agent's by its marker, its idiom and its chain preamble, so what
/// is wrong is the ports the caller named — which is also what a host whose
/// sshd and panel share a port would look like, and no host is, because two
/// daemons cannot bind one port.
pub(crate) fn managed_rules(
    region: &[FirewallRule],
    ports: &RulesetPorts,
) -> Result<Vec<FirewallRule>, FirewallError> {
    // Every refusal below is `PortsDisagree` and not `ForeignRuleset`. By the
    // time control reaches here the marker, the replace idiom, the chain
    // preamble and the closing braces have all matched this agent's own
    // render, so the file IS ours; what has not lined up is the caller's idea
    // of which ports are unconditionally accepted.
    if region.len() < MINIMUM_RULE_LINES {
        return Err(FirewallError::PortsDisagree);
    }

    // The SSH region: the leading run of TCP rules for ports sshd listens on,
    // in the order the template rendered the ports. Everything after it must
    // begin with the panel accept.
    let ssh_region = region
        .iter()
        .take_while(|rule| {
            // Single ports only, matching what `render` routes into a port's
            // block: a range is rendered as an ordinary allow, so a range whose
            // lower bound happens to be an SSH port must not be mistaken for
            // that port's block and swallowed here.
            rule.protocol == NftablesProtocol::Tcp
                && rule.ports.upper().is_none()
                && ports.ssh_ports.contains(&rule.ports.lower())
        })
        .count();

    let Some(panel) = region.get(ssh_region) else {
        return Err(FirewallError::PortsDisagree);
    };
    if panel.protocol != NftablesProtocol::Tcp
        || panel.ports != PortSpan::single(ports.panel_port)
        || !panel.is_open_to_anyone()
    {
        return Err(FirewallError::PortsDisagree);
    }

    let mut rules = Vec::new();
    for port in &ports.ssh_ports {
        // This port's own block, wherever the template put it. A block of
        // exactly one any-source rule IS the unconditional fallback and
        // contributes nothing: reporting it would show every host a rule
        // nobody created, and denying it would report success while the
        // template rendered it straight back (R2).
        let block: Vec<&FirewallRule> = region
            .get(..ssh_region)
            .unwrap_or_default()
            .iter()
            .filter(|rule| rule.ports == PortSpan::single(*port))
            .collect();

        let fallback_only = block.len() == 1 && block.iter().all(|rule| rule.is_open_to_anyone());
        if fallback_only {
            continue;
        }

        rules.extend(block.into_iter().cloned());
    }

    rules.extend_from_slice(region.get(ssh_region + 1..).unwrap_or_default());

    Ok(rules)
}
