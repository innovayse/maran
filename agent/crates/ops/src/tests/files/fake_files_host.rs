//! The in-memory [`FilesHost`] the operation tests drive.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host forks, drops to a hosting account and writes into a real
//! customer's home — none of which a unit test may do. What a unit test pins
//! here is which steps an operation chooses and in which order; the hardening
//! those steps rely on is tested for real, against a temporary directory, in
//! `open_parent_directory_tests`, `write_in_home_tests` and
//! `remove_in_home_tests`.
//!
//! It records the ORDER of the calls as well as their arguments, so an
//! operation that grew a step, or took one in the wrong order, fails a test that
//! has to justify it.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;
use maran_agent_core::validation::system::name::AccountName;

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
use crate::files::{FilesHost, FilesOpError};

/// One thing the host was asked to do.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Call {
    /// A path was resolved and proved contained, as root.
    Resolve(PathBuf),
    /// Content was written: path, the bytes drained from the source, mode, whether an
    /// existing entry was to be refused, and the cap.
    Write(String, Vec<u8>, u32, bool, u64),
    /// An entry was removed, as the account.
    Remove(String),
    /// A directory was created.
    CreateDirectory,
    /// An entry was moved.
    Move,
    /// Permissions were changed.
    ChangePermissions,
    /// A tree was copied.
    Copy,
    /// A tree was removed.
    ForceDelete,
    /// An entry was trashed.
    Trash,
    /// The trash was listed.
    ListTrash,
    /// An entry was restored.
    Restore,
    /// The trash was emptied.
    EmptyTrash,
    /// The trash was purged of expired entries.
    PurgeExpiredTrash,
}

/// A host that records what it was asked and answers what it was told to.
#[derive(Debug, Default)]
pub(crate) struct FakeFilesHost {
    /// Every call, in the order it arrived.
    calls: Mutex<Vec<Call>>,
    /// What `resolve_in_account_home` answers, if not success.
    resolve_fails: Option<FilesOpError>,
    /// What `write_as_account` answers, if not success.
    write_fails: Option<FilesOpError>,
    /// What `remove_as_account` answers, if not success.
    remove_fails: Option<FilesOpError>,
    /// What every file-manager mutation answers instead of success.
    mutation_fails: Option<FilesOpError>,
    /// What `available_bytes` answers; `None` reads as a roomy filesystem.
    available: Option<u64>,
}

impl FakeFilesHost {
    /// A host on which everything succeeds.
    pub(crate) fn new() -> Self {
        Self::default()
    }

    /// A host whose containment check fails with `error`.
    pub(crate) fn failing_resolve(error: FilesOpError) -> Self {
        Self {
            resolve_fails: Some(error),
            ..Self::default()
        }
    }

    /// A host whose write fails with `error`.
    pub(crate) fn failing_write(error: FilesOpError) -> Self {
        Self {
            write_fails: Some(error),
            ..Self::default()
        }
    }

    /// A host whose removal fails with `error`.
    pub(crate) fn failing_remove(error: FilesOpError) -> Self {
        Self {
            remove_fails: Some(error),
            ..Self::default()
        }
    }

    /// A host whose home filesystem has exactly `available` bytes left.
    pub(crate) fn with_available(available: u64) -> Self {
        Self {
            available: Some(available),
            ..Self::default()
        }
    }

    /// Everything the host was asked, in order.
    pub(crate) fn calls(&self) -> Vec<Call> {
        self.calls.lock().unwrap().clone()
    }

    /// A host whose every file-manager mutation fails with `error`.
    pub(crate) fn failing_mutation(error: FilesOpError) -> Self {
        Self {
            mutation_fails: Some(error),
            ..Self::default()
        }
    }

    /// Appends one call to the record.
    fn record(&self, call: Call) {
        self.calls.lock().unwrap().push(call);
    }

    /// Records one mutation and answers the configured failure, if any.
    fn mutate(&self, call: Call) -> Result<(), FilesOpError> {
        self.record(call);

        match &self.mutation_fails {
            Some(error) => Err(clone_error(error)),
            None => Ok(()),
        }
    }
}

impl FilesHost for FakeFilesHost {
    fn write_as_account(
        &self,
        input: &WriteFileInput,
        source: &mut dyn UploadSource,
    ) -> Result<u64, FilesOpError> {
        let mut body = Vec::new();
        loop {
            match source.next_chunk() {
                Ok(Some(chunk)) => body.extend_from_slice(chunk),
                Ok(None) => break,
                Err(_aborted) => return Err(FilesOpError::WriteFailed),
            }
        }
        let written = body.len() as u64;
        let mut components: Vec<&str> = input
            .path
            .parent_components()
            .iter()
            .map(String::as_str)
            .collect();
        components.push(input.path.file_name());
        self.record(Call::Write(
            components.join("/"),
            body,
            input.mode.bits(),
            input.fail_if_exists,
            input.limit.bytes(),
        ));

        match &self.write_fails {
            Some(error) => Err(clone_error(error)),
            None => Ok(written),
        }
    }

