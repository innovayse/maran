//! The startup validation of the web configuration tree.

use maran_distro::DistroAdapter;
use maran_ops::sites::{SiteMaintenanceHost, SitesOpError, reload_web_server};

use super::web_tree_reconciliation::WebTreeReconciliation;

/// Validates the web configuration tree on disk at startup and, when it
/// passes, reloads the running server onto it.
///
/// # Why this exists
///
/// `ops::safe_write` renames new content over the live target BEFORE
/// validating it, deliberately and correctly: the validating tool reads the
/// real tree by path, and a temporary file matches no include glob. The cost
/// is an on-disk midpoint. A process killed between that rename and the
/// commit leaves content no validator has accepted at the real path, with the
/// `RollbackGuard` that would have undone it gone with the process — and the
/// running server unaffected, because it has not been asked to reload. Until
/// this call existed, nothing on the host ever looked: the next reload from
/// any cause was the first thing to find out, and at a restart or a reboot
/// that means every site on the host failing to come up at once.
///
/// # Why observation rather than a record of intent
///
/// A journal written before each rename would say more — which write was
/// killed, and what to put back. It is refused here because it is a file of
/// the agent's own on every customer host, which `rules/architecture.md`
/// forbids ("the agent MUST stay stateless"), and because a record that
/// disagrees with the disk is wrong and cannot know it. Re-validating observes
/// the artefact the system actually uses, on the path it reads it from
/// (`rules/testing.md`), needs no new state, and catches every cause of a bad
/// tree rather than only this one.
///
/// # What it does NOT observe
///
/// Only the web tree — the vhosts under `AgentPaths::NGINX_INCLUDE_DIRECTORY`
/// and the TLS material `nginx -t` loads with them. The php-fpm pool trees are
/// `reconcile_php_pool_trees`'s, beside this call, and are no longer a blind
/// spot: they are version-keyed, so that pass enumerates the installed
/// versions first and answers one outcome per version.
///
/// What remains unobserved is the two systemd jail-mount trees and
/// `vsftpd.conf`. They cannot be observed this way at all: they pass a MUTATING
/// command as their validator (`systemctl daemon-reload`, and `systemctl
/// restart` for FTPS), so re-running it at a start would restart the daemon and
/// drop every live session rather than ask it a question. Closing that costs a
/// PURE validator per tree, which is what does not exist here: `vsftpd` has no
/// `-t`, and a systemd unit file has no check-without-load reachable through
/// this workspace's `DistroAdapter`. The ruling about those three is stated in
/// full on `maran_ops::safe_write::model::Validator`, which is where the
/// mutating validators are constructed and counted; what this file does about
/// them is decline to re-run an action and say so. The blind spot is
/// named in this function's own log output rather than only here, and
/// `the_only_startup_reconciliations_are_the_ones_serve_names`
/// goes red the day one of the three gains a pass.
///
/// A second blind spot sits inside the tree it does read: a site creation
/// killed between the rename of its vhost and the check of `nginx -t`'s warning
/// that one of its names is already served leaves a colliding vhost live, and
/// this pass cannot catch it, because that tree validates with exit 0 — the
/// warning is not this pass's refusal — so it reloads the colliding file.
///
/// # Why it never refuses the start
///
/// This process is the operator's only remote repair tool. Refusing to start
/// on a bad tree removes it at exactly the moment it is needed, and takes
/// accounts, databases, backups and monitoring down for a defect in one file —
/// the same trade the backup-dependency check above is decided by. There is
/// also nothing to roll back to: the previous content lived only in the dead
/// process's memory, and the only undo available here would be to delete the
/// file, which destroys a site rather than restoring one.
pub(crate) fn reconcile_web_tree(
    host: &dyn SiteMaintenanceHost,
    distro: &dyn DistroAdapter,
) -> WebTreeReconciliation {
    match reload_web_server(host, distro) {
        Ok(()) => {
            tracing::info!(
                "the web configuration tree on disk validated and the running server was \
                 reloaded onto it. UNOBSERVED HERE: the SFTP and FTPS jail mount units \
                 and vsftpd.conf — those three trees have no validator that can be run \
                 without also acting on the daemon. The php-fpm pool trees ARE observed, \
                 by reconcile_php_pool_trees, one outcome per installed version. ALSO \
                 UNOBSERVED HERE: a vhost left by a site creation killed mid-write whose \
                 host name another vhost already serves, which nginx -t reports as a \
                 warning and exit 0"
            );
            WebTreeReconciliation::Applied
        }
        Err(SitesOpError::NginxValidation { stderr }) => {
            tracing::error!(
                %stderr,
                "the web configuration tree on disk is REJECTED by its own validator at \
                 startup, so it was not reloaded. Something wrote this tree without \
                 finishing — a config write killed between its rename and its \
                 validation is the case this check exists for. The running server is \
                 unaffected and will refuse a reload, but it will FAIL TO START at the \
                 next restart or reboot, and every config write on this host will fail \
                 validation until the named file is fixed by hand. Nothing here can undo \
                 it: the previous content is gone with the process that held it"
            );
            WebTreeReconciliation::Refused
        }
        Err(error) => {
            tracing::warn!(
                %error,
                "the web configuration tree validated but the reload did not take; the \
                 running server may still be serving an older configuration than the one \
                 on disk"
            );
            WebTreeReconciliation::NotReloaded
        }
    }
}

#[cfg(test)]
#[path = "../tests/startup/reconcile_web_tree_tests.rs"]
mod tests;
