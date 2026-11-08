//! EmptyTrash: everything in the account's trash, gone for good.

use maran_agent_core::validation::system::name::AccountName;

use crate::files::{FilesHost, FilesOpError};

/// Permanently removes everything in the account's trash and answers how many
/// entries went.
///
/// An empty or never-used trash is success with zero. Each entry is removed as a
/// tree (no link followed, no other filesystem entered). Idempotent: a run that
/// was stopped is finished by running it again.
///
/// # Errors
///
/// Returns [`FilesOpError::Saturated`] at the bulk bound, and
/// [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`], [`FilesOpError::TooDeep`],
/// [`FilesOpError::LimitReached`] and [`FilesOpError::RemoveFailed`].
pub fn empty_trash(host: &dyn FilesHost, account: &AccountName) -> Result<u64, FilesOpError> {
    host.empty_trash_as_account(account)
}

#[cfg(test)]
#[path = "../tests/files/empty_trash_tests.rs"]
mod tests;
