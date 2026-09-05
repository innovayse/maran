//! ListRules: the port rules the operator has installed.

use maran_agent_core::agent_paths::AgentPaths;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::ruleset_ports::RulesetPorts;
use crate::firewall::model::ruleset_state::RulesetState;

/// Lists every port rule the agent currently manages.
///
/// The two unconditional accepts — SSH's and the panel's — are deliberately
/// NOT among them. They are properties of the ruleset rather than rules the
/// operator added: nothing created them, `deny_port` cannot take them away
/// (R2 returns SSH's fallback the moment the last TCP rule for that port
/// goes), and reporting them would offer the panel a delete button that
/// silently does nothing. An operator's OWN rule for the SSH port is a rule
/// like any other and is listed.
///
/// A host whose ruleset file has never been written has no rules, which is an
/// ordinary answer and not a failure — it is the state every host is in
/// before the installer seeds it.
///
/// This operation does not take the module lock every mutation is held
/// under, and does not need to: it reads one file in one call, so there is no
/// check-then-act to lose a race, and the worst a concurrent mutation can do
/// is answer with the rules from either side of one atomic rename.
///
/// # Calling this
///
/// Synchronous, and it MUST be invoked from `tokio::task::spawn_blocking` —
/// never awaited on a runtime worker. Unlike this area's mutations it takes no
/// lock, so nothing panics when a caller gets this wrong; it just spawns `nft`
/// and reads a file on the worker, stalling every other command sharing it,
/// and the first symptom under load is an unrelated timeout naming nothing.
/// Nothing here can catch that at run time — a blocking-pool thread and a
/// worker are indistinguishable through tokio's public API, see
/// `firewall_lock`. The call sites are checked instead, by
/// `firewall_service_tests.rs` in the `agent` crate.
///
/// # Errors
///
/// - [`FirewallError::ForeignRuleset`] when the file at the ruleset path was
///   not written by this agent, or cannot be read back as one it wrote.
/// - [`FirewallError::RulesetUnreadable`] when the file is there and will not
///   be read.
/// - [`FirewallError::RenderFailed`] when this agent's own ruleset template —
///   which is what the accepted file shape is derived from — fails to render.
pub fn list_rules(
    host: &dyn FirewallHost,
    ports: &RulesetPorts,
) -> Result<Vec<FirewallRule>, FirewallError> {
    Ok(read_ruleset(host, ports)?.rules().to_vec())
}

/// Reads the rule store: the rendered file at
/// `AgentPaths::nftables_ruleset_path()`.
///
/// One place answers "what does this host currently allow", so the three
/// operations that ask cannot disagree about where the store is or what an
/// absent one means. An absent file is [`RulesetState::empty`] rather than an
/// error, because a host the installer has not seeded yet has no rules —
/// which is a state, not a fault.
///
/// # Errors
///
/// The same conditions as [`list_rules`].
pub(super) fn read_ruleset(
    host: &dyn FirewallHost,
    ports: &RulesetPorts,
) -> Result<RulesetState, FirewallError> {
    match host.read_file(AgentPaths::nftables_ruleset_path())? {
        Some(text) => RulesetState::parse(&text, ports),
        None => Ok(RulesetState::empty()),
    }
}

#[cfg(test)]
#[path = "../tests/firewall/list_rules_tests.rs"]
mod tests;
