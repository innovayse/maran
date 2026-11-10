//! One database found on a source.

use maran_agent_core::validation::db::database_name::DatabaseName;

/// One database an account owns, by the prefix its name carries.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SourceDatabaseRecord {
    /// The database name, validated against its owning account.
    pub name: DatabaseName,
    /// Its size in bytes, or `None` when it was not measured.
    pub size_bytes: Option<u64>,
}
