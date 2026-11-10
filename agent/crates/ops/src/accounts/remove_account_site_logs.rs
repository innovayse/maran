//! Removing an account's site-log directory when the account is deleted (issue #477).

use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::io;
use std::os::fd::AsRawFd;
use std::os::unix::fs::OpenOptionsExt;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::remove_directory_in_directory::remove_directory_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::system::managed_account::ManagedAccount;

use crate::accounts::AccountError;

/// Removes `<site log root>/<account>` with every log file in it.
///
/// The visitor logs of an account's sites are named by the account and the
/// domain, and the panel recycles both: an administrator can create the same
/// account name for another customer, who can create the same domain, and the
/// log tail and the traffic summary open `<domain>.access.log` in that
/// directory whatever its age. A directory that outlived its account therefore
/// handed the previous tenant's visitor addresses, request targets and
/// referrers to the next holder of the name, and kept personal data past the
/// erasure a deletion promises. The site area removes one site's files when
/// the site goes; this removes whatever is left of the account's directory.
///
/// A step of the deletion sequence like the mail spool's, run BEFORE `userdel`
/// and failing closed with the account still present, so the same deletion can
/// be retried. It must be called with the account's lock held; that lock is
/// process-local.
///
/// Idempotent: a root or an account directory that is not there is success.
///
/// # Errors
///
/// - [`AccountError::HomeInspection`] when a directory cannot be opened (other
///   than not existing) or an entry cannot be examined or unlinked.
/// - [`AccountError::SiteLogsRemain`] when the account's entry or anything in
///   it is not what the site area writes (a symlink, a directory, a special
///   file); it is never followed and nothing in it is removed after it.
pub(crate) fn remove_account_site_logs(account: &ManagedAccount) -> Result<(), AccountError> {
    remove_logs_in(AgentPaths::SITE_LOG_ROOT, account.name().as_str())
}

/// The body of [`remove_account_site_logs`] with the root injected, so a test
/// can aim it at a temporary directory instead of the host.
///
/// The root is opened `O_DIRECTORY | O_NOFOLLOW`, the account's directory is
/// opened through that descriptor the same way, every entry is decided on with
/// a no-follow `fstatat` and unlinked relative to the held directory, and only
/// then is the emptied directory itself unlinked from the root. Nothing is
/// reached through a path string a rename could redirect (rules/security.md
/// item 2).
///
/// # Errors
///
/// As [`remove_account_site_logs`].
fn remove_logs_in(root: &str, username: &str) -> Result<(), AccountError> {
    let Some(root_directory) = open_directory(root)? else {
        tracing::info!(root, "site log root absent; skipped");
        return Ok(());
    };
    let name = OsStr::new(username);

    match stat_in_directory(&root_directory, name) {
        Ok(stat) if stat.kind == EntryKind::Directory => {}
        Ok(_) => return Err(remains(username, "the entry is not a directory")),
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(inspection(&error)),
    }

    let held = open_in_directory(
        &root_directory,
        name,
        libc::O_RDONLY | libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC,
    )
    .map_err(|error| inspection(&error))?;

    // Listed through the kernel's name for the held inode, so the listing and
    // the unlinks below describe one directory even if its path is swapped.
    let held_path = format!("/proc/self/fd/{}", held.as_raw_fd());
    let mut names = Vec::new();
    for entry in std::fs::read_dir(held_path).map_err(|error| inspection(&error))? {
        names.push(entry.map_err(|error| inspection(&error))?.file_name());
    }

    // Every entry is judged BEFORE any is removed, so a refusal leaves the
    // directory exactly as it was found rather than half emptied.
    for entry in &names {
        match stat_in_directory(&held, entry) {
            Ok(stat) if stat.kind == EntryKind::File => {}
            Ok(_) => return Err(remains(username, "an entry is not a regular file")),
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(inspection(&error)),
        }
    }
    for entry in &names {
        match remove_file_in_directory(&held, entry) {
            Ok(()) => {}
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(inspection(&error)),
        }
    }

    match remove_directory_in_directory(&root_directory, name) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
        // A file created between the listing and now: the directory is not
        // empty, the account's logs remain, and a retry removes them.
        Err(_) => Err(remains(username, "the directory could not be removed")),
    }
}

/// Opens a directory without following a symlink; `None` when it is absent.
///
/// # Errors
///
/// Returns [`AccountError::HomeInspection`] for any other open failure.
fn open_directory(path: &str) -> Result<Option<File>, AccountError> {
    match OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC)
        .open(path)
    {
        Ok(file) => Ok(Some(file)),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(inspection(&error)),
    }
}

/// Logs the operator-facing reason and builds the refusal (name only).
fn remains(username: &str, reason: &str) -> AccountError {
    tracing::warn!(
        username,
        reason,
        "site logs of the account remain; the deletion was stopped before userdel"
    );
    AccountError::SiteLogsRemain {
        username: username.to_owned(),
    }
}

/// Wraps an I/O error as the area's inspection failure.
fn inspection(error: &io::Error) -> AccountError {
    AccountError::HomeInspection {
        reason: format!("site log removal: {error}"),
    }
}

#[cfg(test)]
#[path = "../tests/accounts/remove_account_site_logs_tests.rs"]
mod tests;
