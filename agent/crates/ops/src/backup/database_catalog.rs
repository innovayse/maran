//! The seam that answers which databases an account still owns.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;

/// Which databases belong to the account being backed up.
///
/// A seam and not a call into `ops::db`, and this is a rule about the crate
/// rather than a preference: **`ops::backup` does not import another `ops`
/// area.** A cross-area import inside `ops` is the violation caught in the
/// firewall plan, where the accounts area reached into another area's helper
/// and the two grew a dependency neither owned. The composition happens one
/// layer up, in `agent/src/services/backup/`, exactly as the accounts service
/// composes four hosts for the deletion cascade — the service wires
/// `ops::db::list_databases` into this seam.
///
/// The answer is a `Vec<DatabaseName>` and not a `Vec<String>`, so that every
/// name reaching the dump client's argv is a value the validating constructor
/// produced. A name on the server that could not be rebuilt through
/// `DatabaseName::for_account` is not one this agent would ever have created,
/// and the listing does not report it.
///
/// Implementations MUST be called from `tokio::task::spawn_blocking`: the one
/// that exists asks a real database server.
pub trait DatabaseCatalog: Send + Sync {
    /// The databases `account` owns, in the order they should be dumped.
    ///
    /// An account with no databases answers an empty list, which is not a
    /// failure: it is what most static sites look like.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::DatabasesUnknown`] when the question could not be
    /// answered at all. A creation refuses on that rather than proceeding with
    /// an empty list — see the variant for why.
    fn databases_of(&self, account: &AccountName) -> Result<Vec<DatabaseName>, BackupError>;
}
