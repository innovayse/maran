//! The ruleset this agent renders for a host with no rules at all.

use maran_templates::nftables::nftables_ruleset::NftablesRuleset;
use maran_templates::nftables::nftables_ssh_port::NftablesSshPort;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::output_policy::OutputPolicy;

/// The SSH port the reference render is asked for.
///
/// Nothing read out of the reference depends on it: the lines taken from it
/// are the marker, the replace idiom, the chain preamble and the two closing
/// braces, and not one of them mentions a port. It differs from
/// [`REFERENCE_PANEL_PORT`] so that the two unconditional rule lines cannot
/// coincide, which is what keeps the cut between "preamble" and "rules"
/// unambiguous.
const REFERENCE_SSH_PORT: u16 = 1;

/// The panel port the reference render is asked for. See
/// [`REFERENCE_SSH_PORT`].
const REFERENCE_PANEL_PORT: u16 = 2;

/// The reference render for a host with `policy`'s facts: the ruleset of a host
/// with no rules at all.
///
/// # Errors
///
/// Returns [`FirewallError::RenderFailed`] when the template fails.
pub(crate) fn render_reference_ruleset(policy: &OutputPolicy) -> Result<String, FirewallError> {
    NftablesRuleset {
        // Exactly one SSH port, whatever the host really has. The lines read
        // out of the reference are the marker, the replace idiom, the chain
        // preamble and the two closing braces, and not one of them mentions a
        // port — but the COUNT of unconditional accepts is used to cut
        // preamble from rules, so the reference has to fix it at one.
        ssh_ports: vec![NftablesSshPort {
            port: REFERENCE_SSH_PORT,
            rules: Vec::new(),
        }],
        panel_port: REFERENCE_PANEL_PORT,
        allows: Vec::new(),
        customer_uid_min: policy.customer_uid_min(),
        self_v4_prefixes: policy
            .self_v4_prefixes()
            .iter()
            .map(|prefix| (prefix.network(), prefix.length()))
            .collect(),
        self_v4: policy.self_v4().to_vec(),
        self_v6: policy.self_v6().to_vec(),
    }
    .render_config()
    .map_err(FirewallError::from)
}
