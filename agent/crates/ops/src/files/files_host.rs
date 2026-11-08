//! The seam between the file operations and the machine they run on.

use std::path::{Path, PathBuf};

use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::FilesOpError;
use crate::files::model::change_permissions_input::ChangePermissionsInput;
use crate::files::model::copy_entry_input::CopyEntryInput;
use crate::files::model::copy_report::CopyReport;
use crate::files::model::create_directory_input::CreateDirectoryInput;
use crate::files::model::force_delete_entry_input::ForceDeleteEntryInput;
use crate::files::model::move_entry_input::MoveEntryInput;
use crate::files::model::permission_report::PermissionReport;
use crate::files::model::purge_expired_trash_input::PurgeExpiredTrashInput;
use crate::files::model::restore_trash_entry_input::RestoreTrashEntryInput;
use crate::files::model::trash_entry_input::TrashEntryInput;
use crate::files::model::trashed_entry::TrashedEntry;
use crate::files::model::upload_source::UploadSource;
use crate::files::model::write_file_input::WriteFileInput;

/// Everything the file operations do to this machine.
///
/// A trait for the same reason `SiteHost` is one: every method here forks,
/// drops to a hosting account and touches a real customer's home, which is
/// precisely what a unit test must never actually do. The one implementation
/// that touches the machine is [`super::ProcessFilesHost`], and it is the
/// smallest piece of the area — the decisions worth reviewing are in the
/// operations, and the hardening worth reviewing is in `write_in_home`,
/// `remove_in_home` and `open_parent_directory`, all three of which a test
/// drives directly against a temporary directory it owns.
///
/// The write creates the missing parent directories itself, in the same
/// privileged step that writes the file (issue #167): the file manager uploads
/// into folders that may not exist yet, and splitting the two walks into two
/// forks would only add a window between them.
pub trait FilesHost: Send + Sync {
    /// Streams `source` into a new file as the account of `input`, atomically, and answers the bytes
    /// written.
    ///
    /// Creates the missing parent directories, writes a temporary file beside the target and
    /// publishes it by rename, all inside one forked child that has dropped to the account.
    /// The source is read on the calling thread and sent to the child; it is never held
    /// whole.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: the underlying fork
    /// blocks in `waitpid`, which on a runtime worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
    /// [`FilesOpError::DirectoryUnusable`], [`FilesOpError::AlreadyExists`],
    /// [`FilesOpError::LimitReached`], [`FilesOpError::QuotaExceeded`] and
    /// [`FilesOpError::WriteFailed`] as `write_in_home` documents them.
    fn write_as_account(
        &self,
        input: &WriteFileInput,
        source: &mut dyn UploadSource,
    ) -> Result<u64, FilesOpError>;

    /// Removes the file at `relative`, as `account`.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`, as
    /// above.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::Privilege`], [`FilesOpError::NotFound`],
    /// [`FilesOpError::NotARegularFile`] and [`FilesOpError::RemoveFailed`] as
    /// `remove_in_home` documents them.
    fn remove_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError>;

    /// Resolves `relative` inside `account`'s home, returning the canonical
    /// path, and reporting whether there is anything there at all.
    ///
    /// **Called by [`super::delete_entry`] and by nothing else**, and the reason
    /// is not containment — the descriptor walk is that, and it needs no help.
    /// This is the only thing in the area that can tell "there is no such entry"
    /// from "the child refused the entry", because the child's outcome crosses
    /// back as an exit status and carries no reason. A removal has to make that
    /// distinction to be idempotent; a write never has to locate anything, so it
    /// does not call this, and a version that did was deleted rather than kept
    /// as a check that could not fail.
    ///
    /// # Errors
    ///
    /// Returns [`FilesOpError::NotFound`] when the path does not exist and
    /// [`FilesOpError::EscapesHome`] when it resolves outside the home.
    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, FilesOpError>;

