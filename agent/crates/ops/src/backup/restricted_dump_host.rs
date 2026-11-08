//! The seam between the restricted loader and the database server.

use std::fs::File;

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::backup_error::BackupError;
use crate::backup::model::import_user::ImportUser;

/// What the restricted loader asks of the machine: make a user that holds one
/// database, load a dump as that user, and take the user away.
///
/// A trait for the reason [`crate::backup::BackupHost`] is one, and a separate
/// trait from it because these three calls are only ever made together, by one
/// function, in one order. Behind this seam the order, the cleanup on every path
/// and the refusal before any of it are testable without a database server.
///
/// **Every method MUST be called from `tokio::task::spawn_blocking`**: each
/// waits on a spawned client for as long as a customer's data takes.
pub trait RestrictedDumpHost: Send + Sync {
    /// Creates `user` with `password` and grants it every privilege on
    /// `database` and on nothing else: no global privilege, no other database,
    /// no `GRANT OPTION`.
    ///
    /// The password reaches the server on the client's standard input and never
    /// on a command line, which any local account can read.
    ///
    /// # Errors
    ///
    /// [`BackupError::LoadFailed`] when the client refuses or cannot be run, and
    /// [`BackupError::Bound`] when it was killed at its deadline.
    fn create_import_user(
        &self,
        user: &ImportUser,
        password: &SecretString,
        database: &DatabaseName,
    ) -> Result<(), BackupError>;

    /// Loads `dump` into the server connected as `user`, with the client's own
    /// commands switched off.
    ///
    /// `dump` is the file the loader has just scanned, opened once: handing the
    /// same descriptor on is what makes the file that was scanned the file that
    /// is loaded. The client's output is discarded, as every load's is.
    ///
    /// # Errors
    ///
    /// [`BackupError::LoadFailed`] when the client refuses, cannot be run, or
    /// cannot be given its credentials, and [`BackupError::Bound`] when it was
    /// killed at its deadline.
    fn load_dump_as(
        &self,
        user: &ImportUser,
        password: &SecretString,
        dump: File,
    ) -> Result<(), BackupError>;

    /// Drops `user`, if it is there.
    ///
    /// Idempotent: a user that is already gone is not a failure, so the loader
    /// can call this on every path, including after a creation that failed
    /// halfway.
    ///
    /// # Errors
    ///
    /// [`BackupError::DropFailed`] when the client refuses or cannot be run, and
    /// [`BackupError::Bound`] when it was killed at its deadline.
    fn drop_import_user(&self, user: &ImportUser) -> Result<(), BackupError>;

    /// Lists the temporary import users the server holds right now.
    ///
    /// Only names of the exact shape [`ImportUser::from_listed`] accepts are
    /// answered; any other row the server printed, including a user whose name
    /// merely starts with the prefix, is left out. An empty list means the
    /// server holds none.
    ///
    /// # Errors
    ///
    /// [`BackupError::ImportUsersUnlistable`] when the client refuses or cannot
    /// be run or its answer is not text, and [`BackupError::Bound`] when it was
    /// killed at its deadline. A failed listing is never an empty one.
    fn leftover_import_users(&self) -> Result<Vec<ImportUser>, BackupError>;
}
