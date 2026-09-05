//! One line of the diagnostic database listing.

use maran_agent_core::validation::db::database_name::DatabaseName;

/// A database this host holds, as the diagnostic listing reports it.
///
/// The name is a [`DatabaseName`] rather than the string the server printed,
/// which is what makes the listing safe to feed back into
/// `drop_database` or
/// `database_size`: a row that could not be
/// rebuilt through `for_account` is not reported at all, so nothing a caller
/// reads out of a listing can be a name this agent would refuse to construct.
///
/// It carries only the name. The size is a separate query per database, and a
/// listing that ran one would turn an operator's overview into a full scan of
/// the server's table metadata; a caller that wants sizes asks for the ones it
/// wants.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DatabaseSummary {
    /// The database's full name, prefix included.
    pub name: DatabaseName,
}
