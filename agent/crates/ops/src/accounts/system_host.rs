//! The seam between account operations and the machine they run on.

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
}
