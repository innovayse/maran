//! The startup pass that gives an already mail-enabled host the local-submission
//! maps a newer agent added.

use maran_distro::DistroAdapter;
use maran_ops::mail::{MailHost, seed_missing_local_submission_maps};

/// Creates, empty, the local-submission maps that an upgrade added to a host that
/// enabled mail under an earlier release (today: the alias fan-out map), and logs
/// what it found.
///
/// # Why it exists
///
/// The rule defers every message while a map it reads is missing. A host upgraded
/// to this agent has the rendered setting that names the fan-out map but no file
/// until the panel next publishes (minutes), so without this pass every submission
/// would be deferred in between. An empty map only means no alias fans out yet; the
/// panel's push replaces it whole.
///
/// # Why it never refuses the start
///
/// For the reason `reconcile_firewall_output_chain` gives: this process is the
/// operator's remote repair tool, and a map that could not be seeded is a fault to
/// report, not a reason to take every other rpc down.
///
/// Answers `Some(true)` when mail was found enabled and the maps are in place,
/// `Some(false)` when mail was never enabled, and `None` when the pass failed.
pub(crate) fn reconcile_mail_maps(host: &dyn MailHost, distro: &dyn DistroAdapter) -> Option<bool> {
    match seed_missing_local_submission_maps(host, distro) {
        Ok(true) => {
            tracing::debug!("the local-submission maps are all in place");
            Some(true)
        }
        Ok(false) => {
            tracing::debug!("mail is not enabled, so there are no local-submission maps to check");
            Some(false)
        }
        Err(error) => {
            tracing::error!(
                %error,
                "the local-submission maps could not be completed; locally submitted mail is deferred \
                 until the panel publishes them"
            );
            None
        }
    }
}

#[cfg(test)]
#[path = "../tests/startup/reconcile_mail_maps_tests.rs"]
mod tests;
