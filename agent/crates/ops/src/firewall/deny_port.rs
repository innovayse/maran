//! DenyPort: take one port rule away, and prove it is really gone.

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;

use crate::firewall::apply_ruleset::apply_ruleset;
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;
use crate::firewall::list_rules::read_ruleset;
use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::ruleset_ports::RulesetPorts;

/// Removes `rule` from the host's firewall and loads the result.
///
/// # This operation is the one the replace idiom exists for
///
/// A deny renders the whole ruleset WITHOUT the rule and applies it. `nft -f`
/// is ADDITIVE, so applying a file that merely omits a rule does not remove
/// it: measured on nftables v1.0.9, re-applying a re-rendered ruleset with
/// the 3306 rule dropped left `3306 rules: 1, loopback rules: 2` — the denied
/// rule still live and every other rule duplicated. What makes the removal
/// real is the create-delete-redeclare idiom at the head of the rendered file,
/// which deletes the table before redeclaring it.
///
/// That is a property of the file rather than of this function, so it is
/// guarded from both sides: the ruleset is only read back when it carries the
/// idiom ([`RulesetState::parse`](crate::firewall::model::ruleset_state::RulesetState::parse)),
/// and the polygon suite asserts on a real kernel that the denied port is
/// absent from `nft list table inet maran` afterwards. The first design of
/// this operation passed every fake-host test while leaving the port open.
///
/// # The SSH port is deliberately fail-open
///
/// Denying the last TCP rule for the SSH port does not close it: the template
/// renders the unconditional accept again the moment no operator rule for
/// that port remains (R2). The deny still succeeds and the rule is still
/// gone — what returns is the fallback, so an operator cannot lock themselves
/// out of the host with a firewall change and have no way back in.
///
/// # Idempotency
///
/// A rule the ruleset does not carry is [`FirewallError::NotFound`], which is
/// the answer to a repeated deny.
///
/// # Calling this
///
/// Synchronous, and it MUST be invoked from `tokio::task::spawn_blocking` —
/// never awaited on a runtime worker. It spawns `nft`, writes to disk, and
/// takes the module lock with `blocking_lock`, which PANICS rather than block
/// a worker. See this area's module documentation for the whole requirement.
///
/// # Errors
///
/// - [`FirewallError::NotFound`] when no such rule is installed. Nothing is
///   written.
/// - [`FirewallError::ForeignRuleset`] when the file at the ruleset path was
///   not written by this agent. Nothing is overwritten.
/// - [`FirewallError::RulesetUnreadable`] when that file is there and will
///   not be read.
/// - [`FirewallError::RuleRefusedByNft`] when `nft --check` rejects the
///   rendered ruleset, carrying its standard error. The live firewall is
///   untouched.
/// - [`FirewallError::RenderFailed`], [`FirewallError::StagingFailed`] and
///   [`FirewallError::NftFailed`] as the apply engine documents them.
pub fn deny_port(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    ports: &RulesetPorts,
    rule: &FirewallRule,
) -> Result<(), FirewallError> {
    let _guard = firewall_lock();

    let current = read_ruleset(host, ports)?;
    if !current.contains(rule) {
        return Err(FirewallError::NotFound);
    }

    let contents = current.without(rule).render(ports)?;

    apply_ruleset(host, distro, AgentPaths::nftables_ruleset_path(), &contents)
}

#[cfg(test)]
#[path = "../tests/firewall/deny_port_tests.rs"]
mod tests;
