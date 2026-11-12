//! The startup pass that brings the firewall's output chain up on a host that
//! does not have it.

use maran_distro::DistroAdapter;
use maran_ops::firewall::{
    FirewallError, FirewallHost, OutputChainOutcome, reconcile_output_chain,
};

/// Makes sure the output chain of the port-25 lockdown exists in the ruleset file
/// and in the kernel, and is current for the host's addresses, at startup, and
/// logs what it found.
///
/// # Why it exists
///
/// The chain is the second layer of the lockdown (the first is the mail server's
/// own refusal of the host's addresses). A host upgraded from a release that
/// rendered no chain kept the old file until somebody allowed or denied a port, and
/// looked healthy meanwhile. Here the host is brought up at the start, and a host
/// that cannot be (an unreadable address table, a ruleset this agent did not write)
/// is reported at `error`, not `debug`, because it is on one layer of two.
///
/// # Why it never refuses the start
///
/// The trade `reconcile_web_tree` is decided by: this process is the operator's
/// only remote repair tool, and a chain that is not applied is a fault to report,
/// not a reason to take every other rpc down.
///
/// Answers the outcome, or `None` when the pass failed, so a test can observe the
/// result without reading the log.
pub(crate) fn reconcile_firewall_output_chain(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> Option<OutputChainOutcome> {
    match reconcile_output_chain(host, distro) {
        Ok(OutputChainOutcome::NotSeeded) => {
            tracing::debug!(
                "no firewall ruleset is seeded yet, so there is no output chain to check"
            );
            Some(OutputChainOutcome::NotSeeded)
        }
        Ok(OutputChainOutcome::Current) => {
            tracing::debug!("the firewall's output chain is current and loaded");
            Some(OutputChainOutcome::Current)
        }
        Ok(outcome) => {
            tracing::warn!(
                ?outcome,
                "the firewall's output chain was missing or out of date and has been applied"
            );
            Some(outcome)
        }
        Err(error) => {
            report_unhealthy(&error);
            None
        }
    }
}

/// Logs that the host is on one layer of the port-25 lockdown.
fn report_unhealthy(error: &FirewallError) {
    tracing::error!(
        %error,
        "the firewall's output chain could not be applied; local customers are refused on port 25 by \
         the mail server only"
    );
}

#[cfg(test)]
#[path = "../tests/startup/reconcile_output_chain_tests.rs"]
mod tests;
