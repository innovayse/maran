//! A [`JailMounts`] that records the accounts it was asked to refresh.

use std::path::{Path, PathBuf};
use std::sync::{Mutex, PoisonError};

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::jail_mounts::JailMounts;

/// Records every refresh, and what a watched path looked like at that moment.
pub(crate) struct RecordingJailMounts {
    /// The accounts refreshed, in order.
    accounts: Mutex<Vec<String>>,
    /// A path whose existence is noted at each refresh, so a test can say that
    /// the parked tree was still there while the jails were mounted.
    watched: Option<PathBuf>,
    /// Whether the watched path existed at each refresh.
    watched_existed: Mutex<Vec<bool>>,
    /// Whether every refresh fails.
    failing: bool,
}

impl RecordingJailMounts {
    /// A seam that succeeds and watches nothing.
    pub(crate) fn new() -> Self {
        Self {
            accounts: Mutex::new(Vec::new()),
            watched: None,
            watched_existed: Mutex::new(Vec::new()),
            failing: false,
        }
    }

    /// Notes whether `path` exists at each refresh.
    pub(crate) fn watching(mut self, path: &Path) -> Self {
        self.watched = Some(path.to_path_buf());
        self
    }

    /// Makes every refresh fail, as a service manager that refuses does.
    pub(crate) fn failing(mut self) -> Self {
        self.failing = true;
        self
    }

    /// The accounts refreshed so far, in order.
    pub(crate) fn refreshed(&self) -> Vec<String> {
        self.accounts
            .lock()
            .unwrap_or_else(PoisonError::into_inner)
            .clone()
    }

    /// Whether the watched path existed at each refresh, in order.
    pub(crate) fn watched_existed(&self) -> Vec<bool> {
        self.watched_existed
            .lock()
            .unwrap_or_else(PoisonError::into_inner)
            .clone()
    }
}

impl JailMounts for RecordingJailMounts {
    fn refresh(&self, account: &AccountName) -> Result<(), BackupError> {
        self.accounts
            .lock()
            .unwrap_or_else(PoisonError::into_inner)
            .push(account.as_str().to_owned());
        if let Some(path) = &self.watched {
            self.watched_existed
                .lock()
                .unwrap_or_else(PoisonError::into_inner)
                .push(path.exists());
        }

        if self.failing {
            return Err(BackupError::JailsNotRefreshed);
        }

        Ok(())
    }
}