    fn remove_as_account(
        &self,
        _account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError> {
        self.record(Call::Remove(relative.as_path().display().to_string()));

        match &self.remove_fails {
            Some(error) => Err(clone_error(error)),
            None => Ok(()),
        }
    }

    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, FilesOpError> {
        self.record(Call::Resolve(relative.to_path_buf()));

        match &self.resolve_fails {
            Some(error) => Err(clone_error(error)),
            None => Ok(PathBuf::from("/home").join(account.as_str()).join(relative)),
        }
    }

    fn available_bytes(&self, _directory: &Path) -> Result<u64, std::io::Error> {
        Ok(self.available.unwrap_or(u64::MAX))
    }

    fn create_directory_as_account(
        &self,
        _input: &CreateDirectoryInput,
    ) -> Result<(), FilesOpError> {
        self.mutate(Call::CreateDirectory)
    }

    fn move_as_account(&self, _input: &MoveEntryInput) -> Result<(), FilesOpError> {
        self.mutate(Call::Move)
    }

    fn change_permissions_as_account(
        &self,
        _input: &ChangePermissionsInput,
    ) -> Result<PermissionReport, FilesOpError> {
        self.mutate(Call::ChangePermissions)?;
        Ok(PermissionReport {
            changed: 3,
            skipped: 1,
        })
    }

    fn copy_as_account(&self, _input: &CopyEntryInput) -> Result<CopyReport, FilesOpError> {
        self.mutate(Call::Copy)?;
        Ok(CopyReport {
            entries: 5,
            bytes: 50,
        })
    }

    fn force_delete_as_account(&self, _input: &ForceDeleteEntryInput) -> Result<u64, FilesOpError> {
        self.mutate(Call::ForceDelete)?;
        Ok(7)
    }

    fn trash_as_account(&self, _input: &TrashEntryInput) -> Result<TrashEntryId, FilesOpError> {
        self.mutate(Call::Trash)?;
        Ok(TrashEntryId::parse("1700000000-00000000000000aa").unwrap())
    }

    fn list_trash_as_account(
        &self,
        _account: &AccountName,
    ) -> Result<Vec<TrashedEntry>, FilesOpError> {
        self.mutate(Call::ListTrash)?;
        Ok(vec![TrashedEntry {
            trash_id: "1700000000-00000000000000aa".to_owned(),
            original_path: "a/b".to_owned(),
            trashed_at_unix: 1_700_000_000,
            is_directory: false,
            size_bytes: 1,
        }])
    }

    fn restore_as_account(&self, _input: &RestoreTrashEntryInput) -> Result<String, FilesOpError> {
        self.mutate(Call::Restore)?;
        Ok("a/b".to_owned())
    }

    fn empty_trash_as_account(&self, _account: &AccountName) -> Result<u64, FilesOpError> {
        self.mutate(Call::EmptyTrash)?;
        Ok(2)
    }

    fn purge_expired_trash_as_account(
        &self,
        _input: &PurgeExpiredTrashInput,
    ) -> Result<u64, FilesOpError> {
        self.mutate(Call::PurgeExpiredTrash)?;
        Ok(4)
    }
}

/// Copies the error a fake was configured with.
///
/// `FilesOpError` is deliberately not `Clone` — nothing in production copies
/// one — so the fake reproduces the few variants its tests use rather than the
/// enum growing a derive for the sake of test code.
fn clone_error(error: &FilesOpError) -> FilesOpError {
    match error {
        FilesOpError::HomeUnusable => FilesOpError::HomeUnusable,
        FilesOpError::DirectoryUnusable => FilesOpError::DirectoryUnusable,
        FilesOpError::EscapesHome => FilesOpError::EscapesHome,
        FilesOpError::NotFound => FilesOpError::NotFound,
        FilesOpError::NotARegularFile => FilesOpError::NotARegularFile,
        FilesOpError::WriteFailed => FilesOpError::WriteFailed,
        FilesOpError::AlreadyExists => FilesOpError::AlreadyExists,
        FilesOpError::QuotaExceeded => FilesOpError::QuotaExceeded,
        FilesOpError::LimitReached(LimitKind::Bytes) => {
            FilesOpError::LimitReached(LimitKind::Bytes)
        }
        FilesOpError::RemoveFailed => FilesOpError::RemoveFailed,
        FilesOpError::Saturated => FilesOpError::Saturated,
        other => panic!("this fake was given an error it cannot reproduce: {other:?}"),
    }
}
