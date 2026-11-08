//! The file manager's mutations, run against this machine.

use std::io::Write;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account_for_bytes::fork_as_account_for_bytes;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::bounded_slots::BoundedSlots;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::FilesOpError;
use crate::files::account_home::account_home;
use crate::files::chmod_in_home::chmod_in_home;
use crate::files::copy_in_home::copy_in_home;
use crate::files::create_directory_in_home::create_directory_in_home;
use crate::files::decode_trash_listing::decode_trash_listing;
use crate::files::empty_trash_in_home::empty_trash_in_home;
use crate::files::encode_trash_listing::encode_trash_listing;
use crate::files::force_delete_in_home::force_delete_in_home;
use crate::files::fork_files_work::fork_files_work;
use crate::files::list_trash_in_home::list_trash_in_home;
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
use crate::files::move_in_home::move_in_home;
use crate::files::mutable_path_components::mutable_path_components;
use crate::files::new_trash_entry_id::new_trash_entry_id;
use crate::files::parse_trashed_original::parse_trashed_original;
use crate::files::purge_in_home::purge_in_home;
use crate::files::restore_in_home::restore_in_home;
use crate::files::trash_in_home::trash_in_home;
use crate::files::trashed_original_in_home::trashed_original_in_home;
use crate::files::work_patience::WorkPatience;

/// Bulk operations admitted at once on the whole host.
const HOST_BULK_SLOTS: usize = 4;

/// Bulk operations admitted at once for one account.
const ACCOUNT_BULK_SLOTS: usize = 2;

/// Most bytes a trash listing's answer may take through the byte channel (48 MiB).
///
/// Ten thousand entries of the longest original path (4096 bytes) and the
/// fixed fields come to about 41 MiB; anything past this ceiling is the child
/// failing, not a bigger trash.
const TRASH_LISTING_CEILING: usize = 48 * 1024 * 1024;

/// Most bytes a recorded original path takes through the byte channel.
const ORIGINAL_PATH_CEILING: usize = 8192;

/// Process-wide counter that keeps two identifiers made in one nanosecond apart.
static NONCE_COUNTER: AtomicU64 = AtomicU64::new(0);

/// Forks to the account for every mutation, and bounds the bulk ones.
///
/// Held by [`super::ProcessFilesHost`], which delegates the mutation methods of
/// `FilesHost` here so that file stays the ACME write and the removal. The part
/// that touches the machine, and deliberately the smallest piece of the area: it resolves an account's ids at the moment of use
/// (never cached, because an account deleted and recreated gets another uid),
/// names what the child must not have to compute, takes a bulk slot where the
/// work is a tree, forks, and reads the child's answer as the closed set it is.
/// What a reviewer of a privileged mutation should read is in the `*_in_home`
/// functions and `tree/`.
///
/// **Bulk slots** (a copy, a removal, a recursive permission change, a purge, an
/// emptying) are two counting semaphores, host-wide and per account, taken
/// without waiting: at the bound the operation is refused with
/// [`FilesOpError::Saturated`] and the caller retries. They order nothing, take
/// no lock and contribute no wait-for edge, so none of the four properties of
/// "What this agent serialises" depends on them; they are not a row of that table
/// for the reason the run-by-hand slots are not.
///
/// **No account lock is taken.** A lock that refuses would make two clicks in a
/// batch fail each other. The stated limitation: an operation in flight while
/// `restore_backup` swaps the home lands in the home that is rolled aside.
#[derive(Debug)]
pub(crate) struct ProcessMutations {
    /// How many bulk operations may run at once: the same pair of counting
    /// semaphores the browse, archive and run-by-hand bounds use (`BoundedSlots`), with the numbers
    /// below.
    slots: BoundedSlots,
}

impl ProcessMutations {
    /// Creates the host with the production bounds: 2 per account, 4 per host.
    #[must_use]
    pub(crate) fn new() -> Self {
        Self {
            slots: BoundedSlots::new(HOST_BULK_SLOTS, ACCOUNT_BULK_SLOTS),
        }
    }

