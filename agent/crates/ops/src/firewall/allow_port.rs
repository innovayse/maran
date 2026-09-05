//! AllowPort: open one port, to everyone or to one source network.

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;

use crate::firewall::apply_ruleset::apply_ruleset;
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;
use crate::firewall::list_rules::read_ruleset;
use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::ruleset_ports::RulesetPorts;

/// Adds `rule` to the host's firewall and loads the result.
///
/// The whole ruleset is re-rendered and re-applied rather than one rule being
/// appended to the running configuration, and that is the design rather than
/// a shortcut: the rendered file is the rule store, so the file and the
/// kernel cannot drift, and the file's replace idiom is what makes the apply
/// CONVERGE on what was rendered instead of adding to what was there. A
/// design that appended reported success while `nft -f`'s additivity left
/// removed rules live — measured, and the reason
/// the apply engine exists in the shape it does.
///
/// `ports` arrives on the request rather than being remembered here (R2).
/// Both values are host facts only the installer knows: a host whose sshd
/// listens on 2222 must not be locked out by a ruleset that only knows 22.
///
/// # Idempotency
///
/// A rule the ruleset already carries is [`FirewallError::AlreadyExists`],
/// and so is a rule that would render the byte-identical file. The second
/// check is not a duplicate of the first — it is what catches an allow for
/// the SSH port from every source, which the unconditional fallback already
/// grants without any rule being recorded. Reporting success there and
/// recording a rule would create a rule that vanishes on the next read, since
/// the fallback and an any-source SSH rule render the same line.
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
/// - [`FirewallError::AlreadyExists`] when the firewall already allows
///   exactly this. Nothing is written.
/// - [`FirewallError::ForeignRuleset`] when the file at the ruleset path was
///   not written by this agent. Nothing is overwritten.
/// - [`FirewallError::RulesetUnreadable`] when that file is there and will
///   not be read.
/// - [`FirewallError::RuleRefusedByNft`] when `nft --check` rejects the
///   rendered ruleset, carrying its standard error. The live firewall is
///   untouched.
/// - [`FirewallError::RenderFailed`], [`FirewallError::StagingFailed`] and
///   [`FirewallError::NftFailed`] as the apply engine documents them.
pub fn allow_port(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    ports: &RulesetPorts,
    rule: &FirewallRule,
) -> Result<(), FirewallError> {
    let _guard = firewall_lock();

    let current = read_ruleset(host, ports)?;
    if current.contains(rule) {
        return Err(FirewallError::AlreadyExists);
    }

    let wanted = current.with(rule);
    let contents = wanted.render(ports)?;
    if contents == current.render(ports)? {
        return Err(FirewallError::AlreadyExists);
    }

    apply_ruleset(host, distro, AgentPaths::nftables_ruleset_path(), &contents)
}

#[cfg(test)]
#[path = "../tests/firewall/allow_port_tests.rs"]
mod tests;
