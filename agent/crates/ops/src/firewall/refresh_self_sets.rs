//! RefreshSelfSets: make the kernel's two address sets follow the host's addresses.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_distro::DistroAdapter;
use maran_templates::nftables::nftables_self_sets::NftablesSelfSets;

use crate::firewall::apply_ruleset::{apply_ruleset, write_ruleset_file};
use crate::firewall::ensure_bans_table::TABLE_FAMILY;
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;
use crate::firewall::model::mask_host_facts::ELEMENTS_PREFIX;
use crate::firewall::model::output_policy::OutputPolicy;
use crate::firewall::model::ruleset_state::RulesetState;
use crate::firewall::model::self_sets_outcome::SelfSetsOutcome;
use crate::firewall::read_output_policy::read_output_policy;

/// `nft`'s verb for showing an object.
const LIST: &str = "list";

/// `nft`'s noun for a set.
const SET: &str = "set";

/// The table the sets live in. Must match `templates/nftables/ruleset.nft.j2`.
const RULES_TABLE: &str = "maran";

/// The set whose presence says the output chain is loaded. Must match the template.
const PROBE_SET: &str = "self_v4";

/// Asks `nft` to parse and type-check a file without loading any of it.
const CHECK: &str = "--check";

/// Tells `nft` to read its input from the file named next.
const FILE: &str = "-f";

/// What `nft` prints on standard error (the locale is pinned to `C` for every spawn) when
/// the object asked about is not in the kernel.
const NO_SUCH_OBJECT: &str = "No such file or directory";

/// Brings the output chain of the port-25 lockdown to the host's present addresses, in the
/// ruleset FILE and in the KERNEL, and answers what it had to do.
///
/// The output chain judges a destination against two address sets. An address the host gains
/// later (a lease, a new interface, a privacy address) is outside them until this runs, which
/// is the gap the agent's periodic check of the host's addresses closes.
///
/// **The facts are read here, inside the firewall lock.** The uid floor and the addresses are
/// read from the host after the lock is taken, the way `allow_port`, `deny_port` and the
/// startup reconcile read them, so an older reading cannot overwrite a newer one that another
/// operation just applied. `addresses` is the reading the periodic check acted on; when the
/// host moved since, the fresh reading is what is applied and the answer is
/// [`SelfSetsOutcome::HostMoved`], so the caller does not record the older reading as followed.
///
/// **File and kernel cannot drift.** The file is rewritten through the same atomic writer a
/// rule change uses (`write_ruleset_file`), and then the kernel gets the sets, so a later
/// `systemctl reload nftables` or any other re-load of the file re-applies the CURRENT
/// addresses and not the ones the file was rendered with. The file is written first: a
/// failure after it leaves a file that is ahead of the kernel, which the next attempt
/// finishes, and never a kernel that is ahead of a file a reload would revert.
///
/// **Two ways into the kernel.** When only the elements of the two sets differ from the file
/// (the ordinary case), the kernel gets a flush and a refill of each set in one `nft -f`
/// transaction: no rule is reloaded, no ban is touched, and a connection is never judged
/// against an empty set. When anything else of the chain differs (the uid floor of the rule,
/// or a chain that was seeded inert because the host's facts were unreadable then), the whole
/// ruleset is applied as a rule change applies it ([`SelfSetsOutcome::Repaired`]), since the
/// rule itself has to change and not only the sets.
///
/// **Only a real refresh is a refresh.** A kernel without the sets ([`SelfSetsOutcome::NotLoaded`],
/// told from a transient `nft` failure by its message) and a host with no ruleset file
/// ([`SelfSetsOutcome::NotSeeded`]) are left to the startup reconcile and written nothing;
/// the caller must not record either as followed. Any other failure of `nft` is an error.
///
/// # Calling this
///
/// Synchronous, and it MUST be invoked from `tokio::task::spawn_blocking`: it
/// spawns `nft`, writes files and takes the module lock.
///
/// # Errors
///
/// - [`FirewallError::HostAddressesUnreadable`] and
///   [`FirewallError::CustomerUidFloorUnusable`] when the host's facts cannot be read; nothing
///   is written, because a chain filled from a guess would leave an address open.
/// - [`FirewallError::RulesetUnreadable`] and [`FirewallError::ForeignRuleset`] as `list_rules`
///   documents them; a file this agent did not write is never touched.
/// - [`FirewallError::NftFailed`] when `nft` cannot be started, fails to list the set for a
///   reason other than its absence, or refuses the load; a transaction that fails changes
///   nothing, so the old sets are intact.
/// - [`FirewallError::StagingFailed`] when a file cannot be written; the kernel was not loaded
///   from it.
/// - [`FirewallError::RuleRefusedByNft`] when `nft --check` refuses what was rendered; the
///   kernel and the file are untouched.
/// - [`FirewallError::Bound`] when `nft` outran its deadline.
/// - [`FirewallError::RenderFailed`] when the template fails.
pub fn refresh_self_sets(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    addresses: &HostAddresses,
) -> Result<SelfSetsOutcome, FirewallError> {
    let _guard = firewall_lock();

    let path = AgentPaths::nftables_ruleset_path();
    let Some(text) = host.read_file(path)? else {
        return Ok(SelfSetsOutcome::NotSeeded);
    };

    let policy = read_output_policy(host, distro)?;
    let wanted = RulesetState::with_current_host_facts(&text, &policy)?;

    if !sets_are_loaded(host, distro)? {
        return Ok(SelfSetsOutcome::NotLoaded);
    }

    let repaired = !only_address_sets_differ(&text, &wanted);
    if repaired {
        apply_ruleset(host, distro, path, &wanted)?;
    } else {
        if wanted != text {
            write_ruleset_file(host, distro, path, &wanted)?;
        }
        load_sets(host, distro, &policy)?;
    }

    if policy != OutputPolicy::for_host(policy.customer_uid_min(), addresses) {
        return Ok(SelfSetsOutcome::HostMoved);
    }

    Ok(if repaired {
        SelfSetsOutcome::Repaired
    } else {
        SelfSetsOutcome::Refreshed
    })
}

