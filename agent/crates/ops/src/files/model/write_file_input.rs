//! Everything one file write is told, already validated.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::upload_limit::UploadLimit;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to write one file inside an account's home.
///
/// Every field is a type that cannot be constructed from anything invalid, so no layer
/// below re-parses any of them (rules/rust.md "Validation first"). The body is not here:
/// it streams from an [`UploadSource`](super::upload_source::UploadSource), because a file
/// of 100 MiB is never held in the root daemon's memory.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WriteFileInput {
    /// The account whose uid the write runs as, and whose home it is contained in.
    pub account: AccountName,
    /// Where the file goes, relative to that home. Missing parent directories are created.
    pub path: MutablePath,
    /// The permission bits the finished file carries, e.g. `0o644`.
    ///
    /// Applied to the descriptor after the write and before the rename, so the file is never
    /// briefly readable by more than it should be, and so the daemon's umask cannot quietly
    /// narrow it.
    pub mode: FileMode,
    /// Whether an existing entry at the path is refused (`true`) or replaced (`false`).
    ///
    /// Replacing swaps a whole regular file in by rename and never writes through a link;
    /// refusing is a rename that cannot replace anything, so the check and the publish are
    /// one atomic step.
    pub fail_if_exists: bool,
    /// The most body bytes this upload may carry, counted as they arrive.
    pub limit: UploadLimit,
}
