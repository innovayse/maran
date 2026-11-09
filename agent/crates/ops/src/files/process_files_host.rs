//! The [`FilesHost`] that actually touches this machine.

use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::child_input_writer::ChildInputWriter;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::available_bytes::available_bytes;
use maran_agent_core::validation::fs::path::resolve_in_home;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::FilesOpError;
use crate::files::account_home::account_home;
use crate::files::child_upload::ChildUpload;
use crate::files::feed_stop::FeedStop;
use crate::files::feed_upload::feed_upload;
use crate::files::fork_upload_work::fork_upload_work;
use crate::files::model::change_permissions_input::ChangePermissionsInput;
use crate::files::model::copy_entry_input::CopyEntryInput;
use crate::files::model::copy_report::CopyReport;
use crate::files::model::create_directory_input::CreateDirectoryInput;
use crate::files::model::force_delete_entry_input::ForceDeleteEntryInput;
use crate::files::model::limit_kind::LimitKind;
use crate::files::model::move_entry_input::MoveEntryInput;
use crate::files::model::permission_report::PermissionReport;
use crate::files::model::purge_expired_trash_input::PurgeExpiredTrashInput;
use crate::files::model::restore_trash_entry_input::RestoreTrashEntryInput;
use crate::files::model::trash_entry_input::TrashEntryInput;
use crate::files::model::trashed_entry::TrashedEntry;
use crate::files::model::upload_source::UploadSource;
use crate::files::model::write_file_input::WriteFileInput;
use crate::files::process_mutations::ProcessMutations;
use crate::files::remove_in_home::remove_in_home;
use crate::files::write_in_home::write_in_home;

/// Prefix every temporary file this host creates carries.
///
/// Named after the product and starting with a dot so that a temporary left
/// behind by a crash is recognisable as ours and is not served by a web server
/// configured to hide dotfiles. It is only ever visible for the microseconds
/// between the create and the rename; the prefix is for the case where it is
/// not.
const TEMPORARY_PREFIX: &str = ".maran-write";

/// Forks to the account for every operation and resolves paths as root.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: what is left here is resolving an account's ids,
/// naming a temporary file, and forking. Everything that a reviewer of a
/// privileged write should read is in `write_in_home`, `remove_in_home` and
/// `open_parent_directory`.
pub struct ProcessFilesHost {
    /// The file manager's mutations, which this host hands to the forked child.
    mutations: ProcessMutations,
}

impl ProcessFilesHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self {
            mutations: ProcessMutations::new(),
        }
    }

    /// A name for the temporary file the write renames into place.
    ///
    /// Built in the PARENT, before the fork, because `write_in_home` runs in a
    /// forked child of a multi-threaded daemon and everything it does not have
    /// to do there is better done here (`fork_as_account`'s contract). It also
    /// means the name is a plain `&str` by the time the child sees it, with no
    /// formatting and no clock call left on that side.
    ///
    /// Uniqueness comes from the process id and the wall clock together: two
    /// writes into one directory at the same nanosecond from the same daemon do
    /// not happen, and if they somehow did, the temporary file is created with
    /// `O_EXCL` — so the second write fails loudly rather than the two of them
    /// interleaving into one file.
    fn temporary_name() -> String {
        let nanoseconds = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_or(0, |since| since.as_nanos());

        format!("{TEMPORARY_PREFIX}-{}-{nanoseconds}", std::process::id())
    }
}

impl Default for ProcessFilesHost {
    fn default() -> Self {
        Self::new()
    }
}

impl super::FilesHost for ProcessFilesHost {
    /// Streams the upload into a file in a forked child that has dropped to the account.
    ///
    /// The ids are resolved here, at the moment of use, and never cached: an account deleted
    /// and recreated between two operations gets a different uid, and a cached one would
    /// write into whoever now holds it. The parent feeds the child framed chunks and the child
    /// answers the bytes written, or the refusal that stopped it.
    fn write_as_account(
        &self,
        input: &WriteFileInput,
        source: &mut dyn UploadSource,
    ) -> Result<u64, FilesOpError> {
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();
        let temporary = Self::temporary_name();
        let limit = input.limit.bytes();

        let mut over_limit = false;
        let mut feed = |writer: &mut ChildInputWriter<'_>| {
            let fed = feed_upload(source, limit, &mut |chunk: &[u8]| writer.send(chunk));
            match fed {
                Ok(()) => writer.complete(),
                Err(stop) => {
                    over_limit = matches!(stop, FeedStop::OverLimit);
                    Err(stop.into_io_error())
                }
            }
        };

