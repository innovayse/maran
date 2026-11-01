//! The in-memory [`PanelChallengeHost`] the panel challenge tests decide against.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use crate::ssl::panel_challenge_host::PanelChallengeHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Files by path, and how much room the filesystem reports.
pub(crate) struct FakePanelChallengeHost {
    pub(crate) files: Mutex<BTreeMap<PathBuf, String>>,
    pub(crate) available: Result<u64, ()>,
    pub(crate) measured: Mutex<Vec<PathBuf>>,
}

impl FakePanelChallengeHost {
    pub(crate) fn with_room(available: u64) -> Self {
        Self {
            files: Mutex::new(BTreeMap::new()),
            available: Ok(available),
            measured: Mutex::new(Vec::new()),
        }
    }

    pub(crate) fn unmeasurable() -> Self {
        Self {
            available: Err(()),
            ..Self::with_room(0)
        }
    }
}

impl PanelChallengeHost for FakePanelChallengeHost {
    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error> {
        self.measured.lock().unwrap().push(directory.to_path_buf());
        self.available
            .map_err(|()| std::io::Error::other("statvfs failed"))
    }

    fn write_challenge(&self, path: &Path, contents: &str) -> Result<(), SslOpError> {
        self.files
            .lock()
            .unwrap()
            .insert(path.to_path_buf(), contents.to_owned());
        Ok(())
    }

    fn remove_challenge(&self, path: &Path) -> Result<(), SslOpError> {
        self.files
            .lock()
            .unwrap()
            .remove(path)
            .map(|_| ())
            .ok_or(SslOpError::ChallengeNotFound)
    }
}
