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
//! It records the ORDER of the calls as well as their arguments, because the
//! order is the property `write_file` exists to get right: the containment
//! check has to happen after the directories are created and before the content
//! is written, and a fake that only remembered "resolve was called" would be
//! green for a version that called it last.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::validation::file_mode::FileMode;
use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::relative_path::RelativePath;

use crate::files::{FilesHost, FilesOpError};

/// One thing the host was asked to do.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Call {
    /// The parent directories were created, as the account.
    CreateParents(String),
    /// A path was resolved and proved contained, as root.
    Resolve(PathBuf),
    /// Content was written, with its mode.
    Write(String, Vec<u8>, u32),
    /// An entry was removed, as the account.
    Remove(String),
}

/// A host that records what it was asked and answers what it was told to.
#[derive(Debug, Default)]
pub(crate) struct FakeFilesHost {
    /// Every call, in the order it arrived.
    calls: Mutex<Vec<Call>>,
    /// What `create_parents_as_account` answers, if not success.
    create_parents_fails: Option<FilesOpError>,
    /// What `resolve_in_account_home` answers, if not success.
    resolve_fails: Option<FilesOpError>,
    /// What `write_as_account` answers, if not success.
    write_fails: Option<FilesOpError>,
    /// What `remove_as_account` answers, if not success.
    remove_fails: Option<FilesOpError>,
}

impl FakeFilesHost {
    /// A host on which everything succeeds.
    pub(crate) fn new() -> Self {
        Self::default()
    }

    /// A host whose directory creation fails with `error`.
    pub(crate) fn failing_create_parents(error: FilesOpError) -> Self {
        Self {
            create_parents_fails: Some(error),
            ..Self::default()
        }
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

    /// Everything the host was asked, in order.
    pub(crate) fn calls(&self) -> Vec<Call> {
        self.calls.lock().unwrap().clone()
    }

    /// Appends one call to the record.
    fn record(&self, call: Call) {
        self.calls.lock().unwrap().push(call);
    }
}

impl FilesHost for FakeFilesHost {
    fn create_parents_as_account(
        &self,
        _account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError> {
        self.record(Call::CreateParents(
            relative.as_path().display().to_string(),
        ));

        match &self.create_parents_fails {
            Some(error) => Err(clone_error(error)),
            None => Ok(()),
        }
    }

    fn write_as_account(
        &self,
        _account: &AccountName,
        relative: &RelativePath,
        contents: &[u8],
        mode: FileMode,
    ) -> Result<(), FilesOpError> {
        self.record(Call::Write(
            relative.as_path().display().to_string(),
            contents.to_vec(),
            mode.bits(),
        ));

        match &self.write_fails {
            Some(error) => Err(clone_error(error)),
            None => Ok(()),
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
        FilesOpError::RemoveFailed => FilesOpError::RemoveFailed,
        other => panic!("this fake was given an error it cannot reproduce: {other:?}"),
    }
}
