//! The startup validation of every installed PHP version's php-fpm pool tree.

use maran_distro::DistroAdapter;
use maran_ops::php::{PhpHost, PoolTreeDecision, PoolTreeOutcome, reload_pool_trees};

/// Validates every installed PHP version's php-fpm pool tree on disk at
/// startup and, for each that passes, reloads that version's service onto it.
///
/// # Why this exists, and why it is separate from the web pass
///
/// The same on-disk midpoint, in the other tree `ops::safe_write` renames
/// into. `php-fpm -t` reads the pool directory by path, so the rename precedes
/// the validation there for the same reason it does under nginx, and a process
/// killed in that window leaves a pool file no validator accepted at the real
/// path with the `RollbackGuard` gone. The consequence is sharper than nginx's
/// in one respect: php-fpm refuses to START on a pool file it cannot parse, so
/// at the next restart every site on the host using that version stops serving
/// at once, and the panel's own screens say the pool was written.
///
/// It is a second function rather than an arm of `reconcile_web_tree` because
/// the answer is per VERSION, not per host: `php-fpm8.3 -t` reads only `8.3`'s
/// pool directory, so one broken file says nothing about the other versions and
/// must not be reported as though it did. Folding them would also lose the
/// version an operator has to go and fix, which is the only actionable part of
/// the message.
///
/// # What it observes, and what it still cannot
///
/// It observes the artefact the daemon reads, on the path it reads it from,
/// through the same validator and the same service name every individual pool
/// write uses — so it needs no state of its own, which is what
/// `rules/architecture.md` requires of this process ("the agent MUST stay
/// stateless"). What it cannot see is WHICH write was killed, or anything that
/// happened while this process was not running: it reports on the tree as it is
/// now, never on the history that produced it. A version with no pool directory
/// is not installed and yields no outcome, which is the honest answer and not a
/// silence — there is no tree there to observe.
///
/// # Why it never refuses the start
///
/// The same trade `reconcile_web_tree` is decided by, and it is not re-argued
/// here: this process is the operator's only remote repair tool, and there is
/// nothing to roll back to, because the previous pool file died with the
/// process that held it in memory.
pub(crate) fn reconcile_php_pool_trees(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
) -> Vec<PoolTreeOutcome> {
    let outcomes = reload_pool_trees(host, distro);

    if outcomes.is_empty() {
        tracing::info!(
            "no PHP version is installed, so there is no php-fpm pool tree to validate at \
             this start"
        );
        return outcomes;
    }

    for outcome in &outcomes {
        match outcome.decision {
            PoolTreeDecision::Applied => tracing::info!(
                version = %outcome.version,
                "this version's php-fpm pool tree on disk validated and its service was \
                 reloaded onto it"
            ),
            PoolTreeDecision::Refused => tracing::error!(
                version = %outcome.version,
                stderr = %outcome.stderr,
                "this version's php-fpm pool tree on disk is REJECTED by its own \
                 validator at startup, so it was not reloaded. Something wrote this tree \
                 without finishing — a pool write killed between its rename and its \
                 validation is the case this check exists for. The running php-fpm is \
                 unaffected and will refuse a reload, but it will FAIL TO START at the \
                 next restart or reboot, and every site on this host using this version \
                 stops serving when it does. Nothing here can undo it: the previous pool \
                 file is gone with the process that held it"
            ),
            PoolTreeDecision::NotReloaded => tracing::warn!(
                version = %outcome.version,
                stderr = %outcome.stderr,
                "this version's php-fpm pool tree validated but the reload did not take; \
                 its workers may still be serving an older pool than the one on disk"
            ),
        }
    }

    outcomes
}

#[cfg(test)]
#[path = "../tests/startup/reconcile_php_pool_trees_tests.rs"]
mod tests;
