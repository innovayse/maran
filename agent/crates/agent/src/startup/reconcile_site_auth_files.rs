//! The startup removal of site credential files whose site is gone.

use maran_ops::sites::{SiteHost, reconcile_orphan_auth_files};

/// Removes the credential files whose site has no vhost, at startup, and logs
/// what it did.
///
/// # Why it exists
///
/// A site's credential file is written before the vhost that names it and
/// removed after it, so a process killed between the steps leaves a file with
/// password hashes for a site that is not there. Nothing reads such a file and
/// only root and the web server's group can list its directory, but it should
/// not outlive the site, and a site created later under the same domain would
/// find a stale file where its own belongs. This pass removes it, and needs no
/// state of its own: the credential directory and the vhost directory are both
/// read as they are now (`rules/architecture.md`: the agent is stateless).
///
/// # Why it never refuses the start
///
/// The same trade `reconcile_sftp_key_files` is decided by: this process is the
/// operator's only remote repair tool, and a stale file is a risk to be
/// reported, not a reason to take every other rpc down. A vhost directory that
/// cannot be listed removes nothing, and the failure is logged for the operator.
///
/// Answers how many files were removed, or `None` when the pass failed, so a
/// test can observe the outcome without reading the log.
pub(crate) fn reconcile_site_auth_files(host: &dyn SiteHost) -> Option<usize> {
    match reconcile_orphan_auth_files(host) {
        Ok(removed) => {
            tracing::info!(
                removed,
                "site credential files whose vhost no longer exists were removed"
            );
            Some(removed)
        }
        Err(error) => {
            tracing::warn!(
                %error,
                "the site credential directory could not be reconciled against the vhost \
                 directory, so a credential file whose site is gone may remain. Nothing was \
                 removed on the strength of an answer that could not be read"
            );
            None
        }
    }
}
