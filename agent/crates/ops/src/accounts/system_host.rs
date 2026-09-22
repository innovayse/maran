//! The seam between account operations and the machine they run on.

use crate::accounts::model::home_metadata::HomeMetadata;
use crate::accounts::{AccountError, CommandOutcome};

/// The operating-system operations the account module needs.
///
/// A trait rather than direct calls to `std::process::Command`, and not for
/// abstraction's sake: creating a user, locking one and deleting a home directory
/// are exactly the operations a test must never really perform. With this seam the
/// decisions — which program, which arguments, in which order, what to do with each
/// exit status — are testable, and the one implementation that actually spawns
/// processes stays small enough to read in full.
///
/// Implementations must spawn with an argv array and never through a shell
/// (rules/security.md item 3): the account name is caller-supplied, and a shell
/// would turn a name this crate has validated into a string a shell re-parses.
pub trait SystemHost: Send + Sync {
    /// Runs `program` with `arguments` and waits for it.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandUnavailable`] when the program cannot be
    /// started at all. A non-zero exit is NOT an error here — it is returned in
    /// the outcome, because each caller reads the status differently.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, AccountError>;

    /// Reports whether a system user exists.
    ///
    /// # Errors
    ///
    /// Returns an error when the lookup itself could not be performed.
    fn user_exists(&self, username: &str) -> Result<bool, AccountError>;

    /// Returns the number of bytes the directory tree occupies.
    ///
    /// # Errors
    ///
    /// Returns an error when the tree cannot be measured.
    fn directory_size(&self, path: &str) -> Result<u64, AccountError>;

    /// Reads the host's local password database, whole.
    ///
    /// The one source `RepairHomeGroups` enumerates hosting accounts from —
    /// the same file and the same parse
    /// (`maran_agent_core::utils::system_accounts::system_accounts`) the
    /// monitoring area's disk-usage figures already use, so "which accounts
    /// exist" answers identically everywhere it is asked.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::HomeInspection`] when the database at `path`
    /// cannot be read.
    fn read_password_database(&self, path: &str) -> Result<String, AccountError>;

    /// Reads `/proc/mounts`, whole — the kernel's own live list of mounted
    /// filesystems and the options each was mounted with.
    ///
    /// Used only to classify quota enforceability
    /// (`super::quota_enforceability::classify`); this agent never mutates a
    /// mount, only reads what one already is.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::HomeInspection`] when `/proc/mounts` cannot be
    /// read.
    fn read_mounts(&self) -> Result<String, AccountError>;

    /// Reads what the filesystem itself says about `path`, with `lstat` and
    /// never `stat` — a symlink is reported as itself, not followed.
    ///
    /// `None` when nothing exists at `path`; that is not an error, it is one of
    /// the answers a home repair predicate needs to hear.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::HomeInspection`] when the path exists but its
    /// metadata could not be read for any other reason.
    fn home_metadata(&self, path: &str) -> Result<Option<HomeMetadata>, AccountError>;
}
