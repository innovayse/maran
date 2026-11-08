//! The list of what an extraction made, kept so a failure can undo exactly that.

use std::ffi::OsStr;
use std::fs::File;

use maran_agent_core::privs::remove_directory_in_directory::remove_directory_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;

use super::created_entry::CreatedEntry;
use super::directory_cursor::DirectoryCursor;

/// Everything this run created, in the order it created it.
///
/// An entry is recorded only when its creation SUCCEEDED, which is what makes
/// the rollback safe to run on any failure: a name that already existed (a file
/// that refused the extraction, a directory that was merged into) was never
/// recorded, so it is never touched. Directories are removed only while empty;
/// one the customer has since put something into stays, which is the right
/// answer for a run that must remove "what it created" and nothing else.
///
/// Bounded by the entry cap (plus the levels of the destination), because every
/// member is charged against the cap before it is written.
#[derive(Debug, Default)]
pub(crate) struct ExtractionLedger {
    /// What was created, oldest first.
    created: Vec<CreatedEntry>,
}

impl ExtractionLedger {
    /// An empty ledger.
    pub(crate) fn new() -> Self {
        Self::default()
    }

    /// Records a file this run created.
    pub(crate) fn record_file(&mut self, path: String) {
        self.created.push(CreatedEntry {
            path,
            is_directory: false,
        });
    }

    /// Records a directory this run created.
    pub(crate) fn record_directory(&mut self, path: String) {
        self.created.push(CreatedEntry {
            path,
            is_directory: true,
        });
    }

    /// Takes away everything recorded, newest first, as the account.
    ///
    /// Best effort and silent on purpose: this runs because the operation
    /// already failed for a reason worth reporting, and replacing that reason
    /// with "cleanup failed" would hide it. Each removal reaches its parent by
    /// descriptor from `home` without following a link, so a directory the
    /// customer replaced meanwhile is not walked into.
    pub(crate) fn roll_back(self, home: &File, uid: u32) {
        let Ok(root) = home.try_clone() else {
            return;
        };
        let mut cursor = DirectoryCursor::new(root, uid);

        for entry in self.created.iter().rev() {
            let components: Vec<&str> = entry.path.split('/').collect();
            let Some((name, parents)) = components.split_last() else {
                continue;
            };
            let Ok(parent) = cursor.enter(parents, None) else {
                continue;
            };
            let _ = if entry.is_directory {
                remove_directory_in_directory(parent, OsStr::new(name))
            } else {
                remove_file_in_directory(parent, OsStr::new(name))
            };
        }
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/extraction_ledger_tests.rs"]
mod tests;
