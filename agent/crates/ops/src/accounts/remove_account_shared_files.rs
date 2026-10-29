//! Removing the files an account owns in shared temporary directories before its uid is freed.

use std::fs::{File, OpenOptions};
use std::io;
use std::os::unix::fs::{MetadataExt, OpenOptionsExt};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::managed_account::ManagedAccount;

use crate::accounts::AccountError;
use crate::accounts::sweep_directory_by_owner::sweep_directory_by_owner;

/// Removes every file `account`'s uid owns in the shared temp directories and
/// verifies none is left.
///
/// Such a file outlives the account and is keyed by a bare uid, so the next
/// account given that uid would inherit it. The roots are
/// [`AgentPaths::SHARED_TEMP_ROOTS`], the agent's bind-mounted view of the
/// host's `/tmp`, `/var/tmp` and `/dev/shm`. A root that does not exist is
/// skipped with a log line. Each root is swept, then swept again; the second
/// pass is the verification and must find nothing.
///
/// Must be called with the account's lock held. That lock is process-local.
///
/// # Errors
///
/// - [`AccountError::HomeInspection`] when a root cannot be opened (other than
///   not existing) or a walk hits an I/O error.
/// - [`AccountError::SharedFilesRemain`] when the verification pass still
///   finds a uid-owned entry, a uid-owned directory holding another owner's
///   entry, or a bound was exceeded. Nothing foreign has been touched.
pub(crate) fn remove_account_shared_files(account: &ManagedAccount) -> Result<(), AccountError> {
    sweep_roots(
        AgentPaths::SHARED_TEMP_ROOTS,
        account.name().as_str(),
        account.uid(),
    )
}

/// The body of [`remove_account_shared_files`] with the roots and uid injected,
/// so a test can aim it at a temporary directory instead of the host.
///
/// # Errors
///
/// As [`remove_account_shared_files`].
fn sweep_roots(roots: &[&str], username: &str, uid: u32) -> Result<(), AccountError> {
    for path in roots {
        let Some(root) = open_root(path)? else {
            tracing::info!(root = *path, "shared temp root absent; skipped");
            continue;
        };
        let device = root.metadata().map_err(inspection)?.dev();

        sweep_directory_by_owner(&root, uid, device).map_err(inspection)?;
        if !sweep_directory_by_owner(&root, uid, device).map_err(inspection)? {
            tracing::warn!(
                username = username,
                uid = uid,
                root = *path,
                "files of the account survived the sweep; the deletion was stopped before userdel"
            );
            return Err(AccountError::SharedFilesRemain {
                username: username.to_owned(),
            });
        }
    }
    Ok(())
}

/// Opens a sweep root as a directory, never through a symlink; `None` when it
/// does not exist.
///
/// # Errors
///
/// Returns [`AccountError::HomeInspection`] for any other open failure.
fn open_root(path: &str) -> Result<Option<File>, AccountError> {
    match OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC)
        .open(path)
    {
        Ok(file) => Ok(Some(file)),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(inspection(error)),
    }
}

/// Wraps an I/O error as the area's inspection failure.
fn inspection(error: io::Error) -> AccountError {
    AccountError::HomeInspection {
        reason: format!("shared temp sweep: {error}"),
    }
}

#[cfg(test)]
#[path = "../tests/accounts/remove_account_shared_files_tests.rs"]
mod tests;
