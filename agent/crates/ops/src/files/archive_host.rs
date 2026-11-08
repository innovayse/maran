//! The seam between the archive operations and the machine they run on.

use std::path::Path;

use crate::files::FilesOpError;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::create_archive_input::CreateArchiveInput;
use crate::files::model::extract_archive_input::ExtractArchiveInput;

/// Everything the archive operations do to this machine.
///
/// Beside [`super::FilesHost`] and [`super::BrowseHost`] and not inside either:
/// archives are the one area that PARSES a format a customer chose, and keeping
/// them behind a seam of their own keeps every dependency of that parsing
/// (`zip`, `tar`, `flate2`) out of the other two. Every method forks, drops to a
/// hosting account and reads or writes a real customer's home, which a unit test
/// must never do. The only implementation that touches the machine is
/// [`super::ProcessArchiveHost`].
///
/// Every method MUST be called from `tokio::task::spawn_blocking`: the fork
/// blocks in `waitpid`, which on a runtime worker stalls every other command.
pub trait ArchiveHost: Send + Sync {
    /// Extracts the archive `input` names, as the account.
    ///
    /// # Errors
    ///
    /// As [`super::extract_archive`].
    fn extract_as_account(
        &self,
        input: &ExtractArchiveInput,
    ) -> Result<ArchiveReport, FilesOpError>;

    /// Creates the archive `input` describes, as the account.
    ///
    /// # Errors
    ///
    /// As [`super::create_archive`].
    fn create_as_account(&self, input: &CreateArchiveInput) -> Result<ArchiveReport, FilesOpError>;

    /// Bytes an unprivileged writer could still add to the filesystem
    /// `directory` lives on. A host question, asked as root and needing no fork.
    ///
    /// # Errors
    ///
    /// Returns the underlying error when the filesystem cannot be measured; the
    /// caller refuses the operation rather than assuming room.
    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error>;
}
