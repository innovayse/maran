//! The output chain's host facts for a caller that must not fail on them.

use maran_distro::DistroAdapter;

use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::model::output_policy::OutputPolicy;
use crate::firewall::read_output_policy::read_output_policy;

/// Reads the output chain's host facts, and when they cannot be read answers
/// [`OutputPolicy::inert`] after logging the reason at `error`.
///
/// For the installer's seed only. The seed prints the first ruleset of the host; if
/// it failed on an unreadable address table or an unusable `login.defs`, the
/// installer would stop (or print nothing) and the host would have NO firewall,
/// which is worse than a firewall whose second port-25 layer is not filled in yet.
/// The inert policy renders the chain with empty address sets, which matches
/// nothing; the agent's start-up pass (`reconcile_output_chain`) reads the same
/// facts again and fills the chain in as soon as they can be read (and the periodic check of the
/// host's addresses, `refresh_self_sets`, re-renders the whole chain if the uid floor was the
/// part that could not be read then), and the mail
/// server's own refusal of the host's addresses is the layer that stands meanwhile.
/// Every other caller keeps [`read_output_policy`] and its errors.
#[must_use]
pub fn read_output_policy_or_inert(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> OutputPolicy {
    match read_output_policy(host, distro) {
        Ok(policy) => policy,
        Err(error) => {
            tracing::error!(
                %error,
                "the host's uid floor and addresses could not be read; the firewall is seeded with an inert \
                 output chain that the agent fills in at its start"
            );
            OutputPolicy::inert()
        }
    }
}

#[cfg(test)]
#[path = "../tests/firewall/read_output_policy_or_inert_tests.rs"]
mod tests;
