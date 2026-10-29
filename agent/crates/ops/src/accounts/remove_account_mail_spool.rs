//! Removing the mail spool file an account leaves behind before its uid is freed.

use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::io;
use std::os::unix::fs::OpenOptionsExt;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_distro::DistroAdapter;

use crate::accounts::AccountError;

/// Removes `<mail directory>/<account name>` when it is a regular file owned by
/// the account's uid, and verifies it is gone.
///
/// `userdel --remove` is not relied on: measured on AlmaLinux 9, the spool of a
/// deleted account survived it, and the next account given the freed uid owned
/// the file. The mail directory is a distro fact
/// ([`DistroAdapter::mail_spool_directory`]).
///
/// Must be called with the account's lock held. That lock is process-local.
///
/// # Errors
///
/// - [`AccountError::HomeInspection`] when the directory cannot be opened (other
///   than not existing) or an entry cannot be examined or unlinked.
/// - [`AccountError::MailSpoolRemains`] when the entry at the account's name is
///   owned by its uid but is not a regular file, or survived the unlink.
pub(crate) fn remove_account_mail_spool(
    distro: &dyn DistroAdapter,
    account: &ManagedAccount,
) -> Result<(), AccountError> {
    remove_spool_in(
        distro.mail_spool_directory(),
        account.name().as_str(),
        account.uid(),
    )
}

/// The body of [`remove_account_mail_spool`] with the directory injected, so a
/// test can aim it at a temporary directory instead of the host.
///
/// The directory is opened `O_DIRECTORY | O_NOFOLLOW` and the entry is decided
/// on and unlinked relative to that descriptor, never through a path string
/// (rules/security.md §2). Only an entry that is a regular file owned by `uid`
/// is removed; a link, a directory or any other uid-owned object is refused and
/// untouched; an entry of another uid is none of this account's business.
///
/// # Errors
///
/// As [`remove_account_mail_spool`].
fn remove_spool_in(directory: &str, username: &str, uid: u32) -> Result<(), AccountError> {
    let Some(held) = open_directory(directory)? else {
        tracing::info!(directory, "mail spool directory absent; skipped");
        return Ok(());
    };
    let name = OsStr::new(username);

    let Some(stat) = stat_entry(&held, name)? else {
        return Ok(());
    };
    if stat.uid != uid {
        return Ok(());
    }
    if stat.kind != EntryKind::File {
        return Err(remains(
            username,
            uid,
            "a uid-owned entry that is not a regular file",
        ));
    }

    match remove_file_in_directory(&held, name) {
        Ok(()) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => return Err(inspection(error)),
    }
    match stat_entry(&held, name)? {
        Some(left) if left.uid == uid => {
            Err(remains(username, uid, "the spool survived the unlink"))
        }
        _ => Ok(()),
    }
}

/// Reads one entry's facts without following a link; `None` when it is absent.
///
/// # Errors
///
/// Returns [`AccountError::HomeInspection`] for any other failure.
fn stat_entry(
    directory: &File,
    name: &OsStr,
) -> Result<Option<maran_agent_core::privs::entry_stat::EntryStat>, AccountError> {
    match stat_in_directory(directory, name) {
        Ok(stat) => Ok(Some(stat)),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(inspection(error)),
    }
}

/// Opens the mail directory as a directory, never through a symlink; `None`
/// when it does not exist.
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
        Err(error) => Err(inspection(error)),
    }
}

/// Logs the operator-facing reason and builds the refusal (name only).
fn remains(username: &str, uid: u32, reason: &str) -> AccountError {
    tracing::warn!(
        username,
        uid,
        reason,
        "mail spool of the account remains; the deletion was stopped before userdel"
    );
    AccountError::MailSpoolRemains {
        username: username.to_owned(),
    }
}

/// Wraps an I/O error as the area's inspection failure.
fn inspection(error: io::Error) -> AccountError {
    AccountError::HomeInspection {
        reason: format!("mail spool removal: {error}"),
    }
}

#[cfg(test)]
#[path = "../tests/accounts/remove_account_mail_spool_tests.rs"]
mod tests;