/// Whether the kernel holds the output chain's sets.
///
/// # Errors
///
/// [`FirewallError::NftFailed`] when `nft` fails for a reason other than the set being absent:
/// "I could not look" is not "there is nothing to refresh".
fn sets_are_loaded(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> Result<bool, FirewallError> {
    let listed = host.run(
        &distro.nft_binary(),
        &[
            END_OF_OPTIONS,
            LIST,
            SET,
            TABLE_FAMILY,
            RULES_TABLE,
            PROBE_SET,
        ],
        CallClass::Query,
    )?;
    if listed.status == 0 {
        return Ok(true);
    }
    if listed.stderr.contains(NO_SUCH_OBJECT) {
        return Ok(false);
    }

    Err(FirewallError::NftFailed {
        stderr: listed.stderr,
    })
}

/// Whether `wanted` differs from `text` in the element lists of the address sets and nowhere
/// else: the same number of lines, and every line that differs is an `elements =` line on
/// both sides.
fn only_address_sets_differ(text: &str, wanted: &str) -> bool {
    let held: Vec<&str> = text.lines().collect();
    let fresh: Vec<&str> = wanted.lines().collect();

    held.len() == fresh.len()
        && held.iter().zip(&fresh).all(|(was, now)| {
            was == now
                || (was.trim().starts_with(ELEMENTS_PREFIX)
                    && now.trim().starts_with(ELEMENTS_PREFIX))
        })
}

/// Replaces the contents of the two sets in the kernel with the policy's, in one `nft -f`
/// transaction, and changes nothing else.
///
/// # Errors
///
/// As [`refresh_self_sets`] for the staging, the check and the load.
fn load_sets(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    policy: &OutputPolicy,
) -> Result<(), FirewallError> {
    let script = NftablesSelfSets {
        self_v4_prefixes: policy
            .self_v4_prefixes()
            .iter()
            .map(|prefix| (prefix.network(), prefix.length()))
            .collect(),
        self_v4: policy.self_v4().to_vec(),
        self_v6: policy.self_v6().to_vec(),
    }
    .render_config()
    .map_err(FirewallError::from)?;

    // Staged beside the ruleset file (the agent's own directory) and never
    // renamed over it: the script is a one-shot, not the rule store.
    let staged = host.stage_file(AgentPaths::nftables_ruleset_path(), &script)?;
    let result = load_script(host, distro, &staged);
    host.discard_file(&staged);

    result
}

/// Checks the staged script and loads it.
///
/// # Errors
///
/// As [`refresh_self_sets`] for the check and the load.
fn load_script(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    staged: &Path,
) -> Result<(), FirewallError> {
    let Some(staged_path) = staged.to_str() else {
        return Err(FirewallError::StagingFailed);
    };

    let checked = host.run(
        &distro.nft_binary(),
        &[CHECK, FILE, staged_path],
        CallClass::Validate,
    )?;
    if checked.status != 0 {
        return Err(FirewallError::RuleRefusedByNft {
            stderr: checked.stderr,
        });
    }

    let loaded = host.run(
        &distro.nft_binary(),
        &[FILE, staged_path],
        CallClass::Mutation,
    )?;
    if loaded.status != 0 {
        return Err(FirewallError::NftFailed {
            stderr: loaded.stderr,
        });
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/firewall/refresh_self_sets_tests.rs"]
mod tests;
