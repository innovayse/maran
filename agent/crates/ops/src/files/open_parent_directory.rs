//! Walking into a customer's home one descriptor at a time.

use std::ffi::OsStr;
use std::fs::File;
use std::path::Path;

use maran_agent_core::validation::fs::relative_path::RelativePath;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::open_home;
use crate::files::open_level::open_level;

/// Opens the directory that will hold `relative`'s file, descending from `home`
/// one component at a time.
///
/// **This is where the containment of a customer file operation actually
/// happens**, and it happens without resolving a path at all. `resolve_in_home`
/// answers "where does this path really lead?" and its own documentation warns
/// that reopening by the original name afterwards reintroduces the race it
/// closes — which for a write is not a hypothetical, because the account owns
/// every directory being walked and can rename one between two syscalls. So
/// nothing here is resolved and reopened: the home is opened once, and each
/// component is reached from the descriptor above it with `openat` and
/// `O_NOFOLLOW`. A descriptor names an inode, so a level renamed, deleted or
/// replaced after it was opened does not redirect anything; and a level that is
/// a symlink is refused rather than followed.
///
/// The caller still runs this inside `fork_as_account`, as the account. The two
/// protections answer different attacks and neither replaces the other: the
/// descriptor walk stops the agent being steered to the wrong inode, and the
/// dropped uid stops it from having any interesting permission when it gets
/// there.
///
/// Every level, the home included, must be a directory the account owns.
/// Ownership and not merely permission, because an account can hand write
/// access to a directory around inside its own home; owning it is the claim
/// that survives.
///
/// `missing` decides what happens to a level that is not there — see
/// [`MissingParents`]. Creation is `mkdirat` followed by an `openat` of the same
/// name, never a create-and-assume: `mkdirat` reports `EEXIST` for a name a
/// customer occupied first, including one they occupied with a symlink, and the
/// `openat` that follows is what refuses that symlink.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] when the account's home is missing,
/// is not a directory, or is not owned by the account;
/// [`FilesOpError::DirectoryUnusable`] when a level below it cannot be created
/// or opened, is not a directory, is a symlink, or is not owned by the account.
pub(crate) fn open_parent_directory(
    home: &Path,
    relative: &RelativePath,
    uid: u32,
    missing: MissingParents,
) -> Result<File, FilesOpError> {
    let mut directory = open_home(home, uid)?;

    for component in relative.parent_components() {
        directory = open_level(&directory, OsStr::new(component.as_str()), uid, missing)?;
    }

    Ok(directory)
}

#[cfg(test)]
#[path = "../tests/files/open_parent_directory_tests.rs"]
mod tests;
