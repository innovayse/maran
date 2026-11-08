//! What a read of one file in a customer's home carries.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::system::name::AccountName;

use super::read_file_input_error::ReadFileInputError;

/// The largest file the agent will stream, in bytes: 512 MiB.
///
/// Above the panel's own download limit (256 MiB) on purpose, so the panel's
/// configuration is always the tighter bound and this one is the backstop for a
/// panel that was misconfigured or compromised.
const CEILING_BYTES: u64 = 512 * 1024 * 1024;

/// One validated request to read a file.
///
/// Constructed only by [`ReadFileInput::new`], so a limit of zero or one above
/// the ceiling cannot exist downstream.
#[derive(Debug, Clone)]
pub struct ReadFileInput {
    /// The account whose home holds the file.
    account: AccountName,
    /// The file, relative to the home. Never the home itself in practice: the
    /// walk refuses a directory.
    path: BrowsePath,
    /// The most bytes the caller accepts; the file is refused beyond it.
    max_bytes: u64,
}

impl ReadFileInput {
    /// Builds a request.
    ///
    /// # Errors
    ///
    /// Returns [`ReadFileInputError::ZeroLimit`] for a limit of zero and
    /// [`ReadFileInputError::AboveCeiling`] for one above 512 MiB.
    pub fn new(
        account: AccountName,
        path: BrowsePath,
        max_bytes: u64,
    ) -> Result<Self, ReadFileInputError> {
        if max_bytes == 0 {
            return Err(ReadFileInputError::ZeroLimit);
        }
        if max_bytes > CEILING_BYTES {
            return Err(ReadFileInputError::AboveCeiling);
        }

        Ok(Self {
            account,
            path,
            max_bytes,
        })
    }

    /// The account whose home holds the file.
    #[must_use]
    pub fn account(&self) -> &AccountName {
        &self.account
    }

    /// The file's path relative to the home.
    #[must_use]
    pub fn path(&self) -> &BrowsePath {
        &self.path
    }

    /// The most bytes the caller accepts.
    #[must_use]
    pub fn max_bytes(&self) -> u64 {
        self.max_bytes
    }
}

#[cfg(test)]
#[path = "../../tests/files/model/read_file_input_tests.rs"]
mod tests;
