//! DeleteEntry: taking one file back out of a customer's home.

use crate::files::model::delete_entry_input::DeleteEntryInput;
use crate::files::{FilesHost, FilesOpError};

/// Removes one file inside an account's home.
///
/// The other half of [`super::write_file`]: an ACME challenge is a single-use
/// proof, and leaving it in the document root after the authority has read it
/// leaves a file the customer did not create in a directory they serve to the
/// internet. Its removal is as privileged as its creation and is protected the
/// same way — a process dropped to the account, reaching the file through a
/// descriptor walk that follows no symlink at any level.
///
/// Two steps, in this order and not the other:
///
/// 1. **Locate the entry, as root**, with `resolve_in_home`.
/// 2. **Unlink it, as the account.** Through the pinned directory, after the
///    entry has been proved to be a regular file the account owns with a single
///    link.
///
/// **Why this operation calls `resolve_in_home` when [`super::write_file`] does
/// not**, since a reader comparing the two will notice and should not have to
/// guess. It is a difference between the operations, not an inconsistency in the
/// code:
///
/// - A write does not have to LOCATE anything. Its descriptor walk constructs
///   the path level by level, creating what is missing, so the walk alone is
///   complete containment and a resolution there could not fail. It was deleted
///   for exactly that reason.
/// - A removal has to locate an entry that already exists, and it needs an
///   answer the forked child structurally cannot give. The child's outcome
///   crosses back as an exit status and carries no reason, so every child-side
///   refusal — a FIFO, a hardlink, a symlink, another account's file — arrives
///   as [`FilesOpError::RemoveFailed`]. **The idempotent
///   [`FilesOpError::NotFound`] `files.proto` requires can therefore only be
///   produced here**, as root, before the fork. That is the whole job of this
///   call, and it is a job the walk cannot do.
///
/// It is also, incidentally, a containment check: canonicalization resolves a
/// symlink left AT the file's own name, so a challenge name replaced with a link
/// to `/etc/passwd` answers [`FilesOpError::EscapesHome`]. That is worth knowing
/// and is NOT what justifies the call — `unlinkat` with no flags removes the
/// entry and never follows it, and the entry is opened with `O_NOFOLLOW` before
/// it is judged, so the removal is safe with or without this step. The
/// `NotFound` is what only this step can produce, and
/// `a_challenge_that_is_already_gone_is_reported_as_not_found` in the polygon is
/// what goes red when it is taken away.
///
/// Idempotent as `files.proto` requires: removing a file that is not there is
/// [`FilesOpError::NotFound`], which the panel reads as "already done" rather
/// than as a fault, so a renewal retried after a timeout does not fail on its
/// own cleanup.
///
/// # Errors
///
/// Returns [`FilesOpError::NotFound`] when the file is not there;
/// [`FilesOpError::EscapesHome`] when the path resolves outside the account's
/// home; [`FilesOpError::Privilege`] when the account cannot be resolved or the
/// privilege drop fails; [`FilesOpError::HomeUnusable`] and
/// [`FilesOpError::DirectoryUnusable`] when the walk to the file is refused;
/// and [`FilesOpError::RemoveFailed`] when the child refused or could not
/// perform the unlink — including the case where the entry turned out not to be
/// a regular file the account owns.
pub fn delete_entry(host: &dyn FilesHost, input: &DeleteEntryInput) -> Result<(), FilesOpError> {
    host.resolve_in_account_home(&input.account, &input.path.as_path())?;

    host.remove_as_account(&input.account, &input.path)
}

#[cfg(test)]
#[path = "../tests/files/delete_entry_tests.rs"]
mod tests;
