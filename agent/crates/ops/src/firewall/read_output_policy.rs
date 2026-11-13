//! Reads the host facts the ruleset's output chain is rendered from.

use std::path::Path;

use maran_agent_core::utils::login_defs_floor::{DEFAULT_ID_FLOOR, login_defs_floor};
use maran_agent_core::utils::login_defs_floor_reading::LoginDefsFloorReading;
use maran_agent_core::utils::parse_host_addresses::{
    PROC_FIB_TRIE, PROC_IF_INET6, parse_host_addresses,
};
use maran_distro::DistroAdapter;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::model::output_policy::OutputPolicy;

/// Reads the customer uid floor and this host's own addresses, through the
/// host seam.
///
/// **Reads only, no program is started.** Both address tables are procfs files
/// of the network namespace the agent runs in, read through the host seam and
/// parsed by [`parse_host_addresses`], the function `read_host_addresses` (the
/// mail stage's own-address table) is built on, so a host's addresses are known
/// without an `ip` binary and the two layers cannot disagree about them, nor
/// about what to do when they cannot be known.
///
/// A missing IPv4 table is an error, not an empty list: every host has a
/// loopback address, so it is a fault, and rendering the output chain without the
/// host's addresses would leave the second layer open while the mail layer
/// refuses. (A missing IPv6 table is IPv6 turned off.) The uid floor is
/// also exact, because a wrong one changes which uids are rejected: the first
/// file of [`FirewallFacts::login_defs_paths`](maran_distro::FirewallFacts::login_defs_paths)
/// that sets `UID_MIN` decides, no file setting it means the documented
/// default, and a value present but unusable is an error.
///
/// # Errors
///
/// - [`FirewallError::CustomerUidFloorUnusable`] when a `login.defs` sets
///   `UID_MIN` to something that is not a positive number.
/// - [`FirewallError::RulesetUnreadable`] when one of the files exists and
///   cannot be read.
/// - [`FirewallError::HostAddressesUnreadable`] when the IPv4 address table is
///   missing or either table is over its bound; the variant names which.
pub fn read_output_policy(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> Result<OutputPolicy, FirewallError> {
    let mut customer_uid_min = DEFAULT_ID_FLOOR;
    for path in distro.login_defs_paths() {
        let Some(text) = host.read_file(Path::new(path))? else {
            continue;
        };
        match login_defs_floor(&text, "UID_MIN") {
            LoginDefsFloorReading::Unset => {}
            LoginDefsFloorReading::Set(value) => {
                customer_uid_min = value;
                break;
            }
            LoginDefsFloorReading::Unusable => return Err(FirewallError::CustomerUidFloorUnusable),
        }
    }

    let fib_trie = host.read_file(Path::new(PROC_FIB_TRIE))?;
    let if_inet6 = host.read_file(Path::new(PROC_IF_INET6))?;
    let host = parse_host_addresses(fib_trie.as_deref(), if_inet6.as_deref())
        .map_err(FirewallError::HostAddressesUnreadable)?;

    Ok(OutputPolicy::for_host(customer_uid_min, &host))
}

#[cfg(test)]
#[path = "../tests/firewall/read_output_policy_tests.rs"]
mod tests;