    /// The clock, in whole Unix seconds. Read once, in the parent, before a fork.
    fn now_unix() -> u64 {
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_or(0, |since| since.as_secs())
    }

    /// A fresh identifier for a trashed entry or a copy in progress.
    ///
    /// The nonce mixes the process id, a nanosecond reading and a counter. It is
    /// not trusted for uniqueness: the directory the identifier names is made
    /// with `mkdirat`, which refuses a name that exists.
    fn fresh_id() -> Result<TrashEntryId, FilesOpError> {
        let nanoseconds = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_or(0, |since| since.subsec_nanos());
        let counter = NONCE_COUNTER.fetch_add(1, Ordering::Relaxed);
        let nonce = (u64::from(std::process::id()) << 40)
            ^ (u64::from(nanoseconds) << 8)
            ^ (counter & 0xff)
            ^ (counter << 48);

        new_trash_entry_id(Self::now_unix(), nonce)
    }
}

impl Default for ProcessMutations {
    fn default() -> Self {
        Self::new()
    }
}

impl ProcessMutations {
    /// Creates the directory in a forked child that has dropped to the account.
    pub(crate) fn create_directory_as_account(
        &self,
        input: &CreateDirectoryInput,
    ) -> Result<(), FilesOpError> {
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();

        fork_files_work(&ids, WorkPatience::Single, || {
            create_directory_in_home(&home, &input.path, input.mode, uid).map(|()| (0, 0))
        })
        .map(|_| ())
    }

    /// Moves the entry in a forked child that has dropped to the account.
    pub(crate) fn move_as_account(&self, input: &MoveEntryInput) -> Result<(), FilesOpError> {
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();

        fork_files_work(&ids, WorkPatience::Single, || {
            move_in_home(&home, &input.source, &input.destination, uid).map(|()| (0, 0))
        })
        .map(|_| ())
    }

    /// Changes the bits in a forked child; a recursive change takes a bulk slot.
    pub(crate) fn change_permissions_as_account(
        &self,
        input: &ChangePermissionsInput,
    ) -> Result<PermissionReport, FilesOpError> {
        let _slot = if input.recursive {
            Some(self.slots.try_acquire(&input.account)?)
        } else {
            None
        };
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();
        let patience = if input.recursive {
            WorkPatience::Bulk
        } else {
            WorkPatience::Single
        };

        let (changed, skipped) = fork_files_work(&ids, patience, || {
            chmod_in_home(
                &home,
                &input.path,
                input.mode,
                input.directory_mode,
                input.recursive,
                uid,
            )
            .map(|report| (report.changed, report.skipped))
        })?;

        Ok(PermissionReport { changed, skipped })
    }

    /// Copies in a forked child, staged, within a bulk slot.
    pub(crate) fn copy_as_account(
        &self,
        input: &CopyEntryInput,
    ) -> Result<CopyReport, FilesOpError> {
        let _slot = self.slots.try_acquire(&input.account)?;
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();
        let stage = Self::fresh_id()?;

        let (entries, bytes) = fork_files_work(&ids, WorkPatience::Bulk, || {
            copy_in_home(
                &home,
                &input.source,
                &input.destination,
                &input.bounds,
                &stage,
                uid,
            )
            .map(|report| (report.entries, report.bytes))
        })?;

        Ok(CopyReport { entries, bytes })
    }

    /// Removes the tree in a forked child, within a bulk slot.
    pub(crate) fn force_delete_as_account(
        &self,
        input: &ForceDeleteEntryInput,
    ) -> Result<u64, FilesOpError> {
        let _slot = self.slots.try_acquire(&input.account)?;
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();

        fork_files_work(&ids, WorkPatience::Bulk, || {
            force_delete_in_home(&home, &input.path, uid).map(|removed| (removed, 0))
        })
        .map(|(removed, _)| removed)
    }

    /// Moves the entry into the trash in a forked child.
    pub(crate) fn trash_as_account(
        &self,
        input: &TrashEntryInput,
    ) -> Result<TrashEntryId, FilesOpError> {
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();
        let id = Self::fresh_id()?;

        fork_files_work(&ids, WorkPatience::Single, || {
            trash_in_home(&home, &input.path, &id, uid).map(|()| (0, 0))
        })?;

        Ok(id)
    }