    /// Bytes an unprivileged writer could still add to the filesystem
    /// `directory` lives on — `f_bavail`, asked of the directory itself.
    ///
    /// A host question and not a customer one, so it is asked as root and
    /// needs no fork: the free-space floor (issue #148) is decided on the
    /// answer before anything is written.
    ///
    /// # Errors
    ///
    /// Returns the underlying error when the filesystem cannot be measured;
    /// the caller refuses the write rather than assuming room.
    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error>;

    /// Creates one directory, and its missing parents, as the account.
    ///
    /// # Errors
    ///
    /// As `create_directory_in_home` documents them, plus
    /// [`FilesOpError::Privilege`] when the account cannot be resolved or the fork
    /// fails.
    fn create_directory_as_account(&self, input: &CreateDirectoryInput)
    -> Result<(), FilesOpError>;

    /// Moves one entry to a name that does not exist, as the account.
    ///
    /// # Errors
    ///
    /// As `move_in_home` documents them, plus [`FilesOpError::Privilege`].
    fn move_as_account(&self, input: &MoveEntryInput) -> Result<(), FilesOpError>;

    /// Changes permission bits, of one entry or of a tree, as the account.
    ///
    /// # Errors
    ///
    /// As `chmod_in_home` documents them, plus [`FilesOpError::Privilege`] and
    /// [`FilesOpError::Saturated`] for a recursive change at the bulk bound.
    fn change_permissions_as_account(
        &self,
        input: &ChangePermissionsInput,
    ) -> Result<PermissionReport, FilesOpError>;

    /// Copies a file or a tree to a name that does not exist, as the account.
    ///
    /// # Errors
    ///
    /// As `copy_in_home` documents them, plus [`FilesOpError::Privilege`] and
    /// [`FilesOpError::Saturated`] at the bulk bound.
    fn copy_as_account(&self, input: &CopyEntryInput) -> Result<CopyReport, FilesOpError>;

    /// Removes a file, a link or a tree, as the account, and answers how many
    /// entries were taken away.
    ///
    /// # Errors
    ///
    /// As `force_delete_in_home` documents them, plus
    /// [`FilesOpError::Privilege`] and [`FilesOpError::Saturated`].
    fn force_delete_as_account(&self, input: &ForceDeleteEntryInput) -> Result<u64, FilesOpError>;

    /// Moves one entry into the account's trash and answers the identifier it has there.
    ///
    /// # Errors
    ///
    /// As `trash_in_home` documents them, plus [`FilesOpError::Privilege`].
    fn trash_as_account(&self, input: &TrashEntryInput) -> Result<TrashEntryId, FilesOpError>;

    /// Lists the account's trash, newest first.
    ///
    /// # Errors
    ///
    /// As `list_trash_in_home` documents them, plus [`FilesOpError::Privilege`]
    /// and [`FilesOpError::TooLarge`] when the answer outgrows its ceiling.
    fn list_trash_as_account(
        &self,
        account: &AccountName,
    ) -> Result<Vec<TrashedEntry>, FilesOpError>;

    /// Moves one trash entry back into the home and answers the path it now has.
    ///
    /// # Errors
    ///
    /// As `restore_in_home` documents them, plus [`FilesOpError::Privilege`].
    fn restore_as_account(&self, input: &RestoreTrashEntryInput) -> Result<String, FilesOpError>;

    /// Removes everything in the account's trash and answers how many entries.
    ///
    /// # Errors
    ///
    /// As `empty_trash_in_home` documents them, plus
    /// [`FilesOpError::Privilege`] and [`FilesOpError::Saturated`].
    fn empty_trash_as_account(&self, account: &AccountName) -> Result<u64, FilesOpError>;

    /// Removes the trash entries older than the retention, and the stale copy
    /// staging, and answers how many trash entries went.
    ///
    /// # Errors
    ///
    /// As `purge_in_home` documents them, plus [`FilesOpError::Privilege`] and
    /// [`FilesOpError::Saturated`].
    fn purge_expired_trash_as_account(
        &self,
        input: &PurgeExpiredTrashInput,
    ) -> Result<u64, FilesOpError>;
}
