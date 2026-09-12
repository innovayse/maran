//! The seam between the login operations and the machine they run on.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::system_account::SystemAccount;

use crate::logins::logins_error::LoginsError;

/// The operating-system operations the login area needs.
///
/// Two methods and nothing else, and the shape of the first one is the lesson
/// this module was made out of. The enumeration it replaces asked its host
/// "give me this account's logins", so the PREDICATE — which passwd row belongs
/// to this account — lived behind the seam, in the one file no unit test drives.
/// The fake answering that question kept its own copy of the predicate, so the
/// two could never disagree, and the defect (rows were selected by the SFTP jail
/// alone, which walks straight past an FTPS login) was invisible to every test
/// in the suite.
///
/// So this seam hands back ROWS. What a row means is decided in
/// [`account_logins`](super::account_logins::account_logins), in the open, where
/// a test can plant a row in either jail and read the classification back.
///
/// Implementations MUST spawn with an argv array against an absolute path taken
/// from the `DistroAdapter`, never through a shell and never through a program
/// name resolved by `PATH` (rules/security.md item 3).
pub trait LoginsHost: Send + Sync {
    /// Reads the host's local password database into one row per entry.
    ///
    /// `passwd_database` is the path the `DistroAdapter` gives for it, passed
    /// in rather than known here for the same reason a program path is: `ops`
    /// names no platform location of its own (rules/architecture.md).
    ///
    /// # Errors
    ///
    /// Returns [`LoginsError::AccountMissing`] when the database cannot be read
    /// at all. A database with no row for an account is not this method's
    /// business — it answers with the rows it found, and the caller decides.
    fn read_passwd(&self, passwd_database: &str) -> Result<Vec<SystemAccount>, LoginsError>;

    /// Runs `program` with `arguments` and waits for it.
    ///
    /// Nothing in this area writes to a child's standard input: it reads a
    /// password STATE and turns a lock, and neither carries a secret. The
    /// method that pipes a credential lives in the area that sets one.
    ///
    /// # Errors
    ///
    /// Returns [`LoginsError::SpawnFailed`] with a `code` of `-1` when the
    /// program cannot be started at all. A non-zero exit is NOT an error here —
    /// it is returned in the outcome, because each caller reads a status
    /// differently.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, LoginsError>;
}
