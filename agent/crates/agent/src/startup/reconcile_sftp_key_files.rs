//! The startup removal of SFTP key files whose login is gone.

use maran_distro::DistroAdapter;
use maran_ops::sftp::{SftpHost, reconcile_orphan_key_files};

/// Removes the SFTP key files whose login is gone, at startup, and logs what it
/// did.
///
/// # Why it exists
///
/// A login's key file is addressed by the login's name, and a login created
/// later under that name would authenticate with the keys of the one that
/// came before. Deletion removes the file first and creation purges a new
/// name's file, but a process killed between the steps of either, or a file put
/// there by hand, leaves one behind; this pass is what removes it. It needs no
/// state of its own — the directory and the password database are both read as
/// they are now (`rules/architecture.md`: the agent is stateless).
///
/// # Why it never refuses the start
///
/// The same trade `reconcile_web_tree` is decided by: this process is the
/// operator's only remote repair tool, and a stale key file is a risk to be
/// reported, not a reason to take every other rpc down. An unreadable password
/// database removes nothing (`reconcile_orphan_key_files`), and the failure is
/// logged for the operator.
///
/// Answers how many files were removed, or `None` when the pass failed, so a
/// test can observe the outcome without reading the log.
pub(crate) fn reconcile_sftp_key_files(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
) -> Option<usize> {
    match reconcile_orphan_key_files(host, distro) {
        Ok(removed) => {
            tracing::info!(
                removed,
                "SFTP key files whose login no longer exists were removed"
            );
            Some(removed)
        }
        Err(error) => {
            tracing::warn!(
                %error,
                "the SFTP key directory could not be reconciled against the password \
                 database, so a key file whose login is gone may remain. Nothing was \
                 removed on the strength of an answer that could not be read"
            );
            None
        }
    }
}