    /// Reads the trash in a forked child and decodes the bounded answer here.
    pub(crate) fn list_trash_as_account(
        &self,
        account: &AccountName,
    ) -> Result<Vec<TrashedEntry>, FilesOpError> {
        let ids = AccountIds::resolve(account)?;
        let home = account_home(account);
        let uid = ids.uid();

        let bytes = fork_as_account_for_bytes(&ids, TRASH_LISTING_CEILING, |sink| {
            let entries = list_trash_in_home(&home, uid).map_err(|_| PrivError::WorkFailed)?;
            sink.write_all(&encode_trash_listing(&entries))
                .map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| match error {
            PrivError::WorkFailed => FilesOpError::DirectoryUnusable,
            PrivError::ResultTooLarge => FilesOpError::TooLarge,
            other => FilesOpError::Privilege(other),
        })?;

        // A malformed answer is the child failing to produce one, never a
        // partial listing.
        decode_trash_listing(&bytes).map_err(|_| FilesOpError::Privilege(PrivError::WorkFailed))
    }

    /// Restores in a forked child; where the entry came from is read first when
    /// the request names no destination.
    ///
    /// The recorded path is the customer's to edit, so it is read in its own
    /// short fork and parsed as a mutable path HERE before it names anything;
    /// the restore itself then runs with that validated destination, and the
    /// answer's path is the one the entry was moved to, never a re-read.
    pub(crate) fn restore_as_account(
        &self,
        input: &RestoreTrashEntryInput,
    ) -> Result<String, FilesOpError> {
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();

        let destination = match &input.destination {
            Some(destination) => destination.clone(),
            None => {
                let recorded = fork_as_account_for_bytes(&ids, ORIGINAL_PATH_CEILING, |sink| {
                    let text = trashed_original_in_home(&home, &input.id, uid)
                        .map_err(|_| PrivError::WorkFailed)?;
                    sink.write_all(text.as_bytes())
                        .map_err(|_| PrivError::WorkFailed)
                })
                .map_err(|error| match error {
                    PrivError::WorkFailed | PrivError::ResultTooLarge => {
                        FilesOpError::InvalidTrashEntry
                    }
                    other => FilesOpError::Privilege(other),
                })?;
                parse_trashed_original(&recorded)?
            }
        };

        fork_files_work(&ids, WorkPatience::Single, || {
            restore_in_home(&home, &input.id, &destination, uid).map(|()| (0, 0))
        })?;

        Ok(mutable_path_components(&destination).join("/"))
    }

    /// Empties the trash in a forked child, within a bulk slot.
    pub(crate) fn empty_trash_as_account(
        &self,
        account: &AccountName,
    ) -> Result<u64, FilesOpError> {
        let _slot = self.slots.try_acquire(account)?;
        let ids = AccountIds::resolve(account)?;
        let home = account_home(account);
        let uid = ids.uid();

        fork_files_work(&ids, WorkPatience::Bulk, || {
            empty_trash_in_home(&home, uid).map(|removed| (removed, 0))
        })
        .map(|(removed, _)| removed)
    }

    /// Purges expired trash in a forked child, within a bulk slot.
    ///
    /// The clock is read here, once, and handed to the child as a number.
    pub(crate) fn purge_expired_trash_as_account(
        &self,
        input: &PurgeExpiredTrashInput,
    ) -> Result<u64, FilesOpError> {
        let _slot = self.slots.try_acquire(&input.account)?;
        let ids = AccountIds::resolve(&input.account)?;
        let home = account_home(&input.account);
        let uid = ids.uid();
        let now = Self::now_unix();
        let days = input.older_than_days.get();

        fork_files_work(&ids, WorkPatience::Bulk, || {
            purge_in_home(&home, days, now, uid).map(|removed| (removed, 0))
        })
        .map(|(removed, _)| removed)
    }
}
