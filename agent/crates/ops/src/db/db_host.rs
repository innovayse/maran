//! The seam between the database operations and the server they run against.

use crate::db::db_error::DbError;

/// The one thing the database module asks of the machine: run a statement.
///
/// A trait rather than direct calls to `std::process::Command`, and for the
/// reason every area here has one: creating a database, minting a user and
/// dropping both are exactly the operations a unit test must never really
/// perform. Behind this seam the decisions — which statements, in which order,
/// what each refusal means — are testable, and the one implementation that
/// really spawns the client stays small enough to read in full.
///
/// The seam is a single method on purpose. An area whose host trait grows a
/// method per operation ends up with the decisions in the host, where the fake
/// answers for them and nothing is tested; here the host can only run a
/// statement, so every decision is above it.
///
/// Implementations must spawn the client with an argv array against the
/// absolute path the `DistroAdapter` gives, never through a shell
/// (rules/security.md item 3), and must connect over the local socket as root
/// with no password: the `unix_socket` plugin authenticates `root@localhost` by
/// the connecting process's uid. The agent therefore holds no database
/// credential at all, which is the strongest statement that can be made about
/// one — a password the agent stores is a password that can be stolen from the
/// agent.
pub trait DbHost: Send + Sync {
    /// Runs exactly one SQL statement and returns what the server printed.
    ///
    /// One statement, not a script: the client is invoked with `--execute`,
    /// which takes a single statement, so an implementation has nowhere to put
    /// a second one even if a caller tried to build one. The output comes back
    /// unformatted and without column headers, so a caller reads values rather
    /// than a table.
    ///
    /// # Errors
    ///
    /// - [`DbError::AccessDenied`] when the server refuses the agent's
    ///   connection, which means the socket authentication plugin is not
    ///   enabled for `root@localhost`.
    /// - [`DbError::AlreadyExists`] and [`DbError::NotFound`] when the server
    ///   refuses with a number that means exactly that, so a caller's
    ///   idempotency check survives losing a race with another writer.
    /// - [`DbError::Unparsable`] when the output is not text, or is longer than
    ///   the implementation will read into memory.
    /// - [`DbError::ClientFailed`] for every other refusal, and for a client
    ///   that could not be started.
    fn execute(&self, statement: &str) -> Result<String, DbError>;
}