        let written = fork_upload_work(&ids, &mut feed, |context| {
            write_in_home(
                &home,
                input,
                &temporary,
                uid,
                &mut ChildUpload::new(context),
            )
        });

        // The parent's own count refused the upload and aborted the child; the child's answer
        // to an abort is a plain failure, which would hide the reason the caller can act on.
        if over_limit {
            return Err(FilesOpError::LimitReached(LimitKind::Bytes));
        }
        written
    }

    /// Removes the file in a forked child that has dropped to the account.
    fn remove_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError> {
        let ids = AccountIds::resolve(account)?;
        let home = account_home(account);
        let uid = ids.uid();

        fork_as_account(&ids, || {
            remove_in_home(&home, relative, uid).map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| match error {
            // NOT `NotFound`, tempting as it is: "already gone" is the common
            // reason a removal fails, but it is not the only one, and the
            // others — a FIFO or a hardlink left at the challenge name, a
            // symlink refused by `O_NOFOLLOW` — are somebody trying something.
            // The child cannot say which it hit, so reporting them all as
            // "nothing was there" would erase exactly the ones worth seeing.
            // The idempotent `NotFound` is produced instead by the root-side
            // containment check in `delete_entry`, which runs BEFORE this and
            // can tell an absent file from a present one.
            PrivError::WorkFailed => FilesOpError::RemoveFailed,
            other => FilesOpError::Privilege(other),
        })
    }

    /// Delegates to `agent-core`'s one containment primitive.
    ///
    /// Runs as root, unlike everything above it, and that is the point: it
    /// needs to see the whole path, including a level the account's own process
    /// might not be allowed to traverse.
    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, FilesOpError> {
        Ok(resolve_in_home(account, relative)?)
    }

    /// Asks `statvfs` of `directory` itself.
    ///
    /// # Errors
    ///
    /// As [`available_bytes`].
    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error> {
        available_bytes(directory)
    }

    /// Delegates to the mutations, which fork to the account.
    fn create_directory_as_account(
        &self,
        input: &CreateDirectoryInput,
    ) -> Result<(), FilesOpError> {
        self.mutations.create_directory_as_account(input)
    }

    /// Delegates to the mutations, which fork to the account.
    fn move_as_account(&self, input: &MoveEntryInput) -> Result<(), FilesOpError> {
        self.mutations.move_as_account(input)
    }

    /// Delegates to the mutations, which fork to the account and bound a tree walk.
    fn change_permissions_as_account(
        &self,
        input: &ChangePermissionsInput,
    ) -> Result<PermissionReport, FilesOpError> {
        self.mutations.change_permissions_as_account(input)
    }

    /// Delegates to the mutations, which fork to the account and bound the work.
    fn copy_as_account(&self, input: &CopyEntryInput) -> Result<CopyReport, FilesOpError> {
        self.mutations.copy_as_account(input)
    }

    /// Delegates to the mutations, which fork to the account and bound the work.
    fn force_delete_as_account(&self, input: &ForceDeleteEntryInput) -> Result<u64, FilesOpError> {
        self.mutations.force_delete_as_account(input)
    }

    /// Delegates to the mutations, which fork to the account.
    fn trash_as_account(&self, input: &TrashEntryInput) -> Result<TrashEntryId, FilesOpError> {
        self.mutations.trash_as_account(input)
    }

    /// Delegates to the mutations, which read the trash in a forked child.
    fn list_trash_as_account(
        &self,
        account: &AccountName,
    ) -> Result<Vec<TrashedEntry>, FilesOpError> {
        self.mutations.list_trash_as_account(account)
    }

    /// Delegates to the mutations, which fork to the account.
    fn restore_as_account(&self, input: &RestoreTrashEntryInput) -> Result<String, FilesOpError> {
        self.mutations.restore_as_account(input)
    }

    /// Delegates to the mutations, which fork to the account and bound the work.
    fn empty_trash_as_account(&self, account: &AccountName) -> Result<u64, FilesOpError> {
        self.mutations.empty_trash_as_account(account)
    }

    /// Delegates to the mutations, which fork to the account and bound the work.
    fn purge_expired_trash_as_account(
        &self,
        input: &PurgeExpiredTrashInput,
    ) -> Result<u64, FilesOpError> {
        self.mutations.purge_expired_trash_as_account(input)
    }
}
