//! The startup convergence of every installed PHP version's OPcache drop-in.

use maran_distro::DistroAdapter;
use maran_ops::php::{OpcacheDropInOutcome, PhpHost, PhpOpError};

/// Brings the OPcache drop-in of every installed PHP version to the current
/// render at startup, and logs what happened per version.
///
/// # Why this exists
///
/// One php-fpm master serves every account's pool of a version, so its OPcache is
/// shared by every tenant. The drop-in carries the isolation directives that keep
/// one tenant from reading another's cached scripts or steering the cache. A
/// version installed before the drop-in existed has none, and one whose file was
/// edited by hand has the wrong one; this pass restores both. A hand edit is
/// overwritten at the next start, which is intended.
///
/// # Why before the socket
///
/// For the reason the other passes here run before it: the write ends in a
/// reload of the version's php-fpm, and no connection has been accepted yet, so
/// no `WritePool` can be validating or reloading the same master at the same
/// time.
///
/// # Why it never refuses the start
///
/// The same trade `reconcile_web_tree` is decided by: this process is the
/// operator's only remote repair tool. A version whose drop-in cannot be written
/// keeps serving with whatever OPcache settings its distribution ships; the log
/// names the version and the reason, and the next start tries again.
///
/// # What it cannot see
///
/// Whether the running php-fpm masters have the settings loaded: it reports what
/// the write protocol answered (validated, reloaded), not a reading of the
/// running configuration.
pub(crate) fn reconcile_opcache_drop_ins(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
) -> Vec<OpcacheDropInOutcome> {
    let outcomes = maran_ops::php::reconcile_opcache_drop_ins(host, distro);

    if outcomes.is_empty() {
        tracing::info!(
            "no PHP version is installed, so there is no OPcache drop-in to converge at this start"
        );
        return outcomes;
    }

    for outcome in &outcomes {
        match &outcome.result {
            Ok(()) => tracing::info!(
                version = %outcome.version,
                "this version's OPcache drop-in is current"
            ),
            // A deadline is not a verdict on the file: the program was stopped
            // before it could judge, so the next start tries again.
            Err(PhpOpError::Bound(bound)) => tracing::warn!(
                version = %outcome.version,
                error = %bound,
                "this version's OPcache drop-in could not be confirmed: a program outlived \
                 its deadline and was stopped"
            ),
            Err(error) => tracing::error!(
                version = %outcome.version,
                error = %error,
                "this version's OPcache drop-in was NOT written, so its shared OPcache runs \
                 without Maran's isolation settings. Fix the reason above and restart the \
                 agent"
            ),
        }
    }

    outcomes
}

#[cfg(test)]
#[path = "../tests/startup/reconcile_opcache_drop_ins_tests.rs"]
mod tests;
