//! ReconcileOutputChain: bring the output chain of the port-25 lockdown up on a
//! host that does not have it, without waiting for the next allow or deny.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::firewall::apply_ruleset::apply_ruleset;
use crate::firewall::ensure_bans_table::TABLE_FAMILY;
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;
use crate::firewall::model::output_chain_outcome::OutputChainOutcome;
use crate::firewall::model::ruleset_state::RulesetState;
use crate::firewall::read_output_policy::read_output_policy;

/// `nft`'s verb for showing an object.
const LIST: &str = "list";

/// `nft`'s noun for a chain.
const CHAIN: &str = "chain";

/// The table the output chain lives in. Must match `templates/nftables/ruleset.nft.j2`.
const RULES_TABLE: &str = "maran";

/// The chain the lockdown's second layer lives in. Must match the template.
const OUTPUT_CHAIN: &str = "output";

/// Makes the host's output chain exist, in the file and in the kernel, and
/// current for the host's own addresses and uid floor.
///
/// Run once when the agent starts: a host upgraded from a release that rendered
/// no output chain would otherwise stay on the first layer of the port-25
/// lockdown until somebody happened to allow or deny a port, while looking
/// healthy. The ports are not needed and not asked for: the rules are carried
/// over byte for byte (see [`RulesetState::with_current_host_facts`]) and only the
/// tail that holds no operator input is rendered again.
///
/// A file this agent did not write is left alone and reported, as every other
/// operation does.
///
/// # Calling this
///
/// Synchronous, and it MUST be invoked from `tokio::task::spawn_blocking`: it
/// spawns `nft`, writes to disk and takes the module lock.
///
/// # Errors
///
/// - [`FirewallError::ForeignRuleset`] and [`FirewallError::RulesetUnreadable`]
///   as `list_rules` documents them. Nothing is written.
/// - [`FirewallError::HostAddressesUnreadable`] and
///   [`FirewallError::CustomerUidFloorUnusable`] when the host facts cannot be
///   read; nothing is written.
/// - The errors of the apply engine when a rewrite is refused or fails.
pub fn reconcile_output_chain(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> Result<OutputChainOutcome, FirewallError> {
    let _guard = firewall_lock();

    let path = AgentPaths::nftables_ruleset_path();
    let Some(text) = host.read_file(path)? else {
        return Ok(OutputChainOutcome::NotSeeded);
    };

    let policy = read_output_policy(host, distro)?;
    let wanted = RulesetState::with_current_host_facts(&text, &policy)?;

    let listed = host.run(
        &distro.nft_binary(),
        &[
            END_OF_OPTIONS,
            LIST,
            CHAIN,
            TABLE_FAMILY,
            RULES_TABLE,
            OUTPUT_CHAIN,
        ],
        CallClass::Query,
    )?;
    let loaded = listed.status == 0;

    if wanted == text && loaded {
        return Ok(OutputChainOutcome::Current);
    }

    apply_ruleset(host, distro, path, &wanted)?;

    Ok(if wanted == text {
        OutputChainOutcome::Loaded
    } else {
        OutputChainOutcome::Rewritten
    })
}

#[cfg(test)]
#[path = "../tests/firewall/reconcile_output_chain_tests.rs"]
mod tests;
