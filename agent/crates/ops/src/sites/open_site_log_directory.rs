//! Opening a site-log directory by path, once, and proving who owns it.

use std::fs::{File, OpenOptions};
use std::os::unix::fs::{MetadataExt, OpenOptionsExt};
use std::path::Path;

use crate::sites::SitesOpError;

/// The uid that must own the log directory and every log file in it.
///
/// Root, and **not the account's** any more. The uid the directory and the
/// files are checked against used to be resolved from the password database,
/// because the account owned them; the whole point of moving the logs to
/// `/var/log/maran/sites/<account>` is that root owns them now, so root is what
/// the inode must say. A check left pointing at the account would not merely be
/// stale — it would refuse every log on a correctly fixed host, which is the
/// direction that gets noticed, but it would also have accepted a file the
/// account had somehow come to own, which is the direction that does not.
///
/// Named rather than written as a bare `0` at the call sites, so that "which
/// uid do the tail and the traffic summary trust?" has one answer to change and
/// one answer to grep for.
pub(crate) const LOG_OWNER_UID: u32 = 0;

/// Opens the log directory, once, and proves `uid` owns it.
///
/// `uid` is [`LOG_OWNER_UID`] — ROOT, not the account's — at every production
/// call site; it is a parameter only so the tests can name their own uid. An
/// earlier version of this line said the check proved the ACCOUNT owned the
/// directory, which stopped being true when the logs moved to
/// `/var/log/maran/sites/<account>`: the sentence outlived the ownership it
/// described, and a doc that says the weaker claim is the one being made is
/// what stops the next reader checking which uid is actually trusted.
///
/// The descriptor returned is what every later open goes through, so this is
/// the only moment the directory is named by a path — and therefore the only
/// moment the path could be swapped.
///
/// Shared by the log tail and the traffic summary and prune, so a refusal added
/// here is a refusal of all three.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when the path cannot be opened as a
/// directory without following a symlink, or is not a directory owned by `uid`.
pub(crate) fn open_site_log_directory(directory: &Path, uid: u32) -> Result<File, SitesOpError> {
    let opened = OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC)
        .open(directory)
        .map_err(|_| SitesOpError::LogUnreadable {
            path: directory.display().to_string(),
        })?;

    let metadata = opened.metadata().map_err(|_| SitesOpError::LogUnreadable {
        path: directory.display().to_string(),
    })?;

    // Root's own directory. Ownership is checked on the DESCRIPTOR that every
    // later open goes through, so this is a claim about the inode the reader
    // will actually read from and not about a name that could be re-pointed
    // afterwards. It used to assert the ACCOUNT owned it, which was the best
    // available claim while the directory sat inside a home the account could
    // rename things in; root owning it is a stronger one, and the tree above it
    // is unreachable to every uid but root.
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(SitesOpError::LogUnreadable {
            path: directory.display().to_string(),
        });
    }

    Ok(opened)
}
